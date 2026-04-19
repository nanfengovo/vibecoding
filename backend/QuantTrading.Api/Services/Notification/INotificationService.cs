namespace QuantTrading.Api.Services.Notification;

public interface INotificationService
{
    Task SendAsync(string subject, string message, List<string>? channels = null);
    Task<bool> TestAsync(string channel);
}

public interface IEmailService
{
    Task<bool> SendAsync(string to, string subject, string body, bool isHtml = false);
    Task<bool> TestAsync();
}

public interface IFeishuService
{
    Task<bool> SendAsync(string title, string content);
    Task<bool> TestAsync();
}

public interface IWechatService
{
    Task<bool> SendAsync(string content);
    Task<bool> SendMarkdownAsync(string content);
    Task<bool> TestAsync();
}
