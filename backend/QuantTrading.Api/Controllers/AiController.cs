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
                    ProviderId = request?.ProviderId ?? string.Empty,
                    Model = request?.Model ?? string.Empty,
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

    [HttpPost("chat")]
    public async Task<ActionResult<AiChatResult>> Chat(
        [FromBody] AiChatRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request?.Question))
        {
            return BadRequest(new { message = "问题不能为空。" });
        }

        try
        {
            var result = await _aiAnalysisService.ChatAsync(
                new AiChatInput
                {
                    Question = request.Question,
                    Symbol = request.Symbol ?? string.Empty,
                    Focus = request.Focus ?? string.Empty,
                    SkillId = request.SkillId ?? string.Empty,
                    ProviderId = request.ProviderId ?? string.Empty,
                    Model = request.Model ?? string.Empty
                },
                cancellationToken);

            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("optimize-prompt")]
    public async Task<ActionResult<AiPromptOptimizeResult>> OptimizePrompt(
        [FromBody] AiPromptOptimizeRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request?.Question))
        {
            return BadRequest(new { message = "待优化问题不能为空。" });
        }

        try
        {
            var result = await _aiAnalysisService.OptimizePromptAsync(
                new AiPromptOptimizeInput
                {
                    Question = request.Question,
                    Symbol = request.Symbol ?? string.Empty,
                    ProviderId = request.ProviderId ?? string.Empty,
                    Model = request.Model ?? string.Empty
                },
                cancellationToken);

            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("models")]
    public async Task<ActionResult<AiModelsResult>> GetModels(
        [FromBody] AiModelsRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _aiAnalysisService.GetModelsAsync(
                new AiModelsInput
                {
                    ProviderId = request?.ProviderId ?? string.Empty,
                    BaseUrl = request?.BaseUrl ?? string.Empty,
                    ApiKey = request?.ApiKey ?? string.Empty
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
    public string ProviderId { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
}

public sealed class AiChatRequest
{
    public string Question { get; set; } = string.Empty;
    public string? Symbol { get; set; }
    public string? Focus { get; set; }
    public string? SkillId { get; set; }
    public string? ProviderId { get; set; }
    public string? Model { get; set; }
}

public sealed class AiPromptOptimizeRequest
{
    public string Question { get; set; } = string.Empty;
    public string? Symbol { get; set; }
    public string? ProviderId { get; set; }
    public string? Model { get; set; }
}

public sealed class AiModelsRequest
{
    public string? ProviderId { get; set; }
    public string? BaseUrl { get; set; }
    public string? ApiKey { get; set; }
}
