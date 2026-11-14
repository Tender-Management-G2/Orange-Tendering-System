using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Orange_Tendering_System.Models.Entities;

public enum ReportSigningStage { Evaluation, Manager, Top }
public enum ReportReviewStage { CommitteeReview, OfficerHandoff, ManagerReview, TopReview }
public enum RevisionTarget { ReportAuthor, SourcingOfficer }
public enum RevisionCorrection { NarrativeOnly, Weights, SupplierBid, FinalScores }
public enum RevisionStatus { PolicyBlocked, AwaitingCorrection, Resubmitted }
public enum SignatureMethod { ElectronicAcknowledgment, CertificateCms }

[Index(nameof(TenderReportId), nameof(Stage), nameof(UserId), IsUnique = true)]
[Index(nameof(TenderReportId), nameof(Stage), nameof(Slot), IsUnique = true)]
public class ReportSignature
{
    public long Id { get; set; }
    public int TenderReportId { get; set; }
    public TenderReport TenderReport { get; set; } = null!;
    public string UserId { get; set; } = "";
    public ApplicationUser User { get; set; } = null!;
    [MaxLength(150)] public string SignerName { get; set; } = "";
    [MaxLength(100)] public string Role { get; set; } = "";
    public ReportSigningStage Stage { get; set; }
    [MaxLength(80)] public string Slot { get; set; } = "";
    [MaxLength(20)] public string Decision { get; set; } = "";
    public DateTime SignedAt { get; set; }
    [MaxLength(64)] public string DocumentHash { get; set; } = "";
    [MaxLength(64)] public string SnapshotHash { get; set; } = "";
    public string ManifestJson { get; set; } = "";
    public SignatureMethod Method { get; set; }
    public byte[]? CmsSignature { get; set; }
    [MaxLength(128)] public string? CertificateThumbprint { get; set; }
    public DateTime? InvalidatedAt { get; set; }
    [MaxLength(1000)] public string? InvalidationReason { get; set; }
}

[Index(nameof(RequestId), IsUnique = true)]
[Index(nameof(TenderReportId), nameof(Status))]
public class ReportRevision
{
    public int Id { get; set; }
    public Guid RequestId { get; set; }
    public int TenderReportId { get; set; }
    public TenderReport TenderReport { get; set; } = null!;
    public ReportReviewStage SourceStage { get; set; }
    public SessionState SourceSessionState { get; set; }
    public RevisionTarget TargetStage { get; set; }
    public RevisionCorrection Correction { get; set; }
    public string ReturningUserId { get; set; } = "";
    public ApplicationUser ReturningUser { get; set; } = null!;
    public string ResponsibleUserId { get; set; } = "";
    public ApplicationUser ResponsibleUser { get; set; } = null!;
    [MaxLength(4000)] public string Feedback { get; set; } = "";
    [MaxLength(4000)] public string RequiredAction { get; set; } = "";
    public DateTime RequestedAt { get; set; }
    public RevisionStatus Status { get; set; }
    public string? PolicyEvidenceJson { get; set; }
    [MaxLength(2000)] public string? Blocker { get; set; }
    public int? ResubmittedReportId { get; set; }
    public TenderReport? ResubmittedReport { get; set; }
    public DateTime? ResubmittedAt { get; set; }
    [MaxLength(10000)] public string? CorrectionSummary { get; set; }
    [Timestamp] public byte[] RowVersion { get; set; } = [];
}
