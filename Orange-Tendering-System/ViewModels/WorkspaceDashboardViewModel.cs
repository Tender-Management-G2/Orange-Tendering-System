namespace Orange_Tendering_System.ViewModels;

// Views accept verified, server-authorized content. Null means unavailable, not zero.
// These presentation models do not grant access or query procurement records.
public sealed class WorkspaceDashboardViewModel
{
    public bool DataConnected { get; init; }
    public string? DisplayName { get; init; }
    public IReadOnlyDictionary<string, int> Metrics { get; init; } = new Dictionary<string, int>();
    public IReadOnlyDictionary<string, decimal> Trends { get; init; } = new Dictionary<string, decimal>();
    public IReadOnlyList<DashboardTask> Tasks { get; init; } = [];
    public IReadOnlyList<DashboardActivity> Activities { get; init; } = [];
    public IReadOnlyList<DashboardInvitation> Invitations { get; init; } = [];
    public IReadOnlyList<DashboardStatus> Statuses { get; init; } = [];
    public DashboardTender? OngoingTender { get; init; }
    public int? CurrentWorkflowStage { get; init; }
    public string Number(string key) => Metrics.TryGetValue(key, out var value) ? value.ToString("N0") : "—";
}
public sealed record DashboardTask(string Title, string Context, string Status);
public sealed record DashboardActivity(string Title, string Context, string Time);
public sealed record DashboardInvitation(string Reference, string Title, string Deadline, string Status);
public sealed record DashboardStatus(string Label, int Count, string Color);
public sealed record DashboardTender(string Reference, string Title, string Status, int? Progress);
