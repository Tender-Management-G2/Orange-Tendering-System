using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Areas.TopCommittee.Services;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models;

namespace Orange_Tendering_System.Services;

public sealed class ReportAccessService(ApplicationDbContext db)
{
    private static readonly Dictionary<string, string[]> Audience = new()
    {
        ["purchase-requests"] = [SystemRoles.SourcingOfficer, SystemRoles.SourcingManager, SystemRoles.TopCommittee],
        ["tenders"] = [SystemRoles.SourcingOfficer, SystemRoles.SourcingManager, SystemRoles.TopCommittee],
        ["suppliers"] = [SystemRoles.SourcingOfficer, SystemRoles.SourcingManager, SystemRoles.TopCommittee],
        ["evaluations"] = [SystemRoles.SourcingOfficer, SystemRoles.SourcingManager, SystemRoles.TopCommittee],
        ["governance"] = [SystemRoles.SourcingManager, SystemRoles.TopCommittee],
        ["management"] = [SystemRoles.TopCommittee]
    };

    public static IReadOnlyList<string> RolesFor(string key) => Audience.GetValueOrDefault(key) ?? [];

    private static bool HasRole(ClaimsPrincipal user, string role) =>
        role == SystemRoles.TopCommittee ? TopExecutiveRoles.IsMember(user) : user.IsInRole(role);

    public async Task<bool> CanReadAsync(string key, ClaimsPrincipal user, CancellationToken ct = default)
    {
        if (!Audience.ContainsKey(key) || user.IsInRole(SystemRoles.Supplier)) return false;
        if (user.IsInRole(SystemRoles.Admin)) return true;
        return (await ReaderRolesAsync(key, user, ct)).Count > 0;
    }

    public async Task<IReadOnlyList<string>> ReaderRolesAsync(string key, ClaimsPrincipal user, CancellationToken ct)
    {
        if (user.IsInRole(SystemRoles.Supplier)) return [];
        var roles = RolesFor(key).Where(role => HasRole(user, role)).ToArray();
        if (roles.Length == 0) return [];
        var blocked = await db.ReportRoleAccess.AsNoTracking().Where(r => r.ReportKey == key && roles.Contains(r.Role) && !r.Allowed)
            .Select(r => r.Role).ToListAsync(ct);
        return roles.Except(blocked).ToArray();
    }

    public async Task<HashSet<string>> AvailableAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        if (user.IsInRole(SystemRoles.Supplier)) return [];
        if (user.IsInRole(SystemRoles.Admin)) return Audience.Keys.ToHashSet();
        var overrides = await ReadOverridesAsync(ct);
        return Audience.Where(r => r.Value.Any(role => HasRole(user, role) && overrides.GetValueOrDefault((r.Key, role), true)))
            .Select(r => r.Key).ToHashSet();
    }

    public Task<Dictionary<(string ReportKey, string Role), bool>> ReadOverridesAsync(CancellationToken ct) =>
        db.ReportRoleAccess.AsNoTracking().ToDictionaryAsync(r => (r.ReportKey, r.Role), r => r.Allowed, ct);
}
