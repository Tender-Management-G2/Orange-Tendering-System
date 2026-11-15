using System.ComponentModel.DataAnnotations;
using Orange_Tendering_System.Models.Entities;

namespace Orange_Tendering_System.ViewModels;

public sealed class AdminBusinessViewModel
{
    // Illustrative values only; never represent actual transactions or platform profit.
    public decimal SampleLicense => 12000m;
    public decimal SampleAnnualCare => 1800m;
    public decimal SampleOperatingCosts => 4200m;
    public decimal SampleRevenue => SampleLicense + SampleAnnualCare;
    public decimal SampleBalance => SampleRevenue - SampleOperatingCosts;
    public DateOnly Today { get; set; }
    public DateTimeOffset LoadedAt { get; set; }
    public List<AdminMaintenanceItem> Maintenance { get; set; } = [];
    public int PendingSuppliers { get; set; }
    public int UnassignedRepresentatives { get; set; }
    public int EvaluationReadySeats { get; set; }
    public int TopReadySeats { get; set; }
    public int OverdueCount => Maintenance.Count(x => x.Open && x.ScheduledDate < Today);
    public int OpenCount => Maintenance.Count(x => x.Open);
    public int CompletedThisYear => Maintenance.Count(x => x.Status == AdminMaintenanceStatus.Completed && x.CompletedDate?.Year == Today.Year);
    public AdminMaintenanceItem? NextMaintenance => Maintenance.Where(x => x.Open).OrderBy(x => x.ScheduledDate).FirstOrDefault();
}
public sealed record AdminMaintenanceItem(int Id, string Title, string Category, DateOnly ScheduledDate,
    int DurationMinutes, string ResponsibleTeam, AdminMaintenanceStatus Status, DateOnly? CompletedDate, string? Notes)
{
    public bool Open => Status is AdminMaintenanceStatus.Scheduled or AdminMaintenanceStatus.InProgress;
    public string StatusLabel => Status == AdminMaintenanceStatus.InProgress ? "In progress" : Status.ToString();
}
public sealed class AdminMaintenanceForm : IValidatableObject
{
    public int Id { get; set; }
    [Required, StringLength(120)] public string Title { get; set; } = "";
    [EnumDataType(typeof(AdminMaintenanceCategory))] public AdminMaintenanceCategory Category { get; set; }
    [DataType(DataType.Date)] public DateOnly ScheduledDate { get; set; }
    [Range(15, 1440)] public int DurationMinutes { get; set; } = 120;
    [Required, StringLength(100)] public string ResponsibleTeam { get; set; } = "";
    [EnumDataType(typeof(AdminMaintenanceStatus))] public AdminMaintenanceStatus Status { get; set; }
    [DataType(DataType.Date)] public DateOnly? CompletedDate { get; set; }
    [StringLength(1000)] public string? Notes { get; set; }
    [StringLength(128)] public string? Version { get; set; }
    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (ScheduledDate == default) yield return new("Choose a scheduled date.", [nameof(ScheduledDate)]);
        if (Status == AdminMaintenanceStatus.Completed && CompletedDate == null)
            yield return new("Enter the completion date for completed maintenance.", [nameof(CompletedDate)]);
        if (Status != AdminMaintenanceStatus.Completed && CompletedDate != null)
            yield return new("A completion date is only used for completed maintenance.", [nameof(CompletedDate)]);
    }
}
