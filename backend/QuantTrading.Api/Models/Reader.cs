using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace QuantTrading.Api.Models;

public class ReaderBook
{
    [Key]
    public long Id { get; set; }

    public int UserId { get; set; }

    [Required]
    [StringLength(300)]
    public string Title { get; set; } = string.Empty;

    [StringLength(160)]
    public string Author { get; set; } = string.Empty;

    [StringLength(20)]
    public string Format { get; set; } = "EPUB"; // EPUB / PDF / MD

    [StringLength(40)]
    public string SourceType { get; set; } = "upload"; // upload / crawler

    [StringLength(80)]
    public string SourceRef { get; set; } = string.Empty;

    [StringLength(64)]
    public string ContentHash { get; set; } = string.Empty;

    [StringLength(260)]
    public string FileName { get; set; } = string.Empty;

    [StringLength(1000)]
    public string StoragePath { get; set; } = string.Empty;

    public long FileSize { get; set; }

    [Column(TypeName = "nvarchar(max)")]
    public string Markdown { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class ReaderProgress
{
    [Key]
    public long Id { get; set; }

    public long BookId { get; set; }

    public int UserId { get; set; }

    [StringLength(1200)]
    public string Locator { get; set; } = string.Empty;

    [StringLength(300)]
    public string ChapterTitle { get; set; } = string.Empty;

    public int? PageNumber { get; set; }

    public decimal? Percentage { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class ReaderHighlight
{
    [Key]
    public long Id { get; set; }

    public long BookId { get; set; }

    public int UserId { get; set; }

    [StringLength(1200)]
    public string Locator { get; set; } = string.Empty;

    [StringLength(300)]
    public string ChapterTitle { get; set; } = string.Empty;

    [Column(TypeName = "nvarchar(max)")]
    public string SelectedText { get; set; } = string.Empty;

    [Column(TypeName = "nvarchar(max)")]
    public string Note { get; set; } = string.Empty;

    [StringLength(20)]
    public string Color { get; set; } = "yellow";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
