using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Orange_Tendering_System.Models.Entities;

[Index(nameof(FinalDecisionNoticeId), IsUnique = true)]
public sealed class InternalAwardReport
{
    public int Id { get; set; }
    public int FinalDecisionNoticeId { get; set; }
    public FinalDecisionNotice FinalDecisionNotice { get; set; } = null!;
    [MaxLength(200)] public string TenderTitle { get; set; } = "";
    public string SupplierName { get; set; } = "";
    public int TenderId { get; set; }
    public DateTime AwardedAt { get; set; }
    public int OfficerNotificationId { get; set; }
    [DeleteBehavior(DeleteBehavior.Restrict)] public Notification OfficerNotification { get; set; } = null!;
    public int ManagerNotificationId { get; set; }
    [DeleteBehavior(DeleteBehavior.Restrict)] public Notification ManagerNotification { get; set; } = null!;
    [MaxLength(500)] public string? Summary { get; set; }
    public bool AiWritten { get; set; }
    public DateTime? OfficerEmailSentAt { get; set; }
    public DateTime? ManagerEmailSentAt { get; set; }
    public int EmailAttempts { get; set; }
    public DateTime? NextAttemptAt { get; set; }
}
