using System.ComponentModel.DataAnnotations;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.Services;

namespace Orange_Tendering_System.ViewModels;

public class RequirementsInput
{
    public int PurchaseRequestId { get; set; }
    [Required, MaxLength(200)] public string Title { get; set; } = "";
    [Required, MaxLength(10000)] public string Description { get; set; } = "";
    [Required, MaxLength(10000)] public string TechnicalRequirements { get; set; } = "";
    [Required, MaxLength(10000)] public string CommercialConditions { get; set; } = "";
    [Required, MaxLength(4000)] public string DeliveryExpectations { get; set; } = "";
    [Required, MaxLength(4000)] public string MandatoryCompliance { get; set; } = "";
    [Required] public string SourcingManagerId { get; set; } = "";
    [Required] public string DepartmentRepresentativeId { get; set; } = "";
    [Required] public string FinancialRepresentativeId { get; set; } = "";
    [Required] public string TechnicalRepresentativeId { get; set; } = "";
    [Range(0.01, double.MaxValue)] public decimal EstimatedValue { get; set; }
    [Required, RegularExpression("^[A-Z]{3}$")] public string Currency { get; set; } = "JOD";
    public bool IsCritical { get; set; }
}
public class ScoreInput
{
    public int BidId { get; set; }
    [Required, Range(typeof(decimal), "0", "100")] public decimal? Financial { get; set; }
    [Required, Range(typeof(decimal), "0", "100")] public decimal? Technical { get; set; }
    [Required, MaxLength(2000)] public string Justification { get; set; } = "";
}
public class SessionInput
{
    public int AiAnalysisId { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
    // Required unless CreateZoomMeeting is set, in which case the link comes from the Zoom API.
    [Url, MaxLength(1000)] public string? MeetingUrl { get; set; }
    public bool CreateZoomMeeting { get; set; }
}
public class BidInput
{
    public int InvitationId { get; set; }
    [Required, MaxLength(10000)] public string TechnicalProposal { get; set; } = "";
    [Required, RegularExpression("^[A-Z]{3}$")] public string Currency { get; set; } = "JOD";
    [Range(1, 3650)] public int DeliveryDays { get; set; }
    [Required, MaxLength(1000)] public string PaymentTerms { get; set; } = "";
    [Range(typeof(decimal), "0", "9999999999999999.99")] public decimal ExtraCosts { get; set; }
    public List<BidLineInput> Lines { get; set; } = [];
}
public class BidLineInput
{
    public int TenderItemId { get; set; }
    [Required] public decimal? UnitPrice { get; set; }
    public string? Specifications { get; set; }
}
public record WorkflowListRow(int TenderId, string Title, string Status);
public class WorkflowDetails
{
    public Tender Tender { get; set; } = null!;
    public WeightRound? Weights { get; set; }
    public EvaluationSession? Session { get; set; }
    public List<Bid> Bids { get; set; } = [];
    public List<AiAnalysis> Analyses { get; set; } = [];
    public List<Document> Documents { get; set; } = [];
    public List<CommitteeVote> Votes { get; set; } = [];
    public List<AuditLog> History { get; set; } = [];
    public List<EvaluationScore> Scores { get; set; } = [];
    public IReadOnlyList<RankedBid> Ranking { get; set; } = [];
    public string UserId { get; set; } = "";
    public bool IsOfficer { get; set; }
    public bool IsEvaluator { get; set; }
    public bool IsManager { get; set; }
    public bool IsTop { get; set; }
    public bool IsSupplier { get; set; }
    public bool CanApproveWeights { get; set; }
    public bool WeightPolicyConfigured { get; set; }
    public bool FinalPolicyConfigured { get; set; }
    public DateTime UtcNow { get; set; }
    // Round control (officer and assigned Sourcing Manager; suppliers only receive their own earlier bids).
    public int MaxRounds { get; set; }
    public int MinimumOffers { get; set; }
    public int DistinctOffers { get; set; }
    public List<RoundSummary> RoundHistory { get; set; } = [];
    public List<RoundDecision> RoundDecisions { get; set; } = [];
    public List<ExternalSupplierInvitation> ExternalInvitations { get; set; } = [];
    public List<Bid> EarlierBids { get; set; } = [];
}
public record RoundSummary(int RoundId, int RoundNumber, string Status, DateTime? OpensAt, DateTime? Deadline, int Invitations, int SubmittedBids, int ExternalInvitations);
