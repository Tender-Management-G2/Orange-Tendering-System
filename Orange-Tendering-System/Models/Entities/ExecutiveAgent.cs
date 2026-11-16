using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Orange_Tendering_System.Models.Entities;

[Index(nameof(UserId), nameof(CreatedAt))]
public sealed class ExecutiveAgentRun
{
    public Guid Id { get; set; }
    [MaxLength(450)] public string UserId { get; set; } = "";
    [MaxLength(64)] public string InputHash { get; set; } = "";
    [MaxLength(20)] public string State { get; set; } = "Running";
    public string? ResultJson { get; set; }
    public DateTime CreatedAt { get; set; }
}

[Index(nameof(RunId), nameof(ToolIndex), IsUnique = true)]
[Index(nameof(DedupHash), IsUnique = true)]
public sealed class ExecutiveAgentAction
{
    public Guid Id { get; set; }
    public Guid RunId { get; set; }
    public int ToolIndex { get; set; }
    [MaxLength(450)] public string UserId { get; set; } = "";
    public int TenderId { get; set; }
    [MaxLength(40)] public string Operation { get; set; } = "";
    [MaxLength(2000)] public string Comment { get; set; } = "";
    [MaxLength(20)] public string Decision { get; set; } = "";
    [MaxLength(64)] public string SnapshotHash { get; set; } = "";
    [MaxLength(64)] public string DedupHash { get; set; } = "";
    public int? ReportId { get; set; }
    [MaxLength(64)] public string? DocumentHash { get; set; }
    [MaxLength(20)] public string State { get; set; } = "Pending";
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? ExecutedAt { get; set; }
    public string? ManifestJson { get; set; }
    [MaxLength(256)] public string? ProofChallenge { get; set; }
    public DateTime? ProofExpiresAt { get; set; }
    public string? WebAuthnAssertionJson { get; set; }
    public byte[]? CmsSignature { get; set; }
    [MaxLength(128)] public string? CertificateThumbprint { get; set; }
}
