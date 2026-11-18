using Orange_Tendering_System.Models.Entities;

namespace Orange_Tendering_System.ViewModels;

public sealed class SourcingManagerAnalyticsViewModel
{
    public int SubmittedCount { get; init; }
    public int AcceptedCount { get; init; }
    public int ReturnedCount { get; init; }
    public int RejectedCount { get; init; }
    public int AwaitingTopCommitteeCount { get; init; }
    public int CompletedTopCommitteeCount { get; init; }
    public string? Search { get; init; }
    public string? Status { get; init; }
    public IReadOnlyList<SourcingManagerAnalyticsRow> Rows { get; init; } = [];
}

public sealed record SourcingManagerAnalyticsRow(
    int ReportId,
    string TenderTitle,
    int VersionNumber,
    string SubmittedBy,
    DateTime? SubmittedAt,
    ReportStatus Status,
    DateTime? ReviewedAt,
    CommitteeRoundStatus? TopCommitteeStatus,
    DateTime? TopCommitteeDecidedAt);
