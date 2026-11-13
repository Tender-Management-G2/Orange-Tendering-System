using System.ComponentModel.DataAnnotations;
using Orange_Tendering_System.Models.Entities;

namespace Orange_Tendering_System.Areas.TopCommittee.ViewModels;

public sealed class VoteInput
{
    public int RoundId { get; set; }
    [Required, EnumDataType(typeof(VoteDecision))] public VoteDecision? Decision { get; set; }
    [StringLength(2000)] public string? Comment { get; set; }
}

public sealed class QuestionInput
{
    public int RoundId { get; set; }
    [Required, StringLength(200)] public string Subject { get; set; } = "";
    [Required, StringLength(10000)] public string Message { get; set; } = "";
}

public sealed class ReplyInput
{
    public int RoundId { get; set; }
    public int ThreadId { get; set; }
    [Required, StringLength(10000)] public string Message { get; set; } = "";
}

public sealed class RoundDetails
{
    public CommitteeRound Round { get; init; } = null!;
    public Tender Tender => Round.TenderRound.Tender;
    public List<CommitteeMember> Members { get; init; } = [];
    public List<Bid> Bids { get; init; } = [];
    public List<TenderReport> Reports { get; init; } = [];
    public List<CommitteeRound> Evaluations { get; init; } = [];
    public List<Clarification> Threads { get; init; } = [];
    public List<Document> Documents { get; init; } = [];
    public List<string> Blockers { get; set; } = [];
    public string UserId { get; init; } = "";
    public ApplicationUser? Manager { get; set; }
    public TenderReport? FullReport { get; set; }
    public bool IsLatest { get; set; }
    public int FailedAttempts { get; set; }
    public bool HasOpenClarifications { get; set; }
    public ReportAnalysis? AiAnalysis { get; set; }
    public List<CommitteeMember> TopMembers => Members.Where(m => m.CommitteeType == CommitteeType.Top).ToList();
    public List<CommitteeVote> ValidVotes => Round.Votes.Where(v => TopMembers.Any(m => m.Id == v.CommitteeMemberId)).ToList();
    public CommitteeVote? MyVote => ValidVotes.SingleOrDefault(v => v.CommitteeMember.UserId == UserId);
    public bool CanVote => Blockers.Count == 0 && MyVote is null;
    public bool CanClarify => IsLatest && Round.Status == CommitteeRoundStatus.InProgress &&
        Tender.Status == TenderStatus.TopCommitteeReview && Manager is { IsActive: true };
}

public sealed class CommitteeDashboard
{
    public string DisplayName { get; init; } = "";
    public int AwaitingVote { get; set; }
    public int Active { get; set; }
    public int Approved { get; set; }
    public int Rejected { get; set; }
    public List<RoundDetails> Pending { get; init; } = [];
    public List<CommitteeRound> Recent { get; init; } = [];
}

public sealed class DecisionHistory
{
    public string? Search { get; init; }
    public CommitteeRoundStatus? Status { get; init; }
    public int? TenderId { get; init; }
    public int Page { get; set; }
    public int Total { get; set; }
    public int Pages => Math.Max(1, (int)Math.Ceiling(Total / 20m));
    public List<CommitteeRound> Rounds { get; set; } = [];
    public RoundDetails? Selected { get; set; }
}

public sealed class RoundSelection
{
    public string TargetAction { get; init; } = "ReviewEvaluation";
    public string UserId { get; init; } = "";
    public DecisionHistory Listing { get; init; } = new();
}

public sealed record CurrencyValue(string Currency, decimal Amount);
public sealed class SupplierStatistic
{
    public int SupplierId { get; init; }
    public string Name { get; init; } = "";
    public SupplierStatus Status { get; init; }
    public int Invitations { get; init; }
    public int SubmittedOffers { get; init; }
    public int Awards { get; set; }
    public List<CurrencyValue> Values { get; set; } = [];
}

public sealed class ManagementStatistics
{
    public int Suppliers { get; init; }
    public int ApprovedSuppliers { get; init; }
    public int Tenders { get; init; }
    public int ActiveTenders { get; init; }
    public int AwardedTenders { get; init; }
    public int CompletedRequests { get; init; }
    public Dictionary<TenderStatus, int> Distribution { get; init; } = [];
    public List<SupplierStatistic> SupplierRows { get; init; } = [];
    public List<CurrencyValue> Values { get; init; } = [];
    public DateTime GeneratedAt { get; } = DateTime.UtcNow;
}

public static class CommitteeDisplay
{
    public static string Date(DateTime? value) => value?.ToString("yyyy-MM-dd HH:mm 'UTC'") ?? "—";
    public static string Status(CommitteeRoundStatus value) => value == CommitteeRoundStatus.InProgress ? "In Progress" : value.ToString();
    public static string Clarification(ClarificationStatus value) => value switch
    {
        ClarificationStatus.Open => "Pending", ClarificationStatus.Closed => "Resolved", _ => "Answered"
    };
    public static decimal Total(Bid bid) => bid.Items.Sum(i => i.Quantity * i.UnitPrice) +
        bid.TaxAmount + bid.ShippingCost + bid.InstallationCost + bid.MaintenanceCost + bid.OtherCost;
}
