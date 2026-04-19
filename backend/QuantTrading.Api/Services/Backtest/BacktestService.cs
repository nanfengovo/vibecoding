using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using QuantTrading.Api.Data;
using QuantTrading.Api.Models;
using QuantTrading.Api.Services.LongBridge;
using QuantTrading.Api.Services.Strategy;

namespace QuantTrading.Api.Services.Backtest;

public class BacktestService : IBacktestService
{
    private readonly QuantTradingDbContext _dbContext;
    private readonly ILongBridgeService _longBridgeService;
    private readonly IStrategyEngine _strategyEngine;
    private readonly ILogger<BacktestService> _logger;

    public BacktestService(
        QuantTradingDbContext dbContext,
        ILongBridgeService longBridgeService,
        IStrategyEngine strategyEngine,
        ILogger<BacktestService> logger)
    {
        _dbContext = dbContext;
        _longBridgeService = longBridgeService;
        _strategyEngine = strategyEngine;
        _logger = logger;
    }

    public async Task<List<Models.Backtest>> GetAllAsync()
    {
        return await _dbContext.Backtests
            .Include(b => b.Strategy)
            .OrderByDescending(b => b.CreatedAt)
            .ToListAsync();
    }

    public async Task<Models.Backtest?> GetByIdAsync(int id)
    {
        return await _dbContext.Backtests
            .Include(b => b.Strategy)
            .FirstOrDefaultAsync(b => b.Id == id);
    }

    public async Task<Models.Backtest> CreateAsync(int strategyId, DateTime startDate, DateTime endDate, decimal initialCapital, string name)
    {
        var backtest = new Models.Backtest
        {
            StrategyId = strategyId,
            Name = name,
            StartDate = startDate,
            EndDate = endDate,
            InitialCapital = initialCapital,
            Status = "pending",
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.Backtests.Add(backtest);
        await _dbContext.SaveChangesAsync();

        return backtest;
    }

    public async Task<Models.Backtest> RunAsync(int id)
    {
        var backtest = await _dbContext.Backtests
            .Include(b => b.Strategy)
            .FirstOrDefaultAsync(b => b.Id == id);

        if (backtest == null)
            throw new ArgumentException("Backtest not found");

        if (backtest.Strategy == null)
            throw new ArgumentException("Strategy not found");

        try
        {
            backtest.Status = "running";
            await _dbContext.SaveChangesAsync();

            var config = JsonConvert.DeserializeObject<StrategyConfig>(backtest.Strategy.ConfigJson);
            if (config == null || !config.Symbols.Any())
            {
                backtest.Status = "failed";
                backtest.ErrorMessage = "Strategy configuration is empty";
                await _dbContext.SaveChangesAsync();
                return backtest;
            }

            // Run backtest for each symbol
            var allTrades = new List<BacktestTrade>();
            var equityCurve = new List<EquityPoint>();
            var currentCapital = backtest.InitialCapital;
            var positions = new Dictionary<string, BacktestPosition>();

            foreach (var symbol in config.Symbols)
            {
                // Get historical data
                var klines = await _longBridgeService.GetKlineAsync(symbol, "1d", backtest.StartDate, backtest.EndDate, 500);
                
                if (!klines.Any())
                    continue;

                foreach (var kline in klines)
                {
                    // Simulate price data
                    var simulatedQuote = new StockQuote
                    {
                        Symbol = symbol,
                        Price = kline.Close,
                        Open = kline.Open,
                        High = kline.High,
                        Low = kline.Low,
                        Volume = kline.Volume,
                        Timestamp = kline.Timestamp
                    };

                    // Check entry conditions
                    var entryMet = await EvaluateConditionsWithHistoricalData(config.EntryConditions, klines, kline);
                    var exitMet = await EvaluateConditionsWithHistoricalData(config.ExitConditions, klines, kline);

                    // Handle entry
                    if (entryMet && !positions.ContainsKey(symbol))
                    {
                        var positionSize = config.RiskManagement?.MaxPositionPercent ?? 100;
                        var investAmount = currentCapital * positionSize / 100;
                        var quantity = Math.Floor(investAmount / kline.Close);

                        if (quantity > 0)
                        {
                            positions[symbol] = new BacktestPosition
                            {
                                Symbol = symbol,
                                Quantity = quantity,
                                EntryPrice = kline.Close,
                                EntryTime = kline.Timestamp
                            };
                            currentCapital -= quantity * kline.Close;
                        }
                    }

                    // Handle exit
                    if (exitMet && positions.ContainsKey(symbol))
                    {
                        var position = positions[symbol];
                        var pnl = (kline.Close - position.EntryPrice) * position.Quantity;
                        var pnlPercent = (kline.Close - position.EntryPrice) / position.EntryPrice * 100;

                        allTrades.Add(new BacktestTrade
                        {
                            Symbol = symbol,
                            Side = "long",
                            Quantity = position.Quantity,
                            EntryPrice = position.EntryPrice,
                            ExitPrice = kline.Close,
                            EntryTime = position.EntryTime,
                            ExitTime = kline.Timestamp,
                            PnL = pnl,
                            PnLPercent = pnlPercent,
                            EntryReason = "Entry conditions met",
                            ExitReason = "Exit conditions met"
                        });

                        currentCapital += position.Quantity * kline.Close;
                        positions.Remove(symbol);
                    }

                    // Check stop loss and take profit
                    if (positions.ContainsKey(symbol))
                    {
                        var position = positions[symbol];
                        var currentPnLPercent = (kline.Close - position.EntryPrice) / position.EntryPrice * 100;

                        var stopLossTriggered = config.RiskManagement?.StopLossPercent.HasValue == true && 
                            currentPnLPercent <= -config.RiskManagement.StopLossPercent.Value;
                        
                        var takeProfitTriggered = config.RiskManagement?.TakeProfitPercent.HasValue == true && 
                            currentPnLPercent >= config.RiskManagement.TakeProfitPercent.Value;

                        if (stopLossTriggered || takeProfitTriggered)
                        {
                            var pnl = (kline.Close - position.EntryPrice) * position.Quantity;

                            allTrades.Add(new BacktestTrade
                            {
                                Symbol = symbol,
                                Side = "long",
                                Quantity = position.Quantity,
                                EntryPrice = position.EntryPrice,
                                ExitPrice = kline.Close,
                                EntryTime = position.EntryTime,
                                ExitTime = kline.Timestamp,
                                PnL = pnl,
                                PnLPercent = currentPnLPercent,
                                EntryReason = "Entry conditions met",
                                ExitReason = stopLossTriggered ? "Stop loss triggered" : "Take profit triggered"
                            });

                            currentCapital += position.Quantity * kline.Close;
                            positions.Remove(symbol);
                        }
                    }

                    // Record equity curve
                    var positionValue = positions.Sum(p => p.Value.Quantity * kline.Close);
                    var totalEquity = currentCapital + positionValue;
                    var maxEquity = equityCurve.Any() ? equityCurve.Max(e => e.Equity) : backtest.InitialCapital;
                    var drawdown = maxEquity > 0 ? (maxEquity - totalEquity) / maxEquity * 100 : 0;

                    equityCurve.Add(new EquityPoint
                    {
                        Date = kline.Timestamp,
                        Equity = totalEquity,
                        Drawdown = drawdown,
                        DailyReturn = equityCurve.Any() ? (totalEquity - equityCurve.Last().Equity) / equityCurve.Last().Equity * 100 : 0
                    });
                }
            }

            // Close any remaining positions at the end
            foreach (var position in positions.Values)
            {
                var lastKline = await _longBridgeService.GetKlineAsync(position.Symbol, "1d", count: 1);
                if (lastKline.Any())
                {
                    var lastPrice = lastKline.First().Close;
                    var pnl = (lastPrice - position.EntryPrice) * position.Quantity;
                    var pnlPercent = (lastPrice - position.EntryPrice) / position.EntryPrice * 100;

                    allTrades.Add(new BacktestTrade
                    {
                        Symbol = position.Symbol,
                        Side = "long",
                        Quantity = position.Quantity,
                        EntryPrice = position.EntryPrice,
                        ExitPrice = lastPrice,
                        EntryTime = position.EntryTime,
                        ExitTime = backtest.EndDate,
                        PnL = pnl,
                        PnLPercent = pnlPercent,
                        EntryReason = "Entry conditions met",
                        ExitReason = "End of backtest period"
                    });

                    currentCapital += position.Quantity * lastPrice;
                }
            }

            // Calculate statistics
            backtest.FinalCapital = currentCapital;
            backtest.TotalReturn = currentCapital - backtest.InitialCapital;
            backtest.TotalReturnPercent = (currentCapital - backtest.InitialCapital) / backtest.InitialCapital * 100;
            
            var tradingDays = (backtest.EndDate - backtest.StartDate).TotalDays;
            var years = tradingDays / 365.0;
            backtest.AnnualizedReturn = years > 0 ? 
                (decimal)(Math.Pow((double)(currentCapital / backtest.InitialCapital), 1.0 / years) - 1) * 100 : 0;

            backtest.MaxDrawdown = equityCurve.Any() ? equityCurve.Max(e => e.Drawdown) : 0;
            backtest.TotalTrades = allTrades.Count;
            backtest.WinningTrades = allTrades.Count(t => t.PnL > 0);
            backtest.LosingTrades = allTrades.Count(t => t.PnL <= 0);
            backtest.WinRate = allTrades.Count > 0 ? (decimal)backtest.WinningTrades / backtest.TotalTrades * 100 : 0;

            var winningPnLs = allTrades.Where(t => t.PnL > 0).Select(t => t.PnL).ToList();
            var losingPnLs = allTrades.Where(t => t.PnL <= 0).Select(t => Math.Abs(t.PnL)).ToList();

            backtest.AverageWin = winningPnLs.Any() ? winningPnLs.Average() : 0;
            backtest.AverageLoss = losingPnLs.Any() ? losingPnLs.Average() : 0;
            backtest.ProfitFactor = backtest.AverageLoss > 0 ? backtest.AverageWin / backtest.AverageLoss : 0;

            // Calculate Sharpe Ratio (simplified)
            if (equityCurve.Count > 1)
            {
                var dailyReturns = equityCurve.Skip(1).Select(e => (double)e.DailyReturn).ToList();
                var avgReturn = dailyReturns.Average();
                var stdDev = Math.Sqrt(dailyReturns.Average(r => Math.Pow(r - avgReturn, 2)));
                backtest.SharpeRatio = stdDev > 0 ? (decimal)(avgReturn / stdDev * Math.Sqrt(252)) : 0;
            }

            backtest.EquityCurveJson = JsonConvert.SerializeObject(equityCurve);
            backtest.TradesJson = JsonConvert.SerializeObject(allTrades);
            backtest.Status = "completed";
            backtest.CompletedAt = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync();
            _logger.LogInformation("Backtest completed: {Name} (ID: {Id})", backtest.Name, backtest.Id);

            return backtest;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Backtest failed: {Name} (ID: {Id})", backtest.Name, id);
            backtest.Status = "failed";
            backtest.ErrorMessage = ex.Message;
            await _dbContext.SaveChangesAsync();
            throw;
        }
    }

    private async Task<bool> EvaluateConditionsWithHistoricalData(
        List<StrategyCondition> conditions, 
        List<StockKline> klines, 
        StockKline currentKline)
    {
        if (!conditions.Any())
            return false;

        var historicalKlines = klines.Where(k => k.Timestamp <= currentKline.Timestamp).ToList();
        if (historicalKlines.Count < 20) // Need minimum data for indicators
            return false;

        var results = new List<bool>();

        foreach (var condition in conditions)
        {
            var result = EvaluateConditionWithHistoricalData(condition, historicalKlines, currentKline);
            results.Add(result);
        }

        var finalResult = results[0];
        for (int i = 1; i < conditions.Count; i++)
        {
            var logicalOp = conditions[i].LogicalOperator;
            if (logicalOp == "OR")
                finalResult = finalResult || results[i];
            else
                finalResult = finalResult && results[i];
        }

        return finalResult;
    }

    private bool EvaluateConditionWithHistoricalData(
        StrategyCondition condition, 
        List<StockKline> klines, 
        StockKline currentKline)
    {
        try
        {
            var closes = klines.Select(k => k.Close).ToList();
            decimal leftValue;
            decimal rightValue;

            switch (condition.Type.ToLower())
            {
                case "price":
                    leftValue = currentKline.Close;
                    rightValue = Convert.ToDecimal(condition.Value);
                    break;

                case "indicator":
                    leftValue = CalculateIndicator(klines, condition.Indicator, condition.Parameters);
                    if (!string.IsNullOrEmpty(condition.CompareIndicator))
                    {
                        rightValue = CalculateIndicator(klines, condition.CompareIndicator, 
                            condition.CompareParameters ?? new Dictionary<string, object>());
                    }
                    else
                    {
                        rightValue = Convert.ToDecimal(condition.Value);
                    }
                    break;

                case "volume":
                    leftValue = currentKline.Volume;
                    rightValue = Convert.ToDecimal(condition.Value);
                    break;

                default:
                    return false;
            }

            return condition.Operator switch
            {
                ">" => leftValue > rightValue,
                "<" => leftValue < rightValue,
                ">=" => leftValue >= rightValue,
                "<=" => leftValue <= rightValue,
                "==" => Math.Abs(leftValue - rightValue) < 0.0001m,
                _ => false
            };
        }
        catch
        {
            return false;
        }
    }

    private decimal CalculateIndicator(List<StockKline> klines, string indicator, Dictionary<string, object> parameters)
    {
        var closes = klines.Select(k => k.Close).ToList();
        var period = parameters.TryGetValue("period", out var p) ? Convert.ToInt32(p) : 20;

        return indicator.ToUpper() switch
        {
            "MA" or "SMA" => closes.Count >= period ? closes.TakeLast(period).Average() : closes.LastOrDefault(),
            "EMA" => CalculateEMA(closes, period),
            "RSI" => CalculateRSI(closes, period),
            _ => closes.LastOrDefault()
        };
    }

    private decimal CalculateEMA(List<decimal> prices, int period)
    {
        if (prices.Count < period) return prices.LastOrDefault();
        
        var multiplier = 2m / (period + 1);
        var ema = prices.Take(period).Average();
        
        foreach (var price in prices.Skip(period))
        {
            ema = (price - ema) * multiplier + ema;
        }
        
        return ema;
    }

    private decimal CalculateRSI(List<decimal> prices, int period)
    {
        if (prices.Count < period + 1) return 50;
        
        var gains = new List<decimal>();
        var losses = new List<decimal>();
        
        for (int i = 1; i < prices.Count; i++)
        {
            var change = prices[i] - prices[i - 1];
            gains.Add(change > 0 ? change : 0);
            losses.Add(change < 0 ? Math.Abs(change) : 0);
        }
        
        var avgGain = gains.TakeLast(period).Average();
        var avgLoss = losses.TakeLast(period).Average();
        
        if (avgLoss == 0) return 100;
        
        var rs = avgGain / avgLoss;
        return 100 - (100 / (1 + rs));
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var backtest = await _dbContext.Backtests.FindAsync(id);
        if (backtest == null)
            return false;

        _dbContext.Backtests.Remove(backtest);
        await _dbContext.SaveChangesAsync();

        return true;
    }

    private class BacktestPosition
    {
        public string Symbol { get; set; } = string.Empty;
        public decimal Quantity { get; set; }
        public decimal EntryPrice { get; set; }
        public DateTime EntryTime { get; set; }
    }
}
