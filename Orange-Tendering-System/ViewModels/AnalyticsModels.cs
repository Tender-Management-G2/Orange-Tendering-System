using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.Services;

namespace Orange_Tendering_System.ViewModels;

public sealed class AnalyticsFilter : IValidatableObject
{
    [StringLength(100)] public string? Search { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    [Range(1, int.MaxValue)] public int? DepartmentId { get; set; }
    [Range(1, int.MaxValue)] public int? SupplierId { get; set; }
    public TenderStatus? TenderStatus { get; set; }
    public PurchaseRequestStatus? RequestStatus { get; set; }
    public SupplierStatus? SupplierStatus { get; set; }
    public string? TenderScope { get; set; }

    public bool HasTenderScope => DepartmentId.HasValue || SupplierId.HasValue || TenderStatus.HasValue || TenderScope != null;
    public DateTime? StartUtc => From is { } day ? Utc(day) : null;
    public DateTime? EndUtc => To is { } day ? Utc(day.AddDays(1)) : null;
    private static DateTime Utc(DateOnly day) => TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(TimeOnly.MinValue), JordanTime.Zone);

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (From == DateOnly.MinValue || To == DateOnly.MinValue || To == DateOnly.MaxValue)
            yield return new ValidationResult("Enter a valid reporting date range.");
        if (From > To) yield return new ValidationResult("The start date must be on or before the end date.");
        if (TenderScope is not (null or "closed" or "closed-unawarded" or "no-offers" or "evaluation-delays"))
            yield return new ValidationResult("Select a valid tender grouping.");
    }

    public Dictionary<string, string> RouteValues()
    {
        var values = new Dictionary<string, string>();
        void Add(string key, string? value) { if (!string.IsNullOrWhiteSpace(value)) values[key] = value; }
        Add("search", Search);
        Add("from", From?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Add("to", To?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Add("departmentId", DepartmentId?.ToString(CultureInfo.InvariantCulture));
        Add("supplierId", SupplierId?.ToString(CultureInfo.InvariantCulture));
        Add("tenderStatus", TenderStatus?.ToString());
        Add("requestStatus", RequestStatus?.ToString());
        Add("supplierStatus", SupplierStatus?.ToString());
        Add("tenderScope", TenderScope);
        return values;
    }
}

public sealed record AnalyticsOption(int Id, string Name);
public sealed record AnalyticsFilterForm(string Action, AnalyticsFilter Filter,
    IReadOnlyList<AnalyticsOption> Departments, IReadOnlyList<AnalyticsOption> Suppliers, string? Section = null);
public sealed record AnalyticsMetric(string Label, string Value, string Action, string Icon, string? Parameter = null, string? Selection = null);
public sealed record AnalyticsBar(string Label, int Count, string Action, string Parameter, string Selection);
public sealed record AnalyticsApprovalMonth(DateOnly Month, double? AverageDays);
public sealed record AnalyticsSupplierAward(int SupplierId, string Name, int Awards);
public sealed record AnalyticsDashboard(DateTime GeneratedAt, AnalyticsFilter Filter,
    IReadOnlyList<AnalyticsMetric> Metrics, IReadOnlyList<AnalyticsBar> RequestStatuses, IReadOnlyList<AnalyticsBar> TenderStatuses,
    IReadOnlyList<AnalyticsBar> Departments, double? AverageApprovalDays, IReadOnlyList<AnalyticsApprovalMonth> ApprovalMonths,
    IReadOnlyList<AnalyticsSupplierAward> TopSuppliers, int TotalSupplierAwards, IReadOnlyList<AnalyticsMetric> Followups);
public sealed record AnalyticsDashboardPage(AnalyticsFilterForm Filters, AnalyticsDashboard Dashboard);
public sealed record AnalyticsReportPage(AnalyticsFilterForm Filters, ReportModel Report, int Table, int Page);
public sealed record AnalyticsAccessRow(string Key, string Title, string Role, bool Allowed);
public sealed record AnalyticsReadScope(bool IsAdmin, bool IsGlobalOfficer = false, string? UserId = null, bool Manager = false, bool Executive = false)
{
    public bool AllTenders => IsAdmin || IsGlobalOfficer;
}
