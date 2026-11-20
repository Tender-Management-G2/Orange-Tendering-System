using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Models.Entities;

namespace Orange_Tendering_System.Services;

// Historical pre-tender votes remain readable. New tenders go from approved weights straight to bidding and invitations;
// the Top Committee reviews the signed evaluation report after supplier offers have been evaluated.
public partial class TenderWorkflowService
{
    public Task SubmitPreTender(int id, string? note, ClaimsPrincipal principal) =>
        Task.FromException(new WorkflowException("Pre-tender submission is retired. Open bidding and invite suppliers after approving the weights."));

    public Task PreTenderVote(int id, PreTenderDecision decision, string? comment, ClaimsPrincipal principal) => Change(id, principal, "PreTenderVoteRecorded", async (t, a) =>
    {
        Require(Enum.IsDefined(decision), "Choose Approve or Return.");
        var member = await Member(t, a, CommitteeType.Top);
        var review = await db.PreTenderReviews.Include(x => x.Votes).Where(x => x.TenderId == id).OrderByDescending(x => x.Attempt).FirstOrDefaultAsync();
        Require(review is { Status: PreTenderReviewStatus.Pending } && t.Status == TenderStatus.Draft, "This tender is not awaiting a pre-tender review.");
        Require(review!.Votes.All(x => x.CommitteeMemberId != member.Id), "Your pre-tender decision is already recorded.");
        comment = decision == PreTenderDecision.Approve ? null : (comment ?? "").Trim();
        Require(decision == PreTenderDecision.Approve || comment!.Length is >= 10 and <= 2000, "Explain what must change before publication (10 to 2000 characters).");
        review.Votes.Add(new PreTenderVote { CommitteeMemberId = member.Id, UserId = a.Id, Decision = decision, Comment = comment, VotedAt = Now });
        if (decision == PreTenderDecision.Return)
        {
            review.Status = PreTenderReviewStatus.Returned; review.CompletedAt = Now;
            await NotifyOfficer(id, "Tender returned by the Top Committee", $"A Top Committee member returned the prepared tender before publication. Reason: {comment} Address it and submit the tender again.");
        }
        else if (review.Votes.Count(x => x.Decision == PreTenderDecision.Approve) >= review.ApprovalsRequired)
        {
            review.Status = PreTenderReviewStatus.Approved; review.CompletedAt = Now;
            await NotifyOfficer(id, "Pre-tender approval complete", "The Top Committee approved the prepared tender. You can now open bidding and invite suppliers.");
        }
        Audit(a, id, "PreTenderVoteEvidence", new { review.Attempt, MemberId = member.Id, Decision = decision.ToString(), Comment = comment });
    });

}
