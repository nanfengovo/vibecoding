using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using QuantTrading.Api.Data;
using QuantTrading.Api.Models;
using QuantTrading.Api.Services.LongBridge;
using QuantTrading.Api.Services.Notification;

namespace QuantTrading.Api.Services.Strategy;

public class StrategyEngine : IStrategyEngine
{
    private readonly QuantTradingDbContext _dbContext;
    private readonly ILongBridgeService _longBridgeService;
    private readonly INotificationService _notificationService;
    private readonly ILogger<StrategyEngine> _logger;

    public StrategyEngine(
        QuantTradingDbContext dbContext,
        ILongBridgeService longBridgeService,
        INotificationService notificationService,
        ILogger<StrategyEngine> logger)
    {
        _dbContext = dbContext;
        _longBridgeService = longBridgeService;
        _notificationService = notificationService;
        _logger = logger;
    }

    public async Task ExecuteStrategyAsync(Models.Strategy strategy)
    {
        if (!strategy.IsEnabled)
            return;

        try
        {
            var config = JsonConvert.DeserializeObject<StrategyConfig>(strategy.ConfigJson);
            if (config == null || !config.Symbols.Any())
                return;

            strategy.IsActive = true;
            strategy.LastExecutedAt = DateTime.UtcNow;
            strategy.ExecutionCount++;

            foreach (var symbol in config.Symbols)
            {
                var entryConditionsMet = await EvaluateConditionsAsync(config, symbol, true);
                var exitConditionsMet = await EvaluateConditionsAsync(config, symbol, false);

                if (entryConditionsMet)
                {
                    await ExecuteActionsAsync(config.Actions.Where(a => a.Type != "sell").ToList(), symbol, config);
                    await LogExecutionAsync(strategy.Id, symbol, "entry", "executed");
                }

                if (exitConditionsMet)
                {
                    await ExecuteActionsAsync(config.Actions.Where(a => a.Type == "sell").ToList(), symbol, config);
                    await LogExecutionAsync(strategy.Id, symbol, "exit", "executed");
                }
            }

            strategy.IsActive = false;
            await _dbContext.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing strategy {StrategyId}", strategy.Id);
            await LogExecutionAsync(strategy.Id, "", "error", "failed", ex.Message);
            
            if (JsonConvert.DeserializeObject<StrategyConfig>(strategy.ConfigJson)?.NotificationSettings?.NotifyOnError == true)
            {
                await _notificationService.SendAsync($"策略执行错误: {strategy.Name}", ex.Message);
            }
        }
    }

    public async Task<bool> EvaluateConditionsAsync(StrategyConfig config, string symbol)
    {
        return await EvaluateConditionsAsync(config, symbol, true);
    }

    private async Task<bool> EvaluateConditionsAsync(StrategyConfig config, string symbol, bool isEntry)
    {
        var conditions = isEntry ? config.EntryConditions : config.ExitConditions;
        if (!conditions.Any())
            return false;

        var results = new List<bool>();

        foreach (var condition in conditions)
        {
            var result = await EvaluateSingleConditionAsync(condition, symbol);
            results.Add(result);
        }

        // Evaluate logical operators
        if (!results.Any())
            return false;

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

    private async Task<bool> EvaluateSingleConditionAsync(StrategyCondition condition, string symbol)
    {
        try
        {
            decimal leftValue;
            decimal rightValue;

            switch (condition.Type.ToLower())
            {
                case "price":
                    var quote = await _longBridgeService.GetQuoteAsync(symbol);
                    if (quote == null) return false;
                    leftValue = quote.Price;
                    rightValue = Convert.ToDecimal(condition.Value);
                    break;

                case "indicator":
                    leftValue = await CalculateIndicatorAsync(symbol, condition.Indicator, condition.Parameters);
                    
                    if (!string.IsNullOrEmpty(condition.CompareIndicator))
                    {
                        rightValue = await CalculateIndicatorAsync(symbol, condition.CompareIndicator, 
                            condition.CompareParameters ?? new Dictionary<string, object>());
                    }
                    else
                    {
                        rightValue = Convert.ToDecimal(condition.Value);
                    }
                    break;

                case "volume":
                    var volQuote = await _longBridgeService.GetQuoteAsync(symbol);
                    if (volQuote == null) return false;
                    leftValue = volQuote.Volume;
                    rightValue = Convert.ToDecimal(condition.Value);
                    break;

                case "change_percent":
                    var cpQuote = await _longBridgeService.GetQuoteAsync(symbol);
                    if (cpQuote == null) return false;
                    var stockInfo = await _longBridgeService.GetStockInfoAsync(symbol);
                    if (stockInfo?.PreviousClose > 0)
                    {
                        leftValue = (cpQuote.Price - stockInfo.PreviousClose) / stockInfo.PreviousClose * 100;
                    }
                    else
                    {
                        return false;
                    }
                    rightValue = Convert.ToDecimal(condition.Value);
                    break;

                default:
                    return false;
            }

            return EvaluateOperator(leftValue, rightValue, condition.Operator);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error evaluating condition for {Symbol}", symbol);
            return false;
        }
    }

    private bool EvaluateOperator(decimal left, decimal right, string op)
    {
        return op switch
        {
            ">" => left > right,
            "<" => left < right,
            ">=" => left >= right,
            "<=" => left <= right,
            "==" => Math.Abs(left - right) < 0.0001m,
            "!=" => Math.Abs(left - right) >= 0.0001m,
            _ => false
        };
    }

    public async Task<decimal> CalculateIndicatorAsync(string symbol, string indicator, Dictionary<string, object> parameters)
    {
        var klines = await _longBridgeService.GetKlineAsync(symbol, "1d", count: 100);
        if (!klines.Any())
            return 0;

        var closes = klines.Select(k => k.Close).ToList();

        return indicator.ToUpper() switch
        {
            "MA" or "SMA" => CalculateMA(closes, GetIntParam(parameters, "period", 20)),
            "EMA" => CalculateEMA(closes, GetIntParam(parameters, "period", 20)),
            "RSI" => CalculateRSI(closes, GetIntParam(parameters, "period", 14)),
            "MACD" => CalculateMACD(closes, 
                GetIntParam(parameters, "fast", 12), 
                GetIntParam(parameters, "slow", 26), 
                GetIntParam(parameters, "signal", 9)).macd,
            "MACD_SIGNAL" => CalculateMACD(closes, 
                GetIntParam(parameters, "fast", 12), 
                GetIntParam(parameters, "slow", 26), 
                GetIntParam(parameters, "signal", 9)).signal,
            "MACD_HIST" => CalculateMACD(closes, 
                GetIntParam(parameters, "fast", 12), 
                GetIntParam(parameters, "slow", 26), 
                GetIntParam(parameters, "signal", 9)).histogram,
            "BOLL_UPPER" => CalculateBollinger(closes, 
                GetIntParam(parameters, "period", 20), 
                GetDecimalParam(parameters, "stddev", 2)).upper,
            "BOLL_MIDDLE" => CalculateBollinger(closes, 
                GetIntParam(parameters, "period", 20), 
                GetDecimalParam(parameters, "stddev", 2)).middle,
            "BOLL_LOWER" => CalculateBollinger(closes, 
                GetIntParam(parameters, "period", 20), 
                GetDecimalParam(parameters, "stddev", 2)).lower,
            "KDJ_K" => CalculateKDJ(klines, 
                GetIntParam(parameters, "period", 9), 
                GetIntParam(parameters, "k_period", 3), 
                GetIntParam(parameters, "d_period", 3)).k,
            "KDJ_D" => CalculateKDJ(klines, 
                GetIntParam(parameters, "period", 9), 
                GetIntParam(parameters, "k_period", 3), 
                GetIntParam(parameters, "d_period", 3)).d,
            "KDJ_J" => CalculateKDJ(klines, 
                GetIntParam(parameters, "period", 9), 
                GetIntParam(parameters, "k_period", 3), 
                GetIntParam(parameters, "d_period", 3)).j,
            "ATR" => CalculateATR(klines, GetIntParam(parameters, "period", 14)),
            _ => closes.LastOrDefault()
        };
    }

    private int GetIntParam(Dictionary<string, object> parameters, string key, int defaultValue)
    {
        if (parameters.TryGetValue(key, out var value))
        {
            return Convert.ToInt32(value);
        }
        return defaultValue;
    }

    private decimal GetDecimalParam(Dictionary<string, object> parameters, string key, decimal defaultValue)
    {
        if (parameters.TryGetValue(key, out var value))
        {
            return Convert.ToDecimal(value);
        }
        return defaultValue;
    }

    private decimal CalculateMA(List<decimal> prices, int period)
    {
        if (prices.Count < period)
            return prices.LastOrDefault();
        
        return prices.TakeLast(period).Average();
    }

    private decimal CalculateEMA(List<decimal> prices, int period)
    {
        if (prices.Count < period)
            return prices.LastOrDefault();
        
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
        if (prices.Count < period + 1)
            return 50;
        
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
        
        if (avgLoss == 0)
            return 100;
        
        var rs = avgGain / avgLoss;
        return 100 - (100 / (1 + rs));
    }

    private (decimal macd, decimal signal, decimal histogram) CalculateMACD(
        List<decimal> prices, int fastPeriod, int slowPeriod, int signalPeriod)
    {
        var fastEma = CalculateEMA(prices, fastPeriod);
        var slowEma = CalculateEMA(prices, slowPeriod);
        var macd = fastEma - slowEma;
        
        // Calculate signal line (EMA of MACD)
        var macdValues = new List<decimal>();
        for (int i = slowPeriod; i <= prices.Count; i++)
        {
            var fast = CalculateEMA(prices.Take(i).ToList(), fastPeriod);
            var slow = CalculateEMA(prices.Take(i).ToList(), slowPeriod);
            macdValues.Add(fast - slow);
        }
        
        var signal = CalculateEMA(macdValues, signalPeriod);
        var histogram = macd - signal;
        
        return (macd, signal, histogram);
    }

    private (decimal upper, decimal middle, decimal lower) CalculateBollinger(
        List<decimal> prices, int period, decimal stdDev)
    {
        var middle = CalculateMA(prices, period);
        var recentPrices = prices.TakeLast(period).ToList();
        
        var variance = recentPrices.Average(p => (p - middle) * (p - middle));
        var standardDeviation = (decimal)Math.Sqrt((double)variance);
        
        var upper = middle + (stdDev * standardDeviation);
        var lower = middle - (stdDev * standardDeviation);
        
        return (upper, middle, lower);
    }

    private (decimal k, decimal d, decimal j) CalculateKDJ(
        List<StockKline> klines, int period, int kPeriod, int dPeriod)
    {
        if (klines.Count < period)
            return (50, 50, 50);
        
        var recentKlines = klines.TakeLast(period).ToList();
        var high = recentKlines.Max(k => k.High);
        var low = recentKlines.Min(k => k.Low);
        var close = recentKlines.Last().Close;
        
        var rsv = high == low ? 50 : ((close - low) / (high - low)) * 100;
        
        // Simplified K, D, J calculation
        var k = rsv;
        var d = k;
        var j = 3 * k - 2 * d;
        
        return (k, d, j);
    }

    private decimal CalculateATR(List<StockKline> klines, int period)
    {
        if (klines.Count < 2)
            return 0;
        
        var trValues = new List<decimal>();
        
        for (int i = 1; i < klines.Count; i++)
        {
            var high = klines[i].High;
            var low = klines[i].Low;
            var prevClose = klines[i - 1].Close;
            
            var tr = Math.Max(high - low, Math.Max(Math.Abs(high - prevClose), Math.Abs(low - prevClose)));
            trValues.Add(tr);
        }
        
        return trValues.TakeLast(period).Average();
    }

    private async Task ExecuteActionsAsync(List<StrategyAction> actions, string symbol, StrategyConfig config)
    {
        foreach (var action in actions)
        {
            try
            {
                switch (action.Type.ToLower())
                {
                    case "buy":
                        await ExecuteBuyAsync(action, symbol, config);
                        break;
                    case "sell":
                        await ExecuteSellAsync(action, symbol, config);
                        break;
                    case "notify":
                        await ExecuteNotifyAsync(action, symbol);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing action {ActionType} for {Symbol}", action.Type, symbol);
            }
        }
    }

    private async Task ExecuteBuyAsync(StrategyAction action, string symbol, StrategyConfig config)
    {
        var cash = await _longBridgeService.GetCashAsync();
        var quote = await _longBridgeService.GetQuoteAsync(symbol);
        
        if (quote == null || quote.Price <= 0)
            return;
        
        decimal quantity;
        if (action.QuantityPercent.HasValue)
        {
            var maxPosition = config.RiskManagement?.MaxPositionPercent ?? 100;
            var availableCash = cash * Math.Min(action.QuantityPercent.Value, maxPosition) / 100;
            quantity = Math.Floor(availableCash / quote.Price);
        }
        else
        {
            quantity = action.Quantity ?? 0;
        }
        
        if (quantity <= 0)
            return;
        
        var orderId = await _longBridgeService.PlaceOrderAsync(symbol, "buy", 
            action.OrderType ?? "market", quantity, action.LimitPrice);
        
        if (!string.IsNullOrEmpty(orderId))
        {
            _logger.LogInformation("Placed buy order: {Symbol} x {Quantity}, OrderId: {OrderId}", 
                symbol, quantity, orderId);
            
            if (config.NotificationSettings?.NotifyOnEntry == true)
            {
                await _notificationService.SendAsync($"买入信号", 
                    $"股票: {symbol}\n数量: {quantity}\n价格: {quote.Price}",
                    config.NotificationSettings.Channels);
            }
        }
    }

    private async Task ExecuteSellAsync(StrategyAction action, string symbol, StrategyConfig config)
    {
        var positions = await _longBridgeService.GetPositionsAsync();
        var position = positions.FirstOrDefault(p => p.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase));
        
        if (position == null || position.Quantity <= 0)
            return;
        
        decimal quantity;
        if (action.QuantityPercent.HasValue)
        {
            quantity = Math.Floor(position.Quantity * action.QuantityPercent.Value / 100);
        }
        else
        {
            quantity = action.Quantity ?? position.Quantity;
        }
        
        quantity = Math.Min(quantity, position.Quantity);
        
        if (quantity <= 0)
            return;
        
        var orderId = await _longBridgeService.PlaceOrderAsync(symbol, "sell", 
            action.OrderType ?? "market", quantity, action.LimitPrice);
        
        if (!string.IsNullOrEmpty(orderId))
        {
            _logger.LogInformation("Placed sell order: {Symbol} x {Quantity}, OrderId: {OrderId}", 
                symbol, quantity, orderId);
            
            if (config.NotificationSettings?.NotifyOnExit == true)
            {
                await _notificationService.SendAsync($"卖出信号", 
                    $"股票: {symbol}\n数量: {quantity}",
                    config.NotificationSettings.Channels);
            }
        }
    }

    private async Task ExecuteNotifyAsync(StrategyAction action, string symbol)
    {
        var message = action.NotifyMessage ?? $"策略触发: {symbol}";
        await _notificationService.SendAsync($"策略通知 - {symbol}", message, action.NotifyChannels);
    }

    private async Task LogExecutionAsync(int strategyId, string symbol, string type, string status, string? error = null)
    {
        var execution = new StrategyExecution
        {
            StrategyId = strategyId,
            Symbol = symbol,
            ExecutionType = type,
            Status = status,
            ExecutedAt = DateTime.UtcNow,
            ErrorMessage = error
        };
        
        _dbContext.StrategyExecutions.Add(execution);
        await _dbContext.SaveChangesAsync();
    }
}
