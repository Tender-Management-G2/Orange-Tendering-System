using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.Services.Reports;
using Orange_Tendering_System.ViewModels;

namespace Orange_Tendering_System.Services;

public partial class TenderWorkflowService
{
    private async Task<(TenderReport Report, EvaluationSession Session)> ReportContext(int reportId)
    {
        var report = await db.TenderReports.Include(x => x.TenderRound).ThenInclude(x => x.Tender).SingleOrDefaultAsync(x => x.Id == reportId);
        Require(report != null, "Report not found.");
        var session = await db.Set<EvaluationSession>().Include(x => x.TenderRound).Include(x => x.WeightRound).Include(x => x.Invitations).Include(x => x.EligibleBids)
            .SingleAsync(x => x.TenderRoundId == report.TenderRoundId);
        return (report, session);
    }
    private async Task ReportReader(Tender tender, ApplicationUser actor)
    {
        Require(!await Role(actor, SystemRoles.Supplier), "Supplier accounts cannot access confidential committee reports.");
        if (await IsOfficer(actor)) return;
        var seat = await db.CommitteeMembers.SingleOrDefaultAsync(x => x.TenderId == tender.Id && x.UserId == actor.Id);
        Require(seat != null && (seat.CommitteeType == CommitteeType.Top ? await Role(actor, SystemRoles.TopCommittee) :
            await Role(actor, SystemRoles.EvaluationCommittee) || seat.Responsibility == CommitteeResponsibility.SourcingManager && await Role(actor, SystemRoles.SourcingManager)), "Current tender assignment and the relevant role are required to view this confidential report.");
    }
    private static ReportReviewStage? ReviewStage(Tender tender, EvaluationSession session) => session.State switch
    {
        SessionState.ReportDraft => ReportReviewStage.CommitteeReview,
        SessionState.Finalized when tender.Status == TenderStatus.OfficerReview => ReportReviewStage.OfficerHandoff,
        SessionState.Finalized when tender.Status == TenderStatus.ManagerReview => ReportReviewStage.ManagerReview,
        SessionState.ManagerApproved when tender.Status == TenderStatus.TopCommitteeReview => ReportReviewStage.TopReview,
        _ => null
    };
    public async Task<ReportReviewModel> ReviewReport(int reportId, ClaimsPrincipal principal)
    {
        var actor = await Actor(principal); var (report, session) = await ReportContext(reportId); var tender = report.TenderRound.Tender;
        await ReportReader(tender, actor);
        await reportDocuments.AssertIntegrity(report, session);
        var stage = ReviewStage(tender, session);
        var current = session.ReportId == report.Id && report.SupersededAt == null;
        var member = await db.CommitteeMembers.SingleOrDefaultAsync(x => x.TenderId == tender.Id && x.UserId == actor.Id);
        var evaluator = member?.CommitteeType == CommitteeType.Evaluation && (await Role(actor, SystemRoles.EvaluationCommittee) || member.Responsibility == CommitteeResponsibility.SourcingManager && await Role(actor, SystemRoles.SourcingManager));
        var manager = member?.Responsibility == CommitteeResponsibility.SourcingManager && await Role(actor, SystemRoles.SourcingManager);
        var officer = await IsOfficer(actor);
        var top = member?.CommitteeType == CommitteeType.Top && await Role(actor, SystemRoles.TopCommittee) && !await Role(actor, SystemRoles.SourcingOfficer) && !await Role(actor, SystemRoles.SourcingManager);
        var result = new ReportReviewModel { Report = report, Tender = tender, UserId = actor.Id, IsCurrent = current, Stage = stage,
            CanSignCommittee = current && stage == ReportReviewStage.CommitteeReview && evaluator,
            CanReviewManager = current && stage == ReportReviewStage.ManagerReview && manager,
            CanVoteTop = current && stage == ReportReviewStage.TopReview && top,
            CanForward = current && stage == ReportReviewStage.OfficerHandoff && officer,
            CanReturn = current && (stage == ReportReviewStage.CommitteeReview && evaluator || stage == ReportReviewStage.OfficerHandoff && officer || stage == ReportReviewStage.ManagerReview && manager || stage == ReportReviewStage.TopReview && top),
            RequireCertificate = reportSigningOptions.Value.RequireCertificateBacked };
        result.Signatures = await db.Set<ReportSignature>().Where(x => x.TenderReportId == reportId).OrderBy(x => x.SignedAt).ToListAsync();
        result.Revisions = await db.Set<ReportRevision>().Where(x => x.TenderReport.TenderRoundId == report.TenderRoundId).OrderBy(x => x.Id).ToListAsync();
        result.Versions = await db.TenderReports.AsNoTracking().Where(x => x.TenderRoundId == report.TenderRoundId && x.ReportType == report.ReportType)
            .OrderByDescending(x => x.VersionNumber).Select(x => new TenderReport { Id = x.Id, VersionNumber = x.VersionNumber, Status = x.Status, CreatedAt = x.CreatedAt, SupersededAt = x.SupersededAt }).ToListAsync();
        return result;
    }
    public async Task<ProtectedReportCopy> ReportPdf(int reportId, ClaimsPrincipal principal)
    {
        var tenderId = await db.TenderReports.Where(x => x.Id == reportId).Select(x => x.TenderRound.TenderId).SingleOrDefaultAsync();
        ProtectedReportCopy? copy = null;
        await Change(tenderId, principal, "ReportPdfAccessed", async (tender, actor) =>
        {
            await ReportReader(tender, actor); var (report, session) = await ReportContext(reportId);
            var bytes = await reportDocuments.AccessCopy(report, session, actor, Now);
            copy = new(bytes, $"Orange-Tender-{tender.Id}-Report-v{report.VersionNumber}.pdf", report.PdfSha256!, ReportCryptography.Hash(bytes));
            db.AuditLogs.Add(new AuditLog { UserId = actor.Id, EntityName = "TenderReport", EntityId = reportId.ToString(), Action = "ProtectedReportViewed", CreatedAt = Now,
                NewValues = JsonSerializer.Serialize(new { ReportId = reportId, report.VersionNumber, CanonicalHash = report.PdfSha256, copy.CopyHash, AccessedUtc = Now }) });
        });
        return copy!;
    }
    public async Task<object> VerifyReport(int reportId, ClaimsPrincipal principal)
    {
        var actor = await Actor(principal); var (report, session) = await ReportContext(reportId); await ReportReader(report.TenderRound.Tender, actor);
        await reportDocuments.AssertIntegrity(report, session);
        var receipts = await db.Set<ReportSignature>().Where(x => x.TenderReportId == reportId).ToListAsync();
        return new { report.Id, report.VersionNumber, report.PdfSha256, report.SnapshotSha256, CanonicalIntegrity = true,
            CurrentVersion = session.ReportId == report.Id && report.SupersededAt == null,
            Signatures = receipts.Select(x => new { x.Id, x.UserId, Stage = x.Stage.ToString(), x.Decision, Method = x.Method.ToString(), x.CertificateThumbprint,
                ReceiptIntegrity = ReportCryptography.Verify(x, report), CertificateBacked = x.Method == SignatureMethod.CertificateCms,
                CertificateTrustCurrentlyValid = x.Method == SignatureMethod.CertificateCms ? (bool?)ReportCryptography.CertificateTrustValid(x) : null,
                ApplicableToCurrentVersion = x.InvalidatedAt == null && session.ReportId == report.Id, x.InvalidatedAt }) };
    }
    public async Task<(byte[] Bytes, string Name, string Type)> SignatureReceipt(long id, ClaimsPrincipal principal, bool manifest = false)
    {
        var actor = await Actor(principal); var signature = await db.Set<ReportSignature>().SingleOrDefaultAsync(x => x.Id == id);
        Require(signature != null, "Signature receipt not found.");
        var (report, session) = await ReportContext(signature.TenderReportId); await ReportReader(report.TenderRound.Tender, actor);
        await reportDocuments.AssertIntegrity(report, session);
        if (manifest) return (System.Text.Encoding.UTF8.GetBytes(signature.ManifestJson), $"Report-{report.Id}-Receipt-{id}.manifest.json", "application/json");
        if (signature.Method == SignatureMethod.CertificateCms) return (signature.CmsSignature!, $"Report-{report.Id}-Receipt-{id}.p7s", "application/pkcs7-signature");
        return (System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { signature.Method, signature.ManifestJson, signature.InvalidatedAt, signature.InvalidationReason })), $"Report-{report.Id}-Acknowledgment-{id}.json", "application/json");
    }
    private async Task CheckReturnAuthority(Tender tender, ApplicationUser actor, ReportReviewStage stage)
    {
        switch (stage)
        {
            case ReportReviewStage.CommitteeReview: await Member(tender, actor, CommitteeType.Evaluation); break;
            case ReportReviewStage.OfficerHandoff: await Officer(tender, actor); break;
            case ReportReviewStage.ManagerReview:
                Require(await Role(actor, SystemRoles.SourcingManager) && await db.CommitteeMembers.AnyAsync(x => x.TenderId == tender.Id && x.UserId == actor.Id && x.Responsibility == CommitteeResponsibility.SourcingManager), "Only the assigned Sourcing Manager can return at Manager Review."); break;
            case ReportReviewStage.TopReview: await Member(tender, actor, CommitteeType.Top); break;
            default: throw new WorkflowException("This is not a valid report review stage.");
        }
    }
    private async Task RequestManagerReturn(int tenderId, string comment, ClaimsPrincipal principal, string? hash)
    {
        var reportId = await db.Set<EvaluationSession>().Where(x => x.TenderRound.TenderId == tenderId).OrderByDescending(x => x.TenderRound.RoundNumber).Select(x => x.ReportId).FirstOrDefaultAsync();
        Require(reportId.HasValue, "No current report exists.");
        await RequestReportRevision(reportId.Value, new ReturnReportInput { RequestId = Guid.NewGuid(), Target = RevisionTarget.SourcingOfficer,
            Correction = RevisionCorrection.NarrativeOnly, Feedback = comment, RequiredAction = comment, DocumentHash = hash ?? "" }, principal);
    }
    private (bool Allowed, string Evidence, string? Blocker) RevisionRoute(ReportReviewStage stage, ReturnReportInput input, ApplicationUser actor)
    {
        if (input.Correction != RevisionCorrection.NarrativeOnly)
            return (false, "No authorized versioned procedure", "Changing approved weights, deadline-locked bids or finalized scores requires a separately approved, implemented versioned procedure. It is not unlocked by a narrative return policy.");
        if (stage == ReportReviewStage.ManagerReview && input.Target == RevisionTarget.SourcingOfficer)
            return (true, "Existing approved Manager-to-Officer narrative correction workflow", null);
        var policy = revisionPolicyOptions.Value;
        var route = policy.Routes.FirstOrDefault(x => x.Source == stage && x.Target == input.Target && x.ReturningUserIds.Contains(actor.Id));
        return !string.IsNullOrWhiteSpace(policy.Version) && route != null ? (true, JsonSerializer.Serialize(new { policy.Version, Route = route }), null) :
            (false, "No matching explicit revision policy", "Awaiting Revision Policy: this source/target route and returning identity have not been explicitly authorized.");
    }
    public async Task RequestReportRevision(int reportId, ReturnReportInput input, ClaimsPrincipal principal)
    {
        var tenderId = await db.TenderReports.Where(x => x.Id == reportId).Select(x => x.TenderRound.TenderId).SingleOrDefaultAsync();
        await Change(tenderId, principal, "ReportReturnedForRevision", async (tender, actor) =>
        {
            var (report, session) = await ReportContext(reportId);
            Require(input.RequestId != Guid.Empty && Enum.IsDefined(input.Target) && Enum.IsDefined(input.Correction) && !string.IsNullOrWhiteSpace(input.Feedback) && input.Feedback.Length <= 4000 && !string.IsNullOrWhiteSpace(input.RequiredAction) && input.RequiredAction.Length <= 4000, "A request ID, valid target, mandatory feedback and required corrective action are required.");
            var repeated = await db.Set<ReportRevision>().SingleOrDefaultAsync(x => x.RequestId == input.RequestId);
            if (repeated != null)
            {
                Require(repeated.ReturningUserId == actor.Id && repeated.TenderReportId == reportId && repeated.Feedback == input.Feedback && repeated.RequiredAction == input.RequiredAction && repeated.TargetStage == input.Target && repeated.Correction == input.Correction && input.DocumentHash == report.PdfSha256, "An idempotency key cannot be reused for a different revision request.");
                await CheckReturnAuthority(tender, actor, repeated.SourceStage); return;
            }
            var stage = ReviewStage(tender, session);
            Require(session.ReportId == reportId && stage.HasValue && report.SupersededAt == null, "Only the current report at an active review stage can be returned.");
            await CheckReturnAuthority(tender, actor, stage.Value);
            await reportDocuments.AssertIntegrity(report, session);
            Require(input.DocumentHash == report.PdfSha256, "The report version/hash changed; reopen the current PDF.");
            Require(await db.AuditLogs.AnyAsync(x => x.EntityName == "TenderReport" && x.EntityId == reportId.ToString() && x.UserId == actor.Id && x.Action == "ProtectedReportViewed" && x.NewValues!.Contains(input.DocumentHash)), "Open the report before returning it for revision.");
            Require(!await db.Set<ReportRevision>().AnyAsync(x => x.TenderReportId == reportId && x.Status != RevisionStatus.Resubmitted), "This report already has an outstanding revision request.");
            var route = RevisionRoute(stage.Value, input, actor);
            var revision = new ReportRevision { RequestId = input.RequestId, TenderReportId = reportId, SourceStage = stage.Value, SourceSessionState = session.State,
                TargetStage = input.Target, Correction = input.Correction, Feedback = input.Feedback, RequiredAction = input.RequiredAction,
                ReturningUserId = actor.Id, ResponsibleUserId = input.Target == RevisionTarget.SourcingOfficer ? (await GlobalOfficer()).Id : report.CreatedByUserId,
                RequestedAt = Now, PolicyEvidenceJson = route.Evidence, Blocker = route.Blocker,
                Status = route.Allowed ? RevisionStatus.AwaitingCorrection : RevisionStatus.PolicyBlocked };
            db.Add(revision);
            session.State = route.Allowed ? SessionState.Returned : SessionState.RevisionBlocked;
            if (route.Allowed) report.Status = ReportStatus.Returned;
            if (stage == ReportReviewStage.ManagerReview && route.Allowed)
            { report.ReviewedAt = Now; report.ReviewedByUserId = actor.Id; report.ReviewComment = input.Feedback; tender.Status = TenderStatus.OfficerReview; }
            Notify(revision.ResponsibleUserId, tenderId, route.Allowed ? "Report revision required" : "Report revision policy blocker", input.Feedback + " Required action: " + input.RequiredAction);
            Audit(actor, tenderId, "RevisionRequestEvidence", new { reportId, report.VersionNumber, revision.SourceStage, input.Target, input.Correction, input.Feedback, input.RequiredAction, revision.ResponsibleUserId, route.Evidence, route.Blocker });
        });
    }
    public async Task AuthorizeBlockedRevision(int revisionId, ClaimsPrincipal principal)
    {
        var tenderId = await db.Set<ReportRevision>().Where(x => x.Id == revisionId).Select(x => x.TenderReport.TenderRound.TenderId).SingleOrDefaultAsync();
        await Change(tenderId, principal, "RevisionPolicyAuthorized", async (tender, actor) =>
        {
            var revision = await db.Set<ReportRevision>().SingleAsync(x => x.Id == revisionId); var (report, session) = await ReportContext(revision.TenderReportId);
            await CheckReturnAuthority(tender, actor, revision.SourceStage);
            Require(revision.ReturningUserId == actor.Id, "Only the original authorized returner may activate this request.");
            if (revision.Status != RevisionStatus.PolicyBlocked) return; // Already activated/resubmitted: do not apply policy or state changes twice.
            Require(revision.ReturningUserId == actor.Id && revision.Status == RevisionStatus.PolicyBlocked && session.State == SessionState.RevisionBlocked && session.ReportId == report.Id, "Only the original authorized returner may activate this blocked request after policy approval.");
            var route = RevisionRoute(revision.SourceStage, new ReturnReportInput { Correction = revision.Correction, Target = revision.TargetStage }, actor);
            Require(route.Allowed, route.Blocker ?? "Revision policy remains blocked.");
            await reportDocuments.AssertIntegrity(report, session);
            revision.Status = RevisionStatus.AwaitingCorrection; revision.PolicyEvidenceJson = route.Evidence; revision.Blocker = null;
            report.Status = ReportStatus.Returned; session.State = SessionState.Returned;
            Notify(revision.ResponsibleUserId, tenderId, "Revision policy authorized", "The recorded narrative correction may now be submitted as a new version.");
        });
    }
    public async Task ResubmitReportRevision(int revisionId, string correction, ClaimsPrincipal principal)
    {
        var tenderId = await db.Set<ReportRevision>().Where(x => x.Id == revisionId).Select(x => x.TenderReport.TenderRound.TenderId).SingleOrDefaultAsync();
        await Change(tenderId, principal, "ReportRevisionResubmitted", async (tender, actor) =>
            await ResubmitRevisionCore(tender, actor, revisionId, correction));
    }
    private async Task ResubmitRevisionCore(Tender tender, ApplicationUser actor, int revisionId, string correction)
    {
        var revision = await db.Set<ReportRevision>().SingleAsync(x => x.Id == revisionId);
        Require(revision.ResponsibleUserId == actor.Id, "Only the responsible transaction owner can correct this report.");
        if (await IsOfficer(actor)) await Officer(tender, actor);
        else await Member(tender, actor, CommitteeType.Evaluation);
        Require(!string.IsNullOrWhiteSpace(correction) && correction.Length <= 10000, "A corrective narrative of up to 10000 characters is required.");
        if (revision.Status == RevisionStatus.Resubmitted) { Require(revision.CorrectionSummary == correction, "A submitted revision cannot be overwritten."); return; }
        var session = await Session(tender.Id); var old = session.Report!;
        Require(revision.Status == RevisionStatus.AwaitingCorrection && revision.Correction == RevisionCorrection.NarrativeOnly && session.State == SessionState.Returned && session.ReportId == revision.TenderReportId, "This revision is blocked, obsolete or not awaiting a permitted narrative correction.");
        await reportDocuments.AssertIntegrity(old, session);
        var ranking = await Ranking(session); // Recalculate from unchanged original inputs, never from browser scores.
        var version = old.VersionNumber + 1;
        var next = new TenderReport { TenderRoundId = session.TenderRoundId, ReportType = old.ReportType, VersionNumber = version, PreviousReportId = old.Id,
            CreatedAt = Now, CreatedByUserId = actor.Id, RecommendedBidId = ranking.Count(x => x.Rank == 1) == 1 ? ranking.Single(x => x.Rank == 1).BidId : null,
            RankingSnapshotJson = JsonSerializer.Serialize(ranking), Content = JsonSerializer.Serialize(new { Justification = ReportDocumentService.Explanation(old.Content), TextualCorrection = correction, RevisionRequestId = revision.Id, OriginalReportId = old.Id }) };
        await reportDocuments.Invalidate(old, version, Now);
        session.Report = next; session.State = SessionState.ReportDraft; tender.Status = TenderStatus.Evaluation;
        session.ReportReviewRound = new CommitteeRound { TenderRoundId = session.TenderRoundId, CommitteeType = CommitteeType.Evaluation, RoundNumber = version,
            TenderReport = next, StartedAt = Now, FinancialWeight = session.WeightRound.ApprovedFinancial, TechnicalWeight = session.WeightRound.ApprovedTechnical };
        revision.ResubmittedReport = next; revision.ResubmittedAt = Now; revision.CorrectionSummary = correction; revision.Status = RevisionStatus.Resubmitted;
        await db.SaveChangesAsync();
        await reportDocuments.Prepare(next, session);
        foreach (var member in await Members(tender.Id, CommitteeType.Evaluation)) Notify(member.UserId, tender.Id, "New report version requires signatures", $"Version {version} incorporates the recorded narrative correction. Review and sign again; no old signature carries forward.");
    }
}
