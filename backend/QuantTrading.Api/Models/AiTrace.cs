using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace QuantTrading.Api.Models;

public class AiToolTraceAuditRecord
{
    [Key]
    public long Id { get; set; }

    public int UserId { get; set; }

    public long? SessionId { get; set; }

    [StringLength(40)]
    public string Orchestrator { get; set; } = string.Empty;

    [StringLength(20)]
    public string ExecutionMode { get; set; } = string.Empty;

    [StringLength(120)]
    public string ToolName { get; set; } = string.Empty;

    [StringLength(60)]
    public string Source { get; set; } = string.Empty;

    [StringLength(20)]
    public string Status { get; set; } = string.Empty;

    public int LatencyMs { get; set; }

    [StringLength(128)]
    public string RequestDigest { get; set; } = string.Empty;

    [StringLength(128)]
    public string ResponseDigest { get; set; } = string.Empty;

    [StringLength(600)]
    public string RequestPreview { get; set; } = string.Empty;

    [StringLength(600)]
    public string ResponsePreview { get; set; } = string.Empty;

    [StringLength(80)]
    public string ErrorCode { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
