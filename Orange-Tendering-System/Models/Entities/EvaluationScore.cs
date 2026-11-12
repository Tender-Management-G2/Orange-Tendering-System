using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Orange_Tendering_System.Models.Entities
{
    // One member's raw score for one bid on one criterion.
    [Index(nameof(BidId), nameof(EvaluationCriterionId), nameof(CommitteeMemberId), IsUnique = true)]
    public class EvaluationScore
    {
        public int Id { get; set; }

        public int BidId { get; set; }
        public Bid Bid { get; set; } = null!;

        public int EvaluationCriterionId { get; set; }
        public EvaluationCriterion EvaluationCriterion { get; set; } = null!;

        public int CommitteeMemberId { get; set; }
        public CommitteeMember CommitteeMember { get; set; } = null!;

        [Precision(9, 4)]
        public decimal Score { get; set; }

        [MaxLength(2000)]
        public string? Comment { get; set; }

        public DateTime ScoredAt { get; set; }
    }
}
