using QuantTrading.Api.Models;

namespace QuantTrading.Api.Services.AI;

public interface IAiAnalysisService
{
    Task<AiConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken = default);

    Task<StockAnalysisResult> AnalyzeStockAsync(
        StockAnalysisInput input,
        CancellationToken cancellationToken = default);

    Task<AiChatResult> ChatAsync(
        AiChatInput input,
        CancellationToken cancellationToken = default);

    Task<AiPromptOptimizeResult> OptimizePromptAsync(
        AiPromptOptimizeInput input,
        CancellationToken cancellationToken = default);

    Task<AiModelsResult> GetModelsAsync(
        AiModelsInput input,
        CancellationToken cancellationToken = default);
}

public sealed class AiConnectionTestResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
}

public sealed class StockAnalysisInput
{
    public string Symbol { get; init; } = string.Empty;
    public string Focus { get; init; } = string.Empty;
    public string ProviderId { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
    public Stock? Stock { get; init; }
    public StockQuote? Quote { get; init; }
    public List<StockKline> Klines { get; init; } = new();
}

public sealed class StockAnalysisResult
{
    public string Symbol { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
    public string Analysis { get; init; } = string.Empty;
    public DateTime GeneratedAt { get; init; } = DateTime.UtcNow;
}

public sealed class AiChatInput
{
    public string Question { get; init; } = string.Empty;
    public string Symbol { get; init; } = string.Empty;
    public string Focus { get; init; } = string.Empty;
    public string SkillId { get; init; } = string.Empty;
    public string ProviderId { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
}

public sealed class AiChatResult
{
    public string Model { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public DateTime GeneratedAt { get; init; } = DateTime.UtcNow;
    public AiChatMarketContext? MarketContext { get; init; }
}

public sealed class AiChatMarketContext
{
    public string Symbol { get; init; } = string.Empty;
    public string Market { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public decimal ChangePercent { get; init; }
    public DateTime QuoteTime { get; init; } = DateTime.UtcNow;
    public int LagSeconds { get; init; }
    public bool MarketOpen { get; init; }
    public string Freshness { get; init; } = "stale";
    public string Source { get; init; } = "longbridge";
}

public sealed class AiPromptOptimizeInput
{
    public string Question { get; init; } = string.Empty;
    public string Symbol { get; init; } = string.Empty;
    public string ProviderId { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
}

public sealed class AiPromptOptimizeResult
{
    public string Model { get; init; } = string.Empty;
    public string OptimizedPrompt { get; init; } = string.Empty;
    public DateTime GeneratedAt { get; init; } = DateTime.UtcNow;
}

public sealed class AiModelsInput
{
    public string ProviderId { get; init; } = string.Empty;
    public string BaseUrl { get; init; } = string.Empty;
    public string ApiKey { get; init; } = string.Empty;
}

public sealed class AiModelsResult
{
    public string ProviderId { get; init; } = string.Empty;
    public List<string> Models { get; init; } = new();
    public DateTime FetchedAt { get; init; } = DateTime.UtcNow;
}
