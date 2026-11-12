using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Orange_Tendering_System.Models.Entities
{
    // One row per submission of a purchase request, holding the Sourcing Officer's decision.
    // Returning and resubmitting creates a new row, so the history is kept.
    [Index(nameof(PurchaseRequestId), nameof(SubmissionNumber), IsUnique = true)]
    public class PurchaseRequestReview
    {
        public int Id { get; set; }

        public int PurchaseRequestId { get; set; }
        public PurchaseRequest PurchaseRequest { get; set; } = null!;

        public int SubmissionNumber { get; set; }
        public DateTime SubmittedAt { get; set; }

        // Copy of the request as submitted (title, justification, priority, needed-by date and all items
        // with quantities and specifications), so earlier submissions can still be shown after the employee edits the request.
        [Required]
        public string SubmittedSnapshotJson { get; set; } = string.Empty;

        public ReviewDecision Decision { get; set; } = ReviewDecision.Pending;

        [MaxLength(2000)]
        public string? Comment { get; set; }

        public string? ReviewedByUserId { get; set; }
        public ApplicationUser? ReviewedByUser { get; set; }
        public DateTime? ReviewedAt { get; set; }
    }
}
