using Microsoft.EntityFrameworkCore;
using QuantTrading.Api.Models;

namespace QuantTrading.Api.Data;

public class QuantTradingDbContext : DbContext
{
    public QuantTradingDbContext(DbContextOptions<QuantTradingDbContext> options) : base(options)
    {
    }

    public DbSet<Stock> Stocks { get; set; } = null!;
    public DbSet<StockQuote> StockQuotes { get; set; } = null!;
    public DbSet<StockKline> StockKlines { get; set; } = null!;
    public DbSet<Strategy> Strategies { get; set; } = null!;
    public DbSet<StrategyExecution> StrategyExecutions { get; set; } = null!;
    public DbSet<Trade> Trades { get; set; } = null!;
    public DbSet<Position> Positions { get; set; } = null!;
    public DbSet<Account> Accounts { get; set; } = null!;
    public DbSet<Backtest> Backtests { get; set; } = null!;
    public DbSet<Review> Reviews { get; set; } = null!;
    public DbSet<MonitorRule> MonitorRules { get; set; } = null!;
    public DbSet<MonitorAlert> MonitorAlerts { get; set; } = null!;
    public DbSet<SystemConfig> SystemConfigs { get; set; } = null!;
    public DbSet<NotificationLog> NotificationLogs { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Stock indexes
        modelBuilder.Entity<Stock>()
            .HasIndex(s => s.Symbol)
            .IsUnique();

        modelBuilder.Entity<StockQuote>()
            .HasIndex(s => new { s.Symbol, s.Timestamp });

        modelBuilder.Entity<StockKline>()
            .HasIndex(s => new { s.Symbol, s.Period, s.Timestamp });

        // Strategy indexes
        modelBuilder.Entity<Strategy>()
            .HasIndex(s => s.Name);

        modelBuilder.Entity<StrategyExecution>()
            .HasIndex(s => new { s.StrategyId, s.ExecutedAt });

        // Trade indexes
        modelBuilder.Entity<Trade>()
            .HasIndex(t => t.Symbol);

        modelBuilder.Entity<Trade>()
            .HasIndex(t => t.CreatedAt);

        modelBuilder.Entity<Position>()
            .HasIndex(p => p.Symbol)
            .IsUnique();

        // Monitor indexes
        modelBuilder.Entity<MonitorAlert>()
            .HasIndex(a => new { a.MonitorRuleId, a.TriggeredAt });

        modelBuilder.Entity<SystemConfig>()
            .HasIndex(c => new { c.Category, c.Key })
            .IsUnique();

        // Seed initial system configs
        modelBuilder.Entity<SystemConfig>().HasData(
            new SystemConfig { Id = 1, Key = "AppKey", Category = "longbridge", Description = "Long Bridge App Key" },
            new SystemConfig { Id = 2, Key = "AppSecret", Category = "longbridge", Description = "Long Bridge App Secret" },
            new SystemConfig { Id = 3, Key = "AccessToken", Category = "longbridge", Description = "Long Bridge Access Token" },
            new SystemConfig { Id = 4, Key = "SmtpServer", Category = "email", Description = "SMTP Server", Value = "smtp.gmail.com" },
            new SystemConfig { Id = 5, Key = "SmtpPort", Category = "email", Description = "SMTP Port", Value = "587" },
            new SystemConfig { Id = 6, Key = "SmtpUsername", Category = "email", Description = "SMTP Username" },
            new SystemConfig { Id = 7, Key = "SmtpPassword", Category = "email", Description = "SMTP Password", IsEncrypted = true },
            new SystemConfig { Id = 8, Key = "FromAddress", Category = "email", Description = "From Email Address" },
            new SystemConfig { Id = 9, Key = "WebhookUrl", Category = "feishu", Description = "Feishu Webhook URL" },
            new SystemConfig { Id = 10, Key = "Secret", Category = "feishu", Description = "Feishu Webhook Secret", IsEncrypted = true },
            new SystemConfig { Id = 11, Key = "WebhookUrl", Category = "wechat", Description = "WeChat Work Webhook URL" },
            new SystemConfig { Id = 12, Key = "Enabled", Category = "proxy", Description = "Enable Proxy", Value = "false" },
            new SystemConfig { Id = 13, Key = "Url", Category = "proxy", Description = "Proxy URL" }
        );
    }
}
