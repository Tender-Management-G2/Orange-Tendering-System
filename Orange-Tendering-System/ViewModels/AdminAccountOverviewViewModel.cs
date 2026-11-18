namespace Orange_Tendering_System.ViewModels;

// Presentation-only sample data; never used for billing, access or accounting.
public sealed class AdminAccountOverviewViewModel
{
    public string Currency => "USD";
    public DateTime NextMaintenanceUtc { get; init; }
    public List<AdminDemoSubscription> Subscriptions { get; init; } = [];
    public int ActiveSubscriptions => Subscriptions.Sum(s => s.Accounts);
    public decimal MonthlyRevenue => Subscriptions.Sum(s => s.Accounts * s.MonthlyPrice);
    public static AdminAccountOverviewViewModel Demo() => new()
    {
        NextMaintenanceUtc = DateTime.UtcNow.Date.AddDays(7).AddHours(2),
        Subscriptions = [new("Starter", 12, 250m, "Monthly"), new("Business", 8, 500m, "Monthly"), new("Enterprise", 6, 900m, "Monthly")]
    };
}
public sealed record AdminDemoSubscription(string Plan, int Accounts, decimal MonthlyPrice, string BillingCycle);
