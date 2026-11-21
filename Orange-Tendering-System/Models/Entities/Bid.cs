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

        // Extra costs on top of the item prices. The bid total is calculated, not stored. A cost that does not apply (or is
        // already included in the unit prices) is zero. Money keeps three decimals (JOD fils).
        [Precision(19, 3)]
        public decimal ShippingCost { get; set; }
        [Precision(19, 3)]
        public decimal InstallationCost { get; set; }
        [Precision(19, 3)]
        public decimal MaintenanceCost { get; set; }
        [MaxLength(1000)]
        public string? MaintenanceDescription { get; set; }
        [Precision(19, 3)]
        public decimal OtherCost { get; set; }
        [MaxLength(500)]
        public string? OtherCostDescription { get; set; }

        // Total tax in the offer currency, always calculated/validated by the server. TaxRate is set when the supplier chose
        // "one rate on the taxable total"; it is null when the supplier stated a total tax amount (mixed rates, exemptions).
        [Precision(19, 3)]
        public decimal TaxAmount { get; set; }
        [Precision(5, 2)]
        public decimal? TaxRate { get; set; }
        // With a rate: true when the supplier states that the additional charges are taxable too (otherwise items only).
        public bool TaxIncludesCharges { get; set; }

        // Warranty and technical terms (optional where they do not apply, e.g. pure services).
        public int? WarrantyMonths { get; set; }
        [MaxLength(60)]
        public string? WarrantyType { get; set; }
        [MaxLength(2000)]
        public string? WarrantyTerms { get; set; }
        [MaxLength(4000)]
        public string? Deviations { get; set; }
        [MaxLength(2000)]
        public string? AfterSalesSupport { get; set; }

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
