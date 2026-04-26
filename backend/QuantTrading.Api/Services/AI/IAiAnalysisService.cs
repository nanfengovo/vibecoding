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
    public IReadOnlyList<string> ConversationContext { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> MemoryContext { get; init; } = Array.Empty<string>();
    public IReadOnlyList<AiKnowledgeReference> KnowledgeContext { get; init; } = Array.Empty<AiKnowledgeReference>();
    public AiReaderContext? ReaderContext { get; init; }
}

public sealed class AiReaderContext
{
    public long BookId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Format { get; init; } = string.Empty;
    public string Locator { get; init; } = string.Empty;
    public string SelectedText { get; init; } = string.Empty;
}

public sealed class AiChatResult
{
    public string Model { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public DateTime GeneratedAt { get; init; } = DateTime.UtcNow;
    public AiChatMarketContext? MarketContext { get; init; }
    public long? SessionId { get; init; }
    public List<AiKnowledgeReference> References { get; init; } = new();
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

public sealed class AiKnowledgeReference
{
    public long DocumentId { get; init; }
    public long ChunkId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string SourceUrl { get; init; } = string.Empty;
    public string Snippet { get; init; } = string.Empty;
}

public sealed class AiPromptOptimizeInput
{
    public string Question { get; init; } = string.Empty;
    public string Symbol { get; init; } = string.Empty;
    public string ProviderId { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
    public string Scene { get; init; } = string.Empty;
    public string ContextText { get; init; } = string.Empty;
    public long? KnowledgeBaseId { get; init; }
    public AiReaderContext? ReaderContext { get; init; }
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
