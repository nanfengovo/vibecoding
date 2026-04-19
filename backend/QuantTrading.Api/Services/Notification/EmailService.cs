using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.EntityFrameworkCore;
using MimeKit;
using QuantTrading.Api.Data;
using QuantTrading.Api.Models;

namespace QuantTrading.Api.Services.Notification;

public class EmailService : IEmailService
{
    private readonly IConfiguration _configuration;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<EmailService> _logger;

    public EmailService(
        IConfiguration configuration, 
        IServiceProvider serviceProvider,
        ILogger<EmailService> logger)
    {
        _configuration = configuration;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    private async Task<Dictionary<string, string>> GetConfigAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<QuantTradingDbContext>();
        
        var configs = await dbContext.SystemConfigs
            .Where(c => c.Category == "email")
            .ToDictionaryAsync(c => c.Key, c => c.Value);
        
        return configs;
    }

    public async Task<bool> SendAsync(string to, string subject, string body, bool isHtml = false)
    {
        try
        {
            var config = await GetConfigAsync();
            
            var smtpServer = config.GetValueOrDefault("SmtpServer") ?? _configuration["Email:SmtpServer"];
            var smtpPort = int.Parse(config.GetValueOrDefault("SmtpPort") ?? _configuration["Email:SmtpPort"] ?? "587");
            var username = config.GetValueOrDefault("SmtpUsername") ?? _configuration["Email:Username"];
            var password = config.GetValueOrDefault("SmtpPassword") ?? _configuration["Email:Password"];
            var fromAddress = config.GetValueOrDefault("FromAddress") ?? _configuration["Email:FromAddress"];
            var fromName = _configuration["Email:FromName"] ?? "QuantTrading System";

            if (string.IsNullOrEmpty(smtpServer) || string.IsNullOrEmpty(username) || 
                string.IsNullOrEmpty(password) || string.IsNullOrEmpty(fromAddress))
            {
                _logger.LogWarning("Email configuration is incomplete");
                return false;
            }

            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(fromName, fromAddress));
            message.To.Add(new MailboxAddress(to, to));
            message.Subject = subject;

            var bodyBuilder = new BodyBuilder();
            if (isHtml)
            {
                bodyBuilder.HtmlBody = body;
            }
            else
            {
                bodyBuilder.TextBody = body;
            }
            message.Body = bodyBuilder.ToMessageBody();

            using var client = new SmtpClient();
            await client.ConnectAsync(smtpServer, smtpPort, SecureSocketOptions.StartTls);
            await client.AuthenticateAsync(username, password);
            await client.SendAsync(message);
            await client.DisconnectAsync(true);

            await LogNotificationAsync("email", to, subject, body, "sent");
            _logger.LogInformation("Email sent to {To}: {Subject}", to, subject);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {To}", to);
            await LogNotificationAsync("email", to, subject, body, "failed", ex.Message);
            return false;
        }
    }

    public async Task<bool> TestAsync()
    {
        var config = await GetConfigAsync();
        var fromAddress = config.GetValueOrDefault("FromAddress") ?? _configuration["Email:FromAddress"];
        
        if (string.IsNullOrEmpty(fromAddress))
            return false;
        
        return await SendAsync(fromAddress, "QuantTrading 测试邮件", "这是一封测试邮件，如果您收到此邮件，说明邮件配置正确。");
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
