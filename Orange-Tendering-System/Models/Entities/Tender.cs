using System.ComponentModel.DataAnnotations;

namespace Orange_Tendering_System.Models.Entities
{
    public class Tender
    {
        public int Id { get; set; }

        public int PurchaseRequestId { get; set; }
        public PurchaseRequest PurchaseRequest { get; set; } = null!;

        [Required, MaxLength(200)]
        public string Title { get; set; } = string.Empty;

        [Required]
        public string Description { get; set; } = string.Empty;

        // Mandatory conditions/documents suppliers must meet (checked per bid during compliance review).
        public string? Requirements { get; set; }

        public TenderStatus Status { get; set; } = TenderStatus.Draft;

        public string CreatedByUserId { get; set; } = string.Empty;
        public ApplicationUser CreatedByUser { get; set; } = null!;
        public DateTime CreatedAt { get; set; }
        [Microsoft.EntityFrameworkCore.Precision(18, 2)]
        public decimal EstimatedValue { get; set; }
        [MaxLength(3)] public string Currency { get; set; } = "JOD";
        public bool IsCritical { get; set; }
        public DateTime? ClosedAt { get; set; }
        [Timestamp] public byte[] RowVersion { get; set; } = [];

        // Final award: set only after all approvals required by the frozen configured policy.
        // The committee's recommended bid is kept separately on TenderReport.RecommendedBidId.
        public int? AwardedBidId { get; set; }
        public Bid? AwardedBid { get; set; }
        public DateTime? AwardedAt { get; set; }
        public string? AwardedByUserId { get; set; }
        public ApplicationUser? AwardedByUser { get; set; }
        public string? AwardApprovalEvidenceJson { get; set; }

        public ICollection<TenderRound> Rounds { get; set; } = new List<TenderRound>();
        public ICollection<TenderItem> Items { get; set; } = new List<TenderItem>();
        public ICollection<CommitteeMember> CommitteeMembers { get; set; } = new List<CommitteeMember>();
        public ICollection<Document> Documents { get; set; } = new List<Document>();
    }
}
