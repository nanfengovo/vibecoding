using QuantTrading.Api.Models;

namespace QuantTrading.Api.Services.AI;

public interface IAiAnalysisService
{
    Task<AiConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken = default);

    Task<StockAnalysisResult> AnalyzeStockAsync(
        StockAnalysisInput input,
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
