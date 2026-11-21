using Orange_Tendering_System.Services;

namespace Orange_Tendering_System.ViewModels;

public sealed class HayaReportsAnalyticsLandingViewModel
{
    public required HayaReportModel Dashboard { get; init; }
    public required List<(string Key, string Title, string Description)> Reports { get; init; }
}
