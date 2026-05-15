using Microsoft.EntityFrameworkCore;
using QuantTrading.Api.Data;

namespace QuantTrading.Api.Services.AI;

public interface IAiOrchestratorConfigProvider
{
    Task<AiOrchestratorRuntimeConfig> GetAsync(CancellationToken cancellationToken = default);
}

public sealed class AiOrchestratorConfigProvider : IAiOrchestratorConfigProvider
{
    private readonly QuantTradingDbContext _dbContext;
    private readonly IConfiguration _configuration;

    public AiOrchestratorConfigProvider(QuantTradingDbContext dbContext, IConfiguration configuration)
    {
        _dbContext = dbContext;
        _configuration = configuration;
    }

    public async Task<AiOrchestratorRuntimeConfig> GetAsync(CancellationToken cancellationToken = default)
    {
        var configs = await _dbContext.SystemConfigs
            .Where(c => c.Category == "aiorchestrator")
            .ToListAsync(cancellationToken);

        var map = configs.ToDictionary(c => c.Key, c => c.Value, StringComparer.OrdinalIgnoreCase);

        var defaultMode = AiExecutionModes.Normalize(
            GetValue(map, "DefaultExecutionMode", _configuration["Ai:Orchestrator:DefaultMode"], AiExecutionModes.Legacy),
            AiExecutionModes.Legacy);

        var defaultToolPolicy = AiToolPolicies.Normalize(
            GetValue(map, "DefaultToolPolicy", _configuration["Ai:Orchestrator:DefaultToolPolicy"], AiToolPolicies.Auto),
            AiToolPolicies.Auto);

        var mcpToolExecutionEnabled = ParseBool(
            GetValue(map, "McpToolExecutionEnabled", _configuration["Ai:Orchestrator:McpToolExecutionEnabled"], "false"),
            false);

        var exposePublicTrace = ParseBool(
            GetValue(map, "ExposePublicTrace", _configuration["Ai:Trace:ExposePublicTrace"], "true"),
            true);

        var auditTraceEnabled = ParseBool(
            GetValue(map, "AuditTraceEnabled", _configuration["Ai:Trace:AuditTraceEnabled"], "false"),
            false);

        var shadowTimeoutMs = ParseInt(
            GetValue(map, "ShadowTimeoutMs", _configuration["Ai:Orchestrator:ShadowTimeoutMs"], "8000"),
            8000,
            min: 2000,
            max: 60000);

        var agentTimeoutMs = ParseInt(
            GetValue(map, "AgentTimeoutMs", _configuration["Ai:Orchestrator:AgentTimeoutMs"], "15000"),
            15000,
            min: 2000,
            max: 120000);

        var categoriesRaw = GetValue(
            map,
            "AllowedToolCategories",
            _configuration["Ai:Mcp:AllowedToolCategories"] ?? _configuration["Ai:Orchestrator:AllowedToolCategories"],
            "market,research,account-readonly");

        var categories = categoriesRaw
            .Split(new[] { ',', ';', '|', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(item => item.Trim())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (categories.Count == 0)
        {
            categories.Add("market");
            categories.Add("research");
            categories.Add("account-readonly");
        }

        return new AiOrchestratorRuntimeConfig
        {
            DefaultExecutionMode = defaultMode,
            DefaultToolPolicy = defaultToolPolicy,
            McpToolExecutionEnabled = mcpToolExecutionEnabled,
            ExposePublicTrace = exposePublicTrace,
            AuditTraceEnabled = auditTraceEnabled,
            ShadowTimeoutMs = shadowTimeoutMs,
            AgentTimeoutMs = agentTimeoutMs,
            AllowedToolCategories = categories
        };
    }

    private static string GetValue(IReadOnlyDictionary<string, string> map, string key, string? fallback, string defaultValue)
    {
        if (map.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
        {
            return value.Trim();
        }

        if (!string.IsNullOrWhiteSpace(fallback))
        {
            return fallback.Trim();
        }

        return defaultValue;
    }

    private static bool ParseBool(string? value, bool fallback)
    {
        return bool.TryParse(value, out var parsed) ? parsed : fallback;
    }

    private static int ParseInt(string? value, int fallback, int min, int max)
    {
        if (!int.TryParse(value, out var parsed))
        {
            return fallback;
        }

        if (parsed < min)
        {
            return min;
        }

        if (parsed > max)
        {
            return max;
        }

        return parsed;
    }
}
