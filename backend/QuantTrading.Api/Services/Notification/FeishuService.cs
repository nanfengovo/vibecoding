using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using QuantTrading.Api.Data;
using QuantTrading.Api.Models;

namespace QuantTrading.Api.Services.Notification;

public class FeishuService : IFeishuService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<FeishuService> _logger;

    public FeishuService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        IServiceProvider serviceProvider,
        ILogger<FeishuService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    private async Task<(string? webhookUrl, string? secret)> GetConfigAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<QuantTradingDbContext>();
        
        var configs = await dbContext.SystemConfigs
            .Where(c => c.Category == "feishu")
            .ToDictionaryAsync(c => c.Key, c => c.Value);
        
        var webhookUrl = configs.GetValueOrDefault("WebhookUrl") ?? _configuration["Feishu:WebhookUrl"];
        var secret = configs.GetValueOrDefault("Secret") ?? _configuration["Feishu:Secret"];
        
        return (webhookUrl, secret);
    }

    public async Task<bool> SendAsync(string title, string content)
    {
        try
        {
            var (webhookUrl, secret) = await GetConfigAsync();
            
            if (string.IsNullOrEmpty(webhookUrl))
            {
                _logger.LogWarning("Feishu webhook URL is not configured");
                return false;
            }

            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var sign = string.Empty;

            if (!string.IsNullOrEmpty(secret))
            {
                sign = GenerateSign(timestamp, secret);
            }

            var payload = new
            {
                timestamp = timestamp.ToString(),
                sign = sign,
                msg_type = "interactive",
                card = new
                {
                    config = new { wide_screen_mode = true },
                    header = new
                    {
                        title = new { tag = "plain_text", content = title },
                        template = "blue"
                    },
                    elements = new[]
                    {
                        new
                        {
                            tag = "markdown",
                            content = content
                        }
                    }
                }
            };

            var client = _httpClientFactory.CreateClient();
            var response = await client.PostAsync(
                webhookUrl,
                new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json")
            );

            var responseContent = await response.Content.ReadAsStringAsync();
            
            if (response.IsSuccessStatusCode)
            {
                await LogNotificationAsync("feishu", webhookUrl, title, content, "sent");
                _logger.LogInformation("Feishu message sent: {Title}", title);
                return true;
            }

            _logger.LogError("Feishu API error: {Response}", responseContent);
            await LogNotificationAsync("feishu", webhookUrl, title, content, "failed", responseContent);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send Feishu message");
            return false;
        }
    }

    private string GenerateSign(long timestamp, string secret)
    {
        var stringToSign = $"{timestamp}\n{secret}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(stringToSign));
        var hash = hmac.ComputeHash(Array.Empty<byte>());
        return Convert.ToBase64String(hash);
    }

    public async Task<bool> TestAsync()
    {
        return await SendAsync("QuantTrading 测试消息", 
            "**测试成功**\n\n如果您看到此消息，说明飞书机器人配置正确。\n\n发送时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
    }

    private async Task LogNotificationAsync(string channel, string recipient, string subject, string content, string status, string? error = null)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<QuantTradingDbContext>();
            
            dbContext.NotificationLogs.Add(new NotificationLog
            {
                Channel = channel,
                Recipient = recipient,
                Subject = subject,
                Content = content,
                Status = status,
                ErrorMessage = error,
                SentAt = DateTime.UtcNow
            });
            
            await dbContext.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to log notification");
        }
    }
}
