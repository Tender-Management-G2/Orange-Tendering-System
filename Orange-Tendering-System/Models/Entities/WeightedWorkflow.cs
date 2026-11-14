using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Orange_Tendering_System.Models.Entities;

public enum WeightState { Proposals, AwaitingApproval, Approved }
public enum SessionState { Scheduled, Closed, Incomplete, ReportDraft, Finalized, ManagerApproved, Returned, Rejected, Awarded, RevisionBlocked }

// Immutable requirements snapshot and proposal round. No post-publication edits are supported.
[Index(nameof(TenderId), nameof(Version), IsUnique = true)]
public class WeightRound
{
    public int Id { get; set; }
    public int TenderId { get; set; }
    public Tender Tender { get; set; } = null!;
    public int Version { get; set; }
    public string RequirementsJson { get; set; } = "";
    public WeightState State { get; set; }
    [Precision(12, 6)] public decimal? FinancialAverage { get; set; }
    [Precision(12, 6)] public decimal? TechnicalAverage { get; set; }
    [Precision(12, 6)] public decimal? ApprovedFinancial { get; set; }
    [Precision(12, 6)] public decimal? ApprovedTechnical { get; set; }
    public string? ApprovedByUserId { get; set; }
    public ApplicationUser? ApprovedByUser { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? ApprovalPolicyJson { get; set; }
    public DateTime CreatedAt { get; set; }
    [Timestamp] public byte[] RowVersion { get; set; } = [];
    public ICollection<WeightProposal> Proposals { get; set; } = new List<WeightProposal>();
}

[Index(nameof(WeightRoundId), nameof(CommitteeMemberId), IsUnique = true)]
[Index(nameof(WeightRoundId), nameof(MemberUserId), IsUnique = true)]
public class WeightProposal
{
    public int Id { get; set; }
    public int WeightRoundId { get; set; }
    public WeightRound WeightRound { get; set; } = null!;
    public int CommitteeMemberId { get; set; }
    public CommitteeMember CommitteeMember { get; set; } = null!;
    public string MemberUserId { get; set; } = "";
    public ApplicationUser MemberUser { get; set; } = null!;
    [Precision(9, 4)] public decimal Financial { get; set; }
    [Precision(9, 4)] public decimal Technical { get; set; }
    public DateTime SubmittedAt { get; set; }
}

[Index(nameof(TenderRoundId), IsUnique = true)]
public class EvaluationSession
{
    public int Id { get; set; }
    public int TenderRoundId { get; set; }
    public TenderRound TenderRound { get; set; } = null!;
    public int WeightRoundId { get; set; }
    public WeightRound WeightRound { get; set; } = null!;
    public int CommitteeRoundId { get; set; }
    public CommitteeRound CommitteeRound { get; set; } = null!;
    public int? ReportReviewRoundId { get; set; }
    public CommitteeRound? ReportReviewRound { get; set; }
    public int AiAnalysisId { get; set; }
    public AiAnalysis AiAnalysis { get; set; } = null!;
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
    [MaxLength(1000)] public string MeetingUrl { get; set; } = "";
    // Set only when the meeting was created through the Zoom API; null for an officer-supplied link.
    [MaxLength(40)] public string? ZoomMeetingId { get; set; }
    // When the system announced that scoring opened. The StartsAt/EndsAt window stays authoritative.
    public DateTime? OpenedAt { get; set; }
    public SessionState State { get; set; }
    public DateTime? ClosedAt { get; set; }
    public string? ClosedByUserId { get; set; }
    public ApplicationUser? ClosedByUser { get; set; }
    public string? ClosureReason { get; set; }
    public string? FinalApprovalPolicyJson { get; set; }
    public int? ReportId { get; set; }
    public TenderReport? Report { get; set; }
    [Timestamp] public byte[] RowVersion { get; set; } = [];
    public ICollection<SessionBid> EligibleBids { get; set; } = new List<SessionBid>();
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public IReadOnlyList<EvaluationParticipation> Submissions => Invitations.Where(x => x.SubmittedAt != null).ToList();
    public ICollection<EvaluationParticipation> Invitations { get; set; } = new List<EvaluationParticipation>();
}

[Index(nameof(EvaluationSessionId), nameof(BidId), IsUnique = true)]
public class SessionBid
{
    public int Id { get; set; }
    public int EvaluationSessionId { get; set; }
    public EvaluationSession EvaluationSession { get; set; } = null!;
    public int BidId { get; set; }
    public Bid Bid { get; set; } = null!;
}

[Index(nameof(EvaluationSessionId), nameof(CommitteeMemberId), IsUnique = true)]
public class EvaluationParticipation
{
    public int Id { get; set; }
    public int EvaluationSessionId { get; set; }
    public EvaluationSession EvaluationSession { get; set; } = null!;
    public int CommitteeMemberId { get; set; }
    public CommitteeMember CommitteeMember { get; set; } = null!;
    public DateTime SentAt { get; set; }
    public DateTime? SubmittedAt { get; set; }
}

public class AiAnalysis
{
    public int Id { get; set; }
    public int TenderRoundId { get; set; }
    public TenderRound TenderRound { get; set; } = null!;
    public string Status { get; set; } = "Running";
    public string Model { get; set; } = "";
    public string EvidenceJson { get; set; } = "";
    public string? Findings { get; set; }
    public DateTime StartedAt { get; set; }
    public long DurationMilliseconds { get; set; }
    public string? ReviewedByUserId { get; set; }
    public ApplicationUser? ReviewedByUser { get; set; }
    public DateTime? ReviewedAt { get; set; }
}
