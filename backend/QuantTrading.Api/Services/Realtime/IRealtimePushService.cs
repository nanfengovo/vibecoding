namespace QuantTrading.Api.Services.Realtime;

public interface IRealtimePushService
{
    Task PushQuoteAsync(string symbol, object quotePayload);
    Task PushTradeAsync(object tradePayload);
    Task PushNotificationAsync(string title, string message, string type = "info");
    Task PushMonitorAlertAsync(object alertPayload);
    Task PushStrategyReloadedAsync(int strategyId);
}
