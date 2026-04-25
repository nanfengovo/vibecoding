using Microsoft.EntityFrameworkCore;
using QuantTrading.Api.Data;
using QuantTrading.Api.Models;

namespace QuantTrading.Api.Services.Auth;

public sealed class DatabaseBootstrapService
{
    private const string DefaultAdminPassword = "Admin@Test2026";

    private readonly QuantTradingDbContext _dbContext;
    private readonly IConfiguration _configuration;
    private readonly IPasswordService _passwordService;
    private readonly ILogger<DatabaseBootstrapService> _logger;

    public DatabaseBootstrapService(
        QuantTradingDbContext dbContext,
        IConfiguration configuration,
        IPasswordService passwordService,
        ILogger<DatabaseBootstrapService> logger)
    {
        _dbContext = dbContext;
        _configuration = configuration;
        _passwordService = passwordService;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        var admin = await EnsureDefaultAdminAsync(cancellationToken);
        await AssignExistingRowsAsync(admin.Id, cancellationToken);
    }

    private async Task EnsureSchemaAsync(CancellationToken cancellationToken)
    {
        if (IsPostgres())
        {
            foreach (var sql in BuildPostgresSchemaSql())
            {
                await _dbContext.Database.ExecuteSqlRawAsync(sql, cancellationToken);
            }

            return;
        }

        foreach (var sql in BuildSqlServerSchemaSql())
        {
            await _dbContext.Database.ExecuteSqlRawAsync(sql, cancellationToken);
        }
    }

    private async Task<AppUser> EnsureDefaultAdminAsync(CancellationToken cancellationToken)
    {
        var configuredUsername = (_configuration["Auth:AdminUsername"] ?? "admin").Trim();
        var username = string.IsNullOrWhiteSpace(configuredUsername) ? "admin" : configuredUsername;
        var configuredPassword = _configuration["Auth:AdminPassword"];
        var effectivePassword = configuredPassword;
        if (string.IsNullOrWhiteSpace(effectivePassword))
        {
            effectivePassword = DefaultAdminPassword;
            _logger.LogWarning("Auth:AdminPassword is not configured. Falling back to default admin password for user {Username}. Please override Auth:AdminPassword in production.", username);
        }
        var existingAdmin = await _dbContext.AppUsers
            .OrderBy(u => u.Id)
            .FirstOrDefaultAsync(u => u.Role == UserRoles.Admin, cancellationToken);

        if (existingAdmin != null)
        {
            await SyncConfiguredAdminCredentialsAsync(existingAdmin, username, effectivePassword, cancellationToken);
            return existingAdmin;
        }

        var password = effectivePassword;

        var admin = new AppUser
        {
            Username = username,
            DisplayName = "管理员",
            Role = UserRoles.Admin,
            PasswordHash = _passwordService.HashPassword(password),
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _dbContext.AppUsers.Add(admin);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return admin;
    }

    private async Task SyncConfiguredAdminCredentialsAsync(
        AppUser admin,
        string configuredUsername,
        string? configuredPassword,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(configuredPassword))
        {
            return;
        }

        var changed = false;
        if (!string.Equals(admin.Username, configuredUsername, StringComparison.Ordinal))
        {
            var usernameTaken = await _dbContext.AppUsers
                .AnyAsync(u => u.Id != admin.Id && u.Username == configuredUsername, cancellationToken);
            if (!usernameTaken)
            {
                admin.Username = configuredUsername;
                admin.UpdatedAt = DateTime.UtcNow;
                changed = true;
            }
        }

        if (!_passwordService.VerifyPassword(configuredPassword, admin.PasswordHash))
        {
            admin.PasswordHash = _passwordService.HashPassword(configuredPassword);
            admin.UpdatedAt = DateTime.UtcNow;
            changed = true;
            _logger.LogInformation("Updated admin credentials for user {Username} from configured Auth:AdminPassword.", configuredUsername);
        }

        if (!admin.IsActive)
        {
            admin.IsActive = true;
            admin.UpdatedAt = DateTime.UtcNow;
            changed = true;
        }

        if (changed)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task AssignExistingRowsAsync(int userId, CancellationToken cancellationToken)
    {
        foreach (var table in UserOwnedTables)
        {
            var sql = IsPostgres()
                ? $"UPDATE \"{table}\" SET \"UserId\" = {userId} WHERE \"UserId\" IS NULL"
                : $"UPDATE [{table}] SET [UserId] = {userId} WHERE [UserId] IS NULL";
            await _dbContext.Database.ExecuteSqlRawAsync(sql, cancellationToken);
        }
    }

    private bool IsPostgres()
    {
        return _dbContext.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static readonly string[] UserOwnedTables =
    {
        "Strategies",
        "StrategyExecutions",
        "Trades",
        "Positions",
        "Accounts",
        "Backtests",
        "Reviews",
        "MonitorRules",
        "MonitorAlerts"
    };

    private static IEnumerable<string> BuildPostgresSchemaSql()
    {
        foreach (var table in UserOwnedTables)
        {
            yield return $"ALTER TABLE \"{table}\" ADD COLUMN IF NOT EXISTS \"UserId\" integer NULL";
        }

        yield return """
CREATE TABLE IF NOT EXISTS "AppUsers" (
    "Id" integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
    "Username" varchar(80) NOT NULL,
    "DisplayName" varchar(120) NOT NULL DEFAULT '',
    "PasswordHash" varchar(500) NOT NULL,
    "Role" varchar(20) NOT NULL,
    "IsActive" boolean NOT NULL DEFAULT true,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    "LastLoginAt" timestamp with time zone NULL
)
""";
        yield return """CREATE UNIQUE INDEX IF NOT EXISTS "IX_AppUsers_Username" ON "AppUsers" ("Username")""";

        yield return """
CREATE TABLE IF NOT EXISTS "UserWatchlistItems" (
    "Id" integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
    "UserId" integer NOT NULL,
    "Symbol" varchar(20) NOT NULL,
    "Notes" varchar(500) NOT NULL DEFAULT '',
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL
)
""";
        yield return """CREATE UNIQUE INDEX IF NOT EXISTS "IX_UserWatchlistItems_UserId_Symbol" ON "UserWatchlistItems" ("UserId", "Symbol")""";

        yield return """
CREATE TABLE IF NOT EXISTS "AiChatSessions" (
    "Id" bigint GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
    "UserId" integer NOT NULL,
    "Title" varchar(120) NOT NULL,
    "Symbol" varchar(20) NOT NULL DEFAULT '',
    "SkillId" varchar(80) NOT NULL DEFAULT '',
    "ProviderId" varchar(120) NOT NULL DEFAULT '',
    "Model" varchar(200) NOT NULL DEFAULT '',
    "LastMarketContextSymbol" varchar(120) NOT NULL DEFAULT '',
    "IsArchived" boolean NOT NULL DEFAULT false,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL
)
""";
        yield return """CREATE INDEX IF NOT EXISTS "IX_AiChatSessions_UserId_UpdatedAt" ON "AiChatSessions" ("UserId", "UpdatedAt")""";

        yield return """
CREATE TABLE IF NOT EXISTS "AiChatMessages" (
    "Id" bigint GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
    "SessionId" bigint NOT NULL,
    "UserId" integer NOT NULL,
    "Role" varchar(20) NOT NULL,
    "Content" text NOT NULL,
    "Model" varchar(200) NOT NULL DEFAULT '',
    "MarketContextJson" text NOT NULL DEFAULT '',
    "IsError" boolean NOT NULL DEFAULT false,
    "CreatedAt" timestamp with time zone NOT NULL
)
""";
        yield return """CREATE INDEX IF NOT EXISTS "IX_AiChatMessages_SessionId_CreatedAt" ON "AiChatMessages" ("SessionId", "CreatedAt")""";

        yield return """
CREATE TABLE IF NOT EXISTS "AiMemories" (
    "Id" bigint GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
    "UserId" integer NOT NULL,
    "Type" varchar(50) NOT NULL,
    "Title" varchar(120) NOT NULL DEFAULT '',
    "Content" text NOT NULL,
    "Symbol" varchar(20) NOT NULL DEFAULT '',
    "Tags" varchar(200) NOT NULL DEFAULT '',
    "Priority" integer NOT NULL DEFAULT 1,
    "IsArchived" boolean NOT NULL DEFAULT false,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL
)
""";
        yield return """CREATE INDEX IF NOT EXISTS "IX_AiMemories_UserId_IsArchived_UpdatedAt" ON "AiMemories" ("UserId", "IsArchived", "UpdatedAt")""";

        yield return """
CREATE TABLE IF NOT EXISTS "CrawlerSources" (
    "Id" bigint GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
    "UserId" integer NOT NULL,
    "Name" varchar(120) NOT NULL,
    "Type" varchar(40) NOT NULL,
    "Url" varchar(1000) NOT NULL,
    "Symbol" varchar(20) NOT NULL DEFAULT '',
    "Tags" varchar(300) NOT NULL DEFAULT '',
    "IsEnabled" boolean NOT NULL DEFAULT true,
    "CrawlIntervalMinutes" integer NOT NULL DEFAULT 360,
    "MaxPages" integer NOT NULL DEFAULT 5,
    "LastRunAt" timestamp with time zone NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL
)
""";
        yield return """
CREATE TABLE IF NOT EXISTS "CrawlerJobs" (
    "Id" bigint GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
    "SourceId" bigint NOT NULL,
    "UserId" integer NOT NULL,
    "Status" varchar(30) NOT NULL,
    "DocumentsFound" integer NOT NULL DEFAULT 0,
    "DocumentsSaved" integer NOT NULL DEFAULT 0,
    "ErrorMessage" text NOT NULL DEFAULT '',
    "StartedAt" timestamp with time zone NOT NULL,
    "FinishedAt" timestamp with time zone NULL
)
""";
        yield return """
CREATE TABLE IF NOT EXISTS "CrawlerDocuments" (
    "Id" bigint GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
    "SourceId" bigint NOT NULL,
    "UserId" integer NOT NULL,
    "Symbol" varchar(20) NOT NULL DEFAULT '',
    "Title" varchar(300) NOT NULL,
    "Url" varchar(1000) NOT NULL,
    "ContentHash" varchar(64) NOT NULL DEFAULT '',
    "Markdown" text NOT NULL,
    "Summary" text NOT NULL DEFAULT '',
    "Tags" varchar(300) NOT NULL DEFAULT '',
    "PublishedAt" timestamp with time zone NULL,
    "CreatedAt" timestamp with time zone NOT NULL
)
""";
        yield return """CREATE INDEX IF NOT EXISTS "IX_CrawlerDocuments_UserId_ContentHash" ON "CrawlerDocuments" ("UserId", "ContentHash")""";

        yield return """
CREATE TABLE IF NOT EXISTS "KnowledgeBases" (
    "Id" bigint GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
    "UserId" integer NOT NULL,
    "Name" varchar(120) NOT NULL,
    "Description" varchar(500) NOT NULL DEFAULT '',
    "IsDefault" boolean NOT NULL DEFAULT false,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL
)
""";
        yield return """
CREATE TABLE IF NOT EXISTS "KnowledgeDocuments" (
    "Id" bigint GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
    "KnowledgeBaseId" bigint NOT NULL,
    "UserId" integer NOT NULL,
    "Title" varchar(300) NOT NULL,
    "SourceUrl" varchar(1000) NOT NULL DEFAULT '',
    "SourceType" varchar(40) NOT NULL DEFAULT 'manual_markdown',
    "ContentHash" varchar(64) NOT NULL DEFAULT '',
    "Markdown" text NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL
)
""";
        yield return """
CREATE TABLE IF NOT EXISTS "KnowledgeChunks" (
    "Id" bigint GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
    "KnowledgeBaseId" bigint NOT NULL,
    "DocumentId" bigint NOT NULL,
    "UserId" integer NOT NULL,
    "ChunkIndex" integer NOT NULL,
    "Heading" varchar(300) NOT NULL DEFAULT '',
    "Content" text NOT NULL,
    "Keywords" varchar(800) NOT NULL DEFAULT '',
    "CreatedAt" timestamp with time zone NOT NULL
)
""";
        yield return """CREATE INDEX IF NOT EXISTS "IX_KnowledgeChunks_KnowledgeBaseId_DocumentId_ChunkIndex" ON "KnowledgeChunks" ("KnowledgeBaseId", "DocumentId", "ChunkIndex")""";
    }

    private static IEnumerable<string> BuildSqlServerSchemaSql()
    {
        foreach (var table in UserOwnedTables)
        {
            yield return $"IF COL_LENGTH('{table}', 'UserId') IS NULL ALTER TABLE [{table}] ADD [UserId] int NULL";
        }

        yield return """
IF OBJECT_ID(N'[AppUsers]', N'U') IS NULL
CREATE TABLE [AppUsers] (
    [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_AppUsers] PRIMARY KEY,
    [Username] nvarchar(80) NOT NULL,
    [DisplayName] nvarchar(120) NOT NULL DEFAULT N'',
    [PasswordHash] nvarchar(500) NOT NULL,
    [Role] nvarchar(20) NOT NULL,
    [IsActive] bit NOT NULL DEFAULT 1,
    [CreatedAt] datetime2 NOT NULL,
    [UpdatedAt] datetime2 NOT NULL,
    [LastLoginAt] datetime2 NULL
)
""";
        yield return """IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AppUsers_Username' AND object_id = OBJECT_ID(N'[AppUsers]')) CREATE UNIQUE INDEX [IX_AppUsers_Username] ON [AppUsers] ([Username])""";

        yield return """
IF OBJECT_ID(N'[UserWatchlistItems]', N'U') IS NULL
CREATE TABLE [UserWatchlistItems] (
    [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_UserWatchlistItems] PRIMARY KEY,
    [UserId] int NOT NULL,
    [Symbol] nvarchar(20) NOT NULL,
    [Notes] nvarchar(500) NOT NULL DEFAULT N'',
    [CreatedAt] datetime2 NOT NULL,
    [UpdatedAt] datetime2 NOT NULL
)
""";
        yield return """IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_UserWatchlistItems_UserId_Symbol' AND object_id = OBJECT_ID(N'[UserWatchlistItems]')) CREATE UNIQUE INDEX [IX_UserWatchlistItems_UserId_Symbol] ON [UserWatchlistItems] ([UserId], [Symbol])""";

        yield return """
IF OBJECT_ID(N'[AiChatSessions]', N'U') IS NULL
CREATE TABLE [AiChatSessions] (
    [Id] bigint IDENTITY(1,1) NOT NULL CONSTRAINT [PK_AiChatSessions] PRIMARY KEY,
    [UserId] int NOT NULL,
    [Title] nvarchar(120) NOT NULL,
    [Symbol] nvarchar(20) NOT NULL DEFAULT N'',
    [SkillId] nvarchar(80) NOT NULL DEFAULT N'',
    [ProviderId] nvarchar(120) NOT NULL DEFAULT N'',
    [Model] nvarchar(200) NOT NULL DEFAULT N'',
    [LastMarketContextSymbol] nvarchar(120) NOT NULL DEFAULT N'',
    [IsArchived] bit NOT NULL DEFAULT 0,
    [CreatedAt] datetime2 NOT NULL,
    [UpdatedAt] datetime2 NOT NULL
)
""";
        yield return """IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AiChatSessions_UserId_UpdatedAt' AND object_id = OBJECT_ID(N'[AiChatSessions]')) CREATE INDEX [IX_AiChatSessions_UserId_UpdatedAt] ON [AiChatSessions] ([UserId], [UpdatedAt])""";

        yield return """
IF OBJECT_ID(N'[AiChatMessages]', N'U') IS NULL
CREATE TABLE [AiChatMessages] (
    [Id] bigint IDENTITY(1,1) NOT NULL CONSTRAINT [PK_AiChatMessages] PRIMARY KEY,
    [SessionId] bigint NOT NULL,
    [UserId] int NOT NULL,
    [Role] nvarchar(20) NOT NULL,
    [Content] nvarchar(max) NOT NULL,
    [Model] nvarchar(200) NOT NULL DEFAULT N'',
    [MarketContextJson] nvarchar(max) NOT NULL DEFAULT N'',
    [IsError] bit NOT NULL DEFAULT 0,
    [CreatedAt] datetime2 NOT NULL
)
""";
        yield return """IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AiChatMessages_SessionId_CreatedAt' AND object_id = OBJECT_ID(N'[AiChatMessages]')) CREATE INDEX [IX_AiChatMessages_SessionId_CreatedAt] ON [AiChatMessages] ([SessionId], [CreatedAt])""";

        yield return """
IF OBJECT_ID(N'[AiMemories]', N'U') IS NULL
CREATE TABLE [AiMemories] (
    [Id] bigint IDENTITY(1,1) NOT NULL CONSTRAINT [PK_AiMemories] PRIMARY KEY,
    [UserId] int NOT NULL,
    [Type] nvarchar(50) NOT NULL,
    [Title] nvarchar(120) NOT NULL DEFAULT N'',
    [Content] nvarchar(max) NOT NULL,
    [Symbol] nvarchar(20) NOT NULL DEFAULT N'',
    [Tags] nvarchar(200) NOT NULL DEFAULT N'',
    [Priority] int NOT NULL DEFAULT 1,
    [IsArchived] bit NOT NULL DEFAULT 0,
    [CreatedAt] datetime2 NOT NULL,
    [UpdatedAt] datetime2 NOT NULL
)
""";
        yield return """IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AiMemories_UserId_IsArchived_UpdatedAt' AND object_id = OBJECT_ID(N'[AiMemories]')) CREATE INDEX [IX_AiMemories_UserId_IsArchived_UpdatedAt] ON [AiMemories] ([UserId], [IsArchived], [UpdatedAt])""";

        yield return CreateSqlServerCrawlerAndKnowledgeTables();
    }

    private static string CreateSqlServerCrawlerAndKnowledgeTables()
    {
        return """
IF OBJECT_ID(N'[CrawlerSources]', N'U') IS NULL
CREATE TABLE [CrawlerSources] ([Id] bigint IDENTITY(1,1) NOT NULL CONSTRAINT [PK_CrawlerSources] PRIMARY KEY, [UserId] int NOT NULL, [Name] nvarchar(120) NOT NULL, [Type] nvarchar(40) NOT NULL, [Url] nvarchar(1000) NOT NULL, [Symbol] nvarchar(20) NOT NULL DEFAULT N'', [Tags] nvarchar(300) NOT NULL DEFAULT N'', [IsEnabled] bit NOT NULL DEFAULT 1, [CrawlIntervalMinutes] int NOT NULL DEFAULT 360, [MaxPages] int NOT NULL DEFAULT 5, [LastRunAt] datetime2 NULL, [CreatedAt] datetime2 NOT NULL, [UpdatedAt] datetime2 NOT NULL);
IF OBJECT_ID(N'[CrawlerJobs]', N'U') IS NULL
CREATE TABLE [CrawlerJobs] ([Id] bigint IDENTITY(1,1) NOT NULL CONSTRAINT [PK_CrawlerJobs] PRIMARY KEY, [SourceId] bigint NOT NULL, [UserId] int NOT NULL, [Status] nvarchar(30) NOT NULL, [DocumentsFound] int NOT NULL DEFAULT 0, [DocumentsSaved] int NOT NULL DEFAULT 0, [ErrorMessage] nvarchar(max) NOT NULL DEFAULT N'', [StartedAt] datetime2 NOT NULL, [FinishedAt] datetime2 NULL);
IF OBJECT_ID(N'[CrawlerDocuments]', N'U') IS NULL
CREATE TABLE [CrawlerDocuments] ([Id] bigint IDENTITY(1,1) NOT NULL CONSTRAINT [PK_CrawlerDocuments] PRIMARY KEY, [SourceId] bigint NOT NULL, [UserId] int NOT NULL, [Symbol] nvarchar(20) NOT NULL DEFAULT N'', [Title] nvarchar(300) NOT NULL, [Url] nvarchar(1000) NOT NULL, [ContentHash] nvarchar(64) NOT NULL DEFAULT N'', [Markdown] nvarchar(max) NOT NULL, [Summary] nvarchar(max) NOT NULL DEFAULT N'', [Tags] nvarchar(300) NOT NULL DEFAULT N'', [PublishedAt] datetime2 NULL, [CreatedAt] datetime2 NOT NULL);
IF OBJECT_ID(N'[KnowledgeBases]', N'U') IS NULL
CREATE TABLE [KnowledgeBases] ([Id] bigint IDENTITY(1,1) NOT NULL CONSTRAINT [PK_KnowledgeBases] PRIMARY KEY, [UserId] int NOT NULL, [Name] nvarchar(120) NOT NULL, [Description] nvarchar(500) NOT NULL DEFAULT N'', [IsDefault] bit NOT NULL DEFAULT 0, [CreatedAt] datetime2 NOT NULL, [UpdatedAt] datetime2 NOT NULL);
IF OBJECT_ID(N'[KnowledgeDocuments]', N'U') IS NULL
CREATE TABLE [KnowledgeDocuments] ([Id] bigint IDENTITY(1,1) NOT NULL CONSTRAINT [PK_KnowledgeDocuments] PRIMARY KEY, [KnowledgeBaseId] bigint NOT NULL, [UserId] int NOT NULL, [Title] nvarchar(300) NOT NULL, [SourceUrl] nvarchar(1000) NOT NULL DEFAULT N'', [SourceType] nvarchar(40) NOT NULL DEFAULT N'manual_markdown', [ContentHash] nvarchar(64) NOT NULL DEFAULT N'', [Markdown] nvarchar(max) NOT NULL, [CreatedAt] datetime2 NOT NULL, [UpdatedAt] datetime2 NOT NULL);
IF OBJECT_ID(N'[KnowledgeChunks]', N'U') IS NULL
CREATE TABLE [KnowledgeChunks] ([Id] bigint IDENTITY(1,1) NOT NULL CONSTRAINT [PK_KnowledgeChunks] PRIMARY KEY, [KnowledgeBaseId] bigint NOT NULL, [DocumentId] bigint NOT NULL, [UserId] int NOT NULL, [ChunkIndex] int NOT NULL, [Heading] nvarchar(300) NOT NULL DEFAULT N'', [Content] nvarchar(max) NOT NULL, [Keywords] nvarchar(800) NOT NULL DEFAULT N'', [CreatedAt] datetime2 NOT NULL);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_CrawlerDocuments_UserId_ContentHash' AND object_id = OBJECT_ID(N'[CrawlerDocuments]')) CREATE INDEX [IX_CrawlerDocuments_UserId_ContentHash] ON [CrawlerDocuments] ([UserId], [ContentHash]);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_KnowledgeChunks_KnowledgeBaseId_DocumentId_ChunkIndex' AND object_id = OBJECT_ID(N'[KnowledgeChunks]')) CREATE INDEX [IX_KnowledgeChunks_KnowledgeBaseId_DocumentId_ChunkIndex] ON [KnowledgeChunks] ([KnowledgeBaseId], [DocumentId], [ChunkIndex]);
""";
    }
}
