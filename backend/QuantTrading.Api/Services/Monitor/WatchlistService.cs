using Microsoft.EntityFrameworkCore;
using QuantTrading.Api.Data;
using QuantTrading.Api.Models;
using QuantTrading.Api.Services.LongBridge;

namespace QuantTrading.Api.Services.Monitor;

public class WatchlistService : IWatchlistService
{
    private readonly QuantTradingDbContext _dbContext;
    private readonly ILongBridgeService _longBridgeService;
    private readonly ILogger<WatchlistService> _logger;

    public WatchlistService(
        QuantTradingDbContext dbContext,
        ILongBridgeService longBridgeService,
        ILogger<WatchlistService> logger)
    {
        _dbContext = dbContext;
        _longBridgeService = longBridgeService;
        _logger = logger;
    }

    public async Task<List<Stock>> GetWatchlistAsync()
    {
        return await _dbContext.Stocks
            .Where(s => s.IsWatched)
            .OrderBy(s => s.Symbol)
            .ToListAsync();
    }

    public async Task<Stock?> AddToWatchlistAsync(string symbol, string? notes = null)
    {
        var normalizedSymbol = NormalizeSymbol(symbol);
        if (string.IsNullOrWhiteSpace(normalizedSymbol))
        {
            return null;
        }

        var existing = await _dbContext.Stocks.FirstOrDefaultAsync(s => s.Symbol == normalizedSymbol);
        
        if (existing != null)
        {
            existing.IsWatched = true;
            if (!string.IsNullOrWhiteSpace(notes))
            {
                existing.Notes = notes.Trim();
            }

            await _dbContext.SaveChangesAsync();
            return existing;
        }
        
        var stockInfo = await _longBridgeService.GetStockInfoAsync(normalizedSymbol)
            ?? CreateFallbackStock(normalizedSymbol);

        if (stockInfo == null)
        {
            _logger.LogWarning("Stock not found: {Symbol}", symbol);
            return null;
        }
        
        stockInfo.IsWatched = true;
        stockInfo.Notes = string.IsNullOrWhiteSpace(notes) ? stockInfo.Notes : notes.Trim();

        _dbContext.Stocks.Add(stockInfo);
        await _dbContext.SaveChangesAsync();
        
        _logger.LogInformation("Added to watchlist: {Symbol}", normalizedSymbol);
        return stockInfo;
    }

    public async Task<bool> RemoveFromWatchlistAsync(int id)
    {
        var stock = await _dbContext.Stocks.FindAsync(id);
        if (stock == null)
            return false;
        
        stock.IsWatched = false;
        await _dbContext.SaveChangesAsync();
        
        _logger.LogInformation("Removed from watchlist: {Symbol}", stock.Symbol);
        return true;
    }

    public async Task<Stock?> UpdateStockNotesAsync(int id, string notes)
    {
        var stock = await _dbContext.Stocks.FindAsync(id);
        if (stock == null)
            return null;
        
        stock.Notes = notes;
        await _dbContext.SaveChangesAsync();
        
        return stock;
    }

    public async Task RefreshWatchlistAsync()
    {
        var watchlist = await GetWatchlistAsync();
        
        if (!watchlist.Any())
            return;
        
        var symbols = watchlist.Select(s => s.Symbol).ToList();
        var quotes = await _longBridgeService.GetQuotesAsync(symbols);
        
        foreach (var quote in quotes)
        {
            var stock = watchlist.FirstOrDefault(s => s.Symbol.Equals(quote.Symbol, StringComparison.OrdinalIgnoreCase));
            if (stock != null)
            {
                var previousClose = stock.CurrentPrice > 0 ? stock.CurrentPrice : quote.Price;
                
                stock.CurrentPrice = quote.Price;
                stock.Open = quote.Open;
                stock.High = quote.High;
                stock.Low = quote.Low;
                stock.Volume = quote.Volume;
                stock.Change = quote.Price - previousClose;
                stock.ChangePercent = previousClose > 0 ? (quote.Price - previousClose) / previousClose * 100 : 0;
                stock.LastUpdated = DateTime.UtcNow;
            }
        }
        
        await _dbContext.SaveChangesAsync();
        _logger.LogInformation("Refreshed watchlist: {Count} stocks", watchlist.Count);
    }

    private static Stock CreateFallbackStock(string symbol)
    {
        return new Stock
        {
            Symbol = symbol,
            Name = symbol,
            Market = InferMarket(symbol),
            LastUpdated = DateTime.UtcNow
        };
    }

    private static string NormalizeSymbol(string symbol)
    {
        var normalized = (symbol ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        if (normalized.Contains('.'))
        {
            return normalized;
        }

        if (normalized.All(char.IsDigit) && normalized.Length == 5)
        {
            return $"{normalized}.HK";
        }

        return $"{normalized}.US";
    }

    private static string InferMarket(string symbol)
    {
        var suffix = symbol.Split('.').LastOrDefault()?.ToUpperInvariant();
        return suffix switch
        {
            "HK" => "HK",
            "CN" => "CN",
            "SG" => "SG",
            "US" => "US",
            _ => "US"
        };
    }
}
