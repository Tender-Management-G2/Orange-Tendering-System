namespace Orange_Tendering_System.ViewModels;

public sealed class RankingSnapshotRow
{
    public int Rank { get; init; }
    public string Supplier { get; init; } = string.Empty;
    public decimal FinalScore { get; init; }
    public bool Eligible { get; init; }
}
