using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.ViewModels;

namespace Orange_Tendering_System.Services;

// Top decision records. A single Reject stops the approval (see TenderWorkflowService.TopRejection.cs); this file only reads and records reasons.
public partial class TenderWorkflowService
{
    private async Task<TopRejectionReview> TopReasonState(Tender tender, EvaluationSession session, CommitteeRound round)
    {
        var votes = await db.CommitteeVotes.AsNoTracking().Include(x => x.CommitteeMember).ThenInclude(x => x.User)
            .Where(x => x.CommitteeRoundId == round.Id).OrderBy(x => x.Id).ToListAsync();
        var seats = await Members(tender.Id, CommitteeType.Top);
        var evidence = await db.AuditLogs.AsNoTracking().Where(x => x.EntityName == "Tender" && x.EntityId == tender.Id.ToString() && x.Action == "TopReasonAgreementEvidence")
            .OrderBy(x => x.Id).ToListAsync();
        var agreements = new Dictionary<int, TopReasonAgreement>();
        foreach (var log in evidence)
        {
            if (log.NewValues == null) continue;
            using var json = JsonDocument.Parse(log.NewValues);
            var value = json.RootElement;
            if (value.GetProperty("ReportId").GetInt32() != session.ReportId || value.GetProperty("CommitteeRoundId").GetInt32() != round.Id) continue;
            var memberId = value.GetProperty("CommitteeMemberId").GetInt32();
            if (!seats.Any(x => x.Id == memberId && x.UserId == log.UserId)) continue;
            agreements[memberId] = new(memberId, (TopRejectionReason)value.GetProperty("Reason").GetInt32(), value.GetProperty("Comment").GetString() ?? "");
        }
        var result = new TopRejectionReview { ReportId = session.ReportId!.Value, TenderId = tender.Id,
            DocumentHash = session.Report!.PdfSha256 ?? "", Votes = votes, Agreements = agreements.Values.ToList() };
        // New rule: the first authorized Reject stops the Top approval; its reason is the final reason.
        var firstReject = votes.FirstOrDefault(x => x.Decision != VoteDecision.Approve);
        if (firstReject != null)
        {
            result.FinalReason = firstReject.RejectionReason;
            result.Status = firstReject.Decision == VoteDecision.Retender
                ? "Re-Tender requested — approval stopped and forwarded to the Sourcing Manager"
                : "Top rejection finalized — approval stopped and forwarded to the Sourcing Manager";
            return result;
        }
        var policy = FinalPolicy(session);
        var approves = votes.Count(x => x.Decision == VoteDecision.Approve);
        var rejects = votes.Count(x => x.Decision == VoteDecision.Reject);
        // Wait for the existing four-seat vote, quorum, tie and veto policy. A reason never turns an approval into a rejection.
        if (seats.Count != 4 || votes.Count != 4 || votes.Count < policy.TopQuorum) return result;
        if (approves >= policy.TopApprovalsRequired && (policy.RejectVoteBlocksAward != true || rejects == 0) &&
            (approves != rejects || policy.TopTieOutcome == "Approve"))
        { result.Status = "Approved under existing voting policy"; return result; }
        if (approves == rejects && policy.TopTieOutcome != "Reject")
        { result.Status = "Pending existing tie policy"; return result; }
        if (rejects == 0 || !(rejects > approves || policy.RejectVoteBlocksAward == true || approves == rejects && policy.TopTieOutcome == "Reject"))
        { result.Status = "Pending existing voting policy"; return result; }
        var reasons = votes.Where(x => x.Decision == VoteDecision.Reject).Select(x => x.RejectionReason).Distinct().ToArray();
        if (reasons.Length == 1 && reasons[0].HasValue && Enum.IsDefined(reasons[0]!.Value)) result.FinalReason = reasons[0];
        else if (agreements.Count == 4 && agreements.Values.Select(x => x.Reason).Distinct().Count() == 1)
            result.FinalReason = agreements.Values.First().Reason;
        result.Status = result.FinalReason.HasValue ? "Top rejection finalized — no downstream action" : "Pending agreement on one rejection reason";
        result.CanAgree = !result.FinalReason.HasValue && round.Status == CommitteeRoundStatus.InProgress;
        return result;
    }

    private async Task FinalizeTopRejectionReason(Tender tender, EvaluationSession session, CommitteeRound round, ApplicationUser actor)
    {
        if (round.Status != CommitteeRoundStatus.InProgress) return;
        var state = await TopReasonState(tender, session, round);
        if (!state.FinalReason.HasValue) return;
        round.Status = CommitteeRoundStatus.Rejected;
        round.CompletedAt = Now;
        Audit(actor, tender.Id, "TopRejectionFinalized", new { ReportId = session.ReportId, CommitteeRoundId = round.Id, Reason = state.FinalReason.Value, DownstreamAction = "None" });
    }

    public async Task<TopRejectionReview> TopRejectionDetails(int reportId, ClaimsPrincipal principal)
    {
        var actor = await Actor(principal);
        var (report, session) = await ReportContext(reportId);
        await Member(report.TenderRound.Tender, actor, CommitteeType.Top);
        await reportDocuments.AssertIntegrity(report, session);
        Require(session.ReportId == reportId && report.SupersededAt == null, "Open the current report version.");
        // ReportContext does not attach the current report navigation.
        session.Report = report;
        var round = await db.CommitteeRounds.SingleAsync(x => x.TenderReportId == reportId && x.CommitteeType == CommitteeType.Top);
        return await TopReasonState(report.TenderRound.Tender, session, round);
    }

    public async Task AgreeTopRejectionReason(int reportId, TopRejectionReason reason, string comment, string documentHash, bool confirmed, ClaimsPrincipal principal)
    {
        var tenderId = await db.TenderReports.Where(x => x.Id == reportId).Select(x => x.TenderRound.TenderId).SingleAsync();
        await Change(tenderId, principal, "TopReasonAgreementRecorded", async (tender, actor) =>
        {
            var member = await Member(tender, actor, CommitteeType.Top);
            var session = await Session(tenderId);
            Require(session.ReportId == reportId && session.State == SessionState.ManagerApproved && tender.Status == TenderStatus.TopCommitteeReview,
                "Only the current report awaiting Top Committee review can receive reason agreements.");
            Require(Enum.IsDefined(reason) && confirmed && !string.IsNullOrWhiteSpace(comment) && comment.Length <= 2000, "Select a valid reason, confirm it and provide a comment (up to 2000 characters).");
            await reportDocuments.AssertIntegrity(session.Report!, session);
            Require(string.Equals(documentHash, session.Report!.PdfSha256, StringComparison.Ordinal), "The report hash changed. Review the current report.");
            var round = await db.CommitteeRounds.SingleAsync(x => x.TenderReportId == reportId && x.CommitteeType == CommitteeType.Top);
            var state = await TopReasonState(tender, session, round);
            var previous = state.Agreements.SingleOrDefault(x => x.CommitteeMemberId == member.Id);
            if (previous?.Reason == reason) return; // Idempotent retries; no duplicate evidence or final decision.
            Require(state.CanAgree, "Reason agreement is only available for an unresolved final Top rejection.");
            Audit(actor, tenderId, "TopReasonAgreementEvidence", new { ReportId = reportId, CommitteeRoundId = round.Id, CommitteeMemberId = member.Id, Reason = reason, Comment = comment.Trim(), DocumentHash = documentHash });
            await db.SaveChangesAsync();
            await FinalizeTopRejectionReason(tender, session, round, actor);
        });
    }
}
