using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace QuantTrading.Api.Models;

public class Backtest
{
    [Key]
    public int Id { get; set; }
    
    public int StrategyId { get; set; }
    
    [ForeignKey("StrategyId")]
    public Strategy? Strategy { get; set; }
    
    [Required]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;
    
    public DateTime StartDate { get; set; }
    
    public DateTime EndDate { get; set; }
    
    [Column(TypeName = "decimal(18,4)")]
    public decimal InitialCapital { get; set; }
    
    [Column(TypeName = "decimal(18,4)")]
    public decimal FinalCapital { get; set; }
    
    [Column(TypeName = "decimal(18,4)")]
    public decimal TotalReturn { get; set; }
    
    [Column(TypeName = "decimal(8,4)")]
    public decimal TotalReturnPercent { get; set; }
    
    [Column(TypeName = "decimal(8,4)")]
    public decimal AnnualizedReturn { get; set; }
    
    [Column(TypeName = "decimal(8,4)")]
    public decimal MaxDrawdown { get; set; }
    
    [Column(TypeName = "decimal(8,4)")]
    public decimal SharpeRatio { get; set; }
    
    [Column(TypeName = "decimal(8,4)")]
    public decimal WinRate { get; set; }
    
    public int TotalTrades { get; set; }
    
    public int WinningTrades { get; set; }
    
    public int LosingTrades { get; set; }
    
    [Column(TypeName = "decimal(18,4)")]
    public decimal AverageWin { get; set; }
    
    [Column(TypeName = "decimal(18,4)")]
    public decimal AverageLoss { get; set; }
    
    [Column(TypeName = "decimal(8,4)")]
    public decimal ProfitFactor { get; set; }
    
    [StringLength(20)]
    public string Status { get; set; } = "pending"; // pending, running, completed, failed
    
    [Column(TypeName = "nvarchar(max)")]
    public string EquityCurveJson { get; set; } = "[]";
    
    [Column(TypeName = "nvarchar(max)")]
    public string TradesJson { get; set; } = "[]";
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    public DateTime? CompletedAt { get; set; }
    
    public string? ErrorMessage { get; set; }
}

public class BacktestTrade
{
    public int Id { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public string Side { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal EntryPrice { get; set; }
    public decimal ExitPrice { get; set; }
    public DateTime EntryTime { get; set; }
    public DateTime ExitTime { get; set; }
    public decimal PnL { get; set; }
    public decimal PnLPercent { get; set; }
    public string EntryReason { get; set; } = string.Empty;
    public string ExitReason { get; set; } = string.Empty;
}

public class EquityPoint
{
    public DateTime Date { get; set; }
    public decimal Equity { get; set; }
    public decimal Drawdown { get; set; }
    public decimal DailyReturn { get; set; }
}

public class Review
{
    [Key]
    public int Id { get; set; }
    
    [Required]
    [StringLength(100)]
    public string Title { get; set; } = string.Empty;
    
    public DateTime ReviewDate { get; set; }
    
    [Column(TypeName = "nvarchar(max)")]
    public string Content { get; set; } = string.Empty;
    
    [Column(TypeName = "nvarchar(max)")]
    public string TradesAnalysisJson { get; set; } = "[]";
    
    [Column(TypeName = "nvarchar(max)")]
    public string LessonsLearned { get; set; } = string.Empty;
    
    [Column(TypeName = "nvarchar(max)")]
    public string ImprovementPlans { get; set; } = string.Empty;
    
    public int? StrategyId { get; set; }
    
    public int? BacktestId { get; set; }
    
    [Column(TypeName = "nvarchar(max)")]
    public string Tags { get; set; } = "[]";
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
