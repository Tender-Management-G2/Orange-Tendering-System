using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Orange_Tendering_System.Models.Entities
{
    // Final-decision relay: Top Committee approvals (3 of 4) -> assigned Sourcing Manager acknowledges and forwards ->
    // the single Sourcing Officer confirms and notifies suppliers. Suppliers are never notified before the last step.
    public enum FinalDecisionStatus { AwaitingManager, ForwardedToOfficer, SuppliersNotified }

    [Index(nameof(TenderId), nameof(TenderReportId), IsUnique = true)]
    public class FinalDecisionNotice
    {
        public int Id { get; set; }
        public int TenderId { get; set; }
        public Tender Tender { get; set; } = null!;
        public int? TenderReportId { get; set; }
        public FinalDecisionStatus Status { get; set; } = FinalDecisionStatus.AwaitingManager;
        public int TopApprovals { get; set; }
        public int TopVotes { get; set; }
        public DateTime CreatedAt { get; set; }

        public string? ManagerUserId { get; set; }
        public DateTime? ManagerAcknowledgedAt { get; set; }
        [MaxLength(1000)] public string? ManagerComment { get; set; }

        public string? OfficerUserId { get; set; }
        public DateTime? SuppliersNotifiedAt { get; set; }

        [Timestamp] public byte[]? RowVersion { get; set; }
        public ICollection<SupplierResultDelivery> Deliveries { get; set; } = new List<SupplierResultDelivery>();
    }

    // One row per supplier user who was told the result: the in-app Notification it created and the e-mail delivery state.
    // The unique index makes a second "Notify Suppliers" (double click, replay, retry) unable to duplicate a notification.
    [Index(nameof(FinalDecisionNoticeId), nameof(RecipientUserId), IsUnique = true)]
    public class SupplierResultDelivery
    {
        public int Id { get; set; }
        public int FinalDecisionNoticeId { get; set; }
        public FinalDecisionNotice FinalDecisionNotice { get; set; } = null!;
        public int TenderId { get; set; }
        public int SupplierId { get; set; }
        public int BidId { get; set; }
        [Required] public string RecipientUserId { get; set; } = string.Empty;
        [Required, MaxLength(20)] public string Outcome { get; set; } = string.Empty;        // Won | Unsuccessful
        public int? NotificationId { get; set; }
        [Required, MaxLength(20)] public string InAppStatus { get; set; } = "Delivered";
        [Required, MaxLength(20)] public string EmailStatus { get; set; } = "NotConfigured"; // NotConfigured | Sent | Failed
        public int EmailAttempts { get; set; }
        [MaxLength(500)] public string? LastError { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? EmailSentAt { get; set; }
    }
}
