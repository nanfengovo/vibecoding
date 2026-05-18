namespace QuantTrading.Api.Services.AI;

public sealed class AgentGatewayAiAnalysisService : IAiAnalysisService
{
    private readonly ILegacyAiAnalysisEngine _legacy;
    private readonly IAgentOrchestrator _agentOrchestrator;
    private readonly IAiOrchestratorConfigProvider _configProvider;
    private readonly IAiToolTraceAuditWriter _auditWriter;
    private readonly ILogger<AgentGatewayAiAnalysisService> _logger;

    public AgentGatewayAiAnalysisService(
        ILegacyAiAnalysisEngine legacy,
        IAgentOrchestrator agentOrchestrator,
        IAiOrchestratorConfigProvider configProvider,
        IAiToolTraceAuditWriter auditWriter,
        ILogger<AgentGatewayAiAnalysisService> logger)
    {
        _legacy = legacy;
        _agentOrchestrator = agentOrchestrator;
        _configProvider = configProvider;
        _auditWriter = auditWriter;
        _logger = logger;
    }

    public Task<AiConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        return _legacy.TestConnectionAsync(cancellationToken);
    }

    public Task<StockAnalysisResult> AnalyzeStockAsync(StockAnalysisInput input, CancellationToken cancellationToken = default)
    {
        return _legacy.AnalyzeStockAsync(input, cancellationToken);
    }

    public Task<AiPromptOptimizeResult> OptimizePromptAsync(AiPromptOptimizeInput input, CancellationToken cancellationToken = default)
    {
        return _legacy.OptimizePromptAsync(input, cancellationToken);
    }

    public Task<AiModelsResult> GetModelsAsync(AiModelsInput input, CancellationToken cancellationToken = default)
    {
        return _legacy.GetModelsAsync(input, cancellationToken);
    }

    public async Task<AiChatResult> ChatAsync(AiChatInput input, CancellationToken cancellationToken = default)
    {
        var runtime = await _configProvider.GetAsync(cancellationToken);
        var executionMode = AiExecutionModes.Normalize(input.ExecutionMode, runtime.DefaultExecutionMode);

        switch (executionMode)
        {
            case AiExecutionModes.Shadow:
                return await ExecuteShadowAsync(input, runtime, cancellationToken);
            case AiExecutionModes.Fallback:
                return await ExecuteFallbackAsync(input, runtime, cancellationToken);
            case AiExecutionModes.Maf:
                return await ExecuteMafAsync(input, runtime, cancellationToken);
            default:
                return await ExecuteLegacyAsync(input, runtime, cancellationToken);
        }
    }

    private async Task<AiChatResult> ExecuteLegacyAsync(
        AiChatInput input,
        AiOrchestratorRuntimeConfig runtime,
        CancellationToken cancellationToken)
    {
        var legacy = await _legacy.ChatAsync(input, cancellationToken);
        return MergeResultMetadata(legacy, "legacy", fallbackApplied: false, shadowCompared: false, runtime, []);
    }

    private async Task<AiChatResult> ExecuteShadowAsync(
        AiChatInput input,
        AiOrchestratorRuntimeConfig runtime,
        CancellationToken cancellationToken)
    {
        var legacyTask = _legacy.ChatAsync(input, cancellationToken);

        var toolCalls = new List<AgentToolCallResult>();
        var compared = false;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromMilliseconds(runtime.ShadowTimeoutMs));
            var shadow = await _agentOrchestrator.ExecuteAsync(input, runtime, cts.Token);
            compared = true;
            toolCalls = shadow.ToolCalls;
            await TryWriteAuditAsync(input, runtime, AiExecutionModes.Shadow, "agent", toolCalls, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogInformation(ex, "Shadow mode comparison failed and was ignored.");
        }

        var legacy = await legacyTask;
        return MergeResultMetadata(legacy, "shadow", fallbackApplied: false, shadowCompared: compared, runtime, toolCalls);
    }

    private async Task<AiChatResult> ExecuteFallbackAsync(
        AiChatInput input,
        AiOrchestratorRuntimeConfig runtime,
        CancellationToken cancellationToken)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromMilliseconds(runtime.AgentTimeoutMs));
            var agent = await _agentOrchestrator.ExecuteAsync(input, runtime, cts.Token);
            await TryWriteAuditAsync(input, runtime, AiExecutionModes.Fallback, "agent", agent.ToolCalls, cancellationToken);
            return MergeResultMetadata(agent.Result, "agent", fallbackApplied: false, shadowCompared: false, runtime, agent.ToolCalls);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Fallback mode agent path failed, switching to legacy.");
            var legacy = await _legacy.ChatAsync(input, cancellationToken);
            return MergeResultMetadata(legacy, "fallback", fallbackApplied: true, shadowCompared: false, runtime, []);
        }
    }

    private async Task<AiChatResult> ExecuteMafAsync(
        AiChatInput input,
        AiOrchestratorRuntimeConfig runtime,
        CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromMilliseconds(runtime.AgentTimeoutMs));
        var agent = await _agentOrchestrator.ExecuteAsync(input, runtime, cts.Token);
        await TryWriteAuditAsync(input, runtime, AiExecutionModes.Maf, "agent", agent.ToolCalls, cancellationToken);
        return MergeResultMetadata(agent.Result, "agent", fallbackApplied: false, shadowCompared: false, runtime, agent.ToolCalls);
    }

    private async Task TryWriteAuditAsync(
        AiChatInput input,
        AiOrchestratorRuntimeConfig runtime,
        string mode,
        string orchestrator,
        IReadOnlyList<AgentToolCallResult> calls,
        CancellationToken cancellationToken)
    {
        if (!runtime.AuditTraceEnabled || calls.Count == 0)
        {
            return;
        }

        var payloads = calls
            .Select(call => AiToolTraceAuditWriter.BuildPayload(input.UserId, input.SessionId, orchestrator, mode, call))
            .ToList();

        await _auditWriter.WriteAsync(payloads, cancellationToken);
    }

    private static AiChatResult MergeResultMetadata(
        AiChatResult origin,
        string orchestrator,
        bool fallbackApplied,
        bool shadowCompared,
        AiOrchestratorRuntimeConfig runtime,
        IReadOnlyList<AgentToolCallResult> toolCalls)
    {
        var publicTrace = runtime.ExposePublicTrace
            ? toolCalls.Select(item => new AiToolTracePublicItem
                {
                    ToolName = item.ToolName,
                    Source = item.Source,
                    Status = item.Status,
                    LatencyMs = item.LatencyMs
                })
                .ToList()
            : new List<AiToolTracePublicItem>();

        return new AiChatResult
        {
            Model = origin.Model,
            Content = origin.Content,
            GeneratedAt = origin.GeneratedAt,
            MarketContext = origin.MarketContext,
            SessionId = origin.SessionId,
            References = origin.References,
            Orchestrator = orchestrator,
            FallbackApplied = fallbackApplied,
            ShadowCompared = shadowCompared,
            ToolTracePublic = publicTrace
        };
    }
}
