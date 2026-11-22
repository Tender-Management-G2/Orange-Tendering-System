using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Areas.TopCommittee.Services;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.Services.Reports;
using Orange_Tendering_System.ViewModels;

namespace Orange_Tendering_System.Services;

public sealed partial class ReportingService
{
    private IQueryable<Bid> SubmittedOffers(IQueryable<Tender> tenders, int? supplierId) =>
        db.Bids.AsNoTracking().Where(b => b.Status == BidStatus.Submitted &&
            (supplierId == null || b.TenderInvitation.SupplierId == supplierId) &&
            tenders.Any(t => t.Id == b.TenderInvitation.TenderRound.TenderId));

    private IQueryable<Tender> ConfidentialTenders(AnalyticsFilter filter, AnalyticsReadScope scope)
    {
        var tenders = TenderQuery(filter);
        if (scope.AllTenders) return tenders;
        return tenders.Where(t => t.CommitteeMembers.Any(m => m.UserId == scope.UserId &&
            (scope.Manager && m.Responsibility == CommitteeResponsibility.SourcingManager ||
            scope.Executive && (m.CommitteeType == CommitteeType.Top || m.CommitteeType == TopCommitteeCompatibility.LegacyTop) &&
                t.Rounds.Any(r => r.CommitteeRounds.Any(c => c.CommitteeType == CommitteeType.Top || c.CommitteeType == TopCommitteeCompatibility.LegacyTop)))));
    }

    private async Task<ReportModel> Evaluations(AnalyticsFilter filter, AnalyticsReadScope scope, CancellationToken ct)
    {
        var m = Start("evaluations", filter);
        var tenders = ConfidentialTenders(filter, scope);
        var offers = SubmittedOffers(tenders, filter.SupplierId);
        if (!scope.AllTenders)
            offers = offers.Where(b => db.Set<EvaluationSession>().Any(s => s.TenderRoundId == b.TenderInvitation.TenderRoundId));
        var bids = await offers.OrderBy(b => b.TenderInvitation.TenderRound.TenderId).ThenBy(b => b.Id)
            .Select(b => new
            {
                b.Id, TenderId = b.TenderInvitation.TenderRound.TenderId, b.TenderInvitation.TenderRound.RoundNumber,
                Supplier = b.TenderInvitation.Supplier.CompanyName, b.CurrencyCode, b.DeliveryDays, b.ComplianceStatus,
                b.ComplianceNotes, b.MissingRequirements, b.TechnicalProposal,
                Price = b.Items.Sum(i => (decimal?)(i.Quantity * i.UnitPrice)) ?? 0,
                Extras = b.ShippingCost + b.InstallationCost + b.MaintenanceCost + b.TaxAmount + b.OtherCost
            }).ToListAsync(ct);
        var scoreQuery = db.EvaluationScores.AsNoTracking().Where(s => offers.Any(b => b.Id == s.BidId));
        if (!scope.IsAdmin)
            scoreQuery = scoreQuery.Where(s => s.EvaluationCriterion.CommitteeRound.Status != CommitteeRoundStatus.InProgress ||
                db.Set<EvaluationSession>().Any(e => e.CommitteeRoundId == s.EvaluationCriterion.CommitteeRoundId &&
                    e.State != SessionState.Scheduled && e.State != SessionState.Incomplete));
        var scores = await scoreQuery
            .OrderBy(s => s.EvaluationCriterion.CommitteeRound.RoundNumber).ThenBy(s => s.EvaluationCriterionId)
            .Select(s => new { s.BidId, s.EvaluationCriterion.CommitteeRound.RoundNumber, s.EvaluationCriterion.CommitteeRoundId,
                s.EvaluationCriterionId, s.EvaluationCriterion.Name, s.EvaluationCriterion.Type, s.EvaluationCriterion.MaxScore, s.Score }).ToListAsync(ct);
        var reports = await db.TenderReports.AsNoTracking().Where(r =>
                (r.ReportType == ReportType.FullEvaluation || r.ReportType == TopCommitteeCompatibility.LegacyUnifiedEvaluation) && r.SubmittedAt != null &&
                tenders.Any(t => t.Id == r.TenderRound.TenderId) && (filter.SupplierId == null ||
                r.TenderRound.Invitations.Any(i => i.SupplierId == filter.SupplierId && i.Bids.Any(b => b.Status == BidStatus.Submitted))))
            .OrderByDescending(r => r.SubmittedAt).ThenByDescending(r => r.Id).Select(r => new
            {
                r.Id, r.TenderRound.TenderId, r.TenderRound.RoundNumber, r.VersionNumber, r.Status, r.SubmittedAt, r.Content,
                r.RankingSnapshotJson, r.RecommendedBidId, RecommendedSupplier = r.RecommendedBid == null ? null : r.RecommendedBid.TenderInvitation.Supplier.CompanyName,
                HasPdf = r.ReportType == ReportType.FullEvaluation && r.PdfContent != null && r.PdfSha256 != null
            }).ToListAsync(ct);
        var bidById = bids.ToDictionary(b => b.Id);
        var excluded = bids.Where(b => b.ComplianceStatus == ComplianceStatus.NonCompliant).ToList();
        m.Kpis.AddRange([new("Submitted offers", K(bids.Count)), new("Compliant offers", K(bids.Count(b => b.ComplianceStatus == ComplianceStatus.Compliant))),
            new("Excluded offers", K(excluded.Count)), new("Pending compliance review", K(bids.Count(b => b.ComplianceStatus is ComplianceStatus.NotChecked or ComplianceStatus.PendingClarification))),
            new("Submitted evaluation reports", K(reports.Count))]);
        m.Tables.Add(new("price-comparison", "Offer price comparison", ["Tender", "Round", "Offer", "Supplier", "Base price", "Additional costs", "Total price", "Currency", "Delivery days", "Compliance"],
            bids.OrderBy(b => b.TenderId).ThenBy(b => b.RoundNumber).ThenBy(b => b.CurrencyCode).ThenBy(b => b.Price + b.Extras).Select(b =>
            new[] { T(b.TenderId), K(b.RoundNumber), K(b.Id), b.Supplier, N(b.Price), N(b.Extras), N(b.Price + b.Extras), b.CurrencyCode,
                b.DeliveryDays?.ToString() ?? "—", Words(b.ComplianceStatus.ToString()) }).ToList()));
        var items = await db.BidItems.AsNoTracking().Where(i => offers.Any(b => b.Id == i.BidId)).OrderBy(i => i.BidId).ThenBy(i => i.TenderItemId)
            .Select(i => new { i.BidId, i.TenderItem.Name, i.OfferedSpecifications, i.Quantity, i.UnitPrice }).ToListAsync(ct);
        m.Tables.Add(new("priced-items", "Priced items and specifications", ["Tender", "Offer", "Item", "Specifications", "Quantity", "Unit price"], items.Where(i => bidById.ContainsKey(i.BidId)).Select(i =>
            new[] { T(bidById[i.BidId].TenderId), K(i.BidId), i.Name, i.OfferedSpecifications ?? "—",
                i.Quantity.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture), N(i.UnitPrice) + " " + bidById[i.BidId].CurrencyCode }).ToList()));
        m.Tables.Add(new("technical-proposals", "Offer technical proposals", ["Tender", "Offer", "Supplier", "Proposal"], bids.Where(b => !string.IsNullOrWhiteSpace(b.TechnicalProposal)).Select(b =>
            new[] { T(b.TenderId), K(b.Id), b.Supplier, b.TechnicalProposal! }).ToList()));
        foreach (var kind in Enum.GetValues<CriterionType>())
        {
            var byCriterion = scores.Where(s => s.Type == kind && bidById.ContainsKey(s.BidId)).GroupBy(s => new { s.BidId, s.CommitteeRoundId, s.RoundNumber, s.EvaluationCriterionId, s.Name, s.MaxScore });
            m.Tables.Add(new(kind == CriterionType.Technical ? "technical-scores" : "financial-scores", kind + " evaluation scores", ["Tender", "Offer", "Supplier", "Evaluation round", "Criterion", "Mean score", "Maximum", "Recorded scores"], byCriterion.Select(g =>
            {
                var b = bidById[g.Key.BidId];
                return new[] { T(b.TenderId), K(b.Id), b.Supplier, K(g.Key.RoundNumber), g.Key.Name, N(g.Average(s => s.Score)),
                    g.Key.MaxScore is { } max ? N(max) : "—", K(g.Count()) };
            }).ToList()));
        }
        var unified = new ReportTable("unified-reports", "Unified evaluation reports", ["Tender", "Round", "Version", "Status", "Submitted", "Recommended supplier", "Recommendation", "Document"], []);
        var rankings = new ReportTable("ranking", "Frozen evaluation ranking", ["Tender", "Round", "Version", "Offer", "Supplier", "Weighted score", "Rank"], []);
        foreach (var report in reports)
        {
            if (scope.IsAdmin && report.HasPdf) unified.Links.Add(new(unified.Rows.Count, 7, "EvaluationPdf", report.Id));
            unified.Rows.Add([T(report.TenderId), K(report.RoundNumber), "v" + K(report.VersionNumber), report.Status.ToString(), D(report.SubmittedAt),
                report.RecommendedSupplier ?? "—", ReportDocumentService.Explanation(report.Content), report.HasPdf ? $"Report #{report.Id}" : "—"]);
            foreach (var ranked in ReadRanks(report.RankingSnapshotJson).Where(r => bidById.ContainsKey(r.BidId)).OrderBy(r => r.Rank))
                rankings.Rows.Add([T(report.TenderId), K(report.RoundNumber), "v" + K(report.VersionNumber), K(ranked.BidId),
                    bidById[ranked.BidId].Supplier, N(ranked.Score), K(ranked.Rank) + (ranked.Tied ? " (tie)" : "")]);
        }
        m.Tables.Add(unified);
        m.Tables.Add(rankings);
        m.Tables.Add(new("excluded", "Excluded offers", ["Tender", "Round", "Offer", "Supplier", "Reason", "Missing requirements"], excluded.Select(b =>
            new[] { T(b.TenderId), K(b.RoundNumber), K(b.Id), b.Supplier, b.ComplianceNotes ?? "—", b.MissingRequirements ?? "—" }).ToList()));
        return m;
    }

    private static IReadOnlyList<RankedBid> ReadRanks(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return JsonSerializer.Deserialize<List<RankedBid>>(json) ?? []; }
        catch (JsonException) { return []; }
    }

    private async Task<ReportModel> Governance(AnalyticsFilter filter, AnalyticsReadScope scope, CancellationToken ct)
    {
        var m = Start("governance", filter);
        var tenders = ConfidentialTenders(filter, scope);
        var reports = await db.TenderReports.AsNoTracking().Where(r =>
                (r.ReportType == ReportType.FullEvaluation || r.ReportType == TopCommitteeCompatibility.LegacyUnifiedEvaluation) && r.SubmittedAt != null &&
                tenders.Any(t => t.Id == r.TenderRound.TenderId))
            .OrderByDescending(r => r.SubmittedAt).ThenByDescending(r => r.Id)
            .Select(r => new { r.Id, r.TenderRound.TenderId, r.TenderRound.RoundNumber, r.VersionNumber, r.Status, r.SubmittedAt, r.ReviewedAt, r.ReviewComment,
                Reviewer = r.ReviewedByUser == null ? null : r.ReviewedByUser.FullName,
                Supplier = r.RecommendedBid == null ? null : r.RecommendedBid.TenderInvitation.Supplier.CompanyName }).ToListAsync(ct);
        var managerReturns = await db.Set<ReportRevision>().AsNoTracking().Where(r => r.SourceStage == ReportReviewStage.ManagerReview &&
                tenders.Any(t => t.Id == r.TenderReport.TenderRound.TenderId))
            .OrderByDescending(r => r.RequestedAt).ThenByDescending(r => r.Id)
            .Select(r => new { r.TenderReport.TenderRound.TenderId, r.TenderReport.VersionNumber, r.RequestedAt, r.ReturningUser.FullName,
                r.Feedback, r.Status, ReportReviewedAt = r.TenderReport.ReviewedAt, ReportStatus = r.TenderReport.Status }).ToListAsync(ct);
        var topRounds = db.CommitteeRounds.AsNoTracking().Where(r =>
            (r.CommitteeType == CommitteeType.Top || r.CommitteeType == TopCommitteeCompatibility.LegacyTop) && tenders.Any(t => t.Id == r.TenderRound.TenderId));
        var rounds = await topRounds.OrderByDescending(r => r.StartedAt).ThenByDescending(r => r.Id)
            .Select(r => new { r.TenderRound.TenderId, BiddingRound = r.TenderRound.RoundNumber, r.RoundNumber, r.Status, r.CompletedAt,
                Approvals = r.Votes.Count(v => v.Decision == VoteDecision.Approve), Rejections = r.Votes.Count(v => v.Decision == VoteDecision.Reject),
                Retenders = r.Votes.Count(v => v.Decision == VoteDecision.Retender) }).ToListAsync(ct);
        var votes = await db.CommitteeVotes.AsNoTracking().Where(v => topRounds.Any(r => r.Id == v.CommitteeRoundId))
            .OrderByDescending(v => v.VotedAt).ThenByDescending(v => v.Id).Select(v => new
            {
                v.CommitteeRound.TenderRound.TenderId, BiddingRound = v.CommitteeRound.TenderRound.RoundNumber, v.CommitteeRound.RoundNumber, v.CommitteeMember.User.FullName,
                v.Decision, v.RejectionReason, v.Comment, v.VotedAt
            }).ToListAsync(ct);
        var decisions = await db.FinalDecisionNotices.AsNoTracking().Where(n => tenders.Any(t => t.Id == n.TenderId))
            .OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id).Select(n => new
            {
                n.TenderId, n.Outcome, n.Status, n.TopApprovals, n.TopVotes, n.CreatedAt, n.ManagerAcknowledgedAt, n.DecisionReason, n.ManagerComment
            }).ToListAsync(ct);
        var awardQuery = Awards(tenders, filter.SupplierId);
        var awards = await awardQuery.OrderByDescending(t => t.AwardedAt).ThenByDescending(t => t.Id).Select(t => new
        {
            t.Id, t.Title, t.AwardedAt, Supplier = t.AwardedBid!.TenderInvitation.Supplier.CompanyName, t.AwardedBidId, t.AwardedBid!.CurrencyCode,
            Price = (t.AwardedBid!.Items.Sum(i => (decimal?)(i.Quantity * i.UnitPrice)) ?? 0) + t.AwardedBid.ShippingCost +
                t.AwardedBid.InstallationCost + t.AwardedBid.MaintenanceCost + t.AwardedBid.TaxAmount + t.AwardedBid.OtherCost
        }).ToListAsync(ct);
        var returns = managerReturns.Where(r => r.ReportStatus != ReportStatus.Returned || r.ReportReviewedAt != r.RequestedAt).ToList();
        m.Kpis.AddRange([new("Manager approvals", K(reports.Count(r => r.Status == ReportStatus.Accepted && r.ReviewedAt != null))),
            new("Manager returns / rejections", K(reports.Count(r => r.ReviewedAt != null && r.Status is ReportStatus.Returned or ReportStatus.Rejected) + returns.Count)),
            new("Top Committee approvals", K(votes.Count(v => v.Decision == VoteDecision.Approve))), new("Awarded tenders", K(awards.Count))]);
        m.Tables.Add(new("recommendations", "Evaluation Committee recommendations", ["Tender", "Round", "Report", "Recommended supplier", "Submitted", "Status"], reports.Select(r =>
            new[] { T(r.TenderId), K(r.RoundNumber), $"#{r.Id} · v{r.VersionNumber}", r.Supplier ?? "—", D(r.SubmittedAt), r.Status.ToString() }).ToList()));
        m.Tables.Add(new("manager-decisions", "Sourcing Manager decisions", ["Tender", "Report version", "Decision", "Date (Jordan)", "Reviewer", "Comment"], reports.Where(r => r.ReviewedAt != null).Select(r =>
            new[] { T(r.TenderId), "v" + K(r.VersionNumber), r.Status.ToString(), Time(r.ReviewedAt), r.Reviewer ?? "—", r.ReviewComment ?? "—" }).Concat(returns.Select(r =>
            new[] { T(r.TenderId), "v" + K(r.VersionNumber), "Return · " + Words(r.Status.ToString()), Time(r.RequestedAt), r.FullName, r.Feedback })).ToList()));
        m.Tables.Add(new("vote-outcomes", "Top Committee voting outcomes", ["Tender", "Bidding round", "Voting round", "Approve", "Reject", "Retender", "Outcome", "Completed"], rounds.Select(r =>
            new[] { T(r.TenderId), K(r.BiddingRound), K(r.RoundNumber), K(r.Approvals), K(r.Rejections), K(r.Retenders), Words(r.Status.ToString()), D(r.CompletedAt) }).ToList()));
        m.Tables.Add(new("votes", "Top Committee individual votes", ["Tender", "Bidding round", "Voting round", "Member", "Decision", "Reason", "Comment", "Voted (Jordan)"], votes.Select(v =>
            new[] { T(v.TenderId), K(v.BiddingRound), K(v.RoundNumber), v.FullName, v.Decision.ToString(), v.RejectionReason?.ToString() ?? "—", v.Comment ?? "—", Time(v.VotedAt) }).ToList()));
        m.Tables.Add(new("final-decisions", "Final decision notices", ["Tender", "Outcome", "Status", "Top approvals / votes", "Recorded", "Manager acknowledgment", "Reason / comment"], decisions.Select(n =>
            new[] { T(n.TenderId), n.Outcome.ToString(), Words(n.Status.ToString()), $"{n.TopApprovals}/{n.TopVotes}", D(n.CreatedAt),
                D(n.ManagerAcknowledgedAt), string.Join(" / ", new[] { n.DecisionReason, n.ManagerComment }.Where(x => !string.IsNullOrWhiteSpace(x))) }).ToList()));
        m.Tables.Add(new("awards", "Awards and winning bids", ["Tender", "Title", "Supplier", "Offer", "Winning price", "Award date"], awards.Select(a =>
            new[] { T(a.Id), a.Title, a.Supplier, K(a.AwardedBidId!.Value), N(a.Price) + " " + a.CurrencyCode, D(a.AwardedAt) }).ToList()));
        if (scope.IsAdmin)
        {
            var contracts = await db.FinalTenderContracts.AsNoTracking().Where(c => awardQuery.Any(t => t.Id == c.TenderId))
                .OrderByDescending(c => c.UploadedAt).ThenByDescending(c => c.Id).Select(c => new { c.Id, c.TenderId, c.FileName, c.UploadedAt }).ToListAsync(ct);
            var table = new ReportTable("contracts", "Final contract documents", ["Tender", "Final contract PDF", "Added (Jordan)"], []);
            foreach (var contract in contracts)
            {
                table.Links.Add(new(table.Rows.Count, 1, "ContractPdf", contract.Id));
                table.Rows.Add([T(contract.TenderId), contract.FileName, Time(contract.UploadedAt)]);
            }
            m.Kpis.Add(new("Final contract files", K(contracts.Count)));
            m.Tables.Add(table);
            await AddAudit(m, ct);
        }
        return m;
    }

    private IQueryable<Tender> Awards(IQueryable<Tender> tenders, int? supplierId) =>
        tenders.Where(t => t.Status == TenderStatus.Awarded && t.AwardedBidId != null &&
            (supplierId == null || t.AwardedBid!.TenderInvitation.SupplierId == supplierId));

    private async Task AddAudit(ReportModel m, CancellationToken ct)
    {
        var filter = m.Filter;
        var audit = db.AuditLogs.AsNoTracking();
        if (filter.StartUtc is { } start) audit = audit.Where(a => a.CreatedAt >= start);
        if (filter.EndUtc is { } end) audit = audit.Where(a => a.CreatedAt < end);
        if (filter.HasTenderScope)
        {
            var tenders = TenderQuery(filter, search: false, dates: false);
            var requests = RequestQuery(new AnalyticsFilter { DepartmentId = filter.DepartmentId, SupplierId = filter.SupplierId,
                TenderStatus = filter.TenderStatus, TenderScope = filter.TenderScope });
            var suppliers = SupplierQuery(new AnalyticsFilter { DepartmentId = filter.DepartmentId, SupplierId = filter.SupplierId,
                TenderStatus = filter.TenderStatus, TenderScope = filter.TenderScope });
            audit = audit.Where(a =>
                a.EntityName == nameof(Tender) && tenders.Any(t => t.Id.ToString() == a.EntityId) ||
                a.EntityName == nameof(PurchaseRequest) && requests.Any(r => r.Id.ToString() == a.EntityId) ||
                a.EntityName == nameof(Supplier) && suppliers.Any(s => s.Id.ToString() == a.EntityId) ||
                a.EntityName == nameof(Bid) && db.Bids.Any(b => b.Id.ToString() == a.EntityId &&
                    (filter.SupplierId == null || b.TenderInvitation.SupplierId == filter.SupplierId) &&
                    tenders.Any(t => t.Id == b.TenderInvitation.TenderRound.TenderId)) ||
                a.EntityName == nameof(TenderInvitation) && db.TenderInvitations.Any(i => i.Id.ToString() == a.EntityId &&
                    (filter.SupplierId == null || i.SupplierId == filter.SupplierId) && tenders.Any(t => t.Id == i.TenderRound.TenderId)) ||
                a.EntityName == nameof(TenderReport) && db.TenderReports.Any(r => r.Id.ToString() == a.EntityId && tenders.Any(t => t.Id == r.TenderRound.TenderId)) ||
                a.EntityName == nameof(FinalTenderContract) && db.FinalTenderContracts.Any(c => c.Id.ToString() == a.EntityId && tenders.Any(t => t.Id == c.TenderId)) ||
                a.EntityName == nameof(Document) && db.Documents.Any(d => d.Id.ToString() == a.EntityId &&
                    (d.SupplierId != null && suppliers.Any(s => s.Id == d.SupplierId) || d.TenderId != null && tenders.Any(t => t.Id == d.TenderId) ||
                    d.PurchaseRequestId != null && requests.Any(r => r.Id == d.PurchaseRequestId))) ||
                a.EntityName == nameof(Clarification) && db.Clarifications.Any(c => c.Id.ToString() == a.EntityId &&
                    (tenders.Any(t => t.Id == c.TenderId || c.Bid != null && t.Id == c.Bid.TenderInvitation.TenderRound.TenderId) ||
                    requests.Any(r => r.Id == c.PurchaseRequestId))));
        }
        if (filter.Search?.Trim() is { Length: > 0 } term)
        {
            var id = SearchId(term).ToString();
            var tenders = TenderQuery(filter, dates: false);
            audit = audit.Where(a => a.Action.Contains(term) || a.EntityName.Contains(term) || a.EntityId.Contains(term) ||
                a.User != null && a.User.FullName.Contains(term) ||
                a.EntityName == nameof(Tender) && (a.EntityId == id || tenders.Any(t => t.Id.ToString() == a.EntityId)) ||
                a.EntityName == nameof(PurchaseRequest) && term.StartsWith("PR-") && a.EntityId == id);
        }
        var logs = await audit.OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id)
            .Select(a => new { a.Id, a.CreatedAt, Actor = a.User == null ? a.UserId : a.User.FullName, a.Action, a.EntityName, a.EntityId }).ToListAsync(ct);
        m.Kpis.Add(new("Audit entries", K(logs.Count)));
        m.Tables.Add(new("audit-summary", "Audit activity by action", ["Action", "Entries"], logs.GroupBy(a => a.Action).OrderByDescending(g => g.Count())
            .Select(g => new[] { Words(g.Key), K(g.Count()) }).ToList()));
        var trail = new ReportTable("audit-trail", "Audit trail", ["ID", "Time (Jordan)", "User", "Action", "Entity", "Record"], []);
        foreach (var log in logs)
        {
            trail.Links.Add(new(trail.Rows.Count, 0, "AuditEntry", log.Id));
            trail.Rows.Add([log.Id.ToString(), Time(log.CreatedAt), log.Actor ?? "System", Words(log.Action), Words(log.EntityName), log.EntityId]);
        }
        m.Tables.Add(trail);
    }

    private async Task<ReportModel> Management(AnalyticsFilter filter, CancellationToken ct)
    {
        var m = Start("management", filter);
        var suppliers = await SupplierQuery(filter).Select(s => new { s.Id, s.CompanyName, s.Status }).ToListAsync(ct);
        var tenders = TenderQuery(filter);
        var statuses = await tenders.GroupBy(t => t.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync(ct);
        var awards = Awards(TenderQuery(filter, dates: false), filter.SupplierId);
        if (filter.StartUtc is { } start) awards = awards.Where(t => t.AwardedAt >= start);
        if (filter.EndUtc is { } end) awards = awards.Where(t => t.AwardedAt < end);
        if (filter.SupplierStatus is { } status) awards = awards.Where(t => t.AwardedBid!.TenderInvitation.Supplier.Status == status);
        var transactions = await awards.Select(t => new
        {
            SupplierId = t.AwardedBid!.TenderInvitation.SupplierId, Supplier = t.AwardedBid.TenderInvitation.Supplier.CompanyName,
            t.AwardedBid.TenderInvitation.Supplier.Status, t.AwardedBid.CurrencyCode,
            Price = (t.AwardedBid.Items.Sum(i => (decimal?)(i.Quantity * i.UnitPrice)) ?? 0) + t.AwardedBid.ShippingCost +
                t.AwardedBid.InstallationCost + t.AwardedBid.MaintenanceCost + t.AwardedBid.TaxAmount + t.AwardedBid.OtherCost
        }).ToListAsync(ct);
        var bySupplier = transactions.ToLookup(t => t.SupplierId);
        var matches = suppliers.Select(s => (s.Id, s.CompanyName, s.Status)).Concat(transactions.Select(t => (Id: t.SupplierId, CompanyName: t.Supplier, t.Status)))
            .DistinctBy(s => s.Id).OrderBy(s => s.CompanyName).ThenBy(s => s.Id);
        m.Kpis.AddRange([new("Registered suppliers", K(suppliers.Count)), new("Approved suppliers", K(suppliers.Count(s => s.Status == SupplierStatus.Approved))),
            new("Tenders created", K(statuses.Sum(g => g.Count))), new("Awards in period", K(transactions.Count)),
            new("Suppliers with multiple awards", K(bySupplier.Count(g => g.Count() > 1))),
            new("Repeat award rate", Pct(transactions.Count - bySupplier.Count, transactions.Count))]);
        m.Tables.Add(new("supplier-awards", "Awarded transactions by supplier", ["Supplier", "Status", "Awards", "Repeat awards", "Share of awards", "Awarded value by currency"], matches.Select(s =>
        {
            var entries = bySupplier[s.Id].ToList();
            var totals = string.Join(" · ", entries.GroupBy(t => t.CurrencyCode).OrderBy(g => g.Key).Select(g => N(g.Sum(t => t.Price)) + " " + g.Key));
            return new[] { s.CompanyName, s.Status.ToString(), K(entries.Count), K(Math.Max(0, entries.Count - 1)), Pct(entries.Count, transactions.Count), totals };
        }).ToList()));
        m.Tables.Add(new("status", "Tenders created by status", ["Status", "Tenders"], Enum.GetValues<TenderStatus>().Select(s =>
            new[] { Words(s.ToString()), K(statuses.Where(g => g.Status == s).Sum(g => g.Count)) }).ToList()));
        m.Tables.Add(new("awarded-values", "Awarded value by currency", ["Currency", "Awards", "Winning bid value"], transactions.GroupBy(t => t.CurrencyCode).OrderBy(g => g.Key).Select(g =>
            new[] { g.Key, K(g.Count()), N(g.Sum(t => t.Price)) }).ToList()));
        return m;
    }

    public async Task<AnalyticsDashboard> DashboardAsync(AnalyticsFilter filter, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var requests = RequestQuery(filter);
        var tenders = TenderQuery(filter);
        var requestCounts = await requests.GroupBy(r => r.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync(ct);
        var tenderCounts = await tenders.GroupBy(t => t.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync(ct);
        var supplierCount = await SupplierQuery(filter).CountAsync(ct);
        var departments = await tenders.GroupBy(t => new { t.PurchaseRequest.DepartmentId, t.PurchaseRequest.Department.Name })
            .Select(g => new { g.Key.DepartmentId, g.Key.Name, Count = g.Count() })
            .OrderByDescending(g => g.Count).ThenBy(g => g.Name).Take(6).ToListAsync(ct);
        var awards = Awards(TenderQuery(filter, dates: false), filter.SupplierId);
        if (filter.StartUtc is { } start) awards = awards.Where(t => t.AwardedAt >= start);
        if (filter.EndUtc is { } end) awards = awards.Where(t => t.AwardedAt < end);
        if (filter.SupplierStatus is { } status) awards = awards.Where(t => t.AwardedBid!.TenderInvitation.Supplier.Status == status);
        var supplierAwards = await awards.GroupBy(t => new
            { t.AwardedBid!.TenderInvitation.SupplierId, t.AwardedBid.TenderInvitation.Supplier.CompanyName })
            .Select(g => new { g.Key.SupplierId, g.Key.CompanyName, Count = g.Count() })
            .OrderByDescending(g => g.Count).ThenBy(g => g.CompanyName).ThenBy(g => g.SupplierId).ToListAsync(ct);
        var acceptedReviews = await db.PurchaseRequestReviews.AsNoTracking()
            .Where(r => r.Decision == ReviewDecision.Accepted && r.ReviewedAt >= r.SubmittedAt &&
                requests.Any(q => q.Id == r.PurchaseRequestId))
            .Select(r => new { r.PurchaseRequestId, r.SubmissionNumber, r.ReviewedAt, r.SubmittedAt }).ToListAsync(ct);
        var approvalTimes = acceptedReviews.GroupBy(r => r.PurchaseRequestId).Select(g => g.MaxBy(r => r.SubmissionNumber)!)
            .Select(r => new { Reviewed = r.ReviewedAt!.Value.ToJordan(), Days = (r.ReviewedAt.Value - r.SubmittedAt).TotalDays }).ToArray();

        var today = DateOnly.FromDateTime(now.ToJordan());
        var trendEnd = filter.To is { } to && to < today ? to : today;
        var endMonth = new DateOnly(trendEnd.Year, trendEnd.Month, 1);
        var monthsBack = Math.Min(5, (endMonth.Year - 1) * 12 + endMonth.Month - 1);
        var approvalMonths = Enumerable.Range(-monthsBack, monthsBack + 1).Select(offset =>
        {
            var month = endMonth.AddMonths(offset);
            var values = approvalTimes.Where(a => a.Reviewed.Year == month.Year && a.Reviewed.Month == month.Month);
            return new AnalyticsApprovalMonth(month, values.Average(a => (double?)a.Days));
        }).ToArray();

        var overdue = await DelayedSessions(now).Where(s => tenders.Any(t => t.Id == s.TenderRound.TenderId))
            .Select(s => s.TenderRound.TenderId).Distinct().CountAsync(ct);
        var offers = await SubmittedOffers(tenders, filter.SupplierId).CountAsync(ct);
        int CountRequests(PurchaseRequestStatus status) => requestCounts.Where(g => RequestGroup(g.Status) == status).Sum(g => g.Count);
        int CountTenders(TenderStatus status) => tenderCounts.Where(g => g.Status == status).Sum(g => g.Count);
        var approved = CountRequests(PurchaseRequestStatus.Approved);
        var open = CountTenders(TenderStatus.Bidding);
        var awarded = CountTenders(TenderStatus.Awarded);
        var drafts = CountTenders(TenderStatus.Draft);
        var metrics = new AnalyticsMetric[]
        {
            new("Purchase requests", K(requestCounts.Sum(g => g.Count)), "PurchaseRequests", "request"),
            new("Open tenders", K(open), "Tenders", "tender", "tenderStatus", nameof(TenderStatus.Bidding)),
            new("Registered suppliers", K(supplierCount), "Suppliers", "users"),
            new("Approval rate", Pct(approved, approved + CountRequests(PurchaseRequestStatus.Rejected)), "PurchaseRequests", "check", "section", "status")
        };
        var requestStatuses = RequestGroups.Select(s => new AnalyticsBar(Words(s.ToString()), CountRequests(s), "PurchaseRequests",
            "requestStatus", s.ToString())).ToArray();
        var tenderStatuses = new List<AnalyticsBar>
        {
            new("Open", open, "Tenders", "tenderStatus", nameof(TenderStatus.Bidding)),
            new("Closed without an award", tenderCounts.Sum(g => g.Count) - open - awarded - drafts, "Tenders", "tenderScope", "closed-unawarded"),
            new("Awarded", awarded, "Tenders", "tenderStatus", nameof(TenderStatus.Awarded))
        };
        if (drafts > 0) tenderStatuses.Add(new("Draft", drafts, "Tenders", "tenderStatus", nameof(TenderStatus.Draft)));
        var departmentBars = departments.Select(d => new AnalyticsBar(d.Name, d.Count, "Tenders", "departmentId", K(d.DepartmentId))).ToArray();
        var followups = new AnalyticsMetric[]
        {
            new("Submitted offers", K(offers), "Evaluations", "compare"),
            new("Overdue evaluations", K(overdue), "Tenders", "history", "tenderScope", "evaluation-delays")
        };
        return new(now, filter, metrics, requestStatuses, tenderStatuses, departmentBars, approvalTimes.Average(a => (double?)a.Days),
            approvalMonths, supplierAwards.Take(3).Select(s => new AnalyticsSupplierAward(s.SupplierId, s.CompanyName, s.Count)).ToArray(),
            supplierAwards.Sum(s => s.Count), followups);
    }

    private async Task<ReportModel> DashboardReport(AnalyticsFilter filter, CancellationToken ct)
    {
        var dashboard = await DashboardAsync(filter, ct);
        var report = new ReportModel { Key = "dashboard", Title = "Reports & Analytics Overview", Filter = filter, GeneratedAt = dashboard.GeneratedAt };
        report.Kpis.AddRange(dashboard.Metrics.Concat(dashboard.Followups).Select(metric => new ReportKpi(metric.Label, metric.Value)));
        string Duration(double? days) => days?.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) ?? "—";
        report.Kpis.Add(new("Average approval duration (days)", Duration(dashboard.AverageApprovalDays)));
        report.Tables.Add(new("request-status", "Purchase requests by status", ["Status", "Requests"],
            dashboard.RequestStatuses.Select(point => new[] { point.Label, K(point.Count) }).ToList()));
        report.Tables.Add(new("tender-status", "Tenders overview", ["Status", "Tenders"],
            dashboard.TenderStatuses.Select(point => new[] { point.Label, K(point.Count) }).ToList()));
        report.Tables.Add(new("approval-months", "Monthly approval duration", ["Month", "Average days"],
            dashboard.ApprovalMonths.Select(month => new[] { month.Month.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture), Duration(month.AverageDays) }).ToList()));
        report.Tables.Add(new("suppliers", "Top suppliers by awards", ["Supplier", "Awards", "Share"],
            dashboard.TopSuppliers.Select(supplier => new[] { supplier.Name, K(supplier.Awards), Pct(supplier.Awards, dashboard.TotalSupplierAwards) }).ToList()));
        report.Tables.Add(new("departments", "Tenders by department", ["Department", "Tenders"],
            dashboard.Departments.Select(department => new[] { department.Label, K(department.Count) }).ToList()));
        return report;
    }
}
