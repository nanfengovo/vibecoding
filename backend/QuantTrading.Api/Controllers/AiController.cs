using Microsoft.AspNetCore.Mvc;
using QuantTrading.Api.Services.AI;
using QuantTrading.Api.Services.LongBridge;

namespace QuantTrading.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AiController : ControllerBase
{
    private readonly IAiAnalysisService _aiAnalysisService;
    private readonly ILongBridgeService _longBridgeService;

    public AiController(IAiAnalysisService aiAnalysisService, ILongBridgeService longBridgeService)
    {
        _aiAnalysisService = aiAnalysisService;
        _longBridgeService = longBridgeService;
    }

    [HttpPost("analyze/stock/{symbol}")]
    public async Task<ActionResult<StockAnalysisResult>> AnalyzeStock(
        string symbol,
        [FromBody] AnalyzeStockRequest? request,
        CancellationToken cancellationToken)
    {
        var normalizedSymbol = (symbol ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalizedSymbol))
        {
            return BadRequest("股票代码不能为空。");
        }

        var stock = await _longBridgeService.GetStockInfoAsync(normalizedSymbol);
        if (stock == null)
        {
            return NotFound("股票不存在。");
        }

        var quote = await _longBridgeService.GetQuoteAsync(normalizedSymbol);
        var period = string.IsNullOrWhiteSpace(request?.Period) ? "D" : request!.Period.Trim().ToUpperInvariant();
        var maxCount = request?.Count is > 0 and <= 1000 ? request.Count.Value : 260;
        var klines = await _longBridgeService.GetKlineAsync(
            normalizedSymbol,
            period,
            request?.Start,
            request?.End,
            maxCount);

        try
        {
            var result = await _aiAnalysisService.AnalyzeStockAsync(
                new StockAnalysisInput
                {
                    Symbol = normalizedSymbol,
                    Focus = request?.Focus ?? string.Empty,
                    Stock = stock,
                    Quote = quote,
                    Klines = klines
                },
                cancellationToken);

            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}

public sealed class AnalyzeStockRequest
{
    public string Period { get; set; } = "D";
    public DateTime? Start { get; set; }
    public DateTime? End { get; set; }
    public int? Count { get; set; }
    public string Focus { get; set; } = string.Empty;
}
