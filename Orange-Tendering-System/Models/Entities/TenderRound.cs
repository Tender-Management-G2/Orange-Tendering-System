using Microsoft.EntityFrameworkCore;

namespace Orange_Tendering_System.Models.Entities
{
    // One tendering attempt. Retendering creates a new round with fresh invitations;
    // older rounds, their invitations and their bids stay recorded and are not counted again.
    [Index(nameof(TenderId), nameof(RoundNumber), IsUnique = true)]
    public class TenderRound
    {
        public int Id { get; set; }

        public int TenderId { get; set; }
        public Tender Tender { get; set; } = null!;

        // 1, 2, 3, ... A round normally needs 3 compliant suppliers; later rounds may proceed with 2 after 2 earlier failed attempts.
        public int RoundNumber { get; set; }

        public TenderRoundStatus Status { get; set; } = TenderRoundStatus.Draft;

        public DateTime? OpensAt { get; set; }
        public DateTime? Deadline { get; set; }
        public DateTime? ClosedAt { get; set; }

        public string CreatedByUserId { get; set; } = string.Empty;
        public ApplicationUser CreatedByUser { get; set; } = null!;
        public DateTime CreatedAt { get; set; }

        public ICollection<TenderInvitation> Invitations { get; set; } = new List<TenderInvitation>();
        public ICollection<CommitteeRound> CommitteeRounds { get; set; } = new List<CommitteeRound>();
        public ICollection<TenderReport> Reports { get; set; } = new List<TenderReport>();
    }
}
