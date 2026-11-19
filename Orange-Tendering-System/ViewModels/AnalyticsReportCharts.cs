using System.Globalization;
using Orange_Tendering_System.Services;

namespace Orange_Tendering_System.ViewModels;

public sealed record AnalyticsReportCharts(string DistributionTitle, IReadOnlyList<AnalyticsBar> Distribution,
    string ComparisonTitle, IReadOnlyList<AnalyticsBar> Comparison)
{
    public static AnalyticsReportCharts Create(ReportModel report)
    {
        var tables = report.Tables.ToDictionary(table => table.Key);
        IReadOnlyList<AnalyticsBar> Group(string key, string column, string? countColumn = null)
        {
            if (!tables.TryGetValue(key, out var table)) return [];
            var label = Array.IndexOf(table.Columns, column);
            var count = countColumn == null ? -1 : Array.IndexOf(table.Columns, countColumn);
            return table.Rows.GroupBy(row => row[label]).Select(group => new AnalyticsBar(group.Key,
                count < 0 ? group.Count() : group.Sum(row => int.Parse(row[count], CultureInfo.InvariantCulture)), "", "", "")).ToArray();
        }

        return report.Key switch
        {
            "purchase-requests" => new("Purchase requests by status", Group("status", "Status", "Requests"),
                "Requests by department", Group("departments", "Department", "Requests")),
            "tenders" => new("Tenders overview", Group("status", "Status", "Tenders"), "Tenders by department", Group("tenders", "Department")),
            "suppliers" => new("Suppliers by status", Group("status", "Status", "Suppliers"),
                "Offers by supplier", Group("supplier-performance", "Supplier", "Offers")),
            "evaluations" => new("Offers by compliance", Group("price-comparison", "Compliance"),
                "Offers by tender", Group("price-comparison", "Tender")),
            "governance" => new("Recommendations by status", Group("recommendations", "Status"),
                "Final decisions by outcome", Group("final-decisions", "Outcome")),
            "management" => new("Tenders overview", Group("status", "Status", "Tenders"),
                "Awards by supplier", Group("supplier-awards", "Supplier", "Awards")),
            _ => throw new ArgumentOutOfRangeException(nameof(report))
        };
    }
}
