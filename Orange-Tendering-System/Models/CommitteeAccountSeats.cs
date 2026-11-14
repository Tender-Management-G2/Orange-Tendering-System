namespace Orange_Tendering_System.Models;

// Account classification only. Tender-specific assignments still control access and voting.
public static class CommitteeAccountSeats
{
    public const string ClaimType = "orange:committee-account-seat";
    public static readonly CommitteeAccountSeat[] All =
    [
        new("evaluation-technical-1", "Evaluation", "Technical Member 1", "First technical specialist", SystemRoles.EvaluationCommittee),
        new("evaluation-technical-2", "Evaluation", "Technical Member 2", "Second technical specialist", SystemRoles.EvaluationCommittee),
        new("evaluation-manager", "Evaluation", "Sourcing Manager", "Sourcing management and report review", SystemRoles.SourcingManager),
        new("evaluation-financial", "Evaluation", "Financial Representative", "Financial evaluation specialist", SystemRoles.EvaluationCommittee),
        new("top-ceo", "Top", "CEO", "Chief Executive Officer", SystemRoles.TopCommittee),
        new("top-member-1", "Top", "Committee Member 1", "Top Committee decision maker", SystemRoles.TopCommittee),
        new("top-member-2", "Top", "Committee Member 2", "Top Committee decision maker", SystemRoles.TopCommittee),
        new("top-member-3", "Top", "Committee Member 3", "Top Committee decision maker", SystemRoles.TopCommittee)
    ];
    public static CommitteeAccountSeat? Find(string? key) => All.SingleOrDefault(x => x.Key == key);
}
public sealed record CommitteeAccountSeat(string Key, string Committee, string Title, string Description, string Role);
