using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace QuantTrading.Api.Models;

public class Trade
{
    [Key]
    public long Id { get; set; }
    
    [StringLength(50)]
    public string OrderId { get; set; } = string.Empty;
    
    [Required]
    [StringLength(20)]
    public string Symbol { get; set; } = string.Empty;
    
    [StringLength(10)]
    public string Side { get; set; } = string.Empty; // buy, sell
    
    [StringLength(20)]
    public string OrderType { get; set; } = string.Empty; // market, limit, stop
    
    [Column(TypeName = "decimal(18,4)")]
    public decimal Quantity { get; set; }
    
    [Column(TypeName = "decimal(18,4)")]
    public decimal? Price { get; set; }
    
    [Column(TypeName = "decimal(18,4)")]
    public decimal? FilledQuantity { get; set; }
    
    [Column(TypeName = "decimal(18,4)")]
    public decimal? FilledPrice { get; set; }
    
    [Column(TypeName = "decimal(18,4)")]
    public decimal? Commission { get; set; }
    
    [StringLength(50)]
    public string Status { get; set; } = string.Empty; // pending, filled, partial, cancelled, rejected
    
    public int? StrategyId { get; set; }
    
    [ForeignKey("StrategyId")]
    public Strategy? Strategy { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    public DateTime? FilledAt { get; set; }
    
    public string? Notes { get; set; }
}

public class Position
{
    [Key]
    public int Id { get; set; }
    
    [Required]
    [StringLength(20)]
    public string Symbol { get; set; } = string.Empty;
    
    [Column(TypeName = "decimal(18,4)")]
    public decimal Quantity { get; set; }
    
    [Column(TypeName = "decimal(18,4)")]
    public decimal AveragePrice { get; set; }
    
    [Column(TypeName = "decimal(18,4)")]
    public decimal CurrentPrice { get; set; }
    
    [Column(TypeName = "decimal(18,4)")]
    public decimal MarketValue { get; set; }
    
    [Column(TypeName = "decimal(18,4)")]
    public decimal UnrealizedPnL { get; set; }
    
    [Column(TypeName = "decimal(8,4)")]
    public decimal UnrealizedPnLPercent { get; set; }
    
    [Column(TypeName = "decimal(18,4)")]
    public decimal RealizedPnL { get; set; }
    
    public DateTime OpenedAt { get; set; }
    
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}

public class Account
{
    [Key]
    public int Id { get; set; }
    
    [StringLength(50)]
    public string AccountId { get; set; } = string.Empty;
    
    [Column(TypeName = "decimal(18,4)")]
    public decimal TotalAssets { get; set; }
    
    [Column(TypeName = "decimal(18,4)")]
    public decimal Cash { get; set; }
    
    [Column(TypeName = "decimal(18,4)")]
    public decimal MarketValue { get; set; }
    
    [Column(TypeName = "decimal(18,4)")]
    public decimal UnrealizedPnL { get; set; }
    
    [Column(TypeName = "decimal(18,4)")]
    public decimal RealizedPnL { get; set; }
    
    [StringLength(10)]
    public string Currency { get; set; } = "USD";
    
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}
