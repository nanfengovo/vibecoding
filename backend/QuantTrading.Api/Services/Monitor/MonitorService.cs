using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using QuantTrading.Api.Data;
using QuantTrading.Api.Models;
using QuantTrading.Api.Services.LongBridge;
using QuantTrading.Api.Services.Notification;
using QuantTrading.Api.Services.Strategy;

namespace QuantTrading.Api.Services.Monitor;

public class MonitorService : IMonitorService
{
    private readonly QuantTradingDbContext _dbContext;
    private readonly ILongBridgeService _longBridgeService;
    private readonly IStrategyEngine _strategyEngine;
    private readonly INotificationService _notificationService;
    private readonly ILogger<MonitorService> _logger;

    public MonitorService(
        QuantTradingDbContext dbContext,
        ILongBridgeService longBridgeService,
        IStrategyEngine strategyEngine,
        INotificationService notificationService,
        ILogger<MonitorService> logger)
    {
        _dbContext = dbContext;
        _longBridgeService = longBridgeService;
        _strategyEngine = strategyEngine;
        _notificationService = notificationService;
        _logger = logger;
    }

    public async Task<List<MonitorRule>> GetAllRulesAsync()
    {
        return await _dbContext.MonitorRules
            .OrderByDescending(r => r.UpdatedAt)
            .ToListAsync();
    }

    public async Task<MonitorRule?> GetRuleByIdAsync(int id)
    {
        return await _dbContext.MonitorRules.FindAsync(id);
    }

    public async Task<MonitorRule> CreateRuleAsync(MonitorRule rule)
    {
        rule.CreatedAt = DateTime.UtcNow;
        rule.UpdatedAt = DateTime.UtcNow;
        
        _dbContext.MonitorRules.Add(rule);
        await _dbContext.SaveChangesAsync();
        
        _logger.LogInformation("Created monitor rule: {Name} (ID: {Id})", rule.Name, rule.Id);
        return rule;
    }

    public async Task<MonitorRule?> UpdateRuleAsync(int id, MonitorRule rule)
    {
        var existing = await _dbContext.MonitorRules.FindAsync(id);
        if (existing == null)
            return null;
        
        existing.Name = rule.Name;
        existing.Description = rule.Description;
        existing.IsEnabled = rule.IsEnabled;
        existing.SymbolsJson = rule.SymbolsJson;
        existing.ConditionsJson = rule.ConditionsJson;
        existing.NotifyChannelsJson = rule.NotifyChannelsJson;
        existing.CheckIntervalSeconds = rule.CheckIntervalSeconds;
        existing.UpdatedAt = DateTime.UtcNow;
        
        await _dbContext.SaveChangesAsync();
        
        _logger.LogInformation("Updated monitor rule: {Name} (ID: {Id})", existing.Name, existing.Id);
        return existing;
    }

    public async Task<bool> DeleteRuleAsync(int id)
    {
        var rule = await _dbContext.MonitorRules.FindAsync(id);
        if (rule == null)
            return false;
        
        _dbContext.MonitorRules.Remove(rule);
        await _dbContext.SaveChangesAsync();
        
        _logger.LogInformation("Deleted monitor rule: {Name} (ID: {Id})", rule.Name, id);
        return true;
    }

    public async Task<bool> ToggleRuleAsync(int id)
    {
        var rule = await _dbContext.MonitorRules.FindAsync(id);
        if (rule == null)
            return false;
        
        rule.IsEnabled = !rule.IsEnabled;
        rule.UpdatedAt = DateTime.UtcNow;
        
        await _dbContext.SaveChangesAsync();
        
        _logger.LogInformation("Toggled monitor rule: {Name} (ID: {Id}, Enabled: {Enabled})", 
            rule.Name, id, rule.IsEnabled);
        return true;
    }

    public async Task ExecuteRulesAsync()
    {
        var rules = await _dbContext.MonitorRules
            .Where(r => r.IsEnabled)
            .ToListAsync();

        foreach (var rule in rules)
        {
            try
            {
                // Check if it's time to run
                if (rule.LastCheckedAt.HasValue)
                {
                    var elapsed = (DateTime.UtcNow - rule.LastCheckedAt.Value).TotalSeconds;
                    if (elapsed < rule.CheckIntervalSeconds)
                        continue;
                }

                await ExecuteRuleAsync(rule);
                
                rule.LastCheckedAt = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing monitor rule {RuleId}", rule.Id);
            }
        }
    }

    private async Task ExecuteRuleAsync(MonitorRule rule)
    {
        var symbols = JsonConvert.DeserializeObject<List<string>>(rule.SymbolsJson) ?? new List<string>();
        var conditions = JsonConvert.DeserializeObject<List<MonitorCondition>>(rule.ConditionsJson) ?? new List<MonitorCondition>();
        var channels = JsonConvert.DeserializeObject<List<string>>(rule.NotifyChannelsJson) ?? new List<string>();

        if (!symbols.Any() || !conditions.Any())
            return;

        foreach (var symbol in symbols)
        {
            var triggered = await EvaluateConditionsAsync(symbol, conditions);
            
            if (triggered)
            {
                var quote = await _longBridgeService.GetQuoteAsync(symbol);
                
                // Create alert
                var alert = new MonitorAlert
                {
                    MonitorRuleId = rule.Id,
                    Symbol = symbol,
                    AlertType = rule.Name,
                    Message = $"监控规则触发: {rule.Name}\n股票: {symbol}\n当前价格: {quote?.Price:F2}",
                    DataJson = JsonConvert.SerializeObject(new { quote, conditions }),
                    TriggeredAt = DateTime.UtcNow
                };
                
                _dbContext.MonitorAlerts.Add(alert);
                await _dbContext.SaveChangesAsync();
                
                // Send notification
                if (channels.Any())
                {
                    await _notificationService.SendAsync(
                        $"监控触发: {rule.Name} - {symbol}",
                        $"规则: {rule.Name}\n股票: {symbol}\n当前价格: {quote?.Price:F2}\n触发时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
                        channels
                    );
                }
                
                rule.LastTriggeredAt = DateTime.UtcNow;
                _logger.LogInformation("Monitor rule triggered: {RuleName} for {Symbol}", rule.Name, symbol);
            }
        }
    }

    private async Task<bool> EvaluateConditionsAsync(string symbol, List<MonitorCondition> conditions)
    {
        if (!conditions.Any())
            return false;

        var results = new List<bool>();

        foreach (var condition in conditions)
        {
            var result = await EvaluateSingleConditionAsync(symbol, condition);
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

    private async Task<bool> EvaluateSingleConditionAsync(string symbol, MonitorCondition condition)
    {
        try
        {
            decimal leftValue;
            decimal rightValue = Convert.ToDecimal(condition.Value);

            switch (condition.Type.ToLower())
            {
                case "price":
                    var quote = await _longBridgeService.GetQuoteAsync(symbol);
                    if (quote == null) return false;
                    leftValue = quote.Price;
                    break;

                case "change_percent":
                    var cpQuote = await _longBridgeService.GetQuoteAsync(symbol);
                    var stockInfo = await _longBridgeService.GetStockInfoAsync(symbol);
                    if (cpQuote == null || stockInfo?.PreviousClose <= 0) return false;
                    leftValue = (cpQuote.Price - stockInfo.PreviousClose) / stockInfo.PreviousClose * 100;
                    break;

                case "volume":
                    var volQuote = await _longBridgeService.GetQuoteAsync(symbol);
                    if (volQuote == null) return false;
                    leftValue = volQuote.Volume;
                    break;

                case "indicator":
                    leftValue = await _strategyEngine.CalculateIndicatorAsync(symbol, condition.Indicator, condition.Parameters);
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error evaluating condition for {Symbol}", symbol);
            return false;
        }
    }

    public async Task<List<MonitorAlert>> GetAlertsAsync(int? ruleId = null, bool? unreadOnly = null, int limit = 50)
    {
        var query = _dbContext.MonitorAlerts.AsQueryable();
        
        if (ruleId.HasValue)
            query = query.Where(a => a.MonitorRuleId == ruleId.Value);
        
        if (unreadOnly == true)
            query = query.Where(a => !a.IsRead);
        
        return await query
            .OrderByDescending(a => a.TriggeredAt)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<bool> MarkAlertReadAsync(long alertId)
    {
        var alert = await _dbContext.MonitorAlerts.FindAsync(alertId);
        if (alert == null)
            return false;
        
        alert.IsRead = true;
        await _dbContext.SaveChangesAsync();
        return true;
    }

    public async Task<bool> MarkAllAlertsReadAsync()
    {
        await _dbContext.MonitorAlerts
            .Where(a => !a.IsRead)
            .ExecuteUpdateAsync(a => a.SetProperty(x => x.IsRead, true));
        
        return true;
    }
}
