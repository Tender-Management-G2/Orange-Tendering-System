using Orange_Tendering_System.Models.Entities;

namespace Orange_Tendering_System.ViewModels;

public sealed class SourcingManagerReportsIndexViewModel
{
    public IReadOnlyList<TenderReport> OfficerReports { get; init; } = [];
    public IReadOnlyList<TopCommitteeResponseRow> TopCommitteeResponses { get; init; } = [];
    public IReadOnlySet<int> ForwardedReportIds { get; init; } = new HashSet<int>();
    public string? Search { get; init; }
    public string? Status { get; init; }
    public bool IsForwarding { get; init; }
}

public sealed record TopCommitteeResponseRow(
    int CommitteeRoundId,
    string TenderTitle,
    int ExecutiveSummaryVersion,
    CommitteeRoundStatus Decision,
    DateTime? DecidedAt);
