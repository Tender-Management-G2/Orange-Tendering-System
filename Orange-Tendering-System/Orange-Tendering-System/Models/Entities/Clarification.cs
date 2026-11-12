using System.ComponentModel.DataAnnotations;

namespace Orange_Tendering_System.Models.Entities
{
    // A clarification thread between two users, linked to a purchase request, a tender or a bid.
    // Who may message whom is enforced with roles, committee assignments and the current stage.
    public class Clarification
    {
        public int Id { get; set; }

        public int? PurchaseRequestId { get; set; }
        public PurchaseRequest? PurchaseRequest { get; set; }

        public int? TenderId { get; set; }
        public Tender? Tender { get; set; }

        public int? BidId { get; set; }
        public Bid? Bid { get; set; }

        public string FromUserId { get; set; } = string.Empty;
        public ApplicationUser FromUser { get; set; } = null!;

        public string ToUserId { get; set; } = string.Empty;
        public ApplicationUser ToUser { get; set; } = null!;

        [Required, MaxLength(200)]
        public string Subject { get; set; } = string.Empty;

        public ClarificationStatus Status { get; set; } = ClarificationStatus.Open;

        public DateTime CreatedAt { get; set; }
        public DateTime? ClosedAt { get; set; }

        public ICollection<ClarificationMessage> Messages { get; set; } = new List<ClarificationMessage>();
    }
}
