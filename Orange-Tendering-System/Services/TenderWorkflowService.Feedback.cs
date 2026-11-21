using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Models.Entities;

namespace Orange_Tendering_System.Services;

// Read-only facts for action feedback (Orange Motion). Nothing here changes workflow state.
public partial class TenderWorkflowService
{
    /// <summary>
    /// Approvals recorded so far in the tender's current committee round of the given type, or null when there is no round.
    /// Evaluation: the round that finalizes the current report version. Top: the round for the current report.
    /// </summary>
    public async Task<int?> CurrentRoundApprovals(int tenderId, CommitteeType type)
    {
        var session = await db.Set<EvaluationSession>().AsNoTracking()
            .Where(x => x.TenderRound.TenderId == tenderId && x.TenderRound.RoundNumber == db.TenderRounds.Where(r => r.TenderId == tenderId).Max(r => r.RoundNumber))
            .Select(x => new { x.TenderRoundId, x.ReportId, x.ReportReviewRoundId, x.CommitteeRoundId })
            .SingleOrDefaultAsync();
        if (session is null) return null;
        int? roundId = type == CommitteeType.Evaluation
            ? session.ReportReviewRoundId ?? session.CommitteeRoundId
            : await db.CommitteeRounds.AsNoTracking()
                .Where(x => x.TenderRoundId == session.TenderRoundId && x.CommitteeType == CommitteeType.Top && x.TenderReportId == session.ReportId)
                .Select(x => (int?)x.Id).SingleOrDefaultAsync();
        if (roundId is null) return null;
        return await db.CommitteeVotes.AsNoTracking().CountAsync(x => x.CommitteeRoundId == roundId && x.Decision == VoteDecision.Approve);
    }

    public Task<int?> TenderIdForReport(int reportId) =>
        db.TenderReports.AsNoTracking().Where(x => x.Id == reportId).Select(x => (int?)x.TenderRound.TenderId).SingleOrDefaultAsync();
}
