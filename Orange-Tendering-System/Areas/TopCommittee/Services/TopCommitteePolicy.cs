using Orange_Tendering_System.Areas.TopCommittee.ViewModels;
using Orange_Tendering_System.Models.Entities;

namespace Orange_Tendering_System.Areas.TopCommittee.Services;

public static class TopCommitteePolicy
{
    // Mutates only the supplied tracked entities. Persistence and notification insertion are the service's atomic transaction.
    public static Notification Complete(RoundDetails details, string actor, DateTime now)
    {
        var outcome = Outcome(details.TopMembers, details.Round.Votes.ToList());
        if (outcome == CommitteeRoundStatus.InProgress || Blockers(details, allowFourthVote: true).Count != 0 ||
            details.TopMembers.All(m => m.UserId != actor) || details.Round.TenderReport is not { } summary)
            throw new InvalidOperationException("This round cannot be finalized or has already been processed.");
        var recommended = details.Bids.SingleOrDefault(b => b.Id == summary.RecommendedBidId)
            ?? throw new InvalidOperationException("The recommended offer is unavailable.");
        var manager = details.Manager!;
        var fullReport = details.FullReport!;
        var comments = string.Join("; ", details.Round.Votes.Where(v => v.Decision == VoteDecision.Reject)
            .Select(v => $"{v.CommitteeMember.User.FullName}: {v.Comment}"));
        details.Round.Status = outcome;
        details.Round.CompletedAt = now;
        summary.ReviewedAt = now;
        summary.ReviewedByUserId = actor;
        summary.ReviewComment = outcome == CommitteeRoundStatus.Approved ? "Approved by all four Top Committee members."
            : comments[..Math.Min(comments.Length, 2000)];
        if (outcome == CommitteeRoundStatus.Approved)
        {
            summary.Status = ReportStatus.Accepted;
            details.Tender.AwardedBidId = recommended.Id;
            details.Tender.AwardedAt = now;
            details.Tender.Status = TenderStatus.Awarded;
        }
        else
        {
            summary.Status = ReportStatus.Returned;
            details.Tender.Status = TenderStatus.ManagerReview;
        }
        return new Notification
        {
            UserId = manager.Id, Title = outcome == CommitteeRoundStatus.Approved
                ? "Top Committee — Final Signed Report Approved" : "Top Committee — Final Decision Rejected",
            Message = $"Tender #{details.Tender.Id}: {details.Tender.Title}. Committee round {details.Round.RoundNumber}: {outcome}. Recommended Supplier: {recommended.TenderInvitation.Supplier.CompanyName}. All four members have voted. Decision date: {CommitteeDisplay.Date(now)}.",
            Link = $"/SourcingManager/Reports/Review/{fullReport.Id}", CreatedAt = now
        };
    }

    public static CommitteeRoundStatus Outcome(IReadOnlyCollection<CommitteeMember> members,
        IReadOnlyCollection<CommitteeVote> votes)
    {
        var ids = members.Where(m => m.CommitteeType == CommitteeType.Top).Select(m => m.Id).ToHashSet();
        if (members.Count != 4 || ids.Count != 4 || members.Select(m => m.UserId).Distinct().Count() != 4)
            throw new InvalidOperationException("Exactly four distinct Top Committee members are required.");
        if (votes.Any(v => !ids.Contains(v.CommitteeMemberId) || !Enum.IsDefined(v.Decision) ||
                (v.Decision == VoteDecision.Reject && string.IsNullOrWhiteSpace(v.Comment))) ||
            votes.Select(v => v.CommitteeMemberId).Distinct().Count() != votes.Count)
            throw new InvalidOperationException("Invalid or duplicate committee votes.");
        if (votes.Count < 4) return CommitteeRoundStatus.InProgress;
        return votes.All(v => v.Decision == VoteDecision.Approve)
            ? CommitteeRoundStatus.Approved : CommitteeRoundStatus.Rejected;
    }

    public static List<string> Blockers(RoundDetails details, bool allowFourthVote = false)
    {
        var result = new List<string>();
        var round = details.Round;
        var tender = details.Tender;
        var summary = round.TenderReport;
        var members = details.TopMembers;
        if (!details.IsLatest) result.Add("This is an outdated round or report version.");
        if (round.Status != CommitteeRoundStatus.InProgress) result.Add("Voting on this round is complete.");
        if (tender.Status != TenderStatus.TopCommitteeReview || tender.AwardedBidId.HasValue || tender.AwardedAt.HasValue)
            result.Add("The tender is not awaiting Top Committee review or is already awarded.");
        if (members.Count != 4 || members.Select(m => m.UserId).Distinct().Count() != 4 ||
            members.Any(m => !m.User.IsActive || m.AssignedAt > round.StartedAt))
            result.Add("Four active members assigned before this round started are required.");
        var evaluationMembers = details.Members.Where(m => m.CommitteeType == CommitteeType.Evaluation).ToList();
        if (evaluationMembers.Count != 4 || evaluationMembers.Select(m => m.Responsibility).Distinct().Count() != 4 ||
            evaluationMembers.Any(m => m.Responsibility is null) ||
            members.Any(m => evaluationMembers.Any(e => e.UserId == m.UserId)))
            result.Add("The two four-member committees must be distinct and correctly assigned.");
        if (details.Manager is null) result.Add("The responsible Sourcing Manager could not be verified.");
        if (summary is null || summary.ReportType != ReportType.ExecutiveSummary || summary.SubmittedAt is null ||
            summary.Status != ReportStatus.Submitted || summary.TenderRoundId != round.TenderRoundId ||
            summary.CreatedByUserId != details.Manager?.Id || string.IsNullOrWhiteSpace(summary.Content))
            result.Add("A submitted executive summary from the responsible Manager is required.");
        var full = details.FullReport;
        if (full is null || full.Status != ReportStatus.Accepted || full.SubmittedAt is null ||
            full.ReviewedAt is null || full.ReviewedByUserId != details.Manager?.Id ||
            full.RecommendedBidId != summary?.RecommendedBidId ||
            full.ReviewedAt < full.SubmittedAt || summary?.SubmittedAt < full.ReviewedAt || round.StartedAt < summary?.SubmittedAt)
            result.Add("The Manager-accepted detailed evaluation and matching recommendation are required.");
        var evaluation = details.Evaluations.OrderByDescending(e => e.RoundNumber).FirstOrDefault();
        if (evaluation is null || evaluation.Status != CommitteeRoundStatus.Approved || evaluation.CompletedAt is null ||
            evaluation.Votes.Count != 4 || evaluation.Votes.Any(v => v.Decision != VoteDecision.Approve ||
                !evaluationMembers.Any(m => m.Id == v.CommitteeMemberId)) ||
            evaluation.Votes.Select(v => v.CommitteeMemberId).Distinct().Count() != 4)
            result.Add("The latest Evaluation Committee round must have four recorded approvals.");
        var minimum = details.FailedAttempts >= 3 ? 2 : 3;
        if (details.Bids.Select(b => b.TenderInvitation.SupplierId).Distinct().Count() < minimum)
            result.Add($"The current tender round requires at least {minimum} distinct submitted supplier offers.");
        var recommended = details.Bids.SingleOrDefault(b => b.Id == summary?.RecommendedBidId);
        if (recommended is null || recommended.Status != BidStatus.Submitted || recommended.SubmittedAt is null ||
            recommended.ComplianceStatus != ComplianceStatus.Compliant ||
            recommended.TenderInvitation.Supplier.Status != SupplierStatus.Approved ||
            recommended.Items.Count == 0 || recommended.Items.Any(i => i.Quantity <= 0 || i.UnitPrice < 0) ||
            recommended.TaxAmount < 0 || recommended.ShippingCost < 0 || recommended.InstallationCost < 0 ||
            recommended.MaintenanceCost < 0 || recommended.OtherCost < 0)
            result.Add("The recommendation must identify a current, submitted, compliant and eligible offer.");
        if (details.HasOpenClarifications) result.Add("Resolve the pending committee/Manager clarifications before voting.");
        if (round.Votes.Any(v => !members.Any(m => m.Id == v.CommitteeMemberId) || !Enum.IsDefined(v.Decision)) ||
            round.Votes.Any(v => v.Decision == VoteDecision.Reject && string.IsNullOrWhiteSpace(v.Comment)) ||
            round.Votes.Select(v => v.CommitteeMemberId).Distinct().Count() != round.Votes.Count ||
            round.Votes.Count > 4 || (!allowFourthVote && round.Votes.Count == 4))
            result.Add("The stored votes require review before another vote can be recorded.");
        return result;
    }
}
