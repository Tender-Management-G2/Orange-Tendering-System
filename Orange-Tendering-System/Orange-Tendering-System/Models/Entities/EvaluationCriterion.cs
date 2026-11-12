using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Orange_Tendering_System.Models.Entities
{
    // A technical or financial criterion confirmed by the Evaluation Committee for one evaluation round.
    // Weight and MaxScore stay null until the committee sets them; no formula is hardcoded.
    public class EvaluationCriterion
    {
        public int Id { get; set; }

        public int CommitteeRoundId { get; set; }
        public CommitteeRound CommitteeRound { get; set; } = null!;

        [Required, MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(1000)]
        public string? Description { get; set; }

        public CriterionType Type { get; set; }

        [Precision(9, 4)]
        public decimal? Weight { get; set; }

        [Precision(9, 4)]
        public decimal? MaxScore { get; set; }

        public int DisplayOrder { get; set; }

        public ICollection<EvaluationScore> Scores { get; set; } = new List<EvaluationScore>();
    }
}
