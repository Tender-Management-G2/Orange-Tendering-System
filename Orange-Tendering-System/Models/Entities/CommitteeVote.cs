using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Orange_Tendering_System.Models.Entities
{
    // One vote per member per committee round. 4/4 Approve is checked in business logic.
    [Index(nameof(CommitteeRoundId), nameof(CommitteeMemberId), IsUnique = true)]
    public class CommitteeVote
    {
        public int Id { get; set; }

        public int CommitteeRoundId { get; set; }
        public CommitteeRound CommitteeRound { get; set; } = null!;

        public int CommitteeMemberId { get; set; }
        public CommitteeMember CommitteeMember { get; set; } = null!;

        public VoteDecision Decision { get; set; }

        [MaxLength(2000)]
        public string? Comment { get; set; }

        public DateTime VotedAt { get; set; }
    }
}
