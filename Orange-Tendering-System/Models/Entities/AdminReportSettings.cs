using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Orange_Tendering_System.Models.Entities;

[PrimaryKey(nameof(ReportKey), nameof(Role))]
public sealed class ReportRoleAccess
{
    [MaxLength(40)] public string ReportKey { get; set; } = "";
    [MaxLength(80)] public string Role { get; set; } = "";
    public bool Allowed { get; set; }
    [Timestamp] public byte[] RowVersion { get; set; } = [];
}

[Index(nameof(TenderId), nameof(Sha256), IsUnique = true)]
public sealed class FinalTenderContract
{
    public int Id { get; set; }
    public int TenderId { get; set; }
    public Tender Tender { get; set; } = null!;
    [MaxLength(260)] public string FileName { get; set; } = "";
    [MaxLength(64)] public string Sha256 { get; set; } = "";
    [MaxLength(32)] public string StorageKey { get; set; } = "";
    public long SizeBytes { get; set; }
    [MaxLength(450)] public string UploadedByUserId { get; set; } = "";
    public DateTime UploadedAt { get; set; }
}
