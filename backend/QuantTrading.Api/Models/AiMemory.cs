using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace QuantTrading.Api.Models;

public class AiChatSessionRecord
{
    [Key]
    public long Id { get; set; }

    public int UserId { get; set; }

    [Required]
    [StringLength(120)]
    public string Title { get; set; } = "新会话";

    [StringLength(20)]
    public string Symbol { get; set; } = string.Empty;

    [StringLength(80)]
    public string SkillId { get; set; } = string.Empty;

    [StringLength(120)]
    public string ProviderId { get; set; } = string.Empty;

    [StringLength(200)]
    public string Model { get; set; } = string.Empty;

    [StringLength(120)]
    public string LastMarketContextSymbol { get; set; } = string.Empty;

    public bool IsArchived { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<AiChatMessageRecord> Messages { get; set; } = new();
}

public class AiChatMessageRecord
{
    [Key]
    public long Id { get; set; }

    public long SessionId { get; set; }

    public int UserId { get; set; }

    [Required]
    [StringLength(20)]
    public string Role { get; set; } = "user";

    [Column(TypeName = "nvarchar(max)")]
    public string Content { get; set; } = string.Empty;

    [StringLength(200)]
    public string Model { get; set; } = string.Empty;

    [Column(TypeName = "nvarchar(max)")]
    public string MarketContextJson { get; set; } = string.Empty;

    public bool IsError { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public AiChatSessionRecord? Session { get; set; }
}

public class AiMemoryRecord
{
    [Key]
    public long Id { get; set; }

    public int UserId { get; set; }

    [Required]
    [StringLength(50)]
    public string Type { get; set; } = "preference";

    [StringLength(120)]
    public string Title { get; set; } = string.Empty;

    [Column(TypeName = "nvarchar(max)")]
    public string Content { get; set; } = string.Empty;

    [StringLength(20)]
    public string Symbol { get; set; } = string.Empty;

    [StringLength(200)]
    public string Tags { get; set; } = string.Empty;

    public int Priority { get; set; } = 1;

    [StringLength(60)]
    public string SourceType { get; set; } = string.Empty;

    [StringLength(1000)]
    public string SourceUrl { get; set; } = string.Empty;

    [StringLength(200)]
    public string SourceRef { get; set; } = string.Empty;

    public long? KnowledgeBaseId { get; set; }

    public long? KnowledgeDocumentId { get; set; }

    [StringLength(120)]
    public string ProviderId { get; set; } = string.Empty;

    [StringLength(200)]
    public string Model { get; set; } = string.Empty;

    [StringLength(40)]
    public string SyncStatus { get; set; } = "pending";

    public DateTime? LastSyncedAt { get; set; }

    public bool IsArchived { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
