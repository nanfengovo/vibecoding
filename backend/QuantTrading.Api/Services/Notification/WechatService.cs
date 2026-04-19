using System.Text;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using QuantTrading.Api.Data;
using QuantTrading.Api.Models;

namespace QuantTrading.Api.Services.Notification;

public class WechatService : IWechatService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<WechatService> _logger;

    public WechatService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        IServiceProvider serviceProvider,
        ILogger<WechatService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    private async Task<string?> GetWebhookUrlAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<QuantTradingDbContext>();
        
        var config = await dbContext.SystemConfigs
            .FirstOrDefaultAsync(c => c.Category == "wechat" && c.Key == "WebhookUrl");
        
        return config?.Value ?? _configuration["Wechat:WebhookUrl"];
    }

    public async Task<bool> SendAsync(string content)
    {
        try
        {
            var webhookUrl = await GetWebhookUrlAsync();
            
            if (string.IsNullOrEmpty(webhookUrl))
            {
                _logger.LogWarning("WeChat webhook URL is not configured");
                return false;
            }

            var payload = new
            {
                msgtype = "text",
                text = new { content }
            };

            return await SendPayloadAsync(webhookUrl, payload, "text", content);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send WeChat message");
            return false;
        }
    }

    public async Task<bool> SendMarkdownAsync(string content)
    {
        try
        {
            var webhookUrl = await GetWebhookUrlAsync();
            
            if (string.IsNullOrEmpty(webhookUrl))
            {
                _logger.LogWarning("WeChat webhook URL is not configured");
                return false;
            }

            var payload = new
            {
                msgtype = "markdown",
                markdown = new { content }
            };

            return await SendPayloadAsync(webhookUrl, payload, "markdown", content);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send WeChat markdown message");
            return false;
        }
    }

    private async Task<bool> SendPayloadAsync(string webhookUrl, object payload, string msgType, string content)
    {
        var client = _httpClientFactory.CreateClient();
        var response = await client.PostAsync(
            webhookUrl,
            new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json")
        );

        var responseContent = await response.Content.ReadAsStringAsync();

        if (response.IsSuccessStatusCode)
        {
            var result = JsonConvert.DeserializeAnonymousType(responseContent, new { errcode = 0, errmsg = "" });
            if (result?.errcode == 0)
            {
                await LogNotificationAsync("wechat", webhookUrl, msgType, content, "sent");
                _logger.LogInformation("WeChat message sent successfully");
                return true;
            }
            
            _logger.LogError("WeChat API error: {Message}", result?.errmsg);
            await LogNotificationAsync("wechat", webhookUrl, msgType, content, "failed", result?.errmsg);
            return false;
        }

        _logger.LogError("WeChat API error: {Response}", responseContent);
        await LogNotificationAsync("wechat", webhookUrl, msgType, content, "failed", responseContent);
        return false;
    }

    public async Task<bool> TestAsync()
    {
        return await SendMarkdownAsync(
            "## QuantTrading 测试消息\n\n" +
            "> 测试成功\n\n" +
            "如果您看到此消息，说明企业微信机器人配置正确。\n\n" +
            $"发送时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
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
