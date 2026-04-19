using QuantTrading.Api.Models;

namespace QuantTrading.Api.Services.Backtest;

public interface IBacktestService
{
    Task<List<Models.Backtest>> GetAllAsync();
    Task<Models.Backtest?> GetByIdAsync(int id);
    Task<Models.Backtest> CreateAsync(int strategyId, DateTime startDate, DateTime endDate, decimal initialCapital, string name);
    Task<Models.Backtest> RunAsync(int id);
    Task<bool> DeleteAsync(int id);
}
