using Microsoft.AspNetCore.SignalR;

namespace QuantTrading.Api.Hubs;

public class TradingHub : Hub
{
    private readonly ILogger<TradingHub> _logger;

    public TradingHub(ILogger<TradingHub> logger)
    {
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("Client connected: {ConnectionId}", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Client disconnected: {ConnectionId}", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    public async Task SubscribeToSymbol(string symbol)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"symbol_{symbol.ToUpper()}");
        _logger.LogInformation("Client {ConnectionId} subscribed to {Symbol}", Context.ConnectionId, symbol);
    }

    // Compatibility method for frontend batch subscriptions.
    public async Task SubscribeQuote(IEnumerable<string> symbols)
    {
        foreach (var symbol in symbols.Where(s => !string.IsNullOrWhiteSpace(s)))
        {
            await SubscribeToSymbol(symbol);
        }
    }

    public async Task UnsubscribeFromSymbol(string symbol)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"symbol_{symbol.ToUpper()}");
        _logger.LogInformation("Client {ConnectionId} unsubscribed from {Symbol}", Context.ConnectionId, symbol);
    }

    // Compatibility method for frontend batch unsubscriptions.
    public async Task UnsubscribeQuote(IEnumerable<string> symbols)
    {
        foreach (var symbol in symbols.Where(s => !string.IsNullOrWhiteSpace(s)))
        {
            await UnsubscribeFromSymbol(symbol);
        }
    }

    public async Task SubscribeStrategy(int strategyId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"strategy_{strategyId}");
        _logger.LogInformation("Client {ConnectionId} subscribed to strategy {StrategyId}", Context.ConnectionId, strategyId);
    }

    public async Task SendQuoteUpdate(string symbol, object quote)
    {
        await Clients.Group($"symbol_{symbol.ToUpper()}").SendAsync("QuoteUpdate", symbol, quote);
    }

    public async Task SendTradeUpdate(object trade)
    {
        await Clients.All.SendAsync("TradeUpdate", trade);
    }

    public async Task SendPositionUpdate(object position)
    {
        await Clients.All.SendAsync("PositionUpdate", position);
    }

    public async Task SendStrategyUpdate(int strategyId, object data)
    {
        await Clients.All.SendAsync("StrategyUpdate", strategyId, data);
    }
}

public class NotificationHub : Hub
{
    private readonly ILogger<NotificationHub> _logger;

    public NotificationHub(ILogger<NotificationHub> logger)
    {
        _logger = logger;
    }

    public async Task SendAlert(object alert)
    {
        await Clients.All.SendAsync("Alert", alert);
    }

    public async Task SendNotification(string title, string message, string type = "info")
    {
        await Clients.All.SendAsync("Notification", new { title, message, type });
    }
}
