using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using QuantTrading.Api.Models;
using QuantTrading.Api.Services.Backtest;

namespace QuantTrading.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class BacktestsController : ControllerBase
{
    private readonly IBacktestService _backtestService;

    public BacktestsController(IBacktestService backtestService)
    {
        _backtestService = backtestService;
    }

    [HttpGet]
    public async Task<ActionResult<List<Backtest>>> GetAll()
    {
        var backtests = await _backtestService.GetAllAsync();
        return Ok(backtests);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<Backtest>> GetById(int id)
    {
        var backtest = await _backtestService.GetByIdAsync(id);
        if (backtest == null)
            return NotFound();
        return Ok(backtest);
    }

    [HttpPost]
    public async Task<ActionResult<Backtest>> Create([FromBody] CreateBacktestRequest request)
    {
        try
        {
            var backtest = await _backtestService.CreateAsync(
                request.StrategyId,
                request.StartDate,
                request.EndDate,
                request.InitialCapital,
                request.Name
            );
            return CreatedAtAction(nameof(GetById), new { id = backtest.Id }, backtest);
        }
        catch (ArgumentException ex)
        {
            return NotFound(ex.Message);
        }
    }

    [HttpPost("{id}/run")]
    public async Task<ActionResult<Backtest>> Run(int id)
    {
        try
        {
            var backtest = await _backtestService.RunAsync(id);
            return Ok(backtest);
        }
        catch (ArgumentException ex)
        {
            return NotFound(ex.Message);
        }
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> Delete(int id)
    {
        var result = await _backtestService.DeleteAsync(id);
        if (!result)
            return NotFound();
        return NoContent();
    }
}

public class CreateBacktestRequest
{
    public int StrategyId { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public decimal InitialCapital { get; set; } = 100000;
}
