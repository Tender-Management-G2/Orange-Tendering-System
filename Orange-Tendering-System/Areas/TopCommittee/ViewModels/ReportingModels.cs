using System.Globalization;

namespace Orange_Tendering_System.Areas.TopCommittee.ViewModels;

public sealed record ReportPoint(string Label, int Count, string Color);
public sealed record ActivityMonth(string Label, int Requests, int Tenders, int Bids, int Evaluations);
public sealed record ReportSummaryRow(string Module, int Total, string Insight);
public sealed record SupplierPerformance(string Name, int Transactions, int Awards)
{
    public decimal? SuccessRate => Transactions > 0 && Awards <= Transactions ? Awards * 100m / Transactions : null;
}
public sealed class ReportingData
{
    public DateTime GeneratedAt { get; } = DateTime.UtcNow;
    public int Suppliers { get; init; }
    public int Tenders { get; init; }
    public int Awards { get; init; }
    public int Requests { get; init; }
    public int Bids { get; init; }
    public int Evaluations { get; init; }
    public int CompletedVotes { get; init; }
    public int ZeroBidTenders { get; init; }
    public decimal? ParticipationRate { get; init; }
    public double? EvaluationDays { get; init; }
    public double? RequestApprovalDays { get; init; }
    public List<ReportPoint> TenderStatuses { get; init; } = [];
    public List<ReportPoint> RequestStatuses { get; init; } = [];
    public List<ReportPoint> SupplierStatuses { get; init; } = [];
    public List<SupplierPerformance> SupplierRows { get; init; } = [];
    public List<ActivityMonth> Months { get; init; } = [];
    public List<ReportSummaryRow> Summary { get; init; } = [];
}
public sealed record ReportingPage(ReportingData Data, bool FullSystem)
{
    public string Title => FullSystem ? "Full System Statistics" : "Reports & Analytics – Top Committee";
    public string Subtitle => FullSystem
        ? "General statistics for purchase requests, tenders, suppliers, bids, and evaluations across the system."
        : "Comprehensive statistics and reports for the sourcing and tender process.";
}
public sealed record ReportChart(string Title, IReadOnlyList<ReportPoint> Points, string CenterLabel = "", bool Horizontal = false);
public static class ReportFormat
{
    public static string Percent(decimal? value) => value.HasValue ? value.Value.ToString("0.#", CultureInfo.InvariantCulture) + "%" : "N/A";
    public static decimal? Ratio(int value, int total) => total > 0 ? value * 100m / total : null;
    public static string Days(double? value) => value.HasValue ? value.Value.ToString("0.#", CultureInfo.InvariantCulture) + " days" : "N/A";
    public static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
