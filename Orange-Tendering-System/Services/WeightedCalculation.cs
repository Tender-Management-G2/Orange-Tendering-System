namespace Orange_Tendering_System.Services;

public static class WeightedCalculation
{
    public static void Range(decimal value)
    {
        if (value < 0 || value > 100 || decimal.Round(value, 4) != value)
            throw new WorkflowException("Values must be between 0 and 100 with at most four decimal places.");
    }
    public static void Weights(decimal financial, decimal technical)
    {
        Range(financial); Range(technical);
        if (financial + technical != 100m) throw new WorkflowException("Financial + Technical must equal exactly 100%.");
    }
    public static (decimal Financial, decimal Technical) Average(IEnumerable<(decimal Financial, decimal Technical)> proposals)
    {
        var values = proposals.ToArray();
        if (values.Length != 4) throw new WorkflowException("All four proposals are required.");
        foreach (var p in values) Weights(p.Financial, p.Technical);
        return (values.Sum(x => x.Financial) / 4m, values.Sum(x => x.Technical) / 4m);
    }
    public static decimal Member(decimal financial, decimal technical, decimal financialWeight, decimal technicalWeight)
    {
        Range(financial); Range(technical);
        if (financialWeight < 0 || technicalWeight < 0 || financialWeight + technicalWeight != 100m)
            throw new WorkflowException("Approved weights must total 100%.");
        return (financial * financialWeight + technical * technicalWeight) / 100m;
    }
    public static decimal Final(IEnumerable<decimal> memberScores)
    {
        var scores = memberScores.ToArray();
        if (scores.Length != 4) throw new WorkflowException("Incomplete evaluation: all four scores are required; resolution policy is undefined.");
        return scores.Sum() / 4m;
    }
    public static IReadOnlyList<RankedBid> Rank(IEnumerable<(int BidId, decimal Score)> scores)
    {
        var rows = scores.OrderByDescending(x => x.Score).ToArray();
        return rows.Select(x => new RankedBid(x.BidId, x.Score, 1 + rows.Count(y => y.Score > x.Score),
            rows.Count(y => y.Score == x.Score) > 1)).ToArray();
    }
}
public record RankedBid(int BidId, decimal Score, int Rank, bool Tied);
public class WorkflowException(string message) : Exception(message);

// Explicit deployment configuration, never inferred from the user's management role.
public class TenderApprovalPolicy
{
    public string? Version { get; set; }
    public string[] WeightApproverUserIds { get; set; } = [];
    public string[] WeightApproverRoles { get; set; } = [];
    // Required: "All", "None", or "ThresholdOrCritical". An unset policy blocks award.
    public string? TopCommitteeRequirement { get; set; }
    public decimal? HighValueThreshold { get; set; }
    public string? ThresholdCurrency { get; set; }
    public int? TopQuorum { get; set; }
    public int? TopApprovalsRequired { get; set; }
    public bool? RejectVoteBlocksAward { get; set; }
    public string? TopTieOutcome { get; set; }
    public bool WeightConfigured => !string.IsNullOrWhiteSpace(Version) && (WeightApproverUserIds.Length > 0 || WeightApproverRoles.Length > 0);
    public bool FinalConfigured => !string.IsNullOrWhiteSpace(Version) &&
        (TopCommitteeRequirement == "None" ||
         ((TopCommitteeRequirement == "All" || (TopCommitteeRequirement == "ThresholdOrCritical" && HighValueThreshold > 0 && !string.IsNullOrWhiteSpace(ThresholdCurrency))) &&
          TopQuorum is >= 1 and <= 4 && TopApprovalsRequired is >= 1 and <= 4 && TopApprovalsRequired <= TopQuorum &&
          RejectVoteBlocksAward.HasValue && TopTieOutcome is "Block" or "Reject" or "Approve"));
}
