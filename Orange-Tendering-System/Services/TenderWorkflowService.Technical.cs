using System.Text.Json;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.ViewModels;

namespace Orange_Tendering_System.Services;

internal sealed record TechnicalCriterionScore(string Name, decimal Weight, decimal Score);

public partial class TenderWorkflowService
{
    private static TechnicalCriterionWeight[] ValidateTechnicalCriteria(IReadOnlyList<TechnicalCriterionInput> input)
    {
        Require(input.Count is >= 1 and <= 20, "Enter between one and twenty technical criteria.");
        Require(input.All(row => row.Name?.Trim().Length is >= 1 and <= 120
            && row.Weight is > 0m and <= 100m && decimal.Round(row.Weight.Value, 2) == row.Weight),
            "Enter a criterion name and a positive percentage with up to two decimal places.");
        var criteria = input.Select(row => new TechnicalCriterionWeight(row.Name!.Trim(), row.Weight!.Value)).ToArray();
        Require(criteria.Select(row => row.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() == criteria.Length,
            "Give each technical criterion a distinct name.");
        var remaining = 100m - criteria.Sum(row => row.Weight);
        Require(remaining == 0m, remaining > 0m ? $"Add {remaining:0.##}% to the technical criteria."
            : $"Reduce the technical criteria by {-remaining:0.##}%.");
        return criteria;
    }

    private static void ValidateEnteredScore(decimal score) =>
        Require(score is >= 0m and <= 99m && score == decimal.Truncate(score), "Enter a whole-number score from 0 to 99.");

    private static (decimal Score, string? Breakdown) CalculateTechnicalScore(
        IReadOnlyList<TechnicalCriterionWeight> criteria, ScoreInput input)
    {
        if (criteria.Count == 0)
        {
            Require(input.Technical.HasValue, "Supply a technical score, including a deliberate zero.");
            ValidateEnteredScore(input.Technical.Value);
            return (input.Technical.Value, null);
        }

        Require(input.TechnicalScores.Count == criteria.Count && input.TechnicalScores.All(score => score.HasValue),
            "Supply a score for every technical criterion.");
        var scores = criteria.Select((criterion, index) =>
        {
            var score = input.TechnicalScores[index]!.Value;
            ValidateEnteredScore(score);
            return new TechnicalCriterionScore(criterion.Name, criterion.Weight, score);
        }).ToArray();
        return (decimal.Round(scores.Sum(row => row.Score * row.Weight / 100m), 4, MidpointRounding.AwayFromZero),
            JsonSerializer.Serialize(scores));
    }
}
