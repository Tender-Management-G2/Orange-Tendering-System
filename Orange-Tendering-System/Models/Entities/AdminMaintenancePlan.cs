using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Orange_Tendering_System.Models.Entities;

public enum AdminMaintenanceStatus { Scheduled, InProgress, Completed, Cancelled }
public enum AdminMaintenanceCategory { AnnualReview, SecurityUpdate, BackupRecovery, PlatformCare }

[Index(nameof(Status), nameof(ScheduledDate))]
public sealed class AdminMaintenancePlan
{
    public int Id { get; set; }
    [Required, MaxLength(120)] public string Title { get; set; } = "";
    public AdminMaintenanceCategory Category { get; set; }
    public DateOnly ScheduledDate { get; set; }
    public int DurationMinutes { get; set; }
    [Required, MaxLength(100)] public string ResponsibleTeam { get; set; } = "";
    public AdminMaintenanceStatus Status { get; set; }
    public DateOnly? CompletedDate { get; set; }
    [MaxLength(1000)] public string? Notes { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    [Timestamp] public byte[] RowVersion { get; set; } = [];
}
