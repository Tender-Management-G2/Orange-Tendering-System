using System.ComponentModel.DataAnnotations;
using Orange_Tendering_System.Models.Entities;

namespace Orange_Tendering_System.ViewModels;

public class ReturnReportInput
{
    public Guid RequestId { get; set; }
    public RevisionTarget Target { get; set; }
    public RevisionCorrection Correction { get; set; }
    [Required, StringLength(64, MinimumLength = 64)] public string DocumentHash { get; set; } = "";
    [Required, MaxLength(4000)] public string Feedback { get; set; } = "";
    [Required, MaxLength(4000)] public string RequiredAction { get; set; } = "";
}
public class ReportReviewModel
{
    public TenderReport Report { get; set; } = null!;
    public Tender Tender { get; set; } = null!;
    public List<ReportSignature> Signatures { get; set; } = [];
    public List<ReportRevision> Revisions { get; set; } = [];
    public List<TenderReport> Versions { get; set; } = [];
    public bool IsCurrent { get; set; }
    public bool CanSignCommittee { get; set; }
    public bool CanReviewManager { get; set; }
    public bool CanVoteTop { get; set; }
    public bool CanReturn { get; set; }
    public bool CanForward { get; set; }
    public bool RequireCertificate { get; set; }
    public string UserId { get; set; } = "";
    public ReportReviewStage? Stage { get; set; }
}
public record ProtectedReportCopy(byte[] Bytes, string Name, string CanonicalHash, string CopyHash);
