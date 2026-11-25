using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models.Entities;

namespace Orange_Tendering_System.Services.Radar;

public sealed record RadarExportRequest(string Format, string ReportType, string? From, string? To, int? TenderId, int[]? SupplierIds,
    int? RoundId = null, string? Language = null);
// Sheets lists every table actually written with its row count, so the assistant can state what the file contains.
public sealed record RadarExportFile(byte[] Bytes, string ContentType, string FileName, string Title = "", string Scope = "", IReadOnlyList<(string Title, int Rows)>? Sheets = null);

// Every export is rebuilt from the caller's authorized snapshot. The browser and the model supply identifiers, never rows or totals.
public sealed class RadarExportService(RadarService radar, RadarDataService data, ApplicationDbContext db, ReportExport writer)
{
    public static readonly string[] Formats = ["xlsx", "docx", "pdf"];
    // Report scopes the page may request, plus "price_timing", the combined manual export of the Price & Timing panel.
    public static readonly string[] Types = ["suppliers", "supplier_comparison", "supplier_participation", "market_overview", "supplier_profile", "tenders",
        "tender_summary", "tender_lifecycle", "tender_offers", "supplier_offers", "price_patterns", "submission_timing", "price_timing"];
    private static readonly HashSet<string> TenderReports = ["tender_summary", "tender_lifecycle", "tender_offers"];

    public async Task<RadarExportFile> BuildAsync(RadarExportRequest request, CancellationToken ct)
    {
        var format = request.Format?.ToLowerInvariant() ?? "";
        if (!Formats.Contains(format) || !Types.Contains(request.ReportType)) throw new RadarToolException("Choose a supported report type and PDF, Excel or Word format.");
        var period = RadarPeriod.Parse(request.From, request.To);
        await data.ScopeAsync(ct);
        data.Invalidate(period);
        var s = await radar.SnapshotAsync(period, ct);
        var tender = request.TenderId is { } id ? s.Tender(id) ?? throw new RadarToolException("The selected tender is unavailable in your reporting scope and period.") : null;
        if (TenderReports.Contains(request.ReportType) && tender == null) throw new RadarToolException("Select an authorized tender for this report.");
        var ids = (request.SupplierIds ?? []).Distinct().ToArray();
        if (ids.Any(id => s.Supplier(id) == null)) throw new RadarToolException("A selected supplier is unavailable in your reporting scope.");
        if (request.ReportType == "supplier_profile" && ids.Length != 1) throw new RadarToolException("Choose one authorized supplier for this report.");
        if (request.RoundId is { } rid && (tender == null || s.Round(rid)?.TenderId != tender.Id)) throw new RadarToolException("The selected round is unavailable for this tender and period.");

        var title = request.ReportType switch
        {
            "tender_lifecycle" => $"Tender Lifecycle · #{tender!.Id} {tender.Title}",
            "tender_offers" => $"Submitted offers · #{tender!.Id} {tender.Title}",
            "tender_summary" => $"Tender report · #{tender!.Id} {tender.Title}",
            "supplier_profile" => $"Supplier report · {s.SupplierName(ids[0])}",
            "suppliers" => "Authorized suppliers",
            "supplier_comparison" => "Supplier comparison",
            "supplier_offers" => "Submitted supplier offers",
            "supplier_participation" => "Supplier tender participation",
            "tenders" => "Authorized tenders",
            "price_patterns" => "Price Patterns",
            "submission_timing" => "Submission Timing",
            "price_timing" => "Price & Timing Intelligence",
            _ => "Supplier market overview"
        };
        var supplierScope = ids.Length == 0 ? "all authorized suppliers" : string.Join(", ", ids.Select(x => s.SupplierName(x)));
        var tenderScope = tender == null ? "all authorized tenders" : $"#{tender.Id} {tender.Title}";
        var scope = $"{s.Scope.Label} · period: {period.Label} · tenders: {tenderScope} · suppliers: {supplierScope}" + (request.RoundId is { } round ? $" · round #{round}" : "");
        var model = new ReportModel { Key = "orange-intelligence", Title = title, GeneratedAt = DateTime.UtcNow, SummaryLast = true,
            Description = $"Authorized scope: {s.Scope.Label}. Period: {period.Label}. Tenders: {tenderScope}. Suppliers: {supplierScope}. Bidding cycle: {(request.RoundId is { } r2 ? $"#{r2}" : "all eligible closed bidding cycles")}." };
        model.Kpis.Add(new ReportKpi("Date basis", "Bidding cycles are included by deadline, closure or creation date in that order. Offers in cycles still open for bidding remain sealed and are never shown."));
        if (tender != null) { model.Kpis.Add(new ReportKpi("Tender ID", tender.Id.ToString(CultureInfo.InvariantCulture))); model.Kpis.Add(new ReportKpi("Tender status", tender.Status.ToString())); }

        var tenderView = tender == null ? null : RadarTenders.Build(s).Single(t => t.TenderId == tender.Id);
        switch (request.ReportType)
        {
            case "tender_lifecycle": await AddLifecycle(model, tenderView!, s, ct); break;
            case "tender_summary": await AddLifecycle(model, tenderView!, s, ct); AddOffers(model, tenderView!, request.RoundId); break;
            case "tender_offers": AddOffers(model, tenderView!, request.RoundId); break;
            case "supplier_offers": AddSupplierOffers(model, s, ids); break;
            case "suppliers": AddSuppliers(model, s, ids, "Suppliers"); AddCategories(model, s); break;
            case "supplier_comparison": AddComparison(model, s, ids); break;
            case "supplier_participation": AddSuppliers(model, s, ids, "Suppliers"); AddTenderParticipation(model, s, ids, tender); break;
            case "supplier_profile": AddProfile(model, s, ids[0]); AddTenderParticipation(model, s, ids, null); AddSupplierOffers(model, s, ids); break;
            case "tenders": AddTenders(model, s); break;
            case "price_patterns": AddPriceTiming(model, s, ids, price: true, timing: false); break;
            case "submission_timing": AddPriceTiming(model, s, ids, price: false, timing: true); break;
            case "price_timing": AddPriceTiming(model, s, ids, price: true, timing: true); break;
            default: AddMarketMetrics(model, s); AddCategories(model, s); AddComparison(model, s, ids); AddMarket(model, s); break;
        }
        var sheets = model.Tables.Select(t => (t.Title, t.Rows.Count)).ToList();
        model.Kpis.Add(new ReportKpi("Records", string.Join("; ", sheets.Select(x => $"{x.Title}: {x.Count}"))));
        if (model.Tables.All(t => t.Rows.Count == 0))
            model.Kpis.Add(new ReportKpi("Result", "No authorized records match these filters. Nothing was omitted or invented."));

        var bytes = format switch { "xlsx" => writer.ToXlsx(model), "docx" => writer.ToDocx(model), _ => writer.ToPdf(model) };
        var mime = format switch { "xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document", _ => "application/pdf" };
        return new RadarExportFile(bytes, mime, $"Orange-Intelligence-{request.ReportType.Replace('_', '-')}-{(tender?.Id.ToString(CultureInfo.InvariantCulture) ?? "market")}-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.{format}",
            title, scope, sheets);
    }

    private static string Pct(double? v) => v == null ? "Not available" : (v.Value * 100).ToString("0.#", CultureInfo.InvariantCulture) + "%";
    private static string Date(DateTime? d) => d?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "Not recorded";
    private static string Money(IReadOnlyList<RadarAnalytics.CurrencyValue> values) => values.Count == 0 ? "None" : string.Join("; ", values.Select(v => $"{v.Currency} {v.Value.ToString("0.00", CultureInfo.InvariantCulture)}"));

    private static void AddSuppliers(ReportModel m, RadarSnapshot s, int[] ids, string title)
    {
        var stats = RadarAnalytics.Suppliers(s).ToDictionary(x => x.SupplierId);
        var rows = s.Suppliers.Where(x => ids.Length == 0 || ids.Contains(x.Id)).OrderBy(x => x.Name).Select(x =>
        {
            var st = stats.GetValueOrDefault(x.Id);
            return new[] { x.Id.ToString(), x.Name, x.Category, x.Status.ToString(), Date(x.CreatedAt), (st?.Invitations ?? 0).ToString(), (st?.Participations ?? 0).ToString(),
                (st?.Tenders ?? 0).ToString(), Pct(st?.ParticipationRate), (st?.Wins ?? 0).ToString(), Date(st?.LastActivity) };
        }).ToList();
        m.Tables.Add(new ReportTable("suppliers", title, ["Supplier ID", "Supplier", "Registered category", "Recorded status", "Registered on", "Invitations", "Offers in closed bidding cycles", "Tenders with an offer", "Response rate", "Official awards", "Last recorded activity"], rows));
    }

    private static void AddCategories(ReportModel m, RadarSnapshot s)
    {
        var bidders = s.Bids.Select(b => b.SupplierId).ToHashSet();
        var rows = s.Suppliers.GroupBy(x => string.IsNullOrWhiteSpace(x.Category) ? "Uncategorised" : x.Category).OrderByDescending(g => g.Count()).ThenBy(g => g.Key)
            .Select(g => new[] { g.Key, g.Count().ToString(), g.Count(x => x.Status == SupplierStatus.Approved).ToString(), g.Count(x => bidders.Contains(x.Id)).ToString() }).ToList();
        m.Tables.Add(new ReportTable("categories", "Categories", ["Registered category", "Suppliers", "Approved suppliers", "Suppliers with closed-round offers"], rows));
    }

    private static void AddComparison(ReportModel m, RadarSnapshot s, int[] ids)
    {
        var rows = RadarAnalytics.Suppliers(s).Where(x => ids.Length == 0 || ids.Contains(x.SupplierId)).OrderByDescending(x => x.Participations).ThenBy(x => x.Name)
            .Select(x => new[] { x.SupplierId.ToString(), x.Name, x.Category, x.Status, x.Invitations.ToString(), x.Participations.ToString(), Pct(x.ParticipationRate), x.AwardedTendersBid.ToString(), x.Wins.ToString(), Pct(x.WinRate),
                Money(x.AwardedValue), x.RelativePrice == null ? "Not available" : ((x.RelativePrice.Value >= 0 ? "+" : "") + (x.RelativePrice.Value * 100).ToString("0.#", CultureInfo.InvariantCulture) + "%"), x.PricedRounds.ToString(),
                x.ComplianceChecked == 0 ? "Not checked" : $"{x.Compliant} of {x.ComplianceChecked}" }).ToList();
        m.Tables.Add(new ReportTable("comparison", "Supplier comparison", ["Supplier ID", "Supplier", "Registered category", "Recorded status", "Invitations", "Offers in closed bidding cycles", "Response rate",
            "Awarded tenders bid on", "Official awards", "Win rate", "Awarded value", "Price vs round median", "Comparable priced rounds", "Compliant offers"], rows));
    }

    // One row per invitation: who was invited to which tender and bidding cycle, and whether an offer was submitted.
    // Rounds still open for bidding stay sealed: only the invitation is shown.
    private static void AddTenderParticipation(ReportModel m, RadarSnapshot s, int[] ids, RTender? tender)
    {
        var bids = s.Bids.ToDictionary(b => b.InvitationId);
        var rows = s.Invitations.Where(i => (ids.Length == 0 || ids.Contains(i.SupplierId)) && (tender == null || i.TenderId == tender.Id))
            .Select(i => (Inv: i, Round: s.Round(i.RoundId)!, Tender: s.Tender(i.TenderId)))
            .OrderBy(x => s.SupplierName(x.Inv.SupplierId)).ThenBy(x => x.Inv.TenderId).ThenBy(x => x.Round.RoundNumber)
            .Select(x =>
            {
                var bid = bids.GetValueOrDefault(x.Inv.Id);
                var offer = !x.Round.Unsealed ? "Sealed until bidding closes" : bid != null ? "Submitted" : "No offer";
                var awarded = bid != null && x.Tender?.AwardedBidId == bid.Id;
                return new[] { x.Inv.SupplierId.ToString(), s.SupplierName(x.Inv.SupplierId), x.Inv.TenderId.ToString(), x.Tender?.Title ?? "", x.Round.Id.ToString(), x.Round.RoundNumber.ToString(),
                    x.Round.Status.ToString(), Date(x.Inv.InvitedAt), x.Inv.Status.ToString(), offer, x.Round.Unsealed ? Date(bid?.SubmittedAt) : "Sealed", Date(x.Round.Deadline), awarded ? "Yes" : "No" };
            }).ToList();
        m.Tables.Add(new ReportTable("participation", "Tender participation", ["Supplier ID", "Supplier", "Tender ID", "Tender", "Bidding cycle ID", "Bidding cycle", "Bidding cycle status", "Invited at (UTC)",
            "Invitation status", "Offer", "Offer submitted at (UTC)", "Bidding cycle deadline (UTC)", "Official award"], rows));
    }

    private static void AddProfile(ReportModel m, RadarSnapshot s, int id)
    {
        var x = s.Supplier(id)!; var st = RadarAnalytics.Suppliers(s).First(a => a.SupplierId == id);
        m.Tables.Add(new ReportTable("profile", "Supplier", ["Field", "Recorded value"], [
            ["Supplier ID", x.Id.ToString()], ["Supplier", x.Name], ["Registered category", x.Category], ["Recorded status", x.Status.ToString()], ["Registered on", Date(x.CreatedAt)],
            ["Invitations", st.Invitations.ToString()], ["Offers in closed bidding cycles", st.Participations.ToString()], ["Response rate", Pct(st.ParticipationRate)],
            ["Official awards", st.Wins.ToString()], ["Win rate", Pct(st.WinRate)], ["Awarded value", Money(st.AwardedValue)], ["Last recorded activity", Date(st.LastActivity)]]));
    }

    private static void AddTenders(ReportModel m, RadarSnapshot s)
    {
        var rows = s.Tenders.OrderByDescending(t => t.CreatedAt).Select(t =>
        {
            var rounds = s.Rounds.Where(r => r.TenderId == t.Id).ToList();
            var award = t.AwardedBidId is { } b ? s.Bids.FirstOrDefault(x => x.Id == b) : null;
            return new[] { t.Id.ToString(), t.Title, t.Department, t.Status.ToString(), Date(t.CreatedAt), rounds.Count.ToString(), s.Invitations.Count(i => i.TenderId == t.Id).ToString(),
                rounds.Any(r => !r.Unsealed) && !rounds.Any(r => r.Unsealed) ? "Sealed" : s.Bids.Count(x => x.TenderId == t.Id).ToString(), award == null ? "None recorded" : s.SupplierName(award.SupplierId), Date(t.AwardedAt) };
        }).ToList();
        m.Tables.Add(new ReportTable("tenders", "Tenders", ["Tender ID", "Tender", "Department", "Status", "Created (UTC)", "Bidding cycles", "Invitations", "Offers in closed bidding cycles", "Awarded supplier", "Awarded at (UTC)"], rows));
    }

    private static void AddMarketMetrics(ReportModel m, RadarSnapshot s)
    {
        var x = RadarAnalytics.Summary(s);
        m.Tables.Add(new ReportTable("market", "Market metrics", ["Metric", "Value"], [
            ["Registered suppliers", x.RegisteredSuppliers.ToString()], ["Approved suppliers", x.EligibleSuppliers.ToString()], ["Suppliers with closed-round offers", x.ParticipatingSuppliers.ToString()],
            ["Tenders with closed bidding cycles", x.AnalyzedTenders.ToString()], ["Closed bidding cycles", x.AnalyzedRounds.ToString()], ["Invitations in closed bidding cycles", x.Invitations.ToString()],
            ["Effective submitted offers", x.SubmittedBids.ToString()], ["Average offers per closed bidding cycle", x.AvgBidsPerRound?.ToString("0.##", CultureInfo.InvariantCulture) ?? "Not available"],
            ["Response rate", Pct(x.ResponseRate)], ["Official awards", x.Awards.ToString()], ["Awarded value", Money(x.AwardedValue)],
            ["Bidding cycles still sealed", x.SealedRounds.ToString()], ["Sealed offers (not shown)", x.SealedBids.ToString()]]));
    }

    private async Task AddLifecycle(ReportModel m, TenderLifecycle t, RadarSnapshot s, CancellationToken ct)
    {
        var roundIds = t.Rounds.Select(r => r.RoundId).ToArray();
        var committees = await db.CommitteeRounds.AsNoTracking().Where(x => roundIds.Contains(x.TenderRoundId))
            .Select(x => new { x.TenderRoundId, x.CommitteeType, x.RoundNumber, x.Status, x.StartedAt, x.CompletedAt }).ToListAsync(ct);
        var reports = await db.TenderReports.AsNoTracking().Where(x => roundIds.Contains(x.TenderRoundId))
            .Select(x => new { x.TenderRoundId, x.ReportType, x.VersionNumber, x.Status, x.SubmittedAt, x.ReviewedAt }).ToListAsync(ct);
        var notices = await db.FinalDecisionNotices.AsNoTracking().Where(x => x.TenderId == t.TenderId)
            .Select(x => new { x.CreatedAt, x.Status, x.Outcome, x.RejectedAt, x.ManagerAcknowledgedAt, x.SuppliersNotifiedAt }).ToListAsync(ct);
        var rows = new List<string[]>();
        void Add(string stage, string eventName, string status, DateTime? at, string source, int? roundId = null)
        {
            rows.Add([stage, roundId?.ToString(CultureInfo.InvariantCulture) ?? "", eventName, status, at?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "Not recorded",
                at == null ? "" : (at.Value - t.CreatedAt).TotalDays.ToString("0.###", CultureInfo.InvariantCulture), source]);
        }
        Add("Bidding & Supplier Submissions", "Tender created", "Recorded", t.CreatedAt, "Tenders.CreatedAt");
        foreach (var r in t.Rounds)
        {
            var source = s.Round(r.RoundId)!;
            Add("Bidding & Supplier Submissions", $"{RadarOfferComparison.CycleLabel(r.RoundNumber)} created", "Recorded", source.CreatedAt, "TenderRounds.CreatedAt", r.RoundId);
            Add("Bidding & Supplier Submissions", $"{RadarOfferComparison.CycleLabel(r.RoundNumber)} deadline", "Recorded", r.Deadline, "TenderRounds.Deadline", r.RoundId);
            Add("Bidding & Supplier Submissions", $"{RadarOfferComparison.CycleLabel(r.RoundNumber)} closed", "Recorded", r.ClosedAt, "TenderRounds.ClosedAt", r.RoundId);
            Add("Bidding & Supplier Submissions", $"{RadarOfferComparison.CycleLabel(r.RoundNumber)} invitations / submitted offers", r.Analysed ? $"{r.Invited} invited; {r.Offers} effective submitted offers" : $"{r.Invited} invited; submitted offers sealed", null, "TenderInvitations / effective Bids", r.RoundId);
            if (r.Analysed) foreach (var b in s.Bids.Where(b => b.RoundId == r.RoundId).OrderBy(b => b.SubmittedAt))
                Add("Bidding & Supplier Submissions", $"Bid #{b.Id} · {s.SupplierName(b.SupplierId)}", "Submitted", b.SubmittedAt, "Bids.SubmittedAt", r.RoundId);
        }
        foreach (var c in committees.OrderBy(x => x.StartedAt))
        {
            var stage = c.CommitteeType == CommitteeType.Evaluation ? "Technical & Financial Evaluation" : "Top Committee Final Decision";
            Add(stage, $"Committee cycle {c.RoundNumber} started", $"Current cycle status: {c.Status}", c.StartedAt, "CommitteeRounds.StartedAt", c.TenderRoundId);
            Add(stage, $"Committee cycle {c.RoundNumber} completed", $"Current cycle status: {c.Status}", c.CompletedAt, "CommitteeRounds.CompletedAt", c.TenderRoundId);
        }
        foreach (var r in reports.OrderBy(x => x.SubmittedAt))
        {
            var stage = r.ReportType == ReportType.ExecutiveSummary ? "Top Committee Final Decision" : r.ReportType == ReportType.FullEvaluation ? "Sourcing Manager Review" : "Technical & Financial Evaluation";
            Add(stage, $"{r.ReportType} report v{r.VersionNumber} submitted", r.Status.ToString(), r.SubmittedAt, "TenderReports.SubmittedAt", r.TenderRoundId);
            Add(stage, $"{r.ReportType} report v{r.VersionNumber} reviewed", r.Status.ToString(), r.ReviewedAt, "TenderReports.ReviewedAt", r.TenderRoundId);
        }
        foreach (var n in notices.OrderBy(x => x.CreatedAt))
        {
            Add("Top Committee Final Decision", "Decision notice created", n.Outcome.ToString(), n.CreatedAt, "FinalDecisionNotices.CreatedAt");
            if (n.RejectedAt != null) Add("Top Committee Final Decision", "Decision rejected", n.Outcome.ToString(), n.RejectedAt, "FinalDecisionNotices.RejectedAt");
            Add("Sourcing Manager Review", "Manager acknowledged final decision", n.Status.ToString(), n.ManagerAcknowledgedAt, "FinalDecisionNotices.ManagerAcknowledgedAt");
            Add("Award & Supplier Notification", "Supplier notification sent", n.Status.ToString(), n.SuppliersNotifiedAt, "FinalDecisionNotices.SuppliersNotifiedAt");
        }
        Add("Award & Supplier Notification", "Official award", t.Status, t.AwardedAt, "Tenders.AwardedAt");
        foreach (var row in rows) row[6] = Recorded(row[6]);
        m.Tables.Add(new ReportTable("lifecycle", "Recorded tender lifecycle", ["Workflow stage", "Bidding cycle ID", "Event", "Recorded status / count", "Recorded at (UTC)", "Days from tender creation", "Recorded in"], rows));
        m.Kpis.Add(new ReportKpi("Lifecycle duration", t.DaysToNotification is { } days ? $"{days:0.###} days, tender creation to supplier notification" : "Not available: supplier notification is not recorded"));
    }

    // "TenderRounds.ClosedAt" -> "Tender rounds · closed at": readable, still traceable to the record.
    private static string Recorded(string field)
    {
        var text = string.Join(" · ", field.Split('.', 2).Select(p => System.Text.RegularExpressions.Regex.Replace(p, "(?<=[a-z])(?=[A-Z])", " ").ToLowerInvariant()));
        return text.Length == 0 ? field : char.ToUpperInvariant(text[0]) + text[1..];
    }

    private static void AddOffers(ReportModel m, TenderLifecycle t, int? roundId)
    {
        var rounds = t.Rounds.Where(r => r.Analysed && (roundId == null || r.RoundId == roundId)).ToList();
        if (roundId != null && rounds.Count == 0) throw new RadarToolException("This round has no accessible closed offers.");
        var rows = rounds.SelectMany(r => r.Bids.Select(b => new[] { t.TenderId.ToString(), r.RoundId.ToString(), r.RoundNumber.ToString(), b.BidId.ToString(), b.SupplierId.ToString(), b.Supplier,
            b.Currency, b.Total.ToString("0.00", CultureInfo.InvariantCulture), b.ComparisonGroup?.ToString() ?? "", b.RelativeToMedian?.ToString("0.######", CultureInfo.InvariantCulture) ?? "",
            b.ComparisonNote, b.Compliance, b.DeliveryDays?.ToString() ?? "", b.Awarded ? "Yes" : "No" })).ToList();
        m.Tables.Add(new ReportTable("offers", "Effective submitted offers by closed bidding cycle", ["Tender ID", "Bidding cycle ID", "Bidding cycle number", "Bid ID", "Supplier ID", "Supplier", "Currency", "Total offer", "Comparison group", "Relative to group median", "Comparison note", "Compliance", "Delivery days", "Official award"], rows));
        m.Kpis.Add(new ReportKpi("Offer scope", $"{rows.Count} effective submitted offers across {rounds.Count} closed bidding cycles; currencies and item structures remain separate."));
    }

    private static void AddSupplierOffers(ReportModel m, RadarSnapshot s, int[] ids)
    {
        var rows = RadarTenders.Build(s).SelectMany(t => t.Rounds.Where(r => r.Analysed).SelectMany(r => r.Bids
            .Where(b => ids.Length == 0 || ids.Contains(b.SupplierId))
            .Select(b => new[] { t.TenderId.ToString(), t.Title, r.RoundId.ToString(), r.RoundNumber.ToString(), b.BidId.ToString(), b.SupplierId.ToString(), b.Supplier,
                b.Currency, b.Total.ToString("0.00", CultureInfo.InvariantCulture), b.ComparisonGroup?.ToString() ?? "",
                b.RelativeToMedian?.ToString("0.######", CultureInfo.InvariantCulture) ?? "", b.ComparisonNote, b.Compliance, b.Awarded ? "Yes" : "No" }))).ToList();
        m.Tables.Add(new ReportTable("supplier-offers", "Effective submitted offers by tender and closed bidding cycle", ["Tender ID", "Tender", "Bidding cycle ID", "Bidding cycle number", "Bid ID", "Supplier ID", "Supplier",
            "Currency", "Total offer", "Comparison group", "Relative to group median", "Comparison note", "Compliance", "Official award"], rows));
        m.Kpis.Add(new ReportKpi("Offer scope", $"{rows.Count} effective submitted offers; each tender, round, currency and item structure is identified separately."));
    }

    private static void AddMarket(ReportModel m, RadarSnapshot s)
    {
        m.Tables.Add(new ReportTable("rounds", "Authorized tender rounds", ["Tender ID", "Tender", "Bidding cycle ID", "Bidding cycle number", "Status", "Deadline (UTC)", "Closed at (UTC)", "Invited", "Submitted offers"],
            s.Rounds.OrderBy(r => r.TenderId).ThenBy(r => r.RoundNumber).Select(r => new[] { r.TenderId.ToString(), s.Tender(r.TenderId)?.Title ?? "", r.Id.ToString(), r.RoundNumber.ToString(), r.Status.ToString(),
                r.Deadline?.ToString("yyyy-MM-dd HH:mm") ?? "Not recorded", r.ClosedAt?.ToString("yyyy-MM-dd HH:mm") ?? "Not recorded", s.Invitations.Count(i => i.RoundId == r.Id).ToString(), r.Unsealed ? s.Bids.Count(b => b.RoundId == r.Id).ToString() : "Sealed" }).ToList()));
    }

    private static void AddPriceTiming(ReportModel m, RadarSnapshot s, int[] ids, bool price, bool timing)
    {
        var rows = RadarBehavior.Build(s).Where(x => ids.Length == 0 || (ids.Length == 1
            ? ids.Contains(x.SupplierA) || ids.Contains(x.SupplierB)
            : ids.Contains(x.SupplierA) && ids.Contains(x.SupplierB))).ToList();
        if (price && !timing)
        {
            m.Tables.Add(new ReportTable("price-patterns", "Price patterns", ["Tender", "Bidding cycle ID", "Supplier A", "Supplier B", "Currency", "Offer A", "Offer B", "Relative gap (B vs A)"],
                rows.Where(x => x.PriceComparable).Select(x => new[] { x.Tender, x.RoundId.ToString(), s.SupplierName(x.SupplierA), s.SupplierName(x.SupplierB), x.Currency ?? "",
                    x.OfferA?.ToString("0.00", CultureInfo.InvariantCulture) ?? "", x.OfferB?.ToString("0.00", CultureInfo.InvariantCulture) ?? "", x.RelativeGap == null ? "" : (x.RelativeGap.Value * 100).ToString("0.##", CultureInfo.InvariantCulture) + "%" }).ToList()));
            m.Kpis.Add(new ReportKpi("Method", "Only closed bidding cycles where both suppliers submitted price-comparable offers (same currency and items) are included."));
            return;
        }
        if (timing && !price)
        {
            m.Tables.Add(new ReportTable("submission-timing", "Submission timing", ["Tender", "Bidding cycle ID", "Supplier A", "Supplier B", "Submitted A (UTC)", "Submitted B (UTC)", "Minutes apart"],
                rows.Where(x => x.MinutesApart != null).Select(x => new[] { x.Tender, x.RoundId.ToString(), s.SupplierName(x.SupplierA), s.SupplierName(x.SupplierB),
                    x.SubmittedA?.ToString("yyyy-MM-dd HH:mm") ?? "", x.SubmittedB?.ToString("yyyy-MM-dd HH:mm") ?? "", x.MinutesApart?.ToString("0.###", CultureInfo.InvariantCulture) ?? "" }).ToList()));
            m.Kpis.Add(new ReportKpi("Method", "Recorded submission times of both suppliers in the same closed bidding cycle. Close timing is context, not evidence of coordination."));
            return;
        }
        m.Tables.Add(new ReportTable("price-timing", "Shared closed-round observations", ["Tender", "Bidding cycle ID", "Supplier A", "Supplier B", "Currency", "Offer A", "Offer B", "Price comparable", "Relative gap", "Submitted A (UTC)", "Submitted B (UTC)", "Minutes apart"],
            rows.Select(x => new[] { x.Tender, x.RoundId.ToString(), s.SupplierName(x.SupplierA), s.SupplierName(x.SupplierB), x.Currency ?? "", x.OfferA?.ToString("0.00", CultureInfo.InvariantCulture) ?? "", x.OfferB?.ToString("0.00", CultureInfo.InvariantCulture) ?? "",
                x.PriceComparable ? "Yes" : "No", x.RelativeGap?.ToString("0.######", CultureInfo.InvariantCulture) ?? "", x.SubmittedA?.ToString("yyyy-MM-dd HH:mm") ?? "", x.SubmittedB?.ToString("yyyy-MM-dd HH:mm") ?? "", x.MinutesApart?.ToString("0.###", CultureInfo.InvariantCulture) ?? "" }).ToList()));
    }
}

// A request the export cannot fulfil (unknown report, record outside the user's scope); its message is shown to the user.
public sealed class RadarToolException(string message) : Exception(message);
