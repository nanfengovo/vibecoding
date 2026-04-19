using Microsoft.AspNetCore.Mvc;
using QuantTrading.Api.Models;
using QuantTrading.Api.Services.Strategy;

namespace QuantTrading.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class StrategiesController : ControllerBase
{
    private readonly IStrategyService _strategyService;
    private readonly IStrategyEngine _strategyEngine;

    public StrategiesController(IStrategyService strategyService, IStrategyEngine strategyEngine)
    {
        _strategyService = strategyService;
        _strategyEngine = strategyEngine;
    }

    [HttpGet]
    public async Task<ActionResult<List<Strategy>>> GetAll()
    {
        var strategies = await _strategyService.GetAllAsync();
        return Ok(strategies);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<Strategy>> GetById(int id)
    {
        var strategy = await _strategyService.GetByIdAsync(id);
        if (strategy == null)
            return NotFound();
        return Ok(strategy);
    }

    [HttpPost]
    public async Task<ActionResult<Strategy>> Create([FromBody] Strategy strategy)
    {
        var created = await _strategyService.CreateAsync(strategy);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<Strategy>> Update(int id, [FromBody] Strategy strategy)
    {
        var updated = await _strategyService.UpdateAsync(id, strategy);
        if (updated == null)
            return NotFound();
        return Ok(updated);
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> Delete(int id)
    {
        var result = await _strategyService.DeleteAsync(id);
        if (!result)
            return NotFound();
        return NoContent();
    }

    [HttpPost("{id}/toggle")]
    public async Task<ActionResult> Toggle(int id)
    {
        var result = await _strategyService.ToggleAsync(id);
        if (!result)
            return NotFound();
        return Ok();
    }

    [HttpPost("{id}/duplicate")]
    public async Task<ActionResult<Strategy>> Duplicate(int id)
    {
        var duplicated = await _strategyService.DuplicateAsync(id);
        if (duplicated == null)
            return NotFound();
        return Ok(duplicated);
    }

    [HttpPost("{id}/execute")]
    public async Task<ActionResult> Execute(int id)
    {
        var strategy = await _strategyService.GetByIdAsync(id);
        if (strategy == null)
            return NotFound();
        
        await _strategyEngine.ExecuteStrategyAsync(strategy);
        return Ok();
    }

    [HttpGet("{id}/executions")]
    public async Task<ActionResult<List<StrategyExecution>>> GetExecutions(int id, [FromQuery] int limit = 50)
    {
        var executions = await _strategyService.GetExecutionsAsync(id, limit);
        return Ok(executions);
    }
}
