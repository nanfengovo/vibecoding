using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace QuantTrading.Api.Models;

public class MonitorRule
{
    [Key]
    public int Id { get; set; }

    public int? UserId { get; set; }
    
    [Required]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;
    
    [StringLength(500)]
    public string Description { get; set; } = string.Empty;
    
    public bool IsEnabled { get; set; } = true;
    
    [Column(TypeName = "nvarchar(max)")]
    public string SymbolsJson { get; set; } = "[]"; // List of symbols to monitor
    
    [Column(TypeName = "nvarchar(max)")]
    public string ConditionsJson { get; set; } = "[]"; // Monitor conditions
    
    [Column(TypeName = "nvarchar(max)")]
    public string NotifyChannelsJson { get; set; } = "[]"; // email, feishu, wechat
    
    public int CheckIntervalSeconds { get; set; } = 60;
    
    public DateTime? LastCheckedAt { get; set; }
    
    public DateTime? LastTriggeredAt { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class MonitorCondition
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Type { get; set; } = string.Empty; // price, change_percent, volume, indicator
    public string Indicator { get; set; } = string.Empty;
    public Dictionary<string, object> Parameters { get; set; } = new();
    public string Operator { get; set; } = string.Empty; // >, <, >=, <=, ==, cross_up, cross_down
    public object? Value { get; set; }
    public string LogicalOperator { get; set; } = "AND";
}

public class MonitorAlert
{
    [Key]
    public long Id { get; set; }

    public int? UserId { get; set; }
    
    public int MonitorRuleId { get; set; }
    
    [ForeignKey("MonitorRuleId")]
    public MonitorRule? MonitorRule { get; set; }
    
    [Required]
    [StringLength(20)]
    public string Symbol { get; set; } = string.Empty;
    
    [StringLength(100)]
    public string AlertType { get; set; } = string.Empty;
    
    [Column(TypeName = "nvarchar(max)")]
    public string Message { get; set; } = string.Empty;
    
    [Column(TypeName = "nvarchar(max)")]
    public string DataJson { get; set; } = "{}";
    
    public bool IsRead { get; set; }
    
    public bool IsNotified { get; set; }
    
    public DateTime TriggeredAt { get; set; } = DateTime.UtcNow;
}

public class SystemConfig
{
    [Key]
    public int Id { get; set; }
    
    [Required]
    [StringLength(100)]
    public string Key { get; set; } = string.Empty;
    
    [Column(TypeName = "nvarchar(max)")]
    public string Value { get; set; } = string.Empty;
    
    [StringLength(50)]
    public string Category { get; set; } = string.Empty; // longbridge, email, feishu, wechat, proxy
    
    [StringLength(200)]
    public string Description { get; set; } = string.Empty;
    
    public bool IsEncrypted { get; set; }
    
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class NotificationLog
{
    [Key]
    public long Id { get; set; }
    
    [StringLength(20)]
    public string Channel { get; set; } = string.Empty; // email, feishu, wechat
    
    [StringLength(200)]
    public string Recipient { get; set; } = string.Empty;
    
    [StringLength(200)]
    public string Subject { get; set; } = string.Empty;
    
    [Column(TypeName = "nvarchar(max)")]
    public string Content { get; set; } = string.Empty;
    
    [StringLength(20)]
    public string Status { get; set; } = string.Empty; // sent, failed
    
    public string? ErrorMessage { get; set; }
    
    public DateTime SentAt { get; set; } = DateTime.UtcNow;
}
