using System.Text.Json;

namespace QuantTrading.Api.Services.AI;

public static class AiExecutionModes
{
    public const string Legacy = "legacy";
    public const string Shadow = "shadow";
    public const string Fallback = "fallback";
    public const string Maf = "maf";
    public const string Hybrid = "hybrid";

    public static string Normalize(string? value, string fallback = Legacy)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            Shadow => Shadow,
            Fallback => Fallback,
            Maf => Maf,
            Hybrid => Fallback,
            Legacy => Legacy,
            _ => fallback
        };
    }
}

public static class AiToolPolicies
{
    public const string Auto = "auto";
    public const string McpFirst = "mcp_first";
    public const string LocalOnly = "local_only";

    public static string Normalize(string? value, string fallback = Auto)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            McpFirst => McpFirst,
            LocalOnly => LocalOnly,
            Auto => Auto,
            _ => fallback
        };
    }
}

public sealed class AiOrchestratorRuntimeConfig
{
    public string DefaultExecutionMode { get; init; } = AiExecutionModes.Legacy;
    public string DefaultToolPolicy { get; init; } = AiToolPolicies.Auto;
    public bool McpToolExecutionEnabled { get; init; }
    public bool ExposePublicTrace { get; init; }
    public bool AuditTraceEnabled { get; init; }
    public int ShadowTimeoutMs { get; init; } = 8000;
    public int AgentTimeoutMs { get; init; } = 15000;
    public IReadOnlySet<string> AllowedToolCategories { get; init; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "market",
            "research",
            "account-readonly"
        };
}

public sealed class AgentToolCallResult
{
    public string ToolName { get; init; } = string.Empty;
    public string Source { get; init; } = "mcp";
    public string Status { get; init; } = "ok";
    public int LatencyMs { get; init; }
    public string OutputSummary { get; init; } = string.Empty;
    public string ErrorCode { get; init; } = string.Empty;
    public JsonElement? SanitizedRequest { get; init; }
    public JsonElement? SanitizedResponse { get; init; }
}

public sealed class AgentExecutionResult
{
    public AiChatResult Result { get; init; } = new();
    public List<AgentToolCallResult> ToolCalls { get; init; } = new();
}

public sealed class AiToolTraceAuditPayload
{
    public int UserId { get; init; }
    public long? SessionId { get; init; }
    public string Orchestrator { get; init; } = string.Empty;
    public string ExecutionMode { get; init; } = string.Empty;
    public string ToolName { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public int LatencyMs { get; init; }
    public string RequestDigest { get; init; } = string.Empty;
    public string ResponseDigest { get; init; } = string.Empty;
    public string RequestPreview { get; init; } = string.Empty;
    public string ResponsePreview { get; init; } = string.Empty;
    public string ErrorCode { get; init; } = string.Empty;
}
