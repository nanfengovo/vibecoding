using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using QuantTrading.Api.Data;

namespace QuantTrading.Api.Services.AI;

public interface IMcpToolProvider
{
    Task<List<AgentToolCallResult>> TryExecuteAsync(
        AiChatInput input,
        AiOrchestratorRuntimeConfig runtime,
        CancellationToken cancellationToken = default);
}

public sealed class LongBridgeMcpToolProvider : IMcpToolProvider
{
    private static readonly Regex SymbolRegex = new(
        @"\b(?:[A-Z]{1,5}\.(?:US|HK|SG)|\d{5}\.HK|\d{6}\.(?:SH|SZ)|(?:SH|SZ)\d{6}|[A-Z]{1,5}|\d{5}|\d{6})\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly QuantTradingDbContext _dbContext;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<LongBridgeMcpToolProvider> _logger;

    public LongBridgeMcpToolProvider(
        QuantTradingDbContext dbContext,
        IHttpClientFactory httpClientFactory,
        ILogger<LongBridgeMcpToolProvider> logger)
    {
        _dbContext = dbContext;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<List<AgentToolCallResult>> TryExecuteAsync(
        AiChatInput input,
        AiOrchestratorRuntimeConfig runtime,
        CancellationToken cancellationToken = default)
    {
        if (!runtime.McpToolExecutionEnabled)
        {
            return [];
        }

        var configs = await _dbContext.SystemConfigs
            .Where(c => c.Category == "longbridge")
            .ToListAsync(cancellationToken);
        var map = configs.ToDictionary(c => c.Key, c => c.Value, StringComparer.OrdinalIgnoreCase);

        var mcpEnabled = ParseBool(GetValue(map, "McpEnabled", "false"), false);
        if (!mcpEnabled)
        {
            return [];
        }

        var serverUrl = GetValue(map, "McpServerUrl", "https://openapi.longbridge.com/mcp");
        if (string.IsNullOrWhiteSpace(serverUrl))
        {
            return [];
        }

        var token = GetValue(map, "McpAuthToken", string.Empty);
        var clientName = GetValue(map, "McpClientName", "QuantTrading");
        var symbol = ResolveSymbol(input);

        if (string.IsNullOrWhiteSpace(symbol))
        {
            return [];
        }

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var calls = new List<AgentToolCallResult>();

        try
        {
            using var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(15);

            var initializeRequest = new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "initialize",
                @params = new
                {
                    protocolVersion = "2025-03-26",
                    capabilities = new { },
                    clientInfo = new
                    {
                        name = string.IsNullOrWhiteSpace(clientName) ? "QuantTrading" : clientName.Trim(),
                        version = "1.0.0"
                    }
                }
            };

            await SendRpcAsync(client, serverUrl, token, initializeRequest, cancellationToken);

            var listRequest = new
            {
                jsonrpc = "2.0",
                id = 2,
                method = "tools/list",
                @params = new { }
            };

            var listJson = await SendRpcAsync(client, serverUrl, token, listRequest, cancellationToken);
            var toolCandidates = ResolveToolCandidates(listJson, runtime.AllowedToolCategories);

            foreach (var tool in toolCandidates.Take(2))
            {
                var startedAt = DateTime.UtcNow;
                var toolWatch = System.Diagnostics.Stopwatch.StartNew();

                try
                {
                    var arguments = BuildArgumentsForTool(tool, symbol);
                    var callRequest = new
                    {
                        jsonrpc = "2.0",
                        id = 100 + calls.Count,
                        method = "tools/call",
                        @params = new
                        {
                            name = tool.Name,
                            arguments
                        }
                    };

                    var callJson = await SendRpcAsync(client, serverUrl, token, callRequest, cancellationToken);
                    var summary = BuildResponseSummary(callJson);
                    calls.Add(new AgentToolCallResult
                    {
                        ToolName = tool.Name,
                        Source = "longbridge-mcp",
                        Status = "ok",
                        LatencyMs = (int)toolWatch.ElapsedMilliseconds,
                        OutputSummary = summary,
                        ErrorCode = string.Empty,
                        SanitizedRequest = ToJsonElement(new
                        {
                            method = "tools/call",
                            name = tool.Name,
                            arguments
                        }),
                        SanitizedResponse = ToJsonElement(new
                        {
                            tool = tool.Name,
                            preview = summary,
                            calledAtUtc = startedAt
                        })
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "MCP tool call failed: {Tool}", tool.Name);
                    calls.Add(new AgentToolCallResult
                    {
                        ToolName = tool.Name,
                        Source = "longbridge-mcp",
                        Status = "failed",
                        LatencyMs = (int)toolWatch.ElapsedMilliseconds,
                        OutputSummary = string.Empty,
                        ErrorCode = "tool_call_failed",
                        SanitizedRequest = ToJsonElement(new
                        {
                            method = "tools/call",
                            name = tool.Name,
                            symbol
                        }),
                        SanitizedResponse = ToJsonElement(new
                        {
                            error = "tool_call_failed"
                        })
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MCP bootstrap failed");
            calls.Add(new AgentToolCallResult
            {
                ToolName = "tools/list",
                Source = "longbridge-mcp",
                Status = "failed",
                LatencyMs = (int)stopwatch.ElapsedMilliseconds,
                OutputSummary = string.Empty,
                ErrorCode = "mcp_unavailable",
                SanitizedRequest = ToJsonElement(new
                {
                    method = "tools/list",
                    endpoint = "redacted"
                }),
                SanitizedResponse = ToJsonElement(new
                {
                    error = "mcp_unavailable"
                })
            });
        }

        return calls;
    }

    private static string ResolveSymbol(AiChatInput input)
    {
        var direct = NormalizeSymbol(input.Symbol);
        if (!string.IsNullOrWhiteSpace(direct))
        {
            return direct;
        }

        var question = (input.Question ?? string.Empty).ToUpperInvariant();
        foreach (Match match in SymbolRegex.Matches(question))
        {
            var candidate = NormalizeSymbol(match.Value);
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                return candidate;
            }
        }

        return string.Empty;
    }

    private static string NormalizeSymbol(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        if (normalized.Contains('.'))
        {
            return normalized;
        }

        if (normalized.All(char.IsDigit) && normalized.Length == 6)
        {
            var suffix = normalized.StartsWith("6", StringComparison.Ordinal) ? "SH" : "SZ";
            return $"{normalized}.{suffix}";
        }

        if (normalized.All(char.IsDigit) && normalized.Length == 5)
        {
            return $"{normalized}.HK";
        }

        return $"{normalized}.US";
    }

    private static List<McpToolCandidate> ResolveToolCandidates(string toolsListJson, IReadOnlySet<string> allowedCategories)
    {
        var candidates = new List<McpToolCandidate>();
        if (string.IsNullOrWhiteSpace(toolsListJson))
        {
            return candidates;
        }

        try
        {
            using var doc = JsonDocument.Parse(toolsListJson);
            var root = doc.RootElement;
            if (!root.TryGetProperty("result", out var result)
                || !result.TryGetProperty("tools", out var tools)
                || tools.ValueKind != JsonValueKind.Array)
            {
                return candidates;
            }

            foreach (var item in tools.EnumerateArray())
            {
                var name = item.TryGetProperty("name", out var nameProp)
                    ? (nameProp.GetString() ?? string.Empty)
                    : string.Empty;
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                if (!IsReadOnlyTool(name))
                {
                    continue;
                }

                var category = InferCategory(name);
                if (!allowedCategories.Contains(category))
                {
                    continue;
                }

                var schema = item.TryGetProperty("inputSchema", out var schemaProp)
                    ? schemaProp.GetRawText()
                    : string.Empty;

                candidates.Add(new McpToolCandidate
                {
                    Name = name,
                    Category = category,
                    InputSchemaJson = schema
                });
            }

            return candidates
                .OrderBy(candidate => ScoreTool(candidate.Name))
                .ThenBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return candidates;
        }
    }

    private static int ScoreTool(string name)
    {
        var lowered = name.ToLowerInvariant();
        if (lowered.Contains("realtime") || lowered.Contains("quote") || lowered.Contains("option_quote"))
        {
            return 0;
        }

        if (lowered.Contains("candlestick") || lowered.Contains("kline") || lowered.Contains("history"))
        {
            return 1;
        }

        return 10;
    }

    private static string InferCategory(string toolName)
    {
        var lowered = toolName.ToLowerInvariant();
        if (lowered.Contains("account") || lowered.Contains("position") || lowered.Contains("balance") || lowered.Contains("cash"))
        {
            return "account-readonly";
        }

        if (lowered.Contains("profile") || lowered.Contains("valuation") || lowered.Contains("dividend") || lowered.Contains("research"))
        {
            return "research";
        }

        return "market";
    }

    private static bool IsReadOnlyTool(string name)
    {
        var lowered = name.ToLowerInvariant();
        var writeHints = new[]
        {
            "place_order", "replace_order", "cancel_order", "submit", "create", "delete", "update", "order_submit"
        };

        return !writeHints.Any(lowered.Contains);
    }

    private static Dictionary<string, object> BuildArgumentsForTool(McpToolCandidate candidate, string symbol)
    {
        var arguments = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        var lowered = candidate.Name.ToLowerInvariant();

        if (lowered.Contains("quote") || lowered.Contains("snapshot") || lowered.Contains("security") || lowered.Contains("kline") || lowered.Contains("candlestick"))
        {
            arguments["symbol"] = symbol;
            arguments["symbols"] = new[] { symbol };
        }

        if (lowered.Contains("candlestick") || lowered.Contains("kline"))
        {
            arguments["period"] = "day";
            arguments["count"] = 20;
        }

        return arguments;
    }

    private async Task<string> SendRpcAsync(
        HttpClient client,
        string serverUrl,
        string token,
        object payload,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, serverUrl.Trim());
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.Trim());
        }

        var content = JsonSerializer.Serialize(payload);
        request.Content = new StringContent(content, Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"MCP call failed: {(int)response.StatusCode}");
        }

        return body;
    }

    private static string BuildResponseSummary(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return string.Empty;
        }

        var compact = json.Replace("\r", " ").Replace("\n", " ").Trim();
        return compact.Length <= 280 ? compact : compact[..280];
    }

    private static JsonElement? ToJsonElement(object payload)
    {
        var json = JsonSerializer.Serialize(payload);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static string GetValue(IReadOnlyDictionary<string, string> map, string key, string fallback)
    {
        return map.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : fallback;
    }

    private static bool ParseBool(string? value, bool fallback)
    {
        return bool.TryParse(value, out var parsed) ? parsed : fallback;
    }

    private sealed class McpToolCandidate
    {
        public string Name { get; init; } = string.Empty;
        public string Category { get; init; } = "market";
        public string InputSchemaJson { get; init; } = string.Empty;
    }
}
