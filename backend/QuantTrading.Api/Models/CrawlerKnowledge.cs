using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace QuantTrading.Api.Models;

public class UserWatchlistItem
{
    [Key]
    public int Id { get; set; }

    public int UserId { get; set; }

    [Required]
    [StringLength(20)]
    public string Symbol { get; set; } = string.Empty;

    [StringLength(500)]
    public string Notes { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class CrawlerSource
{
    [Key]
    public long Id { get; set; }

    public int UserId { get; set; }

    [Required]
    [StringLength(120)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [StringLength(40)]
    public string Type { get; set; } = "longbridge_news";

    [Required]
    [StringLength(1000)]
    public string Url { get; set; } = string.Empty;

    [StringLength(20)]
    public string Symbol { get; set; } = string.Empty;

    [StringLength(300)]
    public string Tags { get; set; } = string.Empty;

    public bool IsEnabled { get; set; } = true;

    public int CrawlIntervalMinutes { get; set; } = 360;

    public int MaxPages { get; set; } = 5;

    public DateTime? LastRunAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class CrawlerJob
{
    [Key]
    public long Id { get; set; }

    public long SourceId { get; set; }

    public int UserId { get; set; }

    [Required]
    [StringLength(30)]
    public string Status { get; set; } = "pending";

    public int DocumentsFound { get; set; }

    public int DocumentsSaved { get; set; }

    [Column(TypeName = "nvarchar(max)")]
    public string ErrorMessage { get; set; } = string.Empty;

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    public DateTime? FinishedAt { get; set; }
}

public class CrawlerDocument
{
    [Key]
    public long Id { get; set; }

    public long SourceId { get; set; }

    public int UserId { get; set; }

    [StringLength(20)]
    public string Symbol { get; set; } = string.Empty;

    [Required]
    [StringLength(300)]
    public string Title { get; set; } = string.Empty;

    [Required]
    [StringLength(1000)]
    public string Url { get; set; } = string.Empty;

    [StringLength(64)]
    public string ContentHash { get; set; } = string.Empty;

    [Column(TypeName = "nvarchar(max)")]
    public string Markdown { get; set; } = string.Empty;

    [Column(TypeName = "nvarchar(max)")]
    public string Summary { get; set; } = string.Empty;

    [StringLength(300)]
    public string Tags { get; set; } = string.Empty;

    public DateTime? PublishedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class KnowledgeBase
{
    [Key]
    public long Id { get; set; }

    public int UserId { get; set; }

    [Required]
    [StringLength(120)]
    public string Name { get; set; } = string.Empty;

    [StringLength(500)]
    public string Description { get; set; } = string.Empty;

    public bool IsDefault { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class KnowledgeDocument
{
    [Key]
    public long Id { get; set; }

    public long KnowledgeBaseId { get; set; }

    public int UserId { get; set; }

    [Required]
    [StringLength(300)]
    public string Title { get; set; } = string.Empty;

    [StringLength(1000)]
    public string SourceUrl { get; set; } = string.Empty;

    [StringLength(40)]
    public string SourceType { get; set; } = "manual_markdown";

    [StringLength(64)]
    public string ContentHash { get; set; } = string.Empty;

    [Column(TypeName = "nvarchar(max)")]
    public string Markdown { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class KnowledgeChunk
{
    [Key]
    public long Id { get; set; }

    public long KnowledgeBaseId { get; set; }

    public long DocumentId { get; set; }

    public int UserId { get; set; }

    public int ChunkIndex { get; set; }

    [StringLength(300)]
    public string Heading { get; set; } = string.Empty;

    [Column(TypeName = "nvarchar(max)")]
    public string Content { get; set; } = string.Empty;

    [StringLength(800)]
    public string Keywords { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
