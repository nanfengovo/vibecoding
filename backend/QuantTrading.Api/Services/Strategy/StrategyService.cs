using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using QuantTrading.Api.Data;
using QuantTrading.Api.Models;

namespace QuantTrading.Api.Services.Strategy;

public class StrategyService : IStrategyService
{
    private readonly QuantTradingDbContext _dbContext;
    private readonly ILogger<StrategyService> _logger;

    public StrategyService(QuantTradingDbContext dbContext, ILogger<StrategyService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<List<Models.Strategy>> GetAllAsync()
    {
        return await _dbContext.Strategies
            .OrderByDescending(s => s.UpdatedAt)
            .ToListAsync();
    }

    public async Task<Models.Strategy?> GetByIdAsync(int id)
    {
        return await _dbContext.Strategies.FindAsync(id);
    }

    public async Task<Models.Strategy> CreateAsync(Models.Strategy strategy)
    {
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
        var existing = await _dbContext.Strategies.FindAsync(id);
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
        var strategy = await _dbContext.Strategies.FindAsync(id);
        if (strategy == null)
            return false;
        
        _dbContext.Strategies.Remove(strategy);
        await _dbContext.SaveChangesAsync();
        
        _logger.LogInformation("Deleted strategy: {Name} (ID: {Id})", strategy.Name, id);
        return true;
    }

    public async Task<bool> ToggleAsync(int id)
    {
        var strategy = await _dbContext.Strategies.FindAsync(id);
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
        var original = await _dbContext.Strategies.FindAsync(id);
        if (original == null)
            return null;
        
        var duplicate = new Models.Strategy
        {
            Name = $"{original.Name} (Copy)",
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
        return await _dbContext.StrategyExecutions
            .Where(e => e.StrategyId == strategyId)
            .OrderByDescending(e => e.ExecutedAt)
            .Take(limit)
            .ToListAsync();
    }
}
