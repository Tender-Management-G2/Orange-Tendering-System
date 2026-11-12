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

        // Final award: set only after 4/4 Top Committee approval.
        // The committee's recommended bid is kept separately on TenderReport.RecommendedBidId.
        public int? AwardedBidId { get; set; }
        public Bid? AwardedBid { get; set; }
        public DateTime? AwardedAt { get; set; }

        public ICollection<TenderRound> Rounds { get; set; } = new List<TenderRound>();
        public ICollection<TenderItem> Items { get; set; } = new List<TenderItem>();
        public ICollection<CommitteeMember> CommitteeMembers { get; set; } = new List<CommitteeMember>();
        public ICollection<Document> Documents { get; set; } = new List<Document>();
    }
}
