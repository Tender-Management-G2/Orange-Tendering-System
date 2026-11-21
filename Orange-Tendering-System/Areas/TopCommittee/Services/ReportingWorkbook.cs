using Orange_Tendering_System.Areas.TopCommittee.ViewModels;

namespace Orange_Tendering_System.Areas.TopCommittee.Services;

public static class ReportingWorkbook
{
    public static byte[] Create(ReportingPage page)
    {
        var d = page.Data;
        var overview = new List<object?[]> { new object?[] { "Report", page.Title }, new object?[] { "Generated (UTC)", CommitteeDisplay.Date(d.GeneratedAt) } };
        var sheets = new List<(string Name, List<object?[]> Rows)> { ("Overview", overview) };
        if (page.FullSystem)
        {
            overview.AddRange([["Purchase Requests", d.Requests], ["Tenders", d.Tenders], ["Suppliers", d.Suppliers], ["Bids (recorded revisions)", d.Bids], ["Evaluation rounds", d.Evaluations]]);
            sheets.Add(("Monthly activity", new List<object?[]> { new object?[] { "Month (UTC)", "Purchase Requests", "Tenders", "Bids", "Evaluations" } }.Concat(d.Months.Select(m => new object?[] { m.Label, m.Requests, m.Tenders, m.Bids, m.Evaluations })).ToList()));
            sheets.Add(("System summary", new List<object?[]> { new object?[] { "Module", "Total", "Key Insight" } }.Concat(d.Summary.Select(r => new object?[] { r.Module, r.Total, r.Insight })).ToList()));
            sheets.Add(("Supplier statuses", Points(d.SupplierStatuses)));
        }
        else
        {
            overview.AddRange([["Total Suppliers", d.Suppliers], ["Total Tenders", d.Tenders], ["Awarded Contracts", d.Awards], ["Purchase Requests", d.Requests], ["Recorded Top Committee votes", d.CompletedVotes]]);
            sheets.Add(("Key metrics", [["Metric", "Value"], ["Supplier participation rate", ReportFormat.Percent(d.ParticipationRate)], ["Tenders with no submitted bids", d.ZeroBidTenders], ["Mean elapsed evaluation time", ReportFormat.Days(d.EvaluationDays)], ["Mean accepted-submission duration", ReportFormat.Days(d.RequestApprovalDays)]]));
            sheets.Add(("Supplier performance", new List<object?[]> { new object?[] { "Supplier", "Submitted-offer transactions", "Awards", "Success rate" } }.Concat(d.SupplierRows.OrderByDescending(s => s.Awards).ThenByDescending(s => s.Transactions).ThenBy(s => s.Name).Take(5).Select(s => new object?[] { s.Name, s.Transactions, s.Awards, ReportFormat.Percent(s.SuccessRate) })).ToList()));
            sheets.Add(("Transaction frequency", new List<object?[]> { new object?[] { "Supplier", "Submitted-offer transactions" } }.Concat(d.SupplierRows.OrderByDescending(s => s.Transactions).ThenBy(s => s.Name).Take(5).Select(s => new object?[] { s.Name, s.Transactions })).ToList()));
        }
        sheets.Add(("Tender statuses", Points(d.TenderStatuses)));
        sheets.Add(("Request statuses", Points(d.RequestStatuses)));
        sheets.Add(("Definitions", [["Metric", "Definition"], ["Open tenders", "All non-terminal stages, including drafts and review. Closed = Rejected or Cancelled; Awarded separately."], ["Transaction", "One tender-round invitation with at least one submitted offer. Revisions do not count twice."], ["Registered suppliers", "All supplier records. Approved and Suspended are subsets."], ["Evaluation delay", "Elapsed StartedAt to CompletedAt; no SLA or planned deadline is stored."], ["Approval duration", "SubmittedAt to ReviewedAt on each request's latest Accepted review with valid timestamps."], ["Converted requests", "Shown separately when present, not silently counted as Accepted."], ["Monthly activity", "Six UTC calendar months, current month partial. CreatedAt; StartedAt for evaluations."]]));
        return StatisticsWorkbook.CreateSheets(sheets);
    }
    private static List<object?[]> Points(IEnumerable<ReportPoint> points) => new List<object?[]> { new object?[] { "Status", "Count" } }.Concat(points.Select(p => new object?[] { p.Label, p.Count })).ToList();
}

