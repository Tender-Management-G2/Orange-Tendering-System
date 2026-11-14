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

        // Populated for electronic approvals of an Executive Summary. VotedAt is the
        // signature timestamp; these fields bind that approval to one immutable report PDF.
        public int? SignatureReportId { get; set; }
        public TenderReport? SignatureReport { get; set; }

        [MaxLength(64)]
        public string? SignatureDocumentHash { get; set; }

        // Sanitized vector ink captured from the authenticated member's signing pad.
        public string? ElectronicSignatureSvg { get; set; }
    }
}
