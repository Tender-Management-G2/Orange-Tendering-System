namespace Orange_Tendering_System.Models;

public static class SystemRoles
{
    public const string Admin = "Admin";
    public const string RequestingDepartment = "Requesting Department";
    public const string SourcingOfficer = "Sourcing Officer";
    public const string Supplier = "Supplier";
    public const string SourcingManager = "Sourcing Manager";
    // Internal workspace permissions retained for existing authorization and historical tenders.
    public const string EvaluationCommittee = "Evaluation Committee";
    public const string TopCommittee = "Top Committee";
    public const string ChiefExecutiveOfficer = "Chief Executive Officer";
    public const string ChiefFinancialOfficer = "Chief Financial Officer";
    public const string ChiefTechnologyOfficer = "Chief Technology Officer";
    public const string ChiefOperationsOfficer = "Chief Operations Officer";
    public const string Technical = "Technical";
    public const string Finance = "Finance";
    public const string DepartmentRepresentative = "Department Representative";

    public static readonly string[] PublicRoles = [Admin, RequestingDepartment, SourcingOfficer, Supplier, SourcingManager,
        ChiefExecutiveOfficer, ChiefFinancialOfficer, ChiefTechnologyOfficer, ChiefOperationsOfficer, Technical, Finance, DepartmentRepresentative];
    public static readonly string[] All = [.. PublicRoles, EvaluationCommittee, TopCommittee];
    public static string[] AccountRoles(string role) => role switch
    {
        ChiefExecutiveOfficer or ChiefFinancialOfficer or ChiefTechnologyOfficer or ChiefOperationsOfficer => [role, TopCommittee],
        Technical or Finance or DepartmentRepresentative => [role, EvaluationCommittee],
        _ => [role]
    };
    public static string[] DisplayRoles(IEnumerable<string?> roles) => roles.Where(r => r != null && PublicRoles.Contains(r)).Cast<string>().Distinct().Order().ToArray();
    public static string RoleText(IEnumerable<string?> roles)
    {
        var actual = roles.ToArray();
        var visible = DisplayRoles(actual);
        return visible.Length > 0 ? string.Join(", ", visible) : actual.Any(r => r is EvaluationCommittee or TopCommittee)
            ? "Committee account — select a job role" : "No Role";
    }
    public static string[] RolesToRemove(IEnumerable<string> current, string role)
    {
        var assigned = current.ToHashSet(StringComparer.Ordinal);
        var needed = assigned.Where(r => r != role && PublicRoles.Contains(r)).SelectMany(AccountRoles).ToHashSet(StringComparer.Ordinal);
        return AccountRoles(role).Where(r => assigned.Contains(r) && (r == role || !needed.Contains(r))).ToArray();
    }
}
