using System.Data;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.ViewModels;

namespace Orange_Tendering_System.Services;

// The Sourcing Manager's executive summary for the Top Committee. It is the Manager's own document (a TenderReport of type
// ExecutiveSummary linked to the signed evaluation report through PreviousReportId). The Manager edits it freely while it is a
// draft; submitting it signs the Manager section of the evaluation report and opens the Top Committee decision. Committee
// scores, votes, signatures and the signed report itself are never modified here.
public partial class TenderWorkflowService
{
    public sealed record ExecutiveFacts(string Reference, string Title, string Department, int RequestId, string? Recommended, decimal? RecommendedTotal, string? Currency,
        decimal? FinancialWeight, decimal? TechnicalWeight, IReadOnlyList<(string Supplier, decimal Score, int Rank)> Ranking, int EligibleOffers, int ReceivedOffers,
        int CommitteeSignatures, int ReportVersion, string? ReportHash);

    public sealed class ExecutiveSummaryPage
    {
        public ReportReviewModel Review { get; set; } = null!;
        public ExecutiveFacts Facts { get; set; } = null!;
        public ExecutiveSummaryInput Input { get; set; } = new();
        public TenderReport? Draft { get; set; }
        public TenderReport? Submitted { get; set; }
        public bool CanEdit { get; set; }
    }

    private async Task<(Tender Tender, EvaluationSession Session)> ManagerStage(int tenderId, ApplicationUser actor)
    {
        Require(await Role(actor, SystemRoles.SourcingManager) && await db.CommitteeMembers.AnyAsync(x => x.TenderId == tenderId && x.UserId == actor.Id && x.Responsibility == CommitteeResponsibility.SourcingManager),
            "Only the Sourcing Manager assigned to this tender can prepare its executive summary.");
        var tender = await db.Tenders.SingleAsync(x => x.Id == tenderId);
        return (tender, await Session(tenderId));
    }

    public async Task<ExecutiveSummaryPage> ExecutiveSummary(int tenderId, ClaimsPrincipal principal)
    {
        var actor = await Actor(principal);
        var (tender, s) = await ManagerStage(tenderId, actor);
        Require(s.ReportId != null, "There is no signed evaluation report yet.");
        var review = await ReviewReport(s.ReportId!.Value, principal);
        var summaries = await db.TenderReports.AsNoTracking().Where(x => x.ReportType == ReportType.ExecutiveSummary && x.PreviousReportId == s.ReportId).OrderByDescending(x => x.VersionNumber).ToListAsync();
        var page = new ExecutiveSummaryPage
        {
            Review = review, Facts = await Facts(tender, s),
            Draft = summaries.FirstOrDefault(x => x.Status == ReportStatus.Draft && x.CreatedByUserId == actor.Id),
            Submitted = summaries.FirstOrDefault(x => x.Status == ReportStatus.Submitted),
            CanEdit = review.CanReviewManager
        };
        page.Input = ReadSummary(page.Submitted ?? page.Draft) ?? await Prefill(tender, s);
        return page;
    }

    public Task SaveExecutiveSummary(int tenderId, ExecutiveSummaryInput input, ClaimsPrincipal principal) =>
        Change(tenderId, principal, "ExecutiveSummaryDraftSaved", async (t, a) => await StoreSummary(t, a, input, submit: false));

    // One transaction: the summary is submitted and the Manager's approval signature is recorded together, or neither is.
    public async Task SubmitExecutiveSummary(int tenderId, ExecutiveSummaryInput input, ClaimsPrincipal principal, string documentHash, bool confirmed, byte[]? signatureImage)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await Change(tenderId, principal, "ExecutiveSummarySubmitted", async (t, a) => await StoreSummary(t, a, input, submit: true));
        await ManagerDecision(tenderId, "Approve", "Executive summary submitted to the Top Committee.", principal, documentHash, confirmed, signatureImage);
        await tx.CommitAsync();
    }

    private async Task StoreSummary(Tender t, ApplicationUser a, ExecutiveSummaryInput input, bool submit)
    {
        var (_, s) = await ManagerStage(t.Id, a);
        Require(t.Status == TenderStatus.ManagerReview && s.State == SessionState.Finalized && s.Report is { ReviewedAt: null }, "The executive summary can only be prepared while the signed report awaits the Manager.");
        var fields = new[] { input.BusinessNeed, input.KeyFindings, input.RecommendationReasons, input.ManagerAssessment };
        Require(fields.All(x => !string.IsNullOrWhiteSpace(x) && x.Trim().Length is >= 20 and <= 4000) && (input.Risks ?? "").Length <= 4000,
            "Complete the business need, key findings, reasons for the recommendation and your assessment (20 to 4000 characters each).");
        Require(!await db.TenderReports.AnyAsync(x => x.ReportType == ReportType.ExecutiveSummary && x.PreviousReportId == s.ReportId && x.Status == ReportStatus.Submitted),
            "The executive summary was already submitted and can no longer change.");
        var draft = await db.TenderReports.SingleOrDefaultAsync(x => x.ReportType == ReportType.ExecutiveSummary && x.PreviousReportId == s.ReportId && x.Status == ReportStatus.Draft && x.CreatedByUserId == a.Id);
        var content = JsonSerializer.Serialize(new
        {
            Input = new ExecutiveSummaryInput { BusinessNeed = input.BusinessNeed.Trim(), KeyFindings = input.KeyFindings.Trim(), RecommendationReasons = input.RecommendationReasons.Trim(),
                Risks = string.IsNullOrWhiteSpace(input.Risks) ? null : input.Risks.Trim(), ManagerAssessment = input.ManagerAssessment.Trim() },
            Facts = await Facts(t, s), EvaluationReportId = s.ReportId, s.Report!.VersionNumber, ReportHash = s.Report.PdfSha256
        });
        if (draft == null)
        {
            var version = (await db.TenderReports.Where(x => x.TenderRoundId == s.TenderRoundId && x.ReportType == ReportType.ExecutiveSummary).MaxAsync(x => (int?)x.VersionNumber) ?? 0) + 1;
            draft = new TenderReport { TenderRoundId = s.TenderRoundId, ReportType = ReportType.ExecutiveSummary, VersionNumber = version, PreviousReportId = s.ReportId,
                RecommendedBidId = s.Report.RecommendedBidId, RankingSnapshotJson = s.Report.RankingSnapshotJson, CreatedByUserId = a.Id, CreatedAt = Now, Content = content };
            db.TenderReports.Add(draft);
        }
        else draft.Content = content;
        if (submit) { draft.Status = ReportStatus.Submitted; draft.SubmittedAt = Now; }
        Audit(a, t.Id, submit ? "ExecutiveSummarySubmittedEvidence" : "ExecutiveSummaryDraftEvidence", new { EvaluationReportId = s.ReportId, s.Report.VersionNumber });
    }

    public static ExecutiveSummaryInput? ReadSummary(TenderReport? summary)
    {
        if (summary == null) return null;
        try
        {
            using var json = JsonDocument.Parse(summary.Content);
            return json.RootElement.TryGetProperty("Input", out var input) ? input.Deserialize<ExecutiveSummaryInput>() : null;
        }
        catch (JsonException) { return null; }
    }

    // The submitted executive summary for the current report (what the Top Committee reads first).
    public async Task<ExecutiveSummaryInput?> SubmittedSummary(int tenderId)
    {
        var reportId = await db.Set<EvaluationSession>().Where(x => x.TenderRound.TenderId == tenderId).OrderByDescending(x => x.TenderRound.RoundNumber).Select(x => x.ReportId).FirstOrDefaultAsync();
        if (reportId == null) return null;
        return ReadSummary(await db.TenderReports.AsNoTracking().Where(x => x.ReportType == ReportType.ExecutiveSummary && x.PreviousReportId == reportId && x.Status == ReportStatus.Submitted)
            .OrderByDescending(x => x.VersionNumber).FirstOrDefaultAsync());
    }

    private async Task<ExecutiveFacts> Facts(Tender t, EvaluationSession s)
    {
        var request = await db.PurchaseRequests.AsNoTracking().Include(x => x.Department).SingleAsync(x => x.Id == t.PurchaseRequestId);
        var bids = await Submitted(s.TenderRoundId);
        IReadOnlyList<RankedBid> ranking = s.AiAnalysisId == null || s.Submissions.Count == 4 ? await Ranking(s) : Array.Empty<RankedBid>();
        string Name(int bidId) => bids.FirstOrDefault(b => b.Id == bidId)?.TenderInvitation.Supplier.CompanyName ?? "Offer #" + bidId;
        var recommended = s.Report?.RecommendedBidId is int rb ? bids.FirstOrDefault(b => b.Id == rb) : null;
        var signatures = s.ReportId == null ? 0 : await db.Set<ReportSignature>().CountAsync(x => x.TenderReportId == s.ReportId && x.Stage == ReportSigningStage.Evaluation && x.InvalidatedAt == null);
        return new ExecutiveFacts(TenderPassportService.Reference(t.Id), t.Title, request.Department.Name, request.Id,
            recommended?.TenderInvitation.Supplier.CompanyName,
            recommended == null ? null : recommended.Items.Sum(i => i.Quantity * i.UnitPrice) + recommended.ShippingCost + recommended.InstallationCost + recommended.MaintenanceCost + recommended.OtherCost + recommended.TaxAmount,
            recommended?.CurrencyCode, s.WeightRound.ApprovedFinancial, s.WeightRound.ApprovedTechnical,
            ranking.OrderBy(x => x.Rank).Select(x => (Name(x.BidId), x.Score, x.Rank)).ToList(), s.EligibleBids.Count, DistinctSuppliers(bids), signatures,
            s.Report?.VersionNumber ?? 0, s.Report?.PdfSha256);
    }

    // Pre-fills only from recorded facts (request, ranking, member recommendations); the Manager writes the assessment.
    private async Task<ExecutiveSummaryInput> Prefill(Tender t, EvaluationSession s)
    {
        var facts = await Facts(t, s);
        var request = await db.PurchaseRequests.AsNoTracking().SingleAsync(x => x.Id == t.PurchaseRequestId);
        var bids = await Submitted(s.TenderRoundId);
        string Name(int? bidId) => bids.FirstOrDefault(b => b.Id == bidId)?.TenderInvitation.Supplier.CompanyName ?? "an offer";
        var financialOnly = s.AiAnalysisId == null;
        var ranking = string.Join("; ", facts.Ranking.Select(r => financialOnly
            ? $"{r.Rank}. {r.Supplier} ({r.Score:0.####} {t.Currency})" : $"{r.Rank}. {r.Supplier} ({r.Score:0.0})"));
        var reasons = s.Invitations.Where(x => !string.IsNullOrWhiteSpace(x.RecommendationReason)).Select(x => $"- Recommends {Name(x.RecommendedBidId)}: {x.RecommendationReason}").ToList();
        var concerns = s.Invitations.Where(x => !string.IsNullOrWhiteSpace(x.Concerns)).Select(x => "- " + x.Concerns).ToList();
        return new ExecutiveSummaryInput
        {
            BusinessNeed = request.Justification,
            KeyFindings = financialOnly
                ? $"{facts.ReceivedOffers} offer(s) received, {facts.EligibleOffers} eligible. Ranking by total price: {ranking}."
                : $"{facts.ReceivedOffers} offer(s) received, {facts.EligibleOffers} eligible and evaluated by the four-member committee with approved weights Financial {facts.FinancialWeight:0.##}% / Technical {facts.TechnicalWeight:0.##}%. Final ranking: {ranking}.",
            RecommendationReasons = reasons.Count > 0 ? string.Join("\n", reasons) : (facts.Recommended != null ? financialOnly ? $"{facts.Recommended} offered the lowest compliant total price." : $"{facts.Recommended} has the highest final weighted score." : ""),
            Risks = concerns.Count > 0 ? string.Join("\n", concerns) : null,
            ManagerAssessment = ""
        };
    }
}
