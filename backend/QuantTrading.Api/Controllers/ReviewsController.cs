using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using QuantTrading.Api.Data;
using QuantTrading.Api.Models;
using QuantTrading.Api.Services.Auth;

namespace QuantTrading.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class ReviewsController : ControllerBase
{
    private readonly QuantTradingDbContext _dbContext;
    private readonly ICurrentUserService _currentUser;

    public ReviewsController(QuantTradingDbContext dbContext, ICurrentUserService currentUser)
    {
        _dbContext = dbContext;
        _currentUser = currentUser;
    }

    [HttpGet]
    public async Task<ActionResult<List<Review>>> GetAll([FromQuery] int limit = 50)
    {
        var userId = await _currentUser.GetEffectiveUserIdAsync();
        var reviews = await _dbContext.Reviews
            .Where(r => r.UserId == userId)
            .OrderByDescending(r => r.ReviewDate)
            .Take(limit)
            .ToListAsync();
        return Ok(reviews);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<Review>> GetById(int id)
    {
        var userId = await _currentUser.GetEffectiveUserIdAsync();
        var review = await _dbContext.Reviews.FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId);
        if (review == null)
            return NotFound();
        return Ok(review);
    }

    [HttpPost]
    public async Task<ActionResult<Review>> Create([FromBody] Review review)
    {
        review.UserId = await _currentUser.GetEffectiveUserIdAsync();
        review.CreatedAt = DateTime.UtcNow;
        review.UpdatedAt = DateTime.UtcNow;
        
        _dbContext.Reviews.Add(review);
        await _dbContext.SaveChangesAsync();
        
        return CreatedAtAction(nameof(GetById), new { id = review.Id }, review);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<Review>> Update(int id, [FromBody] Review review)
    {
        var userId = await _currentUser.GetEffectiveUserIdAsync();
        var existing = await _dbContext.Reviews.FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId);
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
        var userId = await _currentUser.GetEffectiveUserIdAsync();
        var review = await _dbContext.Reviews.FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId);
        if (review == null)
            return NotFound();
        
        _dbContext.Reviews.Remove(review);
        await _dbContext.SaveChangesAsync();
        return NoContent();
    }
}
