using Quartz;
using QuantTrading.Api.Services.Monitor;

namespace QuantTrading.Api.Jobs;

public class MonitorJob : IJob
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<MonitorJob> _logger;

    public MonitorJob(IServiceProvider serviceProvider, ILogger<MonitorJob> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        using var scope = _serviceProvider.CreateScope();
        var monitorService = scope.ServiceProvider.GetRequiredService<IMonitorService>();
        var watchlistService = scope.ServiceProvider.GetRequiredService<IWatchlistService>();

        try
        {
            // Refresh watchlist
            await watchlistService.RefreshWatchlistAsync();
            
            // Execute monitor rules
            await monitorService.ExecuteRulesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing monitor job");
        }
    }
}
