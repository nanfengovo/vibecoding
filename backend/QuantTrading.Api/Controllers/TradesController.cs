using Microsoft.AspNetCore.Mvc;
using QuantTrading.Api.Models;
using QuantTrading.Api.Services.Monitor;

namespace QuantTrading.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TradesController : ControllerBase
{
    private readonly ITradeService _tradeService;

    public TradesController(ITradeService tradeService)
    {
        _tradeService = tradeService;
    }

    [HttpGet]
    public async Task<ActionResult<List<Trade>>> GetTrades(
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        [FromQuery] int limit = 100)
    {
        var trades = await _tradeService.GetTradesAsync(startDate, endDate, limit);
        return Ok(trades);
    }

    [HttpGet("positions")]
    public async Task<ActionResult<List<Position>>> GetPositions()
    {
        var positions = await _tradeService.GetPositionsAsync();
        return Ok(positions);
    }

    [HttpGet("account")]
    public async Task<ActionResult<Account>> GetAccount()
    {
        var account = await _tradeService.GetAccountAsync();
        if (account == null)
            return NotFound();
        return Ok(account);
    }

    [HttpPost]
    public async Task<ActionResult<Trade>> PlaceOrder([FromBody] PlaceOrderRequest request)
    {
        var trade = await _tradeService.PlaceOrderAsync(
            request.Symbol,
            request.Side,
            request.OrderType,
            request.Quantity,
            request.Price,
            request.StrategyId
        );
        
        if (trade == null)
            return BadRequest("Failed to place order");
        
        return Ok(trade);
    }

    [HttpDelete("{orderId}")]
    public async Task<ActionResult> CancelOrder(string orderId)
    {
        var result = await _tradeService.CancelOrderAsync(orderId);
        if (!result)
            return BadRequest("Failed to cancel order");
        return NoContent();
    }
}

public class PlaceOrderRequest
{
    public string Symbol { get; set; } = string.Empty;
    public string Side { get; set; } = string.Empty;
    public string OrderType { get; set; } = "market";
    public decimal Quantity { get; set; }
    public decimal? Price { get; set; }
    public int? StrategyId { get; set; }
}
