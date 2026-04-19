using QuantTrading.Api.Models;

namespace QuantTrading.Api.Services.Strategy;

public interface IStrategyService
{
    Task<List<Models.Strategy>> GetAllAsync();
    Task<Models.Strategy?> GetByIdAsync(int id);
    Task<Models.Strategy> CreateAsync(Models.Strategy strategy);
    Task<Models.Strategy?> UpdateAsync(int id, Models.Strategy strategy);
    Task<bool> DeleteAsync(int id);
    Task<bool> ToggleAsync(int id);
    Task<Models.Strategy?> DuplicateAsync(int id);
    Task<List<StrategyExecution>> GetExecutionsAsync(int strategyId, int limit = 50);
}

public interface IStrategyEngine
{
    Task<bool> EvaluateConditionsAsync(StrategyConfig config, string symbol);
    Task ExecuteStrategyAsync(Models.Strategy strategy);
    Task<decimal> CalculateIndicatorAsync(string symbol, string indicator, Dictionary<string, object> parameters);
}
