using Orange_Tendering_System.Services;

namespace Orange_Tendering_System.ViewModels;

public sealed class ReportsAnalyticsLandingViewModel
{
    public required ReportModel Dashboard { get; init; }
    public required List<(string Key, string Title, string Description)> Reports { get; init; }
}
