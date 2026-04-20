using Microsoft.EntityFrameworkCore;
using Quartz;
using Serilog;
using QuantTrading.Api.Data;
using QuantTrading.Api.Services.AI;
using QuantTrading.Api.Services.Notification;
using QuantTrading.Api.Services.LongBridge;
using QuantTrading.Api.Services.Strategy;
using QuantTrading.Api.Services.Backtest;
using QuantTrading.Api.Services.Monitor;
using QuantTrading.Api.Services.Realtime;
using QuantTrading.Api.Jobs;
using QuantTrading.Api.Hubs;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("logs/quanttrading-.log", rollingInterval: RollingInterval.Day)
    .CreateLogger();

builder.Host.UseSerilog();

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "QuantTrading API", Version = "v1" });
});

// Configure CORS
var corsOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>()?
    .Where(origin => !string.IsNullOrWhiteSpace(origin))
    .Select(origin => origin.Trim().TrimEnd('/'))
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToArray()
    ?? Array.Empty<string>();
var allowVercelSubdomains = builder.Configuration.GetValue<bool>("Cors:AllowVercelSubdomains", true);
var allowAnyOrigin = builder.Configuration.GetValue<bool>("Cors:AllowAnyOrigin", true);

if (corsOrigins.Length == 0)
{
    corsOrigins = new[]
    {
        "http://localhost:5173",
        "http://localhost:3000",
        "http://localhost"
    };
}

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowVue", policy =>
    {
        if (allowAnyOrigin)
        {
            policy.SetIsOriginAllowed(_ => true)
                  .AllowAnyMethod()
                  .AllowAnyHeader()
                  .AllowCredentials();
            return;
        }

        policy.SetIsOriginAllowed(origin =>
            {
                if (string.IsNullOrWhiteSpace(origin))
                {
                    return false;
                }

                var normalized = origin.Trim().TrimEnd('/');
                if (corsOrigins.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (!allowVercelSubdomains || !Uri.TryCreate(normalized, UriKind.Absolute, out var uri))
                {
                    return false;
                }

                return uri.Host.EndsWith(".vercel.app", StringComparison.OrdinalIgnoreCase);
            })
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials();
    });
});

// Configure Database
var databaseProvider = builder.Configuration["Database:Provider"]?.Trim().ToLowerInvariant();
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is required.");

builder.Services.AddDbContext<QuantTradingDbContext>(options =>
{
    var usePostgres = databaseProvider == "postgres"
        || connectionString.Contains("Host=", StringComparison.OrdinalIgnoreCase);

    if (usePostgres)
    {
        options.UseNpgsql(connectionString);
        return;
    }

    options.UseSqlServer(connectionString);
});

// Configure HttpClient with proxy support
builder.Services.AddHttpClient("LongBridge", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["LongBridge:BaseUrl"] ?? "https://openapi.longportapp.com");
    client.DefaultRequestHeaders.Add("Accept", "application/json");
}).ConfigurePrimaryHttpMessageHandler(() =>
{
    var handler = new HttpClientHandler();
    var proxyUrl = builder.Configuration["Proxy:Url"];
    if (!string.IsNullOrEmpty(proxyUrl))
    {
        handler.Proxy = new System.Net.WebProxy(proxyUrl);
        handler.UseProxy = true;
    }
    return handler;
});

// Register Services
builder.Services.AddSingleton<ILongBridgeService, LongBridgeService>();
builder.Services.AddScoped<IStrategyService, StrategyService>();
builder.Services.AddScoped<IStrategyEngine, StrategyEngine>();
builder.Services.AddScoped<IBacktestService, BacktestService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<IFeishuService, FeishuService>();
builder.Services.AddScoped<IWechatService, WechatService>();
builder.Services.AddScoped<IMonitorService, MonitorService>();
builder.Services.AddScoped<IWatchlistService, WatchlistService>();
builder.Services.AddScoped<ITradeService, TradeService>();
builder.Services.AddScoped<IAiAnalysisService, OpenAiAnalysisService>();
builder.Services.AddSingleton<IRealtimePushService, RealtimePushService>();

// Configure Quartz for scheduled jobs
builder.Services.AddQuartz(q =>
{
    q.UseMicrosoftDependencyInjectionJobFactory();
    
    // Strategy execution job
    var strategyJobKey = new JobKey("StrategyExecutionJob");
    q.AddJob<StrategyExecutionJob>(opts => opts.WithIdentity(strategyJobKey));
    q.AddTrigger(opts => opts
        .ForJob(strategyJobKey)
        .WithIdentity("StrategyExecutionTrigger")
        .WithCronSchedule("0 */1 * * * ?")); // Every minute
    
    // Monitor job
    var monitorJobKey = new JobKey("MonitorJob");
    q.AddJob<MonitorJob>(opts => opts.WithIdentity(monitorJobKey));
    q.AddTrigger(opts => opts
        .ForJob(monitorJobKey)
        .WithIdentity("MonitorTrigger")
        .WithCronSchedule("0 */1 * * * ?")); // Every minute
});

builder.Services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);

// Add SignalR for real-time updates
builder.Services.AddSignalR();

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowVue");
app.UseAuthorization();

app.MapGet("/", () => Results.Ok(new
{
    service = "QuantTrading.Api",
    status = "ok",
    utc = DateTime.UtcNow,
    endpoints = new
    {
        health = "/health",
        api = "/api/*",
        hubs = "/hubs/*"
    }
}));
app.MapGet("/health", () => Results.Text("healthy", "text/plain"));
app.MapControllers();
app.MapHub<TradingHub>("/hubs/trading");
app.MapHub<NotificationHub>("/hubs/notification");

// Initialize database
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<QuantTradingDbContext>();
    dbContext.Database.EnsureCreated();
}

app.Run();
