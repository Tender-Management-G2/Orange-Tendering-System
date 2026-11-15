namespace Orange_Tendering_System.Models;

public static class CommitteeAccountSeats
{
    public const string ClaimType = "orange:committee-account-seat";
    public static readonly CommitteeAccountSeat[] All =
    [
        new("evaluation-manager", "Evaluation", "Sourcing Manager", "committee member", SystemRoles.SourcingManager),
        new("evaluation-technical-1", "Evaluation", "Technical", "technical representative", SystemRoles.Technical),
        new("evaluation-financial", "Evaluation", "Finance", "financial representative", SystemRoles.Finance),
        new("evaluation-department", "Evaluation", "Department Representative", "Department representative on the Evaluation Committee", SystemRoles.DepartmentRepresentative),
        new("top-ceo", "Top", "Chief Executive Officer", "CEO member", SystemRoles.ChiefExecutiveOfficer),
        new("top-cfo", "Top", "Chief Financial Officer", "CFO member", SystemRoles.ChiefFinancialOfficer),
        new("top-cto", "Top", "Chief Technology Officer", "CTO member", SystemRoles.ChiefTechnologyOfficer),
        new("top-coo", "Top", "Chief Operations Officer", "COO member", SystemRoles.ChiefOperationsOfficer)
    ];
    public static CommitteeAccountSeat? Find(string? key) => All.SingleOrDefault(x => x.Key == key);
}
public sealed record CommitteeAccountSeat(string Key, string Committee, string Title, string Description, string Role);
