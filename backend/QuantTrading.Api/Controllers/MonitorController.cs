using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using System.Text.Json;
using QuantTrading.Api.Models;
using QuantTrading.Api.Services.Monitor;

namespace QuantTrading.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MonitorController : ControllerBase
{
    private readonly IMonitorService _monitorService;
    private readonly IWatchlistService _watchlistService;

    public MonitorController(IMonitorService monitorService, IWatchlistService watchlistService)
    {
        _monitorService = monitorService;
        _watchlistService = watchlistService;
    }

    [HttpGet("rules")]
    public async Task<ActionResult<List<MonitorRuleDto>>> GetRules()
    {
        var rules = await _monitorService.GetAllRulesAsync();
        return Ok(rules.Select(ToDto).ToList());
    }

    [HttpGet("rules/{id}")]
    public async Task<ActionResult<MonitorRuleDto>> GetRule(int id)
    {
        var rule = await _monitorService.GetRuleByIdAsync(id);
        if (rule == null)
        {
            return NotFound();
        }

        return Ok(ToDto(rule));
    }

    [HttpPost("rules")]
    public async Task<ActionResult<MonitorRuleDto>> CreateRule([FromBody] MonitorRuleUpsertRequest request)
    {
        var entity = new MonitorRule();
        ApplyRequestToEntity(entity, request);
        var created = await _monitorService.CreateRuleAsync(entity);
        return CreatedAtAction(nameof(GetRule), new { id = created.Id }, ToDto(created));
    }

    [HttpPut("rules/{id}")]
    public async Task<ActionResult<MonitorRuleDto>> UpdateRule(int id, [FromBody] MonitorRuleUpsertRequest request)
    {
        var existing = await _monitorService.GetRuleByIdAsync(id);
        if (existing == null)
        {
            return NotFound();
        }

        ApplyRequestToEntity(existing, request);
        var updated = await _monitorService.UpdateRuleAsync(id, existing);
        if (updated == null)
        {
            return NotFound();
        }

        return Ok(ToDto(updated));
    }

    [HttpDelete("rules/{id}")]
    public async Task<ActionResult> DeleteRule(int id)
    {
        var result = await _monitorService.DeleteRuleAsync(id);
        if (!result)
        {
            return NotFound();
        }

        return NoContent();
    }

    [HttpPost("rules/{id}/toggle")]
    public async Task<ActionResult> ToggleRule(int id)
    {
        var result = await _monitorService.ToggleRuleAsync(id);
        if (!result)
        {
            return NotFound();
        }

        return Ok();
    }

    // Legacy watchlist routes kept for frontend compatibility.
    [HttpGet("watchlist")]
    public async Task<ActionResult<List<Stock>>> GetWatchlist()
    {
        var stocks = await _watchlistService.GetWatchlistAsync();
        return Ok(stocks);
    }

    [HttpPost("watchlist")]
    public async Task<ActionResult<Stock>> AddToWatchlist([FromBody] MonitorWatchlistRequest request)
    {
        var stock = await _watchlistService.AddToWatchlistAsync(request.Symbol, request.Notes);
        if (stock == null)
        {
            return NotFound("Stock not found");
        }

        return Ok(stock);
    }

    [HttpDelete("watchlist/{id}")]
    public async Task<ActionResult> RemoveFromWatchlist(int id)
    {
        var result = await _watchlistService.RemoveFromWatchlistAsync(id);
        if (!result)
        {
            return NotFound();
        }

        return NoContent();
    }

    [HttpGet("alerts")]
    public async Task<ActionResult<List<MonitorAlert>>> GetAlerts(
        [FromQuery] int? ruleId,
        [FromQuery] bool? unreadOnly,
        [FromQuery] int limit = 50)
    {
        var alerts = await _monitorService.GetAlertsAsync(ruleId, unreadOnly, limit);
        return Ok(alerts);
    }

    [HttpPost("alerts/{id}/read")]
    public async Task<ActionResult> MarkAlertRead(long id)
    {
        var result = await _monitorService.MarkAlertReadAsync(id);
        if (!result)
        {
            return NotFound();
        }

        return Ok();
    }

    [HttpPost("alerts/read-all")]
    public async Task<ActionResult> MarkAllAlertsRead()
    {
        await _monitorService.MarkAllAlertsReadAsync();
        return Ok();
    }

    private static MonitorRuleDto ToDto(MonitorRule rule)
    {
        var notifications = DeserializeList<NotificationChannelDto>(rule.NotifyChannelsJson);
        if (notifications.Count == 0)
        {
            notifications = DeserializeList<string>(rule.NotifyChannelsJson)
                .Select(t => new NotificationChannelDto { Type = t, Enabled = true })
                .ToList();
        }

        return new MonitorRuleDto
        {
            Id = rule.Id,
            Name = rule.Name,
            Description = rule.Description,
            IsActive = rule.IsEnabled,
            IsEnabled = rule.IsEnabled,
            Symbols = DeserializeList<string>(rule.SymbolsJson),
            Conditions = DeserializeJsonElements(rule.ConditionsJson),
            Notifications = notifications,
            CheckInterval = rule.CheckIntervalSeconds,
            CheckIntervalSeconds = rule.CheckIntervalSeconds,
            LastCheckedAt = rule.LastCheckedAt,
            LastTriggeredAt = rule.LastTriggeredAt,
            CreatedAt = rule.CreatedAt,
            UpdatedAt = rule.UpdatedAt
        };
    }

    private static void ApplyRequestToEntity(MonitorRule target, MonitorRuleUpsertRequest request)
    {
        target.Name = request.Name?.Trim() ?? target.Name;
        target.Description = request.Description?.Trim() ?? string.Empty;
        target.IsEnabled = request.IsActive ?? request.IsEnabled ?? target.IsEnabled;

        var symbols = request.Symbols ?? DeserializeList<string>(request.SymbolsJson ?? "[]");
        target.SymbolsJson = JsonConvert.SerializeObject(symbols.Distinct(StringComparer.OrdinalIgnoreCase).ToList());

        if (request.Conditions is { Count: > 0 })
        {
            target.ConditionsJson = System.Text.Json.JsonSerializer.Serialize(request.Conditions);
        }
        else if (!string.IsNullOrWhiteSpace(request.ConditionsJson))
        {
            target.ConditionsJson = request.ConditionsJson;
        }
        else
        {
            target.ConditionsJson = "[]";
        }

        var notifyChannels = ExtractNotifyChannels(request);
        target.NotifyChannelsJson = JsonConvert.SerializeObject(notifyChannels);

        target.CheckIntervalSeconds = request.CheckInterval
            ?? request.CheckIntervalSeconds
            ?? target.CheckIntervalSeconds;

        target.UpdatedAt = DateTime.UtcNow;
    }

    private static List<string> ExtractNotifyChannels(MonitorRuleUpsertRequest request)
    {
        if (request.NotifyChannels is { Count: > 0 })
        {
            return request.NotifyChannels
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        if (request.Notifications is { Count: > 0 })
        {
            return request.Notifications
                .Where(n => n.Enabled && !string.IsNullOrWhiteSpace(n.Type))
                .Select(n => n.Type.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        if (!string.IsNullOrWhiteSpace(request.NotifyChannelsJson))
        {
            return DeserializeList<string>(request.NotifyChannelsJson)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return new List<string>();
    }

    private static List<T> DeserializeList<T>(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<T>();
        }

        try
        {
            return JsonConvert.DeserializeObject<List<T>>(json) ?? new List<T>();
        }
        catch
        {
            return new List<T>();
        }
    }

    private static List<JsonElement> DeserializeJsonElements(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<JsonElement>();
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return new List<JsonElement>();
            }

            return document.RootElement
                .EnumerateArray()
                .Where(element =>
                {
                    if (element.ValueKind != JsonValueKind.Object)
                    {
                        return true;
                    }

                    var hasType = element.TryGetProperty("type", out _);
                    var hasLegacyValueKind = element.TryGetProperty("ValueKind", out _);
                    return hasType || !hasLegacyValueKind;
                })
                .Select(element => element.Clone())
                .ToList();
        }
        catch
        {
            return new List<JsonElement>();
        }
    }
}

public sealed class MonitorWatchlistRequest
{
    public string Symbol { get; set; } = string.Empty;
    public string? Notes { get; set; }
}

public sealed class MonitorRuleUpsertRequest
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public bool? IsActive { get; set; }
    public bool? IsEnabled { get; set; }
    public List<string>? Symbols { get; set; }
    public string? SymbolsJson { get; set; }
    public List<JsonElement>? Conditions { get; set; }
    public string? ConditionsJson { get; set; }
    public List<NotificationChannelDto>? Notifications { get; set; }
    public List<string>? NotifyChannels { get; set; }
    public string? NotifyChannelsJson { get; set; }
    public int? CheckInterval { get; set; }
    public int? CheckIntervalSeconds { get; set; }
}

public sealed class NotificationChannelDto
{
    public string Type { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
}

public sealed class MonitorRuleDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool IsEnabled { get; set; }
    public List<string> Symbols { get; set; } = new();
    public List<JsonElement> Conditions { get; set; } = new();
    public List<NotificationChannelDto> Notifications { get; set; } = new();
    public int CheckInterval { get; set; } = 60;
    public int CheckIntervalSeconds { get; set; } = 60;
    public DateTime? LastCheckedAt { get; set; }
    public DateTime? LastTriggeredAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
