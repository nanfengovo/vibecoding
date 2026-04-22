using QuantTrading.Api.Models;

namespace QuantTrading.Api.Services.LongBridge;

public interface ILongBridgeService
{
    Task<LongBridgeConnectionTestResult> TestConnectionAsync();

    // Quote API
    Task<StockQuote?> GetQuoteAsync(string symbol);
    Task<StockQuote?> GetQuoteStrictAsync(string symbol);
    Task<List<StockQuote>> GetQuotesAsync(List<string> symbols);
    Task<List<StockKline>> GetKlineAsync(string symbol, string period, DateTime? start = null, DateTime? end = null, int count = 100);
    Task<Stock?> GetStockInfoAsync(string symbol);
    Task<List<Stock>> SearchStocksAsync(string keyword);
    
    // Trade API
    Task<string?> PlaceOrderAsync(string symbol, string side, string orderType, decimal quantity, decimal? price = null);
    Task<bool> CancelOrderAsync(string orderId);
    Task<Trade?> GetOrderAsync(string orderId);
    Task<List<Trade>> GetOrdersAsync(string? status = null, DateTime? startDate = null, DateTime? endDate = null);
    Task<List<Trade>> GetTodayOrdersAsync();
    
    // Account API
    Task<Account?> GetAccountAsync();
    Task<List<Position>> GetPositionsAsync();
    Task<decimal> GetCashAsync();
    
    // Market Status
    Task<bool> IsMarketOpenAsync(string market = "US");
    Task<DateTime?> GetNextMarketOpenAsync(string market = "US");
}

public sealed class LongBridgeConnectionTestResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
}
