using Microsoft.AspNetCore.Mvc;
using QuantTrading.Api.Models;
using QuantTrading.Api.Services.LongBridge;
using QuantTrading.Api.Services.Monitor;

namespace QuantTrading.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class StocksController : ControllerBase
{
    private readonly ILongBridgeService _longBridgeService;
    private readonly IWatchlistService _watchlistService;

    public StocksController(ILongBridgeService longBridgeService, IWatchlistService watchlistService)
    {
        _longBridgeService = longBridgeService;
        _watchlistService = watchlistService;
    }

    [HttpGet("search")]
    public async Task<ActionResult<List<Stock>>> Search([FromQuery] string? keyword, [FromQuery] string? query)
    {
        keyword = string.IsNullOrWhiteSpace(keyword) ? query : keyword;

        if (string.IsNullOrWhiteSpace(keyword))
            return BadRequest("Keyword is required");
        
        var stocks = await _longBridgeService.SearchStocksAsync(keyword);
        return Ok(stocks);
    }

    [HttpGet("{symbol}")]
    public async Task<ActionResult<Stock>> GetStock(string symbol)
    {
        var stock = await _longBridgeService.GetStockInfoAsync(symbol);
        if (stock == null)
            return NotFound();
        return Ok(stock);
    }

    [HttpGet("{symbol}/quote")]
    public async Task<ActionResult<StockQuote>> GetQuote(string symbol)
    {
        var quote = await _longBridgeService.GetQuoteAsync(symbol);
        if (quote == null)
            return NotFound();
        return Ok(quote);
    }

    [HttpGet("{symbol}/profile")]
    public async Task<ActionResult<CompanyProfileResponse>> GetCompanyProfile(string symbol)
    {
        var profile = await _longBridgeService.GetCompanyProfileAsync(symbol);
        if (profile == null)
            return NotFound();

        var response = new CompanyProfileResponse
        {
            Symbol = profile.Symbol,
            Title = profile.Name,
            Overview = profile.Overview,
            SourceUrl = profile.SourceUrl,
            Fields = profile.Fields
                .Select(item => new CompanyProfileField
                {
                    Key = item.Key,
                    Value = item.Value
                })
                .ToList()
        };

        return Ok(response);
    }

    [HttpGet("{symbol}/kline")]
    public async Task<ActionResult<List<StockKline>>> GetKline(
        string symbol, 
        [FromQuery] string period = "1d", 
        [FromQuery] int count = 100,
        [FromQuery] int? limit = null,
        [FromQuery] DateTime? start = null,
        [FromQuery] DateTime? end = null)
    {
        if (limit.HasValue && limit.Value > 0)
        {
            count = limit.Value;
        }

        if ((start.HasValue || end.HasValue) && count < 300)
        {
            count = 1000;
        }

        var klines = await _longBridgeService.GetKlineAsync(symbol, period, start, end, count);
        return Ok(klines);
    }

    [HttpGet("watchlist")]
    public async Task<ActionResult<List<Stock>>> GetWatchlist()
    {
        var stocks = await _watchlistService.GetWatchlistAsync();
        return Ok(stocks);
    }

    [HttpPost("watchlist")]
    public async Task<ActionResult<Stock>> AddToWatchlist([FromBody] AddWatchlistRequest request)
    {
        var stock = await _watchlistService.AddToWatchlistAsync(request.Symbol, request.Notes);
        if (stock == null)
            return NotFound("Stock not found");
        return Ok(stock);
    }

    [HttpDelete("watchlist/{id}")]
    public async Task<ActionResult> RemoveFromWatchlist(int id)
    {
        var result = await _watchlistService.RemoveFromWatchlistAsync(id);
        if (!result)
            return NotFound();
        return NoContent();
    }

    [HttpPut("watchlist/{id}/notes")]
    public async Task<ActionResult<Stock>> UpdateNotes(int id, [FromBody] UpdateNotesRequest request)
    {
        var stock = await _watchlistService.UpdateStockNotesAsync(id, request.Notes);
        if (stock == null)
            return NotFound();
        return Ok(stock);
    }

    [HttpPost("watchlist/refresh")]
    public async Task<ActionResult> RefreshWatchlist()
    {
        await _watchlistService.RefreshWatchlistAsync();
        return Ok();
    }

    [HttpGet("market/status")]
    public async Task<ActionResult<object>> GetMarketStatus([FromQuery] string market = "US")
    {
        var isOpen = await _longBridgeService.IsMarketOpenAsync(market);
        var nextOpen = await _longBridgeService.GetNextMarketOpenAsync(market);
        return Ok(new { isOpen, nextOpen });
    }
}

public class AddWatchlistRequest
{
    public string Symbol { get; set; } = string.Empty;
    public string? Notes { get; set; }
}

public class UpdateNotesRequest
{
    public string Notes { get; set; } = string.Empty;
}

public sealed class CompanyProfileResponse
{
    public string Symbol { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Overview { get; set; } = string.Empty;
    public string SourceUrl { get; set; } = string.Empty;
    public List<CompanyProfileField> Fields { get; set; } = new();
}

public sealed class CompanyProfileField
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}
