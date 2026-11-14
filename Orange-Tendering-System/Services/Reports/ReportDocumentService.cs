using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models.Entities;

namespace Orange_Tendering_System.Services.Reports;

public class ReportDocumentService(ApplicationDbContext db, OrangeReportRenderer renderer, ReportCryptography crypto)
{
    private static string Utc(DateTime date) => DateTime.SpecifyKind(date, DateTimeKind.Utc).ToString("O");
    private static string N(decimal number) => number.ToString("0.##############", CultureInfo.InvariantCulture);
    private static string Name(ApplicationUser user) => string.IsNullOrWhiteSpace(user.FullName) ? user.UserName ?? user.Id : user.FullName;
    private async Task<ReportInputs> Inputs(TenderReport report, EvaluationSession session)
    {
        var tender = await db.Tenders.Include(x => x.Items).SingleAsync(x => x.Id == session.TenderRound.TenderId);
        var weights = session.WeightRound;
        TenderWorkflowService.Require(weights.State == WeightState.Approved && weights.ApprovedFinancial + weights.ApprovedTechnical == 100m && session.Submissions.Count == 4, "Only complete evaluations with approved locked weights can generate a report.");
        var committee = await db.CommitteeMembers.Include(x => x.User).Where(x => x.TenderId == tender.Id && x.CommitteeType == CommitteeType.Evaluation).OrderBy(x => x.Responsibility).ToListAsync();
        TenderWorkflowService.Require(committee.Count == 4 && committee.All(x => x.Responsibility != null), "Four assigned evaluation members are required.");
        var bids = await db.Bids.Include(x => x.Items).ThenInclude(x => x.TenderItem).Include(x => x.Documents).Include(x => x.TenderInvitation).ThenInclude(x => x.Supplier)
            .Where(x => x.TenderInvitation.TenderRoundId == session.TenderRoundId && x.Status == BidStatus.Submitted).OrderBy(x => x.Id).ToListAsync();
        var scores = await db.EvaluationScores.Include(x => x.EvaluationCriterion).Where(x => x.EvaluationCriterion.CommitteeRoundId == session.CommitteeRoundId).ToListAsync();
        var raw = new List<ReportMemberScore>();
        foreach (var bid in session.EligibleBids.OrderBy(x => x.BidId))
        {
            TenderWorkflowService.Require(bids.Any(x => x.Id == bid.BidId && x.ComplianceStatus == ComplianceStatus.Compliant), "A ranked offer is no longer compliant. An authorized resolution procedure is required.");
            foreach (var member in committee)
            {
                var pair = scores.Where(x => x.BidId == bid.BidId && x.CommitteeMemberId == member.Id).ToArray();
                TenderWorkflowService.Require(pair.Length == 2, "Both original scores from every committee member are required.");
                var financial = pair.Single(x => x.EvaluationCriterion.Type == CriterionType.Financial);
                var technical = pair.Single(x => x.EvaluationCriterion.Type == CriterionType.Technical);
                raw.Add(new(bid.BidId, member.Id, financial.Score, technical.Score,
                    WeightedCalculation.Member(financial.Score, technical.Score, weights.ApprovedFinancial!.Value, weights.ApprovedTechnical!.Value),
                    string.Join("\n", pair.OrderBy(x => x.EvaluationCriterion.Type).Select(x => x.Comment).Distinct())));
            }
        }
        var ranking = WeightedCalculation.Rank(raw.GroupBy(x => x.BidId).Select(g => (g.Key, WeightedCalculation.Final(g.Select(x => x.Weighted)))));
        var stored = JsonSerializer.Deserialize<List<RankedBid>>(report.RankingSnapshotJson ?? "[]") ?? [];
        TenderWorkflowService.Require(ranking.OrderBy(x => x.BidId).SequenceEqual(stored.OrderBy(x => x.BidId)), "Recalculated results differ from the approved original inputs. A score-revision policy is required; no score was overwritten.");
        var ai = await db.Set<AiAnalysis>().SingleAsync(x => x.Id == session.AiAnalysisId);
        return new(report.Id, report.VersionNumber, tender.Id, tender.Title, tender.Description, tender.PurchaseRequestId,
            Utc(report.CreatedAt), weights.RequirementsJson,
            tender.Items.OrderBy(x => x.Id).Select(x => new[] { x.Name, N(x.Quantity) + " " + x.Unit, x.Specifications ?? "" }).ToArray(),
            weights.Version, weights.ApprovedFinancial!.Value, weights.ApprovedTechnical!.Value, weights.ApprovedByUserId!, Utc(weights.ApprovedAt!.Value),
            committee.Select(x => new ReportSeat(x.Id, x.UserId, Name(x.User), x.Responsibility!.ToString()!)).ToList(),
            bids.Select(x => new ReportBid(x.Id, x.TenderInvitation.Supplier.CompanyName, x.TenderInvitation.SupplierId, x.CurrencyCode,
                x.Items.Sum(i => i.Quantity * i.UnitPrice) + x.ShippingCost + x.InstallationCost + x.MaintenanceCost + x.TaxAmount + x.OtherCost,
                x.TechnicalProposal ?? "", x.DeliveryDays, x.PaymentTerms ?? "", x.ComplianceStatus.ToString(), x.ComplianceNotes ?? "", x.MissingRequirements ?? "",
                x.Items.OrderBy(i => i.TenderItemId).Select(i => new[] { i.TenderItem.Name, N(i.Quantity), N(i.UnitPrice) + " " + x.CurrencyCode, i.OfferedSpecifications ?? "" }).ToArray(),
                x.Documents.OrderBy(d => d.Id).Select(d => $"Document #{d.Id}: {d.FileName} ({d.SizeBytes} bytes)").ToArray(),
                [ ["Shipping", N(x.ShippingCost)], ["Installation", N(x.InstallationCost)], ["Maintenance", N(x.MaintenanceCost)], ["Tax", N(x.TaxAmount)], ["Other", N(x.OtherCost)] ],
                x.RevisionNumber, x.SubmittedAt.HasValue ? Utc(x.SubmittedAt.Value) : "Not recorded", x.ValidUntil.HasValue ? Utc(x.ValidUntil.Value) : "Not provided")).ToList(),
            raw, ranking, ai.Model, ai.Findings ?? "No findings", ai.EvidenceJson, Explanation(report.Content), ReportCryptography.Hash(report.Content));
    }
    public static string Explanation(string content, int depth = 0)
    {
        if (depth > 20) return "Earlier narrative is preserved in the previous report versions.";
        try
        {
            using var json = JsonDocument.Parse(content);
            if (json.RootElement.TryGetProperty("TextualCorrection", out var correction))
            {
                var original = json.RootElement.TryGetProperty("Justification", out var basis) ? basis.GetString() ?? "" :
                    json.RootElement.TryGetProperty("OriginalReport", out var previous) ? Explanation(previous.GetString() ?? "", depth + 1) : "";
                return original + "\nRevision clarification: " + correction.GetString();
            }
            if (json.RootElement.TryGetProperty("Justification", out var justification)) return justification.GetString() ?? "";
        }
        catch (JsonException) { }
        return "Review the recommendation, source observations and audit record.";
    }
    public async Task Prepare(TenderReport report, EvaluationSession session)
    {
        TenderWorkflowService.Require(report.PdfContent == null && report.SubmittedAt == null, "A frozen document is never overwritten. Create a new report version.");
        var inputs = await Inputs(report, session);
        var prior = await db.TenderReports.AsNoTracking().Where(x => x.TenderRoundId == report.TenderRoundId && x.Id != report.Id).OrderBy(x => x.VersionNumber)
            .Select(x => new { x.VersionNumber, x.Status, x.ReviewComment, x.ReviewedByUserId, x.ReviewedAt }).ToListAsync();
        var returns = await db.Set<ReportRevision>().AsNoTracking().Where(x => x.TenderReport.TenderRoundId == report.TenderRoundId).OrderBy(x => x.Id).ToListAsync();
        var history = new[] { new[] { "Weight approval v" + session.WeightRound.Version, "Approved and locked", session.WeightRound.ApprovedByUserId + " " + Utc(session.WeightRound.ApprovedAt!.Value) },
            new[] { "Report v" + report.VersionNumber, "Generated from all four completed member evaluations", report.CreatedByUserId + " " + Utc(report.CreatedAt) } }
            .Concat(prior.Select(x => new[] { "Version " + x.VersionNumber, x.Status + ": " + x.ReviewComment, (x.ReviewedByUserId ?? "") + " " + (x.ReviewedAt.HasValue ? Utc(x.ReviewedAt.Value) : "") }))
            .Concat(returns.Select(x => new[] { x.SourceStage + " -> " + x.TargetStage, x.Feedback + " Required action: " + x.RequiredAction, x.ReturningUserId + " " + Utc(x.RequestedAt) })).ToArray();
        report.SourceSha256 = Fingerprint(inputs);
        report.SnapshotJson = JsonSerializer.Serialize(new FrozenReport(inputs, history));
        report.SnapshotSha256 = ReportCryptography.Hash(report.SnapshotJson);
        var pdf = renderer.Render(new(inputs, history));
        report.PdfContent = pdf.Bytes; report.PdfSha256 = ReportCryptography.Hash(pdf.Bytes);
        report.SignaturePageIndex = pdf.SignaturePageIndex; report.TemplateVersion = OrangeReportRenderer.TemplateVersion;
    }
    public async Task AssertIntegrity(TenderReport report, EvaluationSession session)
    {
        TenderWorkflowService.Require(report.PdfContent != null && report.SnapshotJson != null && report.PdfSha256 == ReportCryptography.Hash(report.PdfContent) &&
            report.SnapshotSha256 == ReportCryptography.Hash(report.SnapshotJson), "Document tampering or missing generated PDF detected. Signing, forwarding and download are blocked.");
        var current = await Inputs(report, session);
        TenderWorkflowService.Require(report.SourceSha256 == Fingerprint(current), "Verified source records changed after report generation. A controlled new version is required.");
        var signatures = await db.Set<ReportSignature>().Where(x => x.TenderReportId == report.Id).ToListAsync();
        TenderWorkflowService.Require(signatures.All(x => ReportCryptography.Verify(x, report)), "A signature receipt failed document-hash or cryptographic verification.");
    }
    private static string Fingerprint(ReportInputs inputs)
    {
        // Administrative display-name changes do not rewrite the frozen document or invalidate
        // procurement inputs. Their original names remain protected by the snapshot/PDF hashes.
        var invariant = inputs with { Committee = inputs.Committee.Select(x => x with { Name = "" }).ToList(), Bids = inputs.Bids.Select(x => x with { Supplier = "" }).ToList() };
        return ReportCryptography.Hash(ReportCanonicalJson.Serialize(invariant));
    }
    public async Task<bool> Sign(TenderReport report, EvaluationSession session, ApplicationUser actor, ReportSigningStage stage,
        string role, string slot, string decision, string? expectedHash, bool confirmed, DateTime now, byte[]? signatureImage = null)
    {
        TenderWorkflowService.Require(confirmed && !string.IsNullOrWhiteSpace(expectedHash) && expectedHash == report.PdfSha256 && report.SupersededAt == null && report.Status is not (ReportStatus.Returned or ReportStatus.Rejected), "Open and review the current generated PDF, confirm signing, and submit its exact hash. Obsolete versions cannot be signed.");
        await AssertIntegrity(report, session);
        var existing = await db.Set<ReportSignature>().SingleOrDefaultAsync(x => x.TenderReportId == report.Id && x.Stage == stage && x.UserId == actor.Id);
        if (existing != null)
        {
            TenderWorkflowService.Require(existing.Decision == decision && existing.DocumentHash == expectedHash && existing.InvalidatedAt == null, "A different or obsolete signing action already exists.");
            return false; // Same immutable endorsement is idempotent.
        }
        TenderWorkflowService.Require(await db.AuditLogs.AnyAsync(x => x.EntityName == "TenderReport" && x.EntityId == report.Id.ToString() && x.UserId == actor.Id && x.Action == "ProtectedReportViewed" && x.NewValues != null && x.NewValues.Contains(expectedHash)), "Review the generated PDF before signing your assigned section.");
        var signature = new ReportSignature { TenderReportId = report.Id, UserId = actor.Id, SignerName = Name(actor), Role = role, Stage = stage, Slot = slot,
            Decision = decision, SignedAt = now, DocumentHash = report.PdfSha256!, SnapshotHash = report.SnapshotSha256!, SignatureImage = signatureImage };
        crypto.Sign(signature, report.VersionNumber); db.Add(signature);
        db.AuditLogs.Add(new AuditLog { UserId = actor.Id, EntityName = "TenderReport", EntityId = report.Id.ToString(), Action = "ReportVersionSigned", CreatedAt = now,
            NewValues = JsonSerializer.Serialize(new { report.Id, report.VersionNumber, signature.DocumentHash, signature.SnapshotHash, stage, role, slot, decision, signature.Method, signature.CertificateThumbprint }) });
        return true;
    }
    public async Task RequireCommitteeSignatures(TenderReport report, EvaluationSession session)
    {
        await AssertIntegrity(report, session);
        var allReceipts = await db.Set<ReportSignature>().Where(x => x.TenderReportId == report.Id && x.InvalidatedAt == null).ToListAsync();
        TenderWorkflowService.Require(allReceipts.Where(x => x.Method == SignatureMethod.CertificateCms).All(ReportCryptography.CertificateTrustValid), "Current certificate trust/revocation validation failed. Historical receipts remain viewable, but further approval is blocked.");
        var signatures = await db.Set<ReportSignature>().Where(x => x.TenderReportId == report.Id && x.Stage == ReportSigningStage.Evaluation && x.InvalidatedAt == null && x.Decision == "Approve").ToListAsync();
        var members = await db.CommitteeMembers.Where(x => x.TenderId == session.TenderRound.TenderId && x.CommitteeType == CommitteeType.Evaluation).Select(x => x.UserId).ToListAsync();
        TenderWorkflowService.Require(signatures.Count == 4 && members.Count == 4 && signatures.All(x => members.Contains(x.UserId)), "All four current-version committee signatures are required before forwarding or subsequent approval.");
    }
    public async Task Invalidate(TenderReport old, int nextVersion, DateTime now)
    {
        old.SupersededAt = now;
        foreach (var signature in await db.Set<ReportSignature>().Where(x => x.TenderReportId == old.Id && x.InvalidatedAt == null).ToListAsync())
        { signature.InvalidatedAt = now; signature.InvalidationReason = "Superseded by report version " + nextVersion + "; historical cryptographic receipt retained."; }
    }
    public async Task<byte[]> AccessCopy(TenderReport report, EvaluationSession session, ApplicationUser actor, DateTime now)
    {
        await AssertIntegrity(report, session);
        var signatures = await db.Set<ReportSignature>().Where(x => x.TenderReportId == report.Id).OrderBy(x => x.Id).ToListAsync();
        var identity = Name(actor); if (identity.Length > 30) identity = identity[..30] + "...";
        var login = actor.UserName ?? ""; if (login.Length > 40) login = login[..40] + "...";
        bool? topRequired = null;
        if (session.FinalApprovalPolicyJson != null)
        {
            var policy = JsonSerializer.Deserialize<TenderApprovalPolicy>(session.FinalApprovalPolicyJson)!;
            var tender = await db.Tenders.SingleAsync(x => x.Id == session.TenderRound.TenderId);
            topRequired = policy.TopCommitteeRequirement == "None" ? false : policy.TopCommitteeRequirement == "All" ? true :
                tender.Currency == policy.ThresholdCurrency ? tender.IsCritical || tender.EstimatedValue >= policy.HighValueThreshold : null;
        }
        return renderer.AccessCopy(report, signatures, identity + " | " + login + " | User " + ReportCryptography.Hash(actor.Id)[..16], now, topRequired);
    }
}
