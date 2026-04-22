using Microsoft.EntityFrameworkCore;
using QuantTrading.Api.Data;
using QuantTrading.Api.Models;
using QuantTrading.Api.Services.LongBridge;
using QuantTrading.Api.Services.Realtime;

namespace QuantTrading.Api.Services.Monitor;

public class WatchlistService : IWatchlistService
{
    private readonly QuantTradingDbContext _dbContext;
    private readonly ILongBridgeService _longBridgeService;
    private readonly IRealtimePushService _realtimePushService;
    private readonly ILogger<WatchlistService> _logger;

    public WatchlistService(
        QuantTradingDbContext dbContext,
        ILongBridgeService longBridgeService,
        IRealtimePushService realtimePushService,
        ILogger<WatchlistService> logger)
    {
        _dbContext = dbContext;
        _longBridgeService = longBridgeService;
        _realtimePushService = realtimePushService;
        _logger = logger;
    }

    public async Task<List<Stock>> GetWatchlistAsync()
    {
        var rows = await _dbContext.Stocks
            .Where(s => s.IsWatched)
            .OrderBy(s => s.Symbol)
            .ToListAsync();

        var changed = false;
        foreach (var item in rows.Where(s => IsPoorDisplayName(s.Name, s.Symbol)).ToList())
        {
            try
            {
                var latest = await _longBridgeService.GetStockInfoAsync(item.Symbol);
                if (latest == null)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(latest.Name) && !string.Equals(item.Name, latest.Name, StringComparison.Ordinal))
                {
                    item.Name = latest.Name;
                    changed = true;
                }

                if (!string.IsNullOrWhiteSpace(latest.Market) && !string.Equals(item.Market, latest.Market, StringComparison.OrdinalIgnoreCase))
                {
                    item.Market = latest.Market;
                    changed = true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to enrich watchlist stock name for {Symbol}", item.Symbol);
            }

            item.Currency = ResolveCurrencyCode(item.Symbol, item.Market);
        }

        if (changed)
        {
            await _dbContext.SaveChangesAsync();
        }

        foreach (var item in rows)
        {
            item.Currency = ResolveCurrencyCode(item.Symbol, item.Market);
        }

        return rows;
    }

    public async Task<Stock?> AddToWatchlistAsync(string symbol, string? notes = null)
    {
        var normalizedSymbol = NormalizeSymbol(symbol);
        if (string.IsNullOrWhiteSpace(normalizedSymbol))
        {
            return null;
        }

        var baseSymbol = normalizedSymbol.Split('.').FirstOrDefault() ?? normalizedSymbol;
        var symbolCandidates = new[]
        {
            normalizedSymbol,
            baseSymbol,
            $"{baseSymbol}.US",
            $"{baseSymbol}.HK",
            $"{baseSymbol}.CN",
            $"{baseSymbol}.SH",
            $"{baseSymbol}.SZ",
            $"{baseSymbol}.SG"
        };

        var existing = await _dbContext.Stocks
            .FirstOrDefaultAsync(s => symbolCandidates.Contains(s.Symbol));
        
        if (existing != null)
        {
            if (!HasValidMarketData(existing))
            {
                _logger.LogWarning("Reject existing invalid watchlist symbol: {Symbol}", existing.Symbol);
                if (existing.IsWatched)
                {
                    existing.IsWatched = false;
                    await _dbContext.SaveChangesAsync();
                }
                return null;
            }

            existing.IsWatched = true;
            existing.Currency = ResolveCurrencyCode(existing.Symbol, existing.Market);
            if (!string.IsNullOrWhiteSpace(notes))
            {
                existing.Notes = notes.Trim();
            }

            await _dbContext.SaveChangesAsync();
            return existing;
        }
        
        // 优先使用搜索结果校验；若 security list 不完整，则回退到直接按代码拉取详情。
        var candidates = await _longBridgeService.SearchStocksAsync(normalizedSymbol);
        var matched = candidates.FirstOrDefault(s => SymbolEquals(s.Symbol, normalizedSymbol));
        var stockInfo = await _longBridgeService.GetStockInfoAsync(matched?.Symbol ?? normalizedSymbol);

        if (stockInfo == null)
        {
            _logger.LogWarning("Stock detail not found from LongBridge: {Symbol}", normalizedSymbol);
            return null;
        }

        if (!HasValidMarketData(stockInfo))
        {
            _logger.LogWarning("Reject invalid watchlist symbol with empty market data: {Symbol}", normalizedSymbol);
            return null;
        }
        
        stockInfo.IsWatched = true;
        stockInfo.Currency = ResolveCurrencyCode(stockInfo.Symbol, stockInfo.Market);
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
            var stock = watchlist.FirstOrDefault(s => SymbolEquals(s.Symbol, quote.Symbol));
            if (stock != null)
            {
                var previousClose = quote.PreviousClose > 0
                    ? quote.PreviousClose
                    : (stock.PreviousClose > 0 ? stock.PreviousClose : stock.CurrentPrice);
                
                if (quote.Price > 0)
                {
                    stock.CurrentPrice = quote.Price;
                }

                if (previousClose > 0)
                {
                    stock.PreviousClose = previousClose;
                }

                if (quote.Open > 0)
                {
                    stock.Open = quote.Open;
                }

                if (quote.High > 0)
                {
                    stock.High = quote.High;
                }

                if (quote.Low > 0)
                {
                    stock.Low = quote.Low;
                }

                if (quote.Volume > 0)
                {
                    stock.Volume = quote.Volume;
                }

                stock.Change = stock.CurrentPrice - previousClose;
                stock.ChangePercent = previousClose > 0 ? (stock.CurrentPrice - previousClose) / previousClose * 100 : 0;
                stock.LastUpdated = quote.Timestamp > DateTime.UnixEpoch
                    ? DateTime.SpecifyKind(quote.Timestamp, DateTimeKind.Utc)
                    : DateTime.UtcNow;
                stock.Currency = ResolveCurrencyCode(stock.Symbol, stock.Market);

                await _realtimePushService.PushQuoteAsync(stock.Symbol, new
                {
                    symbol = stock.Symbol,
                    name = stock.Name,
                    current = stock.CurrentPrice,
                    previousClose = stock.PreviousClose,
                    change = stock.Change,
                    changePercent = stock.ChangePercent,
                    high = stock.High,
                    low = stock.Low,
                    open = stock.Open,
                    volume = stock.Volume,
                    turnover = quote.Turnover,
                    timestamp = stock.LastUpdated
                });
            }
        }
        
        await _dbContext.SaveChangesAsync();
        _logger.LogInformation("Refreshed watchlist: {Count} stocks", watchlist.Count);
    }

    private static string ResolveCurrencyCode(string? symbol, string? market)
    {
        var marketCode = InferMarket(symbol, market);
        return marketCode switch
        {
            "SH" or "SZ" => "CNY",
            "HK" => "HKD",
            "SG" => "SGD",
            _ => "USD"
        };
    }

    private static string InferMarket(string? symbol, string? market)
    {
        var explicitMarket = (market ?? string.Empty).Trim().ToUpperInvariant();
        if (!string.IsNullOrWhiteSpace(explicitMarket))
        {
            return explicitMarket;
        }

        var normalizedSymbol = NormalizeSymbol(symbol ?? string.Empty);
        if (normalizedSymbol.Contains('.'))
        {
            return normalizedSymbol.Split('.').LastOrDefault()?.ToUpperInvariant() ?? "US";
        }

        return "US";
    }

    private static string NormalizeSymbol(string symbol)
    {
        var normalized = (symbol ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        if (normalized.StartsWith("SH", StringComparison.OrdinalIgnoreCase)
            && normalized.Length == 8
            && normalized.Skip(2).All(char.IsDigit))
        {
            return $"{normalized[2..]}.SH";
        }

        if (normalized.StartsWith("SZ", StringComparison.OrdinalIgnoreCase)
            && normalized.Length == 8
            && normalized.Skip(2).All(char.IsDigit))
        {
            return $"{normalized[2..]}.SZ";
        }

        if (normalized.Contains('.'))
        {
            return normalized;
        }

        if (normalized.All(char.IsDigit) && normalized.Length == 6)
        {
            var market = normalized.StartsWith("6", StringComparison.Ordinal)
                || normalized.StartsWith("9", StringComparison.Ordinal)
                || normalized.StartsWith("5", StringComparison.Ordinal)
                ? "SH"
                : "SZ";
            return $"{normalized}.{market}";
        }

        if (normalized.All(char.IsDigit) && normalized.Length == 5)
        {
            return $"{normalized}.HK";
        }

        return $"{normalized}.US";
    }

    private static bool SymbolEquals(string? left, string? right)
    {
        var normalizedLeft = NormalizeSymbol(left ?? string.Empty);
        var normalizedRight = NormalizeSymbol(right ?? string.Empty);
        if (normalizedLeft.Equals(normalizedRight, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var leftBase = normalizedLeft.Split('.').FirstOrDefault() ?? normalizedLeft;
        var rightBase = normalizedRight.Split('.').FirstOrDefault() ?? normalizedRight;
        return leftBase.Equals(rightBase, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasValidMarketData(Stock stock)
    {
        return stock.CurrentPrice > 0
            || stock.PreviousClose > 0
            || stock.Open > 0
            || stock.High > 0
            || stock.Low > 0
            || stock.Volume > 0
            || stock.High52Week > 0
            || stock.Low52Week > 0
            || stock.AvgVolume > 0
            || stock.MarketCap > 0;
    }

    private static bool IsPoorDisplayName(string? name, string symbol)
    {
        var current = (name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(current))
        {
            return true;
        }

        var normalized = NormalizeSymbol(symbol);
        var baseSymbol = normalized.Split('.').FirstOrDefault() ?? normalized;
        if (current.Equals(normalized, StringComparison.OrdinalIgnoreCase)
            || current.Equals(baseSymbol, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return current.All(char.IsDigit);
    }

}
