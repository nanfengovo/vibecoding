using Microsoft.AspNetCore.SignalR;
using QuantTrading.Api.Hubs;

namespace QuantTrading.Api.Services.Realtime;

public class RealtimePushService : IRealtimePushService
{
    private readonly IHubContext<TradingHub> _tradingHubContext;
    private readonly ILogger<RealtimePushService> _logger;

    public RealtimePushService(
        IHubContext<TradingHub> tradingHubContext,
        ILogger<RealtimePushService> logger)
    {
        _tradingHubContext = tradingHubContext;
        _logger = logger;
    }

    public async Task PushQuoteAsync(string symbol, object quotePayload)
    {
        try
        {
            await _tradingHubContext.Clients
                .Group($"symbol_{symbol.ToUpperInvariant()}")
                .SendAsync("QuoteUpdate", symbol, quotePayload);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to push quote update for {Symbol}", symbol);
        }
    }

    public async Task PushTradeAsync(object tradePayload)
    {
        try
        {
            await _tradingHubContext.Clients.All.SendAsync("TradeUpdate", tradePayload);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to push trade update");
        }
    }

    public async Task PushNotificationAsync(string title, string message, string type = "info")
    {
        try
        {
            await _tradingHubContext.Clients.All.SendAsync("Notification", new
            {
                title,
                message,
                type
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to push notification");
        }
    }

    public async Task PushMonitorAlertAsync(object alertPayload)
    {
        try
        {
            await _tradingHubContext.Clients.All.SendAsync("MonitorAlert", alertPayload);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to push monitor alert");
        }
    }

    public async Task PushStrategyReloadedAsync(int strategyId)
    {
        try
        {
            await _tradingHubContext.Clients.All.SendAsync("StrategyReloaded", strategyId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to push strategy reloaded event for {StrategyId}", strategyId);
        }
    }
}
