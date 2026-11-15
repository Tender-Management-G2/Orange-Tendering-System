using System.Security.Claims;
using Orange_Tendering_System.Models;

namespace Orange_Tendering_System.Areas.TopCommittee.Services;

public static class TopExecutiveRoles
{
    public const string Allowed = SystemRoles.TopCommittee + ",CEO,CFO,CTO,COO,Chief Executive Officer (CEO),Chief Financial Officer (CFO),Chief Technology Officer (CTO),Chief Operations Officer (COO),Chief Executive Officer,Chief Financial Officer,Chief Technology Officer,Chief Operations Officer";
    public static string? Title(IEnumerable<string> roles)
    {
        var positions = roles.Select(role => role switch
        {
            "CEO" or "Chief Executive Officer (CEO)" or "Chief Executive Officer" => "CEO",
            "CFO" or "Chief Financial Officer (CFO)" or "Chief Financial Officer" => "CFO",
            "CTO" or "Chief Technology Officer (CTO)" or "Chief Technology Officer" => "CTO",
            "COO" or "Chief Operations Officer (COO)" or "Chief Operations Officer" => "COO",
            _ => null
        }).Where(title => title != null).Distinct().ToList();
        return positions.Count == 1 ? positions[0] : null;
    }
    public static bool IsMember(ClaimsPrincipal user) => Allowed.Split(',').Any(user.IsInRole);
}
