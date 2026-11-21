using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.ViewModels;

namespace Orange_Tendering_System.Services.Assistant;

// Who is asking, decided only from the authenticated principal and SQL Server, never from the model or the browser.
public sealed record AssistantUser(string Id, string DisplayName, string Role, string[] Roles, int? DepartmentId, string? DepartmentName, int? SupplierId)
{
    public bool Is(string role) => Role == role;
    public bool IsCommitteeRole => Role is SystemRoles.EvaluationCommittee or SystemRoles.SourcingManager or SystemRoles.TopCommittee;
    // Estimated tender values and spending are shown only to roles that already see them in their own workspace.
    public bool SeesFinancials => Role is SystemRoles.SourcingOfficer or SystemRoles.SourcingManager or SystemRoles.TopCommittee;
    public bool SeesAllDepartments => Role is SystemRoles.SourcingOfficer or SystemRoles.SourcingManager or SystemRoles.TopCommittee;
}

public sealed class AssistantAccess(ApplicationDbContext db)
{
    public static string? UserId(ClaimsPrincipal principal) => principal.FindFirstValue(ClaimTypes.NameIdentifier);

    // Same active-role rule as the global search (SearchController): the first internal workspace the user belongs to, else Supplier.
    public static string ActiveRole(ClaimsPrincipal principal)
    {
        if (principal.IsInRole(SystemRoles.Supplier)) return SystemRoles.Supplier;
        return WorkspaceNavigation.Workspaces.FirstOrDefault(w => principal.IsInRole(w.Role) && w.Area != "Supplier")?.Role ?? "";
    }

    public async Task<AssistantUser?> ResolveAsync(ClaimsPrincipal principal, CancellationToken ct = default)
    {
        var id = UserId(principal);
        if (id == null) return null;
        var row = await db.Users.AsNoTracking().Where(u => u.Id == id && u.IsActive)
            .Select(u => new { u.FullName, u.Email, u.DepartmentId, Department = u.Department != null ? u.Department.Name : null, u.SupplierId })
            .SingleOrDefaultAsync(ct);
        if (row == null) return null;
        var name = !string.IsNullOrWhiteSpace(row.FullName) ? row.FullName.Trim() : (row.Email ?? "").Split('@')[0];
        var roles = SystemRoles.All.Where(principal.IsInRole).ToArray();
        return new AssistantUser(id, name, ActiveRole(principal), roles, row.DepartmentId, row.Department, row.SupplierId);
    }

    // Row-level scope for tenders. Mirrors InternalDashboardService.SearchAsync: committee roles see only tenders they are seated on,
    // a department sees tenders created from its own requests, Admin and Supplier see no internal tender rows here.
    public IQueryable<Tender> Tenders(AssistantUser user)
    {
        var q = db.Tenders.AsNoTracking();
        return user.Role switch
        {
            SystemRoles.SourcingOfficer => q,
            SystemRoles.RequestingDepartment => user.DepartmentId == null ? q.Where(_ => false) : q.Where(t => t.PurchaseRequest.DepartmentId == user.DepartmentId),
            SystemRoles.EvaluationCommittee => q.Where(t => db.CommitteeMembers.Any(m => m.TenderId == t.Id && m.UserId == user.Id && m.CommitteeType == CommitteeType.Evaluation)),
            SystemRoles.SourcingManager => q.Where(t => db.CommitteeMembers.Any(m => m.TenderId == t.Id && m.UserId == user.Id && m.CommitteeType == CommitteeType.Evaluation && m.Responsibility == CommitteeResponsibility.SourcingManager)),
            SystemRoles.TopCommittee => q.Where(t => db.CommitteeMembers.Any(m => m.TenderId == t.Id && m.UserId == user.Id && m.CommitteeType == CommitteeType.Top)),
            _ => q.Where(_ => false)
        };
    }

    // Purchase requests: a department sees its own; the Sourcing Officer sees submitted requests (not other people's drafts).
    public IQueryable<PurchaseRequest> Requests(AssistantUser user)
    {
        var q = db.PurchaseRequests.AsNoTracking();
        return user.Role switch
        {
            SystemRoles.RequestingDepartment => user.DepartmentId == null ? q.Where(_ => false) : q.Where(p => p.DepartmentId == user.DepartmentId),
            SystemRoles.SourcingOfficer => q.Where(p => p.Status != PurchaseRequestStatus.Draft || p.CreatedByUserId == user.Id),
            _ => q.Where(_ => false)
        };
    }

    public bool CanSeeSuppliers(AssistantUser user) => user.Role is SystemRoles.Admin or SystemRoles.SourcingOfficer;
}
