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
    public DbSet<AppUser> AppUsers { get; set; } = null!;
    public DbSet<UserWatchlistItem> UserWatchlistItems { get; set; } = null!;
    public DbSet<AiChatSessionRecord> AiChatSessions { get; set; } = null!;
    public DbSet<AiChatMessageRecord> AiChatMessages { get; set; } = null!;
    public DbSet<AiMemoryRecord> AiMemories { get; set; } = null!;
    public DbSet<CrawlerSource> CrawlerSources { get; set; } = null!;
    public DbSet<CrawlerJob> CrawlerJobs { get; set; } = null!;
    public DbSet<CrawlerDocument> CrawlerDocuments { get; set; } = null!;
    public DbSet<KnowledgeBase> KnowledgeBases { get; set; } = null!;
    public DbSet<KnowledgeDocument> KnowledgeDocuments { get; set; } = null!;
    public DbSet<KnowledgeChunk> KnowledgeChunks { get; set; } = null!;
    public DbSet<ReaderBook> ReaderBooks { get; set; } = null!;
    public DbSet<ReaderProgress> ReaderProgresses { get; set; } = null!;
    public DbSet<ReaderHighlight> ReaderHighlights { get; set; } = null!;

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

        modelBuilder.Entity<AppUser>()
            .HasIndex(u => u.Username)
            .IsUnique();

        modelBuilder.Entity<UserWatchlistItem>()
            .HasIndex(w => new { w.UserId, w.Symbol })
            .IsUnique();

        modelBuilder.Entity<AiChatSessionRecord>()
            .HasIndex(s => new { s.UserId, s.UpdatedAt });

        modelBuilder.Entity<AiChatMessageRecord>()
            .HasIndex(m => new { m.SessionId, m.CreatedAt });

        modelBuilder.Entity<AiChatMessageRecord>()
            .HasOne(m => m.Session)
            .WithMany(s => s.Messages)
            .HasForeignKey(m => m.SessionId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<AiMemoryRecord>()
            .HasIndex(m => new { m.UserId, m.IsArchived, m.UpdatedAt });

        modelBuilder.Entity<AiMemoryRecord>()
            .HasIndex(m => new { m.UserId, m.SourceType, m.UpdatedAt });

        modelBuilder.Entity<AiMemoryRecord>()
            .HasIndex(m => new { m.UserId, m.KnowledgeBaseId, m.UpdatedAt });

        modelBuilder.Entity<AiMemoryRecord>()
            .HasIndex(m => new { m.UserId, m.SourceRef });

        modelBuilder.Entity<AiMemoryRecord>()
            .HasIndex(m => new { m.UserId, m.KnowledgeDocumentId, m.UpdatedAt });

        modelBuilder.Entity<CrawlerSource>()
            .HasIndex(s => new { s.UserId, s.IsEnabled });

        modelBuilder.Entity<CrawlerDocument>()
            .HasIndex(d => new { d.UserId, d.ContentHash });

        modelBuilder.Entity<KnowledgeBase>()
            .HasIndex(k => new { k.UserId, k.Name });

        modelBuilder.Entity<KnowledgeDocument>()
            .HasIndex(d => new { d.KnowledgeBaseId, d.ContentHash });

        modelBuilder.Entity<KnowledgeChunk>()
            .HasIndex(c => new { c.KnowledgeBaseId, c.DocumentId, c.ChunkIndex });

        modelBuilder.Entity<ReaderBook>()
            .HasIndex(b => new { b.UserId, b.UpdatedAt });

        modelBuilder.Entity<ReaderBook>()
            .HasIndex(b => new { b.UserId, b.ContentHash });

        modelBuilder.Entity<ReaderProgress>()
            .HasIndex(p => new { p.UserId, p.BookId })
            .IsUnique();

        modelBuilder.Entity<ReaderHighlight>()
            .HasIndex(h => new { h.UserId, h.BookId, h.CreatedAt });

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

        if (Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true)
        {
            ConfigurePostgresTextColumns(modelBuilder);
        }
    }

    private static void ConfigurePostgresTextColumns(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Strategy>()
            .Property(item => item.ConfigJson)
            .HasColumnType("text");
        modelBuilder.Entity<StrategyExecution>()
            .Property(item => item.DetailsJson)
            .HasColumnType("text");

        modelBuilder.Entity<Backtest>()
            .Property(item => item.EquityCurveJson)
            .HasColumnType("text");
        modelBuilder.Entity<Backtest>()
            .Property(item => item.TradesJson)
            .HasColumnType("text");
        modelBuilder.Entity<Review>()
            .Property(item => item.Content)
            .HasColumnType("text");
        modelBuilder.Entity<Review>()
            .Property(item => item.TradesAnalysisJson)
            .HasColumnType("text");
        modelBuilder.Entity<Review>()
            .Property(item => item.LessonsLearned)
            .HasColumnType("text");
        modelBuilder.Entity<Review>()
            .Property(item => item.ImprovementPlans)
            .HasColumnType("text");
        modelBuilder.Entity<Review>()
            .Property(item => item.Tags)
            .HasColumnType("text");

        modelBuilder.Entity<MonitorRule>()
            .Property(item => item.SymbolsJson)
            .HasColumnType("text");
        modelBuilder.Entity<MonitorRule>()
            .Property(item => item.ConditionsJson)
            .HasColumnType("text");
        modelBuilder.Entity<MonitorRule>()
            .Property(item => item.NotifyChannelsJson)
            .HasColumnType("text");
        modelBuilder.Entity<MonitorAlert>()
            .Property(item => item.Message)
            .HasColumnType("text");
        modelBuilder.Entity<MonitorAlert>()
            .Property(item => item.DataJson)
            .HasColumnType("text");
        modelBuilder.Entity<SystemConfig>()
            .Property(item => item.Value)
            .HasColumnType("text");
        modelBuilder.Entity<NotificationLog>()
            .Property(item => item.Content)
            .HasColumnType("text");

        modelBuilder.Entity<AiChatMessageRecord>()
            .Property(item => item.Content)
            .HasColumnType("text");
        modelBuilder.Entity<AiChatMessageRecord>()
            .Property(item => item.MarketContextJson)
            .HasColumnType("text");
        modelBuilder.Entity<AiMemoryRecord>()
            .Property(item => item.Content)
            .HasColumnType("text");
        modelBuilder.Entity<CrawlerJob>()
            .Property(item => item.ErrorMessage)
            .HasColumnType("text");
        modelBuilder.Entity<CrawlerDocument>()
            .Property(item => item.Markdown)
            .HasColumnType("text");
        modelBuilder.Entity<CrawlerDocument>()
            .Property(item => item.Summary)
            .HasColumnType("text");
        modelBuilder.Entity<KnowledgeDocument>()
            .Property(item => item.Markdown)
            .HasColumnType("text");
        modelBuilder.Entity<KnowledgeChunk>()
            .Property(item => item.Content)
            .HasColumnType("text");
        modelBuilder.Entity<ReaderBook>()
            .Property(item => item.Markdown)
            .HasColumnType("text");
        modelBuilder.Entity<ReaderHighlight>()
            .Property(item => item.SelectedText)
            .HasColumnType("text");
        modelBuilder.Entity<ReaderHighlight>()
            .Property(item => item.Note)
            .HasColumnType("text");
    }
}
