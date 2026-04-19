using Microsoft.EntityFrameworkCore;
using Quartz;
using QuantTrading.Api.Data;
using QuantTrading.Api.Services.Strategy;

namespace QuantTrading.Api.Jobs;

public class StrategyExecutionJob : IJob
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<StrategyExecutionJob> _logger;

    public StrategyExecutionJob(IServiceProvider serviceProvider, ILogger<StrategyExecutionJob> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<QuantTradingDbContext>();
        var strategyEngine = scope.ServiceProvider.GetRequiredService<IStrategyEngine>();

        var enabledStrategies = await dbContext.Strategies
            .Where(s => s.IsEnabled)
            .ToListAsync();

        foreach (var strategy in enabledStrategies)
        {
            try
            {
                await strategyEngine.ExecuteStrategyAsync(strategy);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing strategy {StrategyId}", strategy.Id);
            }
        }
    }
}
