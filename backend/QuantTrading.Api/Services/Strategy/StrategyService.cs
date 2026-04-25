using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using QuantTrading.Api.Data;
using QuantTrading.Api.Models;
using QuantTrading.Api.Services.Auth;

namespace QuantTrading.Api.Services.Strategy;

public class StrategyService : IStrategyService
{
    private readonly QuantTradingDbContext _dbContext;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<StrategyService> _logger;

    public StrategyService(QuantTradingDbContext dbContext, ICurrentUserService currentUser, ILogger<StrategyService> logger)
    {
        _dbContext = dbContext;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<List<Models.Strategy>> GetAllAsync()
    {
        var userId = await _currentUser.GetEffectiveUserIdAsync();
        return await _dbContext.Strategies
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.UpdatedAt)
            .ToListAsync();
    }

    public async Task<Models.Strategy?> GetByIdAsync(int id)
    {
        var userId = await _currentUser.GetEffectiveUserIdAsync();
        return await _dbContext.Strategies.FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId);
    }

    public async Task<Models.Strategy> CreateAsync(Models.Strategy strategy)
    {
        strategy.UserId = await _currentUser.GetEffectiveUserIdAsync();
        strategy.CreatedAt = DateTime.UtcNow;
        strategy.UpdatedAt = DateTime.UtcNow;
        strategy.Version = 1;
        
        _dbContext.Strategies.Add(strategy);
        await _dbContext.SaveChangesAsync();
        
        _logger.LogInformation("Created strategy: {Name} (ID: {Id})", strategy.Name, strategy.Id);
        return strategy;
    }

    public async Task<Models.Strategy?> UpdateAsync(int id, Models.Strategy strategy)
    {
        var userId = await _currentUser.GetEffectiveUserIdAsync();
        var existing = await _dbContext.Strategies.FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId);
        if (existing == null)
            return null;
        
        existing.Name = strategy.Name;
        existing.Description = strategy.Description;
        existing.ConfigJson = strategy.ConfigJson;
        existing.IsEnabled = strategy.IsEnabled;
        existing.UpdatedAt = DateTime.UtcNow;
        existing.Version++;
        
        await _dbContext.SaveChangesAsync();
        
        _logger.LogInformation("Updated strategy: {Name} (ID: {Id}, Version: {Version})", 
            existing.Name, existing.Id, existing.Version);
        
        return existing;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var userId = await _currentUser.GetEffectiveUserIdAsync();
        var strategy = await _dbContext.Strategies.FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId);
        if (strategy == null)
            return false;
        
        _dbContext.Strategies.Remove(strategy);
        await _dbContext.SaveChangesAsync();
        
        _logger.LogInformation("Deleted strategy: {Name} (ID: {Id})", strategy.Name, id);
        return true;
    }

    public async Task<bool> ToggleAsync(int id)
    {
        var userId = await _currentUser.GetEffectiveUserIdAsync();
        var strategy = await _dbContext.Strategies.FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId);
        if (strategy == null)
            return false;
        
        strategy.IsEnabled = !strategy.IsEnabled;
        strategy.UpdatedAt = DateTime.UtcNow;
        
        await _dbContext.SaveChangesAsync();
        
        _logger.LogInformation("Toggled strategy: {Name} (ID: {Id}, Enabled: {Enabled})", 
            strategy.Name, id, strategy.IsEnabled);
        
        return true;
    }

    public async Task<Models.Strategy?> DuplicateAsync(int id)
    {
        var userId = await _currentUser.GetEffectiveUserIdAsync();
        var original = await _dbContext.Strategies.FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId);
        if (original == null)
            return null;
        
        var duplicate = new Models.Strategy
        {
            Name = $"{original.Name} (Copy)",
            UserId = userId,
            Description = original.Description,
            ConfigJson = original.ConfigJson,
            IsEnabled = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Version = 1
        };
        
        _dbContext.Strategies.Add(duplicate);
        await _dbContext.SaveChangesAsync();
        
        _logger.LogInformation("Duplicated strategy: {OriginalName} -> {NewName}", original.Name, duplicate.Name);
        return duplicate;
    }

    public async Task<List<StrategyExecution>> GetExecutionsAsync(int strategyId, int limit = 50)
    {
        var userId = await _currentUser.GetEffectiveUserIdAsync();
        return await _dbContext.StrategyExecutions
            .Where(e => e.StrategyId == strategyId && e.UserId == userId)
            .OrderByDescending(e => e.ExecutedAt)
            .Take(limit)
            .ToListAsync();
    }
}
