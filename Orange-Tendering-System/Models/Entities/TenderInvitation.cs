using Microsoft.EntityFrameworkCore;

namespace Orange_Tendering_System.Models.Entities
{
    // A supplier can be invited once per round, and invited again in a later round.
    [Index(nameof(TenderRoundId), nameof(SupplierId), IsUnique = true)]
    public class TenderInvitation
    {
        public int Id { get; set; }

        public int TenderRoundId { get; set; }
        public TenderRound TenderRound { get; set; } = null!;

        public int SupplierId { get; set; }
        public Supplier Supplier { get; set; } = null!;

        // Ignoring an invitation leaves it Pending and creates no bid.
        public InvitationStatus Status { get; set; } = InvitationStatus.Pending;

        public string InvitedByUserId { get; set; } = string.Empty;
        public ApplicationUser InvitedByUser { get; set; } = null!;
        public DateTime InvitedAt { get; set; }
        public DateTime? RespondedAt { get; set; }

        // Bid versions for this invitation (1, 2, ...).
        public ICollection<Bid> Bids { get; set; } = new List<Bid>();
    }
}
