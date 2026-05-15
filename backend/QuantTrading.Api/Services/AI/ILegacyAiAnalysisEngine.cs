namespace QuantTrading.Api.Services.AI;

public interface ILegacyAiAnalysisEngine
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
