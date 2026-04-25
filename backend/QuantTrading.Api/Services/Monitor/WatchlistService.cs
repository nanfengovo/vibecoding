using Microsoft.EntityFrameworkCore;
using QuantTrading.Api.Data;
using QuantTrading.Api.Models;
using QuantTrading.Api.Services.Auth;
using QuantTrading.Api.Services.LongBridge;
using QuantTrading.Api.Services.Realtime;

namespace QuantTrading.Api.Services.Monitor;

public class WatchlistService : IWatchlistService
{
    private readonly QuantTradingDbContext _dbContext;
    private readonly ICurrentUserService _currentUser;
    private readonly ILongBridgeService _longBridgeService;
    private readonly IRealtimePushService _realtimePushService;
    private readonly ILogger<WatchlistService> _logger;

    public WatchlistService(
        QuantTradingDbContext dbContext,
        ICurrentUserService currentUser,
        ILongBridgeService longBridgeService,
        IRealtimePushService realtimePushService,
        ILogger<WatchlistService> logger)
    {
        _dbContext = dbContext;
        _currentUser = currentUser;
        _longBridgeService = longBridgeService;
        _realtimePushService = realtimePushService;
        _logger = logger;
    }

    public async Task<List<Stock>> GetWatchlistAsync()
    {
        var userId = await _currentUser.GetEffectiveUserIdAsync();
        await EnsureLegacyWatchlistMigratedAsync(userId);

        var watchItems = await _dbContext.UserWatchlistItems
            .Where(item => item.UserId == userId)
            .OrderBy(item => item.Symbol)
            .ToListAsync();

        var symbols = watchItems.Select(item => item.Symbol).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var rows = await _dbContext.Stocks
            .AsNoTracking()
            .Where(s => symbols.Contains(s.Symbol))
            .ToListAsync();

        var changed = false;
        foreach (var watchItem in watchItems)
        {
            if (rows.Any(stock => SymbolEquals(stock.Symbol, watchItem.Symbol)))
            {
                continue;
            }

            try
            {
                var latest = await _longBridgeService.GetStockInfoAsync(watchItem.Symbol);
                if (latest != null && HasValidMarketData(latest))
                {
                    latest.Currency = ResolveCurrencyCode(latest.Symbol, latest.Market);
                    _dbContext.Stocks.Add(latest);
                    rows.Add(latest);
                    changed = true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to hydrate watchlist stock {Symbol}", watchItem.Symbol);
            }
        }

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
                    var tracked = await _dbContext.Stocks.FirstOrDefaultAsync(stock => stock.Symbol == item.Symbol);
                    if (tracked != null)
                    {
                        tracked.Name = latest.Name;
                    }
                    changed = true;
                }

                if (!string.IsNullOrWhiteSpace(latest.Market) && !string.Equals(item.Market, latest.Market, StringComparison.OrdinalIgnoreCase))
                {
                    item.Market = latest.Market;
                    var tracked = await _dbContext.Stocks.FirstOrDefaultAsync(stock => stock.Symbol == item.Symbol);
                    if (tracked != null)
                    {
                        tracked.Market = latest.Market;
                    }
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

        return watchItems
            .Select(item =>
            {
                var stock = rows.FirstOrDefault(row => SymbolEquals(row.Symbol, item.Symbol));
                if (stock == null)
                {
                    return null;
                }

                return ToWatchlistStock(stock, item);
            })
            .Where(item => item != null)
            .Cast<Stock>()
            .ToList();
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

        var userId = await _currentUser.GetEffectiveUserIdAsync();
        var existingWatchItem = await _dbContext.UserWatchlistItems
            .FirstOrDefaultAsync(item => item.UserId == userId && symbolCandidates.Contains(item.Symbol));
        if (existingWatchItem != null)
        {
            if (!string.IsNullOrWhiteSpace(notes))
            {
                existingWatchItem.Notes = notes.Trim();
                existingWatchItem.UpdatedAt = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync();
            }

            var watchedStock = await _dbContext.Stocks.FirstOrDefaultAsync(s => s.Symbol == existingWatchItem.Symbol)
                ?? await _longBridgeService.GetStockInfoAsync(existingWatchItem.Symbol);
            if (watchedStock == null || !HasValidMarketData(watchedStock))
            {
                return null;
            }

            return ToWatchlistStock(watchedStock, existingWatchItem);
        }

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
            var watchItem = new UserWatchlistItem
            {
                UserId = userId,
                Symbol = existing.Symbol,
                Notes = string.IsNullOrWhiteSpace(notes) ? existing.Notes ?? string.Empty : notes.Trim(),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _dbContext.UserWatchlistItems.Add(watchItem);

            if (!string.IsNullOrWhiteSpace(watchItem.Notes))
            {
                existing.Notes = watchItem.Notes;
            }

            await _dbContext.SaveChangesAsync();
            return ToWatchlistStock(existing, watchItem);
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
        var createdWatchItem = new UserWatchlistItem
        {
            UserId = userId,
            Symbol = stockInfo.Symbol,
            Notes = stockInfo.Notes ?? string.Empty,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _dbContext.UserWatchlistItems.Add(createdWatchItem);
        await _dbContext.SaveChangesAsync();
        
        _logger.LogInformation("Added to watchlist: {Symbol}", normalizedSymbol);
        return ToWatchlistStock(stockInfo, createdWatchItem);
    }

    public async Task<bool> RemoveFromWatchlistAsync(int id)
    {
        var userId = await _currentUser.GetEffectiveUserIdAsync();
        var item = await _dbContext.UserWatchlistItems
            .FirstOrDefaultAsync(row => row.Id == id && row.UserId == userId);
        if (item == null)
            return false;
        
        _dbContext.UserWatchlistItems.Remove(item);
        await _dbContext.SaveChangesAsync();
        
        _logger.LogInformation("Removed from watchlist: {Symbol}", item.Symbol);
        return true;
    }

    public async Task<Stock?> UpdateStockNotesAsync(int id, string notes)
    {
        var userId = await _currentUser.GetEffectiveUserIdAsync();
        var item = await _dbContext.UserWatchlistItems
            .FirstOrDefaultAsync(row => row.Id == id && row.UserId == userId);
        if (item == null)
            return null;
        
        item.Notes = notes;
        item.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        var stock = await _dbContext.Stocks.FirstOrDefaultAsync(row => row.Symbol == item.Symbol);
        if (stock != null)
        {
            return ToWatchlistStock(stock, item);
        }

        return null;
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

    private async Task EnsureLegacyWatchlistMigratedAsync(int userId)
    {
        var hasUserItems = await _dbContext.UserWatchlistItems.AnyAsync(item => item.UserId == userId);
        if (hasUserItems)
        {
            return;
        }

        var legacyRows = await _dbContext.Stocks
            .Where(s => s.IsWatched)
            .OrderBy(s => s.Symbol)
            .ToListAsync();
        if (legacyRows.Count == 0)
        {
            return;
        }

        foreach (var stock in legacyRows)
        {
            _dbContext.UserWatchlistItems.Add(new UserWatchlistItem
            {
                UserId = userId,
                Symbol = stock.Symbol,
                Notes = stock.Notes ?? string.Empty,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        await _dbContext.SaveChangesAsync();
    }

    private static Stock ToWatchlistStock(Stock stock, UserWatchlistItem item)
    {
        return new Stock
        {
            Id = item.Id,
            Symbol = stock.Symbol,
            Name = stock.Name,
            Market = stock.Market,
            Currency = ResolveCurrencyCode(stock.Symbol, stock.Market),
            CurrentPrice = stock.CurrentPrice,
            PreviousClose = stock.PreviousClose,
            Open = stock.Open,
            High = stock.High,
            Low = stock.Low,
            Volume = stock.Volume,
            Change = stock.Change,
            ChangePercent = stock.ChangePercent,
            LastUpdated = stock.LastUpdated,
            IsWatched = true,
            Notes = item.Notes,
            MarketCap = stock.MarketCap,
            High52Week = stock.High52Week,
            Low52Week = stock.Low52Week,
            AvgVolume = stock.AvgVolume,
            Pe = stock.Pe,
            Eps = stock.Eps,
            Dividend = stock.Dividend
        };
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
