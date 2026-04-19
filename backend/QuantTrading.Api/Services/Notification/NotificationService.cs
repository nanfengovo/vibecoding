using Microsoft.EntityFrameworkCore;
using QuantTrading.Api.Data;

namespace QuantTrading.Api.Services.Notification;

public class NotificationService : INotificationService
{
    private readonly IEmailService _emailService;
    private readonly IFeishuService _feishuService;
    private readonly IWechatService _wechatService;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(
        IEmailService emailService,
        IFeishuService feishuService,
        IWechatService wechatService,
        IServiceProvider serviceProvider,
        ILogger<NotificationService> logger)
    {
        _emailService = emailService;
        _feishuService = feishuService;
        _wechatService = wechatService;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task SendAsync(string subject, string message, List<string>? channels = null)
    {
        channels ??= new List<string> { "email", "feishu", "wechat" };
        
        var tasks = new List<Task>();

        if (channels.Contains("email", StringComparer.OrdinalIgnoreCase))
        {
            var emailRecipients = await GetEmailRecipientsAsync();
            foreach (var email in emailRecipients)
            {
                tasks.Add(_emailService.SendAsync(email, subject, message));
            }
        }

        if (channels.Contains("feishu", StringComparer.OrdinalIgnoreCase))
        {
            tasks.Add(_feishuService.SendAsync(subject, message));
        }

        if (channels.Contains("wechat", StringComparer.OrdinalIgnoreCase))
        {
            var markdownMessage = $"## {subject}\n\n{message}";
            tasks.Add(_wechatService.SendMarkdownAsync(markdownMessage));
        }

        await Task.WhenAll(tasks);
    }

    public async Task<bool> TestAsync(string channel)
    {
        return channel.ToLower() switch
        {
            "email" => await _emailService.TestAsync(),
            "feishu" => await _feishuService.TestAsync(),
            "wechat" => await _wechatService.TestAsync(),
            _ => false
        };
    }

    private async Task<List<string>> GetEmailRecipientsAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<QuantTradingDbContext>();
        
        var config = await dbContext.SystemConfigs
            .FirstOrDefaultAsync(c => c.Category == "email" && c.Key == "Recipients");
        
        if (config != null && !string.IsNullOrEmpty(config.Value))
        {
            return config.Value.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
        }
        
        // Fallback to FromAddress
        var fromConfig = await dbContext.SystemConfigs
            .FirstOrDefaultAsync(c => c.Category == "email" && c.Key == "FromAddress");
        
        if (fromConfig != null && !string.IsNullOrEmpty(fromConfig.Value))
        {
            return new List<string> { fromConfig.Value };
        }
        
        return new List<string>();
    }
}
