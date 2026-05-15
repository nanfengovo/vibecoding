using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using QuantTrading.Api.Data;
using QuantTrading.Api.Models;

namespace QuantTrading.Api.Services.AI;

public interface IAiToolTraceAuditWriter
{
    Task WriteAsync(IEnumerable<AiToolTraceAuditPayload> payloads, CancellationToken cancellationToken = default);
}

public sealed class AiToolTraceAuditWriter : IAiToolTraceAuditWriter
{
    private readonly QuantTradingDbContext _dbContext;

    public AiToolTraceAuditWriter(QuantTradingDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task WriteAsync(IEnumerable<AiToolTraceAuditPayload> payloads, CancellationToken cancellationToken = default)
    {
        var rows = payloads
            .Where(item => !string.IsNullOrWhiteSpace(item.ToolName))
            .Select(item => new AiToolTraceAuditRecord
            {
                UserId = item.UserId,
                SessionId = item.SessionId,
                Orchestrator = Trim(item.Orchestrator, 40),
                ExecutionMode = Trim(item.ExecutionMode, 20),
                ToolName = Trim(item.ToolName, 120),
                Source = Trim(item.Source, 60),
                Status = Trim(item.Status, 20),
                LatencyMs = item.LatencyMs,
                RequestDigest = Trim(item.RequestDigest, 128),
                ResponseDigest = Trim(item.ResponseDigest, 128),
                RequestPreview = Trim(item.RequestPreview, 600),
                ResponsePreview = Trim(item.ResponsePreview, 600),
                ErrorCode = Trim(item.ErrorCode, 80),
                CreatedAt = DateTime.UtcNow
            })
            .ToList();

        if (rows.Count == 0)
        {
            return;
        }

        _dbContext.Set<AiToolTraceAuditRecord>().AddRange(rows);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public static AiToolTraceAuditPayload BuildPayload(
        int userId,
        long? sessionId,
        string orchestrator,
        string mode,
        AgentToolCallResult call)
    {
        var requestText = call.SanitizedRequest?.GetRawText() ?? string.Empty;
        var responseText = call.SanitizedResponse?.GetRawText() ?? string.Empty;

        return new AiToolTraceAuditPayload
        {
            UserId = userId,
            SessionId = sessionId,
            Orchestrator = orchestrator,
            ExecutionMode = mode,
            ToolName = call.ToolName,
            Source = call.Source,
            Status = call.Status,
            LatencyMs = call.LatencyMs,
            RequestDigest = ComputeSha256(requestText),
            ResponseDigest = ComputeSha256(responseText),
            RequestPreview = BuildPreview(requestText),
            ResponsePreview = BuildPreview(responseText),
            ErrorCode = call.ErrorCode
        };
    }

    private static string BuildPreview(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return string.Empty;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var safe = RedactElement(doc.RootElement);
            var compact = JsonSerializer.Serialize(safe);
            compact = compact.Replace("\r", " ").Replace("\n", " ");
            return compact.Length <= 560 ? compact : compact[..560];
        }
        catch
        {
            var compact = json.Replace("\r", " ").Replace("\n", " ");
            return compact.Length <= 560 ? compact : compact[..560];
        }
    }

    private static object? RedactElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => RedactObject(element),
            JsonValueKind.Array => element.EnumerateArray().Select(RedactElement).ToList(),
            JsonValueKind.String => RedactString(element.GetString() ?? string.Empty),
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static Dictionary<string, object?> RedactObject(JsonElement element)
    {
        var map = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in element.EnumerateObject())
        {
            if (ShouldRedactKey(property.Name))
            {
                map[property.Name] = "[redacted]";
                continue;
            }

            map[property.Name] = RedactElement(property.Value);
        }

        return map;
    }

    private static bool ShouldRedactKey(string key)
    {
        var lowered = key.ToLowerInvariant();
        return lowered.Contains("token")
            || lowered.Contains("authorization")
            || lowered.Contains("apikey")
            || lowered.Contains("api_key")
            || lowered.Contains("endpoint")
            || lowered.Contains("account")
            || lowered.Contains("raw")
            || lowered.Contains("response");
    }

    private static string RedactString(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return "[redacted-url]";
        }

        if (value.Length > 120)
        {
            return value[..120];
        }

        return value;
    }

    private static string ComputeSha256(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string Trim(string value, int max)
    {
        var clean = (value ?? string.Empty).Trim();
        return clean.Length <= max ? clean : clean[..max];
    }
}
