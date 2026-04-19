using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuantTrading.Api.Data;
using QuantTrading.Api.Models;

namespace QuantTrading.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ReviewsController : ControllerBase
{
    private readonly QuantTradingDbContext _dbContext;

    public ReviewsController(QuantTradingDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<ActionResult<List<Review>>> GetAll([FromQuery] int limit = 50)
    {
        var reviews = await _dbContext.Reviews
            .OrderByDescending(r => r.ReviewDate)
            .Take(limit)
            .ToListAsync();
        return Ok(reviews);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<Review>> GetById(int id)
    {
        var review = await _dbContext.Reviews.FindAsync(id);
        if (review == null)
            return NotFound();
        return Ok(review);
    }

    [HttpPost]
    public async Task<ActionResult<Review>> Create([FromBody] Review review)
    {
        review.CreatedAt = DateTime.UtcNow;
        review.UpdatedAt = DateTime.UtcNow;
        
        _dbContext.Reviews.Add(review);
        await _dbContext.SaveChangesAsync();
        
        return CreatedAtAction(nameof(GetById), new { id = review.Id }, review);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<Review>> Update(int id, [FromBody] Review review)
    {
        var existing = await _dbContext.Reviews.FindAsync(id);
        if (existing == null)
            return NotFound();
        
        existing.Title = review.Title;
        existing.ReviewDate = review.ReviewDate;
        existing.Content = review.Content;
        existing.TradesAnalysisJson = review.TradesAnalysisJson;
        existing.LessonsLearned = review.LessonsLearned;
        existing.ImprovementPlans = review.ImprovementPlans;
        existing.StrategyId = review.StrategyId;
        existing.BacktestId = review.BacktestId;
        existing.Tags = review.Tags;
        existing.UpdatedAt = DateTime.UtcNow;
        
        await _dbContext.SaveChangesAsync();
        return Ok(existing);
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> Delete(int id)
    {
        var review = await _dbContext.Reviews.FindAsync(id);
        if (review == null)
            return NotFound();
        
        _dbContext.Reviews.Remove(review);
        await _dbContext.SaveChangesAsync();
        return NoContent();
    }
}
