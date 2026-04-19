using QuantTrading.Api.Models;

namespace QuantTrading.Api.Services.Monitor;

public interface IMonitorService
{
    Task<List<MonitorRule>> GetAllRulesAsync();
    Task<MonitorRule?> GetRuleByIdAsync(int id);
    Task<MonitorRule> CreateRuleAsync(MonitorRule rule);
    Task<MonitorRule?> UpdateRuleAsync(int id, MonitorRule rule);
    Task<bool> DeleteRuleAsync(int id);
    Task<bool> ToggleRuleAsync(int id);
    Task ExecuteRulesAsync();
    Task<List<MonitorAlert>> GetAlertsAsync(int? ruleId = null, bool? unreadOnly = null, int limit = 50);
    Task<bool> MarkAlertReadAsync(long alertId);
    Task<bool> MarkAllAlertsReadAsync();
}

public interface IWatchlistService
{
    Task<List<Stock>> GetWatchlistAsync();
    Task<Stock?> AddToWatchlistAsync(string symbol, string? notes = null);
    Task<bool> RemoveFromWatchlistAsync(int id);
    Task<Stock?> UpdateStockNotesAsync(int id, string notes);
    Task RefreshWatchlistAsync();
}

public interface ITradeService
{
    Task<List<Trade>> GetTradesAsync(DateTime? startDate = null, DateTime? endDate = null, int limit = 100);
    Task<List<Position>> GetPositionsAsync();
    Task<Account?> GetAccountAsync();
    Task<Trade?> PlaceOrderAsync(string symbol, string side, string orderType, decimal quantity, decimal? price = null, int? strategyId = null);
    Task<bool> CancelOrderAsync(string orderId);
}
