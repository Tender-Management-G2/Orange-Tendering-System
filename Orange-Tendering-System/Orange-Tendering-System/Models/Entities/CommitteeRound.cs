using Microsoft.EntityFrameworkCore;

namespace Orange_Tendering_System.Models.Entities
{
    // One review/voting cycle of the Evaluation or Top Committee. A rejection or resubmission
    // starts a new round, so earlier criteria, scores and votes are preserved.
    [Index(nameof(TenderRoundId), nameof(CommitteeType), nameof(RoundNumber), IsUnique = true)]
    public class CommitteeRound
    {
        public int Id { get; set; }

        public int TenderRoundId { get; set; }
        public TenderRound TenderRound { get; set; } = null!;

        public CommitteeType CommitteeType { get; set; }

        public int RoundNumber { get; set; }

        // The report version the committee is reviewing/voting on
        // (OfficerComparison for Evaluation, ExecutiveSummary for Top).
        public int? TenderReportId { get; set; }
        public TenderReport? TenderReport { get; set; }

        public CommitteeRoundStatus Status { get; set; } = CommitteeRoundStatus.InProgress;

        // Optional technical/financial split decided by the Evaluation Committee. Not fixed by the system.
        [Precision(9, 4)]
        public decimal? TechnicalWeight { get; set; }
        [Precision(9, 4)]
        public decimal? FinancialWeight { get; set; }

        public DateTime StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }

        public ICollection<EvaluationCriterion> Criteria { get; set; } = new List<EvaluationCriterion>();
        public ICollection<CommitteeVote> Votes { get; set; } = new List<CommitteeVote>();
    }
}
