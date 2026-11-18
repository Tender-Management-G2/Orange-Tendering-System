using System.ComponentModel.DataAnnotations;

namespace Orange_Tendering_System.Models.Entities
{
    // One table for every uploaded file. The file itself is stored outside wwwroot; only metadata is kept here.
    // Exactly one owner column should be set (enforced in application logic).
    public class Document
    {
        public int Id { get; set; }

        [Required, MaxLength(260)]
        public string FileName { get; set; } = string.Empty;

        [Required, MaxLength(500)]
        public string StoragePath { get; set; } = string.Empty;

        [Required, MaxLength(120)]
        public string ContentType { get; set; } = string.Empty;

        [MaxLength(64)]
        public string? ContentHash { get; set; }

        public long SizeBytes { get; set; }

        // e.g. "CommercialRegistration", "TaxCertificate", "TechnicalProposal".
        [MaxLength(60)]
        public string? DocumentType { get; set; }

        public string UploadedByUserId { get; set; } = string.Empty;
        public ApplicationUser UploadedByUser { get; set; } = null!;
        public DateTime UploadedAt { get; set; }

        public DateTime? ExpiresAt { get; set; }

        // Owner (one of these).
        public int? SupplierId { get; set; }
        public Supplier? Supplier { get; set; }

        public int? PurchaseRequestId { get; set; }
        public PurchaseRequest? PurchaseRequest { get; set; }

        public int? TenderId { get; set; }
        public Tender? Tender { get; set; }

        public int? BidId { get; set; }
        public Bid? Bid { get; set; }

        public int? ClarificationMessageId { get; set; }
        public ClarificationMessage? ClarificationMessage { get; set; }

        public int? TenderReportId { get; set; }
        public TenderReport? TenderReport { get; set; }

        // Verification (mainly Admin review of supplier legal documents).
        public DocumentVerificationStatus? VerificationStatus { get; set; }

        [MaxLength(1000)]
        public string? VerificationNotes { get; set; }
        public string? VerifiedByUserId { get; set; }
        public ApplicationUser? VerifiedByUser { get; set; }
        public DateTime? VerifiedAt { get; set; }
    }
}
