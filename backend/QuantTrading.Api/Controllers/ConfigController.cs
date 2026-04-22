using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using QuantTrading.Api.Data;
using QuantTrading.Api.Models;
using QuantTrading.Api.Services.AI;
using QuantTrading.Api.Services.LongBridge;
using QuantTrading.Api.Services.Notification;

namespace QuantTrading.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ConfigController : ControllerBase
{
    private const string MaskedValue = "******";

    private readonly QuantTradingDbContext _dbContext;
    private readonly INotificationService _notificationService;
    private readonly ILongBridgeService _longBridgeService;
    private readonly IAiAnalysisService _aiAnalysisService;
    private readonly IConfiguration _configuration;

    public ConfigController(
        QuantTradingDbContext dbContext,
        INotificationService notificationService,
        ILongBridgeService longBridgeService,
        IAiAnalysisService aiAnalysisService,
        IConfiguration configuration)
    {
        _dbContext = dbContext;
        _notificationService = notificationService;
        _longBridgeService = longBridgeService;
        _aiAnalysisService = aiAnalysisService;
        _configuration = configuration;
    }

    [HttpGet]
    public async Task<ActionResult<SystemConfigResponse>> GetAll()
    {
        var configs = await LoadConfigMapAsync();
        var proxySnapshot = BuildProxySnapshot(configs);
        var openAiProviders = GetOpenAiProviders(configs, maskApiKeys: true);
        var activeOpenAiProviderId = GetActiveOpenAiProviderId(configs, openAiProviders);
        var activeOpenAiProvider = openAiProviders
            .FirstOrDefault(item => item.Id == activeOpenAiProviderId)
            ?? openAiProviders.First();

        var response = new SystemConfigResponse
        {
            LongBridge = new LongBridgeConfigResponse
            {
                AppKey = GetString(configs, "longbridge", "AppKey", _configuration["LongBridge:AppKey"]),
                AppSecret = GetSecret(configs, "longbridge", "AppSecret", _configuration["LongBridge:AppSecret"]),
                AccessToken = GetSecret(configs, "longbridge", "AccessToken", _configuration["LongBridge:AccessToken"]),
                BaseUrl = GetString(configs, "longbridge", "BaseUrl", _configuration["LongBridge:BaseUrl"] ?? "https://openapi.longportapp.com")
            },
            Proxy = new ProxyConfigResponse
            {
                Enabled = proxySnapshot.Enabled,
                Host = proxySnapshot.Host,
                Port = proxySnapshot.Port,
                Username = proxySnapshot.Username,
                Password = string.IsNullOrEmpty(proxySnapshot.Password) ? string.Empty : MaskedValue
            },
            Email = new EmailConfigResponse
            {
                Enabled = GetBool(configs, "email", "Enabled"),
                SmtpHost = GetString(configs, "email", "SmtpServer", _configuration["Email:SmtpServer"] ?? "smtp.gmail.com"),
                SmtpPort = GetInt(configs, "email", "SmtpPort", ParseInt(_configuration["Email:SmtpPort"], 587)),
                Username = GetString(configs, "email", "SmtpUsername", _configuration["Email:Username"]),
                Password = GetSecret(configs, "email", "SmtpPassword", _configuration["Email:Password"]),
                FromAddress = GetString(configs, "email", "FromAddress", _configuration["Email:FromAddress"]),
                ToAddresses = ParseList(GetString(configs, "email", "Recipients")),
                UseSsl = GetBool(configs, "email", "UseSsl", ParseBool(_configuration["Email:UseSsl"], true))
            },
            Feishu = new FeishuConfigResponse
            {
                Enabled = GetBool(configs, "feishu", "Enabled"),
                WebhookUrl = GetString(configs, "feishu", "WebhookUrl", _configuration["Feishu:WebhookUrl"]),
                SignSecret = GetSecret(configs, "feishu", "Secret", _configuration["Feishu:Secret"])
            },
            Wechat = new WechatConfigResponse
            {
                Enabled = GetBool(configs, "wechat", "Enabled"),
                WebhookUrl = GetString(configs, "wechat", "WebhookUrl", _configuration["Wechat:WebhookUrl"])
            },
            OpenAi = new OpenAiConfigResponse
            {
                Enabled = GetBool(configs, "openai", "Enabled", ParseBool(_configuration["OpenAI:Enabled"])),
                ApiKey = activeOpenAiProvider.ApiKey,
                BaseUrl = activeOpenAiProvider.BaseUrl,
                Model = activeOpenAiProvider.Model,
                ActiveProviderId = activeOpenAiProviderId,
                Providers = openAiProviders
            }
        };

        return Ok(response);
    }

    [HttpGet("{category}")]
    public async Task<ActionResult<Dictionary<string, string>>> GetByCategory(string category)
    {
        var configs = await _dbContext.SystemConfigs
            .Where(c => c.Category == category)
            .ToDictionaryAsync(
                c => c.Key,
                c => ShouldMaskValue(c.Category, c.Key, c.IsEncrypted) && !string.IsNullOrEmpty(c.Value)
                    ? MaskedValue
                    : c.Value);

        return Ok(configs);
    }

    [HttpPut]
    public async Task<ActionResult> Update([FromBody] UpdateSystemConfigRequest request)
    {
        if (request.LongBridge is not null)
        {
            await UpdateLongBridgeAsync(request.LongBridge);
        }

        if (request.Proxy is not null)
        {
            await UpdateProxyAsync(request.Proxy);
        }

        if (request.Email is not null)
        {
            await UpdateEmailAsync(request.Email);
        }

        if (request.Feishu is not null)
        {
            await UpdateFeishuAsync(request.Feishu);
        }

        if (request.Wechat is not null)
        {
            await UpdateWechatAsync(request.Wechat);
        }

        if (request.OpenAi is not null)
        {
            await UpdateOpenAiAsync(request.OpenAi);
        }

        await _dbContext.SaveChangesAsync();
        return Ok();
    }

    [HttpPut("{category}")]
    public async Task<ActionResult> UpdateCategory(string category, [FromBody] Dictionary<string, string> values)
    {
        foreach (var (key, value) in values)
        {
            await UpsertConfigAsync(category, key, value, BuildDescription(category, key));
        }

        await _dbContext.SaveChangesAsync();
        return Ok();
    }

    [HttpPost("test/{channel}")]
    public async Task<ActionResult> TestNotification(string channel)
    {
        if (channel.Equals("longbridge", StringComparison.OrdinalIgnoreCase))
        {
            var longBridgeResult = await _longBridgeService.TestConnectionAsync();
            if (longBridgeResult.Success)
            {
                return Ok(new { success = true, message = longBridgeResult.Message });
            }

            return BadRequest(new { success = false, message = longBridgeResult.Message });
        }

        if (channel.Equals("openai", StringComparison.OrdinalIgnoreCase))
        {
            var openAiResult = await _aiAnalysisService.TestConnectionAsync();
            if (openAiResult.Success)
            {
                return Ok(new { success = true, message = openAiResult.Message });
            }

            return BadRequest(new { success = false, message = openAiResult.Message });
        }

        var result = await _notificationService.TestAsync(channel);
        if (result)
        {
            return Ok(new { success = true, message = "Test notification sent" });
        }

        return BadRequest(new { success = false, message = "Failed to send test notification" });
    }

    [HttpGet("notifications/logs")]
    public async Task<ActionResult<List<NotificationLog>>> GetNotificationLogs([FromQuery] int limit = 50)
    {
        var logs = await _dbContext.NotificationLogs
            .OrderByDescending(l => l.SentAt)
            .Take(limit)
            .ToListAsync();

        return Ok(logs);
    }

    private async Task UpdateLongBridgeAsync(LongBridgeUpdateRequest config)
    {
        await UpsertConfigAsync("longbridge", "AppKey", config.AppKey, "Long Bridge App Key");
        await UpsertConfigAsync("longbridge", "AppSecret", config.AppSecret, "Long Bridge App Secret");
        await UpsertConfigAsync("longbridge", "AccessToken", config.AccessToken, "Long Bridge Access Token");
        await UpsertConfigAsync("longbridge", "BaseUrl", NormalizeLongBridgeBaseUrl(config.BaseUrl), "Long Bridge API Base Url");
    }

    private async Task UpdateProxyAsync(ProxyUpdateRequest config)
    {
        await UpsertConfigAsync("proxy", "Enabled", FormatBool(config.Enabled), "Enable Proxy");
        await UpsertConfigAsync("proxy", "Host", config.Host, "Proxy Host");
        await UpsertConfigAsync("proxy", "Port", config.Port?.ToString(), "Proxy Port");
        await UpsertConfigAsync("proxy", "Username", config.Username, "Proxy Username");
        await UpsertConfigAsync("proxy", "Password", config.Password, "Proxy Password");
    }

    private async Task UpdateEmailAsync(EmailUpdateRequest config)
    {
        await UpsertConfigAsync("email", "Enabled", FormatBool(config.Enabled), "Enable Email");
        await UpsertConfigAsync("email", "SmtpServer", config.SmtpHost, "SMTP Server");
        await UpsertConfigAsync("email", "SmtpPort", config.SmtpPort?.ToString(), "SMTP Port");
        await UpsertConfigAsync("email", "SmtpUsername", config.Username, "SMTP Username");
        await UpsertConfigAsync("email", "SmtpPassword", config.Password, "SMTP Password");
        await UpsertConfigAsync("email", "FromAddress", config.FromAddress, "From Email Address");
        await UpsertConfigAsync("email", "Recipients", config.ToAddresses is null ? null : string.Join(",", config.ToAddresses), "Notification Recipients");
        await UpsertConfigAsync("email", "UseSsl", FormatBool(config.UseSsl), "Use SSL");
    }

    private async Task UpdateFeishuAsync(FeishuUpdateRequest config)
    {
        await UpsertConfigAsync("feishu", "Enabled", FormatBool(config.Enabled), "Enable Feishu");
        await UpsertConfigAsync("feishu", "WebhookUrl", config.WebhookUrl, "Feishu Webhook URL");
        await UpsertConfigAsync("feishu", "Secret", config.SignSecret, "Feishu Webhook Secret");
    }

    private async Task UpdateWechatAsync(WechatUpdateRequest config)
    {
        await UpsertConfigAsync("wechat", "Enabled", FormatBool(config.Enabled), "Enable Wechat");
        await UpsertConfigAsync("wechat", "WebhookUrl", config.WebhookUrl, "Wechat Webhook URL");
    }

    private async Task UpdateOpenAiAsync(OpenAiUpdateRequest config)
    {
        var existingConfigs = await _dbContext.SystemConfigs
            .Where(c => c.Category == "openai")
            .ToListAsync();

        var existingMap = existingConfigs.ToDictionary(c => c.Key, c => c.Value, StringComparer.OrdinalIgnoreCase);
        var existingProviders = ParseOpenAiProviders(existingMap.GetValueOrDefault("Providers"), maskApiKeys: false);
        if (existingProviders.Count == 0)
        {
            existingProviders.Add(new OpenAiProviderItem
            {
                Id = "default",
                Name = "默认模型源",
                ApiKey = existingMap.GetValueOrDefault("ApiKey") ?? string.Empty,
                BaseUrl = existingMap.GetValueOrDefault("BaseUrl") ?? "https://api.openai.com/v1",
                Model = existingMap.GetValueOrDefault("Model") ?? "gpt-4o-mini"
            });
        }

        var providers = NormalizeOpenAiProviders(config, existingProviders);
        if (providers.Count == 0)
        {
            providers.Add(new OpenAiProviderItem
            {
                Id = "default",
                Name = "默认模型源",
                ApiKey = string.Empty,
                BaseUrl = "https://api.openai.com/v1",
                Model = "gpt-4o-mini"
            });
        }

        var preferredProviderId = string.IsNullOrWhiteSpace(config.ActiveProviderId)
            ? string.Empty
            : config.ActiveProviderId.Trim();
        var activeProvider = providers.FirstOrDefault(p => p.Id == preferredProviderId) ?? providers[0];

        await UpsertConfigAsync("openai", "Enabled", FormatBool(config.Enabled), "Enable OpenAI");
        await UpsertConfigAsync("openai", "ApiKey", activeProvider.ApiKey, "OpenAI API Key");
        await UpsertConfigAsync("openai", "BaseUrl", activeProvider.BaseUrl, "OpenAI Base URL");
        await UpsertConfigAsync("openai", "Model", activeProvider.Model, "OpenAI Model");
        await UpsertConfigAsync("openai", "ActiveProviderId", activeProvider.Id, "OpenAI Active Provider Id");
        await UpsertConfigAsync("openai", "Providers", JsonConvert.SerializeObject(providers), "OpenAI Providers");
    }

    private async Task UpsertConfigAsync(string category, string key, string? value, string description)
    {
        if (value is null)
        {
            return;
        }

        if (ShouldMaskValue(category, key, isEncrypted: false) && value == MaskedValue)
        {
            return;
        }

        var config = await _dbContext.SystemConfigs
            .FirstOrDefaultAsync(c => c.Category == category && c.Key == key);

        if (config is null)
        {
            _dbContext.SystemConfigs.Add(new SystemConfig
            {
                Category = category,
                Key = key,
                Value = value,
                Description = description,
                IsEncrypted = ShouldMaskValue(category, key, isEncrypted: false),
                UpdatedAt = DateTime.UtcNow
            });
            return;
        }

        config.Value = value;
        config.Description = string.IsNullOrWhiteSpace(description) ? config.Description : description;
        config.IsEncrypted = config.IsEncrypted || ShouldMaskValue(category, key, isEncrypted: false);
        config.UpdatedAt = DateTime.UtcNow;
    }

    private async Task<Dictionary<string, Dictionary<string, SystemConfig>>> LoadConfigMapAsync()
    {
        var configs = await _dbContext.SystemConfigs.ToListAsync();
        return configs
            .GroupBy(c => c.Category, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.ToDictionary(c => c.Key, c => c, StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);
    }

    private ProxySnapshot BuildProxySnapshot(Dictionary<string, Dictionary<string, SystemConfig>> configs)
    {
        var host = GetString(configs, "proxy", "Host");
        var port = GetInt(configs, "proxy", "Port");
        var username = GetString(configs, "proxy", "Username");
        var password = GetString(configs, "proxy", "Password");

        var proxyUrl = GetString(configs, "proxy", "Url", _configuration["Proxy:Url"]);
        PopulateProxyPartsFromUrl(proxyUrl, ref host, ref port, ref username, ref password);

        return new ProxySnapshot
        {
            Enabled = GetBool(configs, "proxy", "Enabled", ParseBool(_configuration["Proxy:Enabled"])),
            Host = host,
            Port = port,
            Username = username,
            Password = password
        };
    }

    private static void PopulateProxyPartsFromUrl(string? proxyUrl, ref string host, ref int port, ref string username, ref string password)
    {
        if (string.IsNullOrWhiteSpace(proxyUrl) || !Uri.TryCreate(proxyUrl, UriKind.Absolute, out var uri))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(host))
        {
            host = uri.Host;
        }

        if (port == 0)
        {
            port = uri.IsDefaultPort
                ? uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ? 443 : 80
                : uri.Port;
        }

        if (!string.IsNullOrWhiteSpace(uri.UserInfo))
        {
            var userInfo = uri.UserInfo.Split(':', 2);
            if (string.IsNullOrWhiteSpace(username) && userInfo.Length >= 1)
            {
                username = Uri.UnescapeDataString(userInfo[0]);
            }

            if (string.IsNullOrWhiteSpace(password) && userInfo.Length == 2)
            {
                password = Uri.UnescapeDataString(userInfo[1]);
            }
        }
    }

    private static string? FormatBool(bool? value)
    {
        return value?.ToString().ToLowerInvariant();
    }

    private static bool ParseBool(string? value, bool fallback = false)
    {
        return bool.TryParse(value, out var parsed) ? parsed : fallback;
    }

    private static int ParseInt(string? value, int fallback = 0)
    {
        return int.TryParse(value, out var parsed) ? parsed : fallback;
    }

    private static string[] ParseList(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? Array.Empty<string>()
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static bool TryGetConfigEntry(
        IReadOnlyDictionary<string, Dictionary<string, SystemConfig>> configs,
        string category,
        string key,
        out SystemConfig? config)
    {
        config = null;
        if (!configs.TryGetValue(category, out var categoryConfigs))
        {
            return false;
        }

        if (!categoryConfigs.TryGetValue(key, out config))
        {
            return false;
        }

        return true;
    }

    private static string GetString(
        IReadOnlyDictionary<string, Dictionary<string, SystemConfig>> configs,
        string category,
        string key,
        string? fallback = "")
    {
        return TryGetConfigEntry(configs, category, key, out var config)
            ? config?.Value ?? string.Empty
            : fallback ?? string.Empty;
    }

    private static string GetSecret(
        IReadOnlyDictionary<string, Dictionary<string, SystemConfig>> configs,
        string category,
        string key,
        string? fallback = "")
    {
        if (TryGetConfigEntry(configs, category, key, out var config))
        {
            return string.IsNullOrEmpty(config?.Value) ? string.Empty : MaskedValue;
        }

        return string.IsNullOrEmpty(fallback) ? string.Empty : MaskedValue;
    }

    private static bool GetBool(
        IReadOnlyDictionary<string, Dictionary<string, SystemConfig>> configs,
        string category,
        string key,
        bool fallback = false)
    {
        return TryGetConfigEntry(configs, category, key, out var config)
            ? ParseBool(config?.Value, fallback)
            : fallback;
    }

    private static int GetInt(
        IReadOnlyDictionary<string, Dictionary<string, SystemConfig>> configs,
        string category,
        string key,
        int fallback = 0)
    {
        return TryGetConfigEntry(configs, category, key, out var config)
            ? ParseInt(config?.Value, fallback)
            : fallback;
    }

    private List<OpenAiProviderItem> GetOpenAiProviders(
        IReadOnlyDictionary<string, Dictionary<string, SystemConfig>> configs,
        bool maskApiKeys)
    {
        var providers = ParseOpenAiProviders(GetString(configs, "openai", "Providers"), maskApiKeys);
        if (providers.Count > 0)
        {
            return providers;
        }

        var legacyApiKey = maskApiKeys
            ? GetSecret(configs, "openai", "ApiKey", _configuration["OpenAI:ApiKey"])
            : GetString(configs, "openai", "ApiKey", _configuration["OpenAI:ApiKey"]);

        return new List<OpenAiProviderItem>
        {
            new()
            {
                Id = "default",
                Name = "默认模型源",
                ApiKey = legacyApiKey,
                BaseUrl = GetString(configs, "openai", "BaseUrl", _configuration["OpenAI:BaseUrl"] ?? "https://api.openai.com/v1"),
                Model = GetString(configs, "openai", "Model", _configuration["OpenAI:Model"] ?? "gpt-4o-mini")
            }
        };
    }

    private static string GetActiveOpenAiProviderId(
        IReadOnlyDictionary<string, Dictionary<string, SystemConfig>> configs,
        IReadOnlyList<OpenAiProviderItem> providers)
    {
        var preferred = GetString(configs, "openai", "ActiveProviderId");
        if (providers.Any(item => item.Id == preferred))
        {
            return preferred;
        }

        return providers.FirstOrDefault()?.Id ?? "default";
    }

    private static List<OpenAiProviderItem> ParseOpenAiProviders(string? raw, bool maskApiKeys)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new List<OpenAiProviderItem>();
        }

        try
        {
            var rows = JsonConvert.DeserializeObject<List<OpenAiProviderItem>>(raw) ?? new List<OpenAiProviderItem>();
            var normalized = new List<OpenAiProviderItem>();
            foreach (var row in rows)
            {
                var id = (row.Id ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                var apiKey = (row.ApiKey ?? string.Empty).Trim();
                normalized.Add(new OpenAiProviderItem
                {
                    Id = id,
                    Name = string.IsNullOrWhiteSpace(row.Name) ? id : row.Name.Trim(),
                    ApiKey = maskApiKeys && !string.IsNullOrWhiteSpace(apiKey) ? MaskedValue : apiKey,
                    BaseUrl = string.IsNullOrWhiteSpace(row.BaseUrl) ? "https://api.openai.com/v1" : row.BaseUrl.Trim(),
                    Model = string.IsNullOrWhiteSpace(row.Model) ? "gpt-4o-mini" : row.Model.Trim()
                });
            }

            return normalized;
        }
        catch
        {
            return new List<OpenAiProviderItem>();
        }
    }

    private static List<OpenAiProviderItem> NormalizeOpenAiProviders(
        OpenAiUpdateRequest request,
        IReadOnlyList<OpenAiProviderItem> existingProviders)
    {
        var existingMap = existingProviders
            .Where(item => !string.IsNullOrWhiteSpace(item.Id))
            .ToDictionary(item => item.Id, item => item, StringComparer.OrdinalIgnoreCase);

        List<OpenAiProviderItem> rawProviders;
        if (request.Providers is { Count: > 0 })
        {
            rawProviders = request.Providers
                .Select(item => new OpenAiProviderItem
                {
                    Id = item.Id,
                    Name = item.Name,
                    ApiKey = item.ApiKey,
                    BaseUrl = item.BaseUrl,
                    Model = item.Model
                })
                .ToList();
        }
        else
        {
            rawProviders = new List<OpenAiProviderItem>
            {
                new()
                {
                    Id = "default",
                    Name = "默认模型源",
                    ApiKey = request.ApiKey,
                    BaseUrl = request.BaseUrl,
                    Model = request.Model
                }
            };
        }

        var normalized = new List<OpenAiProviderItem>();
        var seq = 1;
        foreach (var row in rawProviders)
        {
            var id = string.IsNullOrWhiteSpace(row.Id) ? $"provider-{seq++}" : row.Id.Trim();
            if (normalized.Any(item => item.Id == id))
            {
                continue;
            }

            existingMap.TryGetValue(id, out var existing);
            var incomingApiKey = (row.ApiKey ?? string.Empty).Trim();
            var mergedApiKey = incomingApiKey == MaskedValue
                ? (existing?.ApiKey ?? string.Empty)
                : incomingApiKey;

            normalized.Add(new OpenAiProviderItem
            {
                Id = id,
                Name = string.IsNullOrWhiteSpace(row.Name) ? $"模型源 {normalized.Count + 1}" : row.Name.Trim(),
                ApiKey = mergedApiKey,
                BaseUrl = string.IsNullOrWhiteSpace(row.BaseUrl)
                    ? (string.IsNullOrWhiteSpace(existing?.BaseUrl) ? "https://api.openai.com/v1" : existing.BaseUrl)
                    : row.BaseUrl.Trim(),
                Model = string.IsNullOrWhiteSpace(row.Model)
                    ? (string.IsNullOrWhiteSpace(existing?.Model) ? "gpt-4o-mini" : existing.Model)
                    : row.Model.Trim()
            });
        }

        return normalized;
    }

    private static bool ShouldMaskValue(string category, string key, bool isEncrypted)
    {
        if (isEncrypted)
        {
            return true;
        }

        return (category.ToLowerInvariant(), key.ToLowerInvariant()) switch
        {
            ("longbridge", "appsecret") => true,
            ("longbridge", "accesstoken") => true,
            ("proxy", "password") => true,
            ("email", "smtppassword") => true,
            ("feishu", "secret") => true,
            ("openai", "apikey") => true,
            ("openai", "providers") => true,
            _ => false
        };
    }

    private static string BuildDescription(string category, string key)
    {
        return (category.ToLowerInvariant(), key.ToLowerInvariant()) switch
        {
            ("longbridge", "appkey") => "Long Bridge App Key",
            ("longbridge", "appsecret") => "Long Bridge App Secret",
            ("longbridge", "accesstoken") => "Long Bridge Access Token",
            ("longbridge", "baseurl") => "Long Bridge API Base Url",
            ("proxy", "enabled") => "Enable Proxy",
            ("proxy", "host") => "Proxy Host",
            ("proxy", "port") => "Proxy Port",
            ("proxy", "username") => "Proxy Username",
            ("proxy", "password") => "Proxy Password",
            ("email", "enabled") => "Enable Email",
            ("email", "smtpserver") => "SMTP Server",
            ("email", "smtpport") => "SMTP Port",
            ("email", "smtpusername") => "SMTP Username",
            ("email", "smtppassword") => "SMTP Password",
            ("email", "fromaddress") => "From Email Address",
            ("email", "recipients") => "Notification Recipients",
            ("email", "usessl") => "Use SSL",
            ("feishu", "enabled") => "Enable Feishu",
            ("feishu", "webhookurl") => "Feishu Webhook URL",
            ("feishu", "secret") => "Feishu Webhook Secret",
            ("wechat", "enabled") => "Enable Wechat",
            ("wechat", "webhookurl") => "Wechat Webhook URL",
            ("openai", "enabled") => "Enable OpenAI",
            ("openai", "apikey") => "OpenAI API Key",
            ("openai", "baseurl") => "OpenAI Base URL",
            ("openai", "model") => "OpenAI Model",
            ("openai", "activeproviderid") => "OpenAI Active Provider Id",
            ("openai", "providers") => "OpenAI Providers",
            _ => string.Empty
        };
    }

    private static string? NormalizeLongBridgeBaseUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return value.Trim();
        }

        var host = uri.Host.ToLowerInvariant();
        if (host == "open.longbridge.com")
        {
            return "https://openapi.longbridge.com";
        }

        if (host == "open.longbridge.cn")
        {
            return "https://openapi.longbridge.cn";
        }

        if (host == "openapi.longbridge.com" || host == "openapi.longbridge.cn")
        {
            return $"{uri.Scheme}://{uri.Host}";
        }

        return value.Trim();
    }

    public sealed class UpdateSystemConfigRequest
    {
        public LongBridgeUpdateRequest? LongBridge { get; set; }
        public ProxyUpdateRequest? Proxy { get; set; }
        public EmailUpdateRequest? Email { get; set; }
        public FeishuUpdateRequest? Feishu { get; set; }
        public WechatUpdateRequest? Wechat { get; set; }
        public OpenAiUpdateRequest? OpenAi { get; set; }
    }

    public sealed class LongBridgeUpdateRequest
    {
        public string? AppKey { get; set; }
        public string? AppSecret { get; set; }
        public string? AccessToken { get; set; }
        public string? BaseUrl { get; set; }
    }

    public sealed class ProxyUpdateRequest
    {
        public bool? Enabled { get; set; }
        public string? Host { get; set; }
        public int? Port { get; set; }
        public string? Username { get; set; }
        public string? Password { get; set; }
    }

    public sealed class EmailUpdateRequest
    {
        public bool? Enabled { get; set; }
        public string? SmtpHost { get; set; }
        public int? SmtpPort { get; set; }
        public string? Username { get; set; }
        public string? Password { get; set; }
        public string? FromAddress { get; set; }
        public string[]? ToAddresses { get; set; }
        public bool? UseSsl { get; set; }
    }

    public sealed class FeishuUpdateRequest
    {
        public bool? Enabled { get; set; }
        public string? WebhookUrl { get; set; }
        public string? SignSecret { get; set; }
    }

    public sealed class WechatUpdateRequest
    {
        public bool? Enabled { get; set; }
        public string? WebhookUrl { get; set; }
    }

    public sealed class OpenAiUpdateRequest
    {
        public bool? Enabled { get; set; }
        public string? ApiKey { get; set; }
        public string? BaseUrl { get; set; }
        public string? Model { get; set; }
        public string? ActiveProviderId { get; set; }
        public List<OpenAiProviderItem>? Providers { get; set; }
    }

    public sealed class SystemConfigResponse
    {
        public LongBridgeConfigResponse LongBridge { get; init; } = new();
        public ProxyConfigResponse Proxy { get; init; } = new();
        public EmailConfigResponse Email { get; init; } = new();
        public FeishuConfigResponse Feishu { get; init; } = new();
        public WechatConfigResponse Wechat { get; init; } = new();
        public OpenAiConfigResponse OpenAi { get; init; } = new();
    }

    public sealed class LongBridgeConfigResponse
    {
        public string AppKey { get; init; } = string.Empty;
        public string AppSecret { get; init; } = string.Empty;
        public string AccessToken { get; init; } = string.Empty;
        public string BaseUrl { get; init; } = string.Empty;
    }

    public sealed class ProxyConfigResponse
    {
        public bool Enabled { get; init; }
        public string Host { get; init; } = string.Empty;
        public int Port { get; init; }
        public string Username { get; init; } = string.Empty;
        public string Password { get; init; } = string.Empty;
    }

    public sealed class EmailConfigResponse
    {
        public bool Enabled { get; init; }
        public string SmtpHost { get; init; } = string.Empty;
        public int SmtpPort { get; init; }
        public string Username { get; init; } = string.Empty;
        public string Password { get; init; } = string.Empty;
        public string FromAddress { get; init; } = string.Empty;
        public string[] ToAddresses { get; init; } = Array.Empty<string>();
        public bool UseSsl { get; init; }
    }

    public sealed class FeishuConfigResponse
    {
        public bool Enabled { get; init; }
        public string WebhookUrl { get; init; } = string.Empty;
        public string SignSecret { get; init; } = string.Empty;
    }

    public sealed class WechatConfigResponse
    {
        public bool Enabled { get; init; }
        public string WebhookUrl { get; init; } = string.Empty;
    }

    public sealed class OpenAiConfigResponse
    {
        public bool Enabled { get; init; }
        public string ApiKey { get; init; } = string.Empty;
        public string BaseUrl { get; init; } = string.Empty;
        public string Model { get; init; } = string.Empty;
        public string ActiveProviderId { get; init; } = string.Empty;
        public List<OpenAiProviderItem> Providers { get; init; } = new();
    }

    public sealed class OpenAiProviderItem
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
        public string BaseUrl { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
    }

    private sealed class ProxySnapshot
    {
        public bool Enabled { get; init; }
        public string Host { get; init; } = string.Empty;
        public int Port { get; init; }
        public string Username { get; init; } = string.Empty;
        public string Password { get; init; } = string.Empty;
    }
}
