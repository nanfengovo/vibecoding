namespace QuantTrading.Api.Services.AI;

public interface IAgentOrchestrator
{
    Task<AgentExecutionResult> ExecuteAsync(
        AiChatInput input,
        AiOrchestratorRuntimeConfig runtime,
        CancellationToken cancellationToken = default);
}

public sealed class AgentOrchestrator : IAgentOrchestrator
{
    private readonly ILegacyAiAnalysisEngine _legacy;
    private readonly IMcpToolProvider _mcpToolProvider;
    private readonly ISkillPromptComposer _skillPromptComposer;

    public AgentOrchestrator(
        ILegacyAiAnalysisEngine legacy,
        IMcpToolProvider mcpToolProvider,
        ISkillPromptComposer skillPromptComposer)
    {
        _legacy = legacy;
        _mcpToolProvider = mcpToolProvider;
        _skillPromptComposer = skillPromptComposer;
    }

    public async Task<AgentExecutionResult> ExecuteAsync(
        AiChatInput input,
        AiOrchestratorRuntimeConfig runtime,
        CancellationToken cancellationToken = default)
    {
        var toolPolicy = AiToolPolicies.Normalize(input.ToolPolicy, runtime.DefaultToolPolicy);
        var skillPrompt = _skillPromptComposer.Compose(input.SkillId, input);

        var toolCalls = new List<AgentToolCallResult>();
        if (!string.Equals(toolPolicy, AiToolPolicies.LocalOnly, StringComparison.OrdinalIgnoreCase))
        {
            var mcpCalls = await _mcpToolProvider.TryExecuteAsync(input, runtime, cancellationToken);
            if (mcpCalls.Count > 0)
            {
                toolCalls.AddRange(mcpCalls);
            }
        }

        var augmented = BuildAugmentedInput(input, skillPrompt, toolCalls);
        var result = await _legacy.ChatAsync(augmented, cancellationToken);

        return new AgentExecutionResult
        {
            Result = result,
            ToolCalls = toolCalls
        };
    }

    private static AiChatInput BuildAugmentedInput(
        AiChatInput input,
        string skillPrompt,
        IReadOnlyList<AgentToolCallResult> toolCalls)
    {
        var focusSegments = new List<string>();
        if (!string.IsNullOrWhiteSpace(input.Focus))
        {
            focusSegments.Add(input.Focus.Trim());
        }
        if (!string.IsNullOrWhiteSpace(skillPrompt))
        {
            focusSegments.Add(skillPrompt.Trim());
        }

        var conversationContext = input.ConversationContext.ToList();
        foreach (var call in toolCalls.Where(item => string.Equals(item.Status, "ok", StringComparison.OrdinalIgnoreCase)))
        {
            if (string.IsNullOrWhiteSpace(call.OutputSummary))
            {
                continue;
            }

            conversationContext.Add($"工具结果({call.ToolName})：{call.OutputSummary}");
        }

        return new AiChatInput
        {
            UserId = input.UserId,
            SessionId = input.SessionId,
            Question = input.Question,
            Symbol = input.Symbol,
            Focus = string.Join("\n", focusSegments.Where(item => !string.IsNullOrWhiteSpace(item))),
            SkillId = string.Empty,
            ProviderId = input.ProviderId,
            Model = input.Model,
            ExecutionMode = input.ExecutionMode,
            ToolPolicy = input.ToolPolicy,
            MemoryProfile = input.MemoryProfile,
            AllowToolCategories = input.AllowToolCategories,
            ConversationContext = conversationContext,
            MemoryContext = input.MemoryContext,
            KnowledgeContext = input.KnowledgeContext,
            ReaderContext = input.ReaderContext
        };
    }
}
