using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Orange_Tendering_System.Models.Entities
{
    // One row per bid version. A submitted bid is never edited: resubmitting creates a new row
    // with the next RevisionNumber and the old one becomes Superseded.
    // The supplier is known through TenderInvitation.SupplierId, so "3 offers" = 3 distinct invitations in the round.
    [Index(nameof(TenderInvitationId), nameof(RevisionNumber), IsUnique = true)]
    public class Bid
    {
        public int Id { get; set; }

        public int TenderInvitationId { get; set; }
        public TenderInvitation TenderInvitation { get; set; } = null!;

        public int RevisionNumber { get; set; } = 1;

        public BidStatus Status { get; set; } = BidStatus.Draft;

        public string? TechnicalProposal { get; set; }

        [Required, MaxLength(3)]
        public string CurrencyCode { get; set; } = "JOD";

        public int? DeliveryDays { get; set; }

        [MaxLength(1000)]
        public string? PaymentTerms { get; set; }

        public DateTime? ValidUntil { get; set; }

        // Extra costs on top of the item prices. The bid total is calculated, not stored.
        [Precision(18, 2)]
        public decimal ShippingCost { get; set; }
        [Precision(18, 2)]
        public decimal InstallationCost { get; set; }
        [Precision(18, 2)]
        public decimal MaintenanceCost { get; set; }
        [Precision(18, 2)]
        public decimal TaxAmount { get; set; }
        [Precision(18, 2)]
        public decimal OtherCost { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime? SubmittedAt { get; set; }

        // Sourcing Officer compliance review. A non-compliant bid still counts as received but cannot be recommended or awarded.
        // A resubmitted version is a new row, so each version keeps its own review.
        public ComplianceStatus ComplianceStatus { get; set; } = ComplianceStatus.NotChecked;

        // Reasons for non-compliance.
        [MaxLength(2000)]
        public string? ComplianceNotes { get; set; }

        // Missing documents or unmet tender requirements, e.g. "Tax certificate; 3-year warranty".
        [MaxLength(2000)]
        public string? MissingRequirements { get; set; }

        public string? ComplianceCheckedByUserId { get; set; }
        public ApplicationUser? ComplianceCheckedByUser { get; set; }
        public DateTime? ComplianceCheckedAt { get; set; }

        public ICollection<BidItem> Items { get; set; } = new List<BidItem>();
        public ICollection<Document> Documents { get; set; } = new List<Document>();
    }
}
