using Microsoft.EntityFrameworkCore;
using QuantTrading.Api.Data;
using QuantTrading.Api.Models;
using QuantTrading.Api.Services.LongBridge;

namespace QuantTrading.Api.Services.Monitor;

public class TradeService : ITradeService
{
    private readonly QuantTradingDbContext _dbContext;
    private readonly ILongBridgeService _longBridgeService;
    private readonly ILogger<TradeService> _logger;

    public TradeService(
        QuantTradingDbContext dbContext,
        ILongBridgeService longBridgeService,
        ILogger<TradeService> logger)
    {
        _dbContext = dbContext;
        _longBridgeService = longBridgeService;
        _logger = logger;
    }

    public async Task<List<Trade>> GetTradesAsync(DateTime? startDate = null, DateTime? endDate = null, int limit = 100)
    {
        var query = _dbContext.Trades.AsQueryable();
        
        if (startDate.HasValue)
            query = query.Where(t => t.CreatedAt >= startDate.Value);
        
        if (endDate.HasValue)
            query = query.Where(t => t.CreatedAt <= endDate.Value);
        
        var localTrades = await query
            .OrderByDescending(t => t.CreatedAt)
            .Take(limit)
            .ToListAsync();
        
        // Also fetch from LongBridge
        var lbTrades = await _longBridgeService.GetOrdersAsync(startDate: startDate, endDate: endDate);
        
        // Merge trades (prefer local for duplicates)
        var localOrderIds = localTrades.Select(t => t.OrderId).ToHashSet();
        var newTrades = lbTrades.Where(t => !localOrderIds.Contains(t.OrderId)).ToList();
        
        // Save new trades to database
        if (newTrades.Any())
        {
            _dbContext.Trades.AddRange(newTrades);
            await _dbContext.SaveChangesAsync();
            localTrades.AddRange(newTrades);
        }
        
        return localTrades.OrderByDescending(t => t.CreatedAt).Take(limit).ToList();
    }

    public async Task<List<Position>> GetPositionsAsync()
    {
        var positions = await _longBridgeService.GetPositionsAsync();
        
        // Update local positions
        foreach (var position in positions)
        {
            var existing = await _dbContext.Positions.FirstOrDefaultAsync(p => p.Symbol == position.Symbol);
            if (existing != null)
            {
                existing.Quantity = position.Quantity;
                existing.AveragePrice = position.AveragePrice;
                existing.CurrentPrice = position.CurrentPrice;
                existing.MarketValue = position.MarketValue;
                existing.UnrealizedPnL = position.UnrealizedPnL;
                existing.UnrealizedPnLPercent = position.UnrealizedPnLPercent;
                existing.LastUpdated = DateTime.UtcNow;
            }
            else
            {
                position.OpenedAt = DateTime.UtcNow;
                _dbContext.Positions.Add(position);
            }
        }
        
        // Remove closed positions
        var currentSymbols = positions.Select(p => p.Symbol).ToHashSet();
        var closedPositions = await _dbContext.Positions
            .Where(p => !currentSymbols.Contains(p.Symbol))
            .ToListAsync();
        
        _dbContext.Positions.RemoveRange(closedPositions);
        await _dbContext.SaveChangesAsync();
        
        return positions;
    }

    public async Task<Account?> GetAccountAsync()
    {
        var account = await _longBridgeService.GetAccountAsync();
        
        if (account != null)
        {
            var existing = await _dbContext.Accounts.FirstOrDefaultAsync();
            if (existing != null)
            {
                existing.TotalAssets = account.TotalAssets;
                existing.Cash = account.Cash;
                existing.MarketValue = account.MarketValue;
                existing.UnrealizedPnL = account.UnrealizedPnL;
                existing.Currency = account.Currency;
                existing.LastUpdated = DateTime.UtcNow;
            }
            else
            {
                _dbContext.Accounts.Add(account);
            }
            
            await _dbContext.SaveChangesAsync();
        }
        
        return account;
    }

    public async Task<Trade?> PlaceOrderAsync(string symbol, string side, string orderType, decimal quantity, decimal? price = null, int? strategyId = null)
    {
        var orderId = await _longBridgeService.PlaceOrderAsync(symbol, side, orderType, quantity, price);
        
        if (string.IsNullOrEmpty(orderId))
        {
            _logger.LogError("Failed to place order: {Symbol} {Side} {Quantity}", symbol, side, quantity);
            return null;
        }
        
        var trade = new Trade
        {
            OrderId = orderId,
            Symbol = symbol.ToUpper(),
            Side = side.ToLower(),
            OrderType = orderType.ToLower(),
            Quantity = quantity,
            Price = price,
            Status = "pending",
            StrategyId = strategyId,
            CreatedAt = DateTime.UtcNow
        };
        
        _dbContext.Trades.Add(trade);
        await _dbContext.SaveChangesAsync();
        
        _logger.LogInformation("Order placed: {Symbol} {Side} {Quantity} @ {Price}, OrderId: {OrderId}", 
            symbol, side, quantity, price, orderId);
        
        return trade;
    }

    public async Task<bool> CancelOrderAsync(string orderId)
    {
        var result = await _longBridgeService.CancelOrderAsync(orderId);
        
        if (result)
        {
            var trade = await _dbContext.Trades.FirstOrDefaultAsync(t => t.OrderId == orderId);
            if (trade != null)
            {
                trade.Status = "cancelled";
                await _dbContext.SaveChangesAsync();
            }
            
            _logger.LogInformation("Order cancelled: {OrderId}", orderId);
        }
        
        return result;
    }
}
