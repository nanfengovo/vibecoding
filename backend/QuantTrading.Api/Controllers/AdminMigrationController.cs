using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuantTrading.Api.Data;

namespace QuantTrading.Api.Controllers;

[ApiController]
[Route("api/admin/migration")]
public class AdminMigrationController : ControllerBase
{
    private readonly QuantTradingDbContext _dbContext;
    private readonly IConfiguration _configuration;

    public AdminMigrationController(QuantTradingDbContext dbContext, IConfiguration configuration)
    {
        _dbContext = dbContext;
        _configuration = configuration;
    }

    [HttpGet("summary")]
    public async Task<ActionResult<MigrationSummaryResponse>> GetSummary(CancellationToken cancellationToken)
    {
        if (!TryAuthorize(out var failure))
        {
            return failure!;
        }

        return Ok(await BuildSummaryAsync(cancellationToken));
    }

    [HttpPost("import")]
    public async Task<ActionResult<MigrationSummaryResponse>> Import(
        CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
        return StatusCode(StatusCodes.Status410Gone, new
        {
            message = "破坏性导入已禁用。请使用增量迁移脚本，避免清空 SystemConfigs 和业务数据。"
        });
    }

    private bool TryAuthorize(out ActionResult? failure)
    {
        failure = null;

        var configuredToken = _configuration["Admin:MigrationToken"];
        if (string.IsNullOrWhiteSpace(configuredToken))
        {
            failure = NotFound();
            return false;
        }

        var requestToken = Request.Headers["X-Migration-Token"].FirstOrDefault();
        if (!string.Equals(configuredToken, requestToken, StringComparison.Ordinal))
        {
            failure = Unauthorized(new { message = "无效的迁移令牌。" });
            return false;
        }

        return true;
    }

    private async Task<MigrationSummaryResponse> BuildSummaryAsync(CancellationToken cancellationToken)
    {
        return new MigrationSummaryResponse
        {
            Accounts = await _dbContext.Accounts.CountAsync(cancellationToken),
            Backtests = await _dbContext.Backtests.CountAsync(cancellationToken),
            MonitorAlerts = await _dbContext.MonitorAlerts.CountAsync(cancellationToken),
            MonitorRules = await _dbContext.MonitorRules.CountAsync(cancellationToken),
            NotificationLogs = await _dbContext.NotificationLogs.CountAsync(cancellationToken),
            Positions = await _dbContext.Positions.CountAsync(cancellationToken),
            Reviews = await _dbContext.Reviews.CountAsync(cancellationToken),
            StockKlines = await _dbContext.StockKlines.CountAsync(cancellationToken),
            StockQuotes = await _dbContext.StockQuotes.CountAsync(cancellationToken),
            Stocks = await _dbContext.Stocks.CountAsync(cancellationToken),
            Strategies = await _dbContext.Strategies.CountAsync(cancellationToken),
            StrategyExecutions = await _dbContext.StrategyExecutions.CountAsync(cancellationToken),
            SystemConfigs = await _dbContext.SystemConfigs.CountAsync(cancellationToken),
            Trades = await _dbContext.Trades.CountAsync(cancellationToken)
        };
    }
}

public sealed class MigrationSummaryResponse
{
    public int Accounts { get; set; }
    public int Backtests { get; set; }
    public int MonitorAlerts { get; set; }
    public int MonitorRules { get; set; }
    public int NotificationLogs { get; set; }
    public int Positions { get; set; }
    public int Reviews { get; set; }
    public int StockKlines { get; set; }
    public int StockQuotes { get; set; }
    public int Stocks { get; set; }
    public int Strategies { get; set; }
    public int StrategyExecutions { get; set; }
    public int SystemConfigs { get; set; }
    public int Trades { get; set; }
}
