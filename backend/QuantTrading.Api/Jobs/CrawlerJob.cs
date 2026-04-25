using Quartz;
using QuantTrading.Api.Services.Crawler;

namespace QuantTrading.Api.Jobs;

public sealed class CrawlerJob : IJob
{
    private readonly ICrawlerService _crawlerService;
    private readonly ILogger<CrawlerJob> _logger;

    public CrawlerJob(ICrawlerService crawlerService, ILogger<CrawlerJob> logger)
    {
        _crawlerService = crawlerService;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var count = await _crawlerService.RunDueSourcesAsync(context.CancellationToken);
        if (count > 0)
        {
            _logger.LogInformation("Crawler scheduled run completed: {Count} source(s)", count);
        }
    }
}
