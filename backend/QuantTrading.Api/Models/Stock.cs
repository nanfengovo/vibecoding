using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace QuantTrading.Api.Models;

public class Stock
{
    [Key]
    public int Id { get; set; }
    
    [Required]
    [StringLength(20)]
    public string Symbol { get; set; } = string.Empty;
    
    [StringLength(200)]
    public string Name { get; set; } = string.Empty;
    
    [StringLength(10)]
    public string Market { get; set; } = "US"; // US, HK, etc.

    [NotMapped]
    [StringLength(10)]
    public string Currency { get; set; } = "USD";
    
    [Column(TypeName = "decimal(18,4)")]
    public decimal CurrentPrice { get; set; }
    
    [Column(TypeName = "decimal(18,4)")]
    public decimal PreviousClose { get; set; }
    
    [Column(TypeName = "decimal(18,4)")]
    public decimal Open { get; set; }
    
    [Column(TypeName = "decimal(18,4)")]
    public decimal High { get; set; }
    
    [Column(TypeName = "decimal(18,4)")]
    public decimal Low { get; set; }
    
    public long Volume { get; set; }
    
    [Column(TypeName = "decimal(18,4)")]
    public decimal Change { get; set; }
    
    [Column(TypeName = "decimal(8,4)")]
    public decimal ChangePercent { get; set; }
    
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    
    public bool IsWatched { get; set; }
    
    public string? Notes { get; set; }

    [NotMapped]
    public decimal MarketCap { get; set; }

    [NotMapped]
    public decimal High52Week { get; set; }

    [NotMapped]
    public decimal Low52Week { get; set; }

    [NotMapped]
    public long AvgVolume { get; set; }

    [NotMapped]
    public decimal Pe { get; set; }

    [NotMapped]
    public decimal Eps { get; set; }

    [NotMapped]
    public decimal Dividend { get; set; }
}

public class StockQuote
{
    [Key]
    public long Id { get; set; }
    
    [Required]
    [StringLength(20)]
    public string Symbol { get; set; } = string.Empty;
    
    [Column(TypeName = "decimal(18,4)")]
    public decimal Price { get; set; }
    
    [Column(TypeName = "decimal(18,4)")]
    public decimal Open { get; set; }
    
    [Column(TypeName = "decimal(18,4)")]
    public decimal High { get; set; }
    
    [Column(TypeName = "decimal(18,4)")]
    public decimal Low { get; set; }
    
    public long Volume { get; set; }
    
    public DateTime Timestamp { get; set; }

    [NotMapped]
    public decimal PreviousClose { get; set; }

    [NotMapped]
    public decimal Change { get; set; }

    [NotMapped]
    public decimal ChangePercent { get; set; }

    [NotMapped]
    public decimal Turnover { get; set; }
}

public class StockKline
{
    [Key]
    public long Id { get; set; }
    
    [Required]
    [StringLength(20)]
    public string Symbol { get; set; } = string.Empty;
    
    [Column(TypeName = "decimal(18,4)")]
    public decimal Open { get; set; }
    
    [Column(TypeName = "decimal(18,4)")]
    public decimal High { get; set; }
    
    [Column(TypeName = "decimal(18,4)")]
    public decimal Low { get; set; }
    
    [Column(TypeName = "decimal(18,4)")]
    public decimal Close { get; set; }
    
    public long Volume { get; set; }
    
    [Column(TypeName = "decimal(18,2)")]
    public decimal Turnover { get; set; }
    
    public DateTime Timestamp { get; set; }
    
    [StringLength(10)]
    public string Period { get; set; } = "1d"; // 1m, 5m, 15m, 30m, 1h, 1d, 1w, 1M
}
