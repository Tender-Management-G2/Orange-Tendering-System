// ORANGE INTELLIGENCE - the Tenders tab downloads:
//   "tender_lifecycle"  Excel  every recorded workflow event of the tender
//   "tender_offers"     Excel  the effective submitted offers of one closed bidding cycle
//   "tender_summary"    PDF    lifecycle + offers in one report
// Each file is rebuilt on the server from the user's authorized data. The browser only sends identifiers
// (tender id, cycle id, period), never rows or totals, so a user cannot export data outside their scope.
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models.Entities;

namespace Orange_Tendering_System.Services.Intelligence;

public class IntelligenceExportService
{
    private readonly IntelligenceService intelligence;
    private readonly ApplicationDbContext db;
    private readonly ReportExport writer;

    public IntelligenceExportService(IntelligenceService intelligence, ApplicationDbContext db, ReportExport writer)
    {
        this.intelligence = intelligence;
        this.db = db;
        this.writer = writer;
    }

    public async Task<ExportFile> BuildAsync(ExportRequest request, CancellationToken ct)
    {
        // ---- 1. Check the request ----
        string format = (request.Format ?? "").ToLowerInvariant();
        string reportType = request.ReportType ?? "";
        if (format != "xlsx" && format != "pdf" && format != "docx")
        {
            throw new IntelligenceExportException("Choose a PDF, Excel or Word file.");
        }
        if (reportType != "tender_lifecycle" && reportType != "tender_offers" && reportType != "tender_summary")
        {
            throw new IntelligenceExportException("Choose a supported tender report.");
        }

        // ---- 2. Load fresh authorized data (refresh = true: an export never uses a cached copy) ----
        ProcurementSnapshot snapshot = await intelligence.LoadSnapshotAsync(request.From, request.To, true, ct);
        TenderRecord? tender = request.TenderId == null ? null : snapshot.Tender(request.TenderId.Value);
        if (tender == null)
        {
            throw new IntelligenceExportException("The selected tender is unavailable in your reporting scope and period.");
        }
        if (request.RoundId != null)
        {
            RoundRecord? round = snapshot.Round(request.RoundId.Value);
            if (round == null || round.TenderId != tender.Id)
            {
                throw new IntelligenceExportException("The selected round is unavailable for this tender and period.");
            }
        }
        TenderDetails details = TenderCalculator.BuildTender(snapshot, tender.Id)!;

        // ---- 3. Describe the report ----
        string title = "Tender report · #" + tender.Id + " " + tender.Title;
        if (reportType == "tender_lifecycle")
        {
            title = "Tender Lifecycle · #" + tender.Id + " " + tender.Title;
        }
        else if (reportType == "tender_offers")
        {
            title = "Submitted offers · #" + tender.Id + " " + tender.Title;
        }
        string cycleText = request.RoundId == null ? "all eligible closed bidding cycles" : "#" + request.RoundId;
        ReportModel model = new ReportModel
        {
            Key = "orange-intelligence",
            Title = title,
            GeneratedAt = DateTime.UtcNow,
            SummaryLast = true,
            Description = "Authorized scope: " + snapshot.Scope.Label + ". Period: " + snapshot.Period.Label
                + ". Tenders: #" + tender.Id + " " + tender.Title + ". Bidding cycle: " + cycleText + "."
        };
        model.Kpis.Add(new ReportKpi("Date basis", "Bidding cycles are included by deadline, closure or creation date in that order. Offers in cycles still open for bidding remain sealed and are never shown."));
        model.Kpis.Add(new ReportKpi("Tender ID", tender.Id.ToString(CultureInfo.InvariantCulture)));
        model.Kpis.Add(new ReportKpi("Tender status", tender.Status.ToString()));

        // ---- 4. Add the tables ----
        if (reportType == "tender_lifecycle" || reportType == "tender_summary")
        {
            await AddLifecycleTableAsync(model, details, snapshot, ct);
        }
        if (reportType == "tender_offers" || reportType == "tender_summary")
        {
            AddOffersTable(model, details, request.RoundId);
        }

        List<string> sheets = new List<string>();
        bool anyRows = false;
        foreach (ReportTable table in model.Tables)
        {
            sheets.Add(table.Title + ": " + table.Rows.Count);
            if (table.Rows.Count > 0)
            {
                anyRows = true;
            }
        }
        model.Kpis.Add(new ReportKpi("Records", string.Join("; ", sheets)));
        if (!anyRows)
        {
            model.Kpis.Add(new ReportKpi("Result", "No authorized records match these filters. Nothing was omitted or invented."));
        }

        // ---- 5. Write the file (ReportExport is the shared Excel / Word / PDF writer of the system) ----
        ExportFile file = new ExportFile();
        file.Sheets = sheets;
        if (format == "xlsx")
        {
            file.Bytes = writer.ToXlsx(model);
            file.ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
        }
        else if (format == "docx")
        {
            file.Bytes = writer.ToDocx(model);
            file.ContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
        }
        else
        {
            file.Bytes = writer.ToPdf(model);
            file.ContentType = "application/pdf";
        }
        string stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        file.FileName = "Orange-Intelligence-" + reportType.Replace('_', '-') + "-" + tender.Id + "-" + stamp + "-" + Guid.NewGuid().ToString("N") + "." + format;
        return file;
    }

    // One row per recorded workflow event: cycles, offers, committee cycles, reports, final decision, award.
    private async Task AddLifecycleTableAsync(ReportModel model, TenderDetails tender, ProcurementSnapshot snapshot, CancellationToken ct)
    {
        List<int> roundIds = new List<int>();
        foreach (TenderRound round in tender.Rounds)
        {
            roundIds.Add(round.RoundId);
        }

        // EF Core: three read-only queries for the evaluation workflow records of these cycles.
        var committees = await db.CommitteeRounds.AsNoTracking()
            .Where(x => roundIds.Contains(x.TenderRoundId))
            .Select(x => new { x.TenderRoundId, x.CommitteeType, x.RoundNumber, x.Status, x.StartedAt, x.CompletedAt })
            .ToListAsync(ct);
        var reports = await db.TenderReports.AsNoTracking()
            .Where(x => roundIds.Contains(x.TenderRoundId))
            .Select(x => new { x.TenderRoundId, x.ReportType, x.VersionNumber, x.Status, x.SubmittedAt, x.ReviewedAt })
            .ToListAsync(ct);
        var notices = await db.FinalDecisionNotices.AsNoTracking()
            .Where(x => x.TenderId == tender.TenderId)
            .Select(x => new { x.CreatedAt, x.Status, x.Outcome, x.RejectedAt, x.ManagerAcknowledgedAt, x.SuppliersNotifiedAt })
            .ToListAsync(ct);

        List<string[]> rows = new List<string[]>();
        const string bidding = "Bidding & Supplier Submissions";
        const string evaluation = "Technical & Financial Evaluation";
        const string topCommittee = "Top Committee Final Decision";
        const string managerReview = "Sourcing Manager Review";
        const string notification = "Award & Supplier Notification";

        AddEvent(rows, tender, bidding, "Tender created", "Recorded", tender.CreatedAt, "Tenders.CreatedAt", null);
        foreach (TenderRound round in tender.Rounds)
        {
            RoundRecord source = snapshot.Round(round.RoundId)!;
            string cycle = IntelligenceService.CycleLabel(round.RoundNumber);
            AddEvent(rows, tender, bidding, cycle + " created", "Recorded", source.CreatedAt, "TenderRounds.CreatedAt", round.RoundId);
            AddEvent(rows, tender, bidding, cycle + " deadline", "Recorded", round.Deadline, "TenderRounds.Deadline", round.RoundId);
            AddEvent(rows, tender, bidding, cycle + " closed", "Recorded", round.ClosedAt, "TenderRounds.ClosedAt", round.RoundId);
            string counts = round.Analysed
                ? round.Invited + " invited; " + round.Offers + " effective submitted offers"
                : round.Invited + " invited; submitted offers sealed";
            AddEvent(rows, tender, bidding, cycle + " invitations / submitted offers", counts, null, "TenderInvitations / effective Bids", round.RoundId);
            if (round.Analysed)
            {
                foreach (BidRecord bid in snapshot.Bids.Where(bid => bid.RoundId == round.RoundId).OrderBy(bid => bid.SubmittedAt))
                {
                    AddEvent(rows, tender, bidding, "Bid #" + bid.Id + " · " + snapshot.SupplierName(bid.SupplierId), "Submitted", bid.SubmittedAt, "Bids.SubmittedAt", round.RoundId);
                }
            }
        }
        foreach (var committee in committees.OrderBy(x => x.StartedAt))
        {
            string stage = committee.CommitteeType == CommitteeType.Evaluation ? evaluation : topCommittee;
            string status = "Current cycle status: " + committee.Status;
            AddEvent(rows, tender, stage, "Committee cycle " + committee.RoundNumber + " started", status, committee.StartedAt, "CommitteeRounds.StartedAt", committee.TenderRoundId);
            AddEvent(rows, tender, stage, "Committee cycle " + committee.RoundNumber + " completed", status, committee.CompletedAt, "CommitteeRounds.CompletedAt", committee.TenderRoundId);
        }
        foreach (var report in reports.OrderBy(x => x.SubmittedAt))
        {
            string stage = evaluation;
            if (report.ReportType == ReportType.ExecutiveSummary)
            {
                stage = topCommittee;
            }
            else if (report.ReportType == ReportType.FullEvaluation)
            {
                stage = managerReview;
            }
            string name = report.ReportType + " report v" + report.VersionNumber;
            AddEvent(rows, tender, stage, name + " submitted", report.Status.ToString(), report.SubmittedAt, "TenderReports.SubmittedAt", report.TenderRoundId);
            AddEvent(rows, tender, stage, name + " reviewed", report.Status.ToString(), report.ReviewedAt, "TenderReports.ReviewedAt", report.TenderRoundId);
        }
        foreach (var notice in notices.OrderBy(x => x.CreatedAt))
        {
            AddEvent(rows, tender, topCommittee, "Decision notice created", notice.Outcome.ToString(), notice.CreatedAt, "FinalDecisionNotices.CreatedAt", null);
            if (notice.RejectedAt != null)
            {
                AddEvent(rows, tender, topCommittee, "Decision rejected", notice.Outcome.ToString(), notice.RejectedAt, "FinalDecisionNotices.RejectedAt", null);
            }
            AddEvent(rows, tender, managerReview, "Manager acknowledged final decision", notice.Status.ToString(), notice.ManagerAcknowledgedAt, "FinalDecisionNotices.ManagerAcknowledgedAt", null);
            AddEvent(rows, tender, notification, "Supplier notification sent", notice.Status.ToString(), notice.SuppliersNotifiedAt, "FinalDecisionNotices.SuppliersNotifiedAt", null);
        }
        AddEvent(rows, tender, notification, "Official award", tender.Status, tender.AwardedAt, "Tenders.AwardedAt", null);

        string[] columns = { "Workflow stage", "Bidding cycle ID", "Event", "Recorded status / count", "Recorded at (UTC)", "Days from tender creation", "Recorded in" };
        model.Tables.Add(new ReportTable("lifecycle", "Recorded tender lifecycle", columns, rows));
        string duration = "Not available: supplier notification is not recorded";
        if (tender.DaysToNotification != null)
        {
            duration = tender.DaysToNotification.Value.ToString("0.###", CultureInfo.InvariantCulture) + " days, tender creation to supplier notification";
        }
        model.Kpis.Add(new ReportKpi("Lifecycle duration", duration));
    }

    private static void AddEvent(List<string[]> rows, TenderDetails tender, string stage, string eventName, string status, DateTime? at, string source, int? roundId)
    {
        string recordedAt = "Not recorded";
        string days = "";
        if (at != null)
        {
            recordedAt = at.Value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            days = (at.Value - tender.CreatedAt).TotalDays.ToString("0.###", CultureInfo.InvariantCulture);
        }
        string cycle = roundId == null ? "" : roundId.Value.ToString(CultureInfo.InvariantCulture);
        rows.Add(new string[] { stage, cycle, eventName, status, recordedAt, days, ReadableSource(source) });
    }

    // "TenderRounds.ClosedAt" -> "Tender rounds · closed at": readable, still traceable to the database column.
    private static string ReadableSource(string field)
    {
        string[] parts = field.Split('.', 2);
        List<string> readable = new List<string>();
        foreach (string part in parts)
        {
            readable.Add(Regex.Replace(part, "(?<=[a-z])(?=[A-Z])", " ").ToLowerInvariant());
        }
        string text = string.Join(" · ", readable);
        if (text.Length == 0)
        {
            return field;
        }
        return char.ToUpperInvariant(text[0]) + text.Substring(1);
    }

    // The offers of one closed cycle (roundId), or of every closed cycle of the tender (roundId = null).
    private static void AddOffersTable(ReportModel model, TenderDetails tender, int? roundId)
    {
        List<string[]> rows = new List<string[]>();
        int cycles = 0;
        foreach (TenderRound round in tender.Rounds)
        {
            if (!round.Analysed || (roundId != null && round.RoundId != roundId))
            {
                continue;
            }
            cycles++;
            foreach (TenderOffer offer in round.Bids)
            {
                rows.Add(new string[]
                {
                    tender.TenderId.ToString(CultureInfo.InvariantCulture),
                    round.RoundId.ToString(CultureInfo.InvariantCulture),
                    round.RoundNumber.ToString(CultureInfo.InvariantCulture),
                    offer.BidId.ToString(CultureInfo.InvariantCulture),
                    offer.SupplierId.ToString(CultureInfo.InvariantCulture),
                    offer.Supplier,
                    offer.Currency,
                    offer.Total.ToString("0.00", CultureInfo.InvariantCulture),
                    offer.ComparisonGroup == null ? "" : offer.ComparisonGroup.Value.ToString(CultureInfo.InvariantCulture),
                    offer.RelativeToMedian == null ? "" : offer.RelativeToMedian.Value.ToString("0.######", CultureInfo.InvariantCulture),
                    offer.ComparisonNote,
                    offer.Compliance,
                    offer.DeliveryDays == null ? "" : offer.DeliveryDays.Value.ToString(CultureInfo.InvariantCulture),
                    offer.Awarded ? "Yes" : "No"
                });
            }
        }
        if (roundId != null && cycles == 0)
        {
            throw new IntelligenceExportException("This round has no accessible closed offers.");
        }
        string[] columns = { "Tender ID", "Bidding cycle ID", "Bidding cycle number", "Bid ID", "Supplier ID", "Supplier", "Currency", "Total offer", "Comparison group", "Relative to group median", "Comparison note", "Compliance", "Delivery days", "Official award" };
        model.Tables.Add(new ReportTable("offers", "Effective submitted offers by closed bidding cycle", columns, rows));
        model.Kpis.Add(new ReportKpi("Offer scope", rows.Count + " effective submitted offers across " + cycles + " closed bidding cycles; currencies and item structures remain separate."));
    }
}
