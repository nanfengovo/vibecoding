using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace QuantTrading.Api.Models;

public class Strategy
{
    [Key]
    public int Id { get; set; }
    
    [Required]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;
    
    [StringLength(500)]
    public string Description { get; set; } = string.Empty;
    
    public bool IsActive { get; set; }
    
    public bool IsEnabled { get; set; }
    
    [Column(TypeName = "nvarchar(max)")]
    public string ConfigJson { get; set; } = "{}";
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    
    public DateTime? LastExecutedAt { get; set; }
    
    public int ExecutionCount { get; set; }
    
    public int Version { get; set; } = 1;
}

public class StrategyConfig
{
    public List<string> Symbols { get; set; } = new();
    public List<StrategyCondition> EntryConditions { get; set; } = new();
    public List<StrategyCondition> ExitConditions { get; set; } = new();
    public List<StrategyAction> Actions { get; set; } = new();
    public RiskManagement RiskManagement { get; set; } = new();
    public NotificationSettings NotificationSettings { get; set; } = new();
}

public class StrategyCondition
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Type { get; set; } = string.Empty; // price, indicator, volume, time
    public string Indicator { get; set; } = string.Empty; // MA, EMA, MACD, RSI, KDJ, BOLL
    public Dictionary<string, object> Parameters { get; set; } = new();
    public string Operator { get; set; } = string.Empty; // >, <, >=, <=, ==, cross_up, cross_down
    public object? Value { get; set; }
    public string? CompareIndicator { get; set; } // For comparing two indicators
    public Dictionary<string, object>? CompareParameters { get; set; }
    public string LogicalOperator { get; set; } = "AND"; // AND, OR
}

public class StrategyAction
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Type { get; set; } = string.Empty; // buy, sell, notify
    public decimal? Quantity { get; set; }
    public decimal? QuantityPercent { get; set; } // Percentage of available funds
    public string? OrderType { get; set; } // market, limit
    public decimal? LimitPrice { get; set; }
    public List<string> NotifyChannels { get; set; } = new(); // email, feishu, wechat
    public string? NotifyMessage { get; set; }
}

public class RiskManagement
{
    public decimal? StopLossPercent { get; set; }
    public decimal? TakeProfitPercent { get; set; }
    public decimal? MaxPositionPercent { get; set; }
    public decimal? MaxDailyLossPercent { get; set; }
    public int? MaxTradesPerDay { get; set; }
}

public class NotificationSettings
{
    public bool NotifyOnEntry { get; set; }
    public bool NotifyOnExit { get; set; }
    public bool NotifyOnError { get; set; }
    public List<string> Channels { get; set; } = new();
}

public class StrategyExecution
{
    [Key]
    public long Id { get; set; }
    
    public int StrategyId { get; set; }
    
    [ForeignKey("StrategyId")]
    public Strategy? Strategy { get; set; }
    
    [StringLength(20)]
    public string Symbol { get; set; } = string.Empty;
    
    [StringLength(50)]
    public string ExecutionType { get; set; } = string.Empty; // entry, exit, alert
    
    [StringLength(50)]
    public string Status { get; set; } = string.Empty; // pending, executed, failed
    
    [Column(TypeName = "nvarchar(max)")]
    public string DetailsJson { get; set; } = "{}";
    
    public DateTime ExecutedAt { get; set; } = DateTime.UtcNow;
    
    public string? ErrorMessage { get; set; }
}
