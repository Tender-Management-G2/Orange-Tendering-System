using Orange_Tendering_System.Models.Entities;

namespace Orange_Tendering_System.ViewModels;

public record TopReasonAgreement(int CommitteeMemberId, TopRejectionReason Reason, string Comment);
public class TopRejectionReview
{
    public int ReportId { get; init; }
    public int TenderId { get; init; }
    public string DocumentHash { get; init; } = "";
    public string Status { get; set; } = "Voting in progress";
    public TopRejectionReason? FinalReason { get; set; }
    public bool CanAgree { get; set; }
    public List<CommitteeVote> Votes { get; init; } = [];
    public List<TopReasonAgreement> Agreements { get; init; } = [];
}
