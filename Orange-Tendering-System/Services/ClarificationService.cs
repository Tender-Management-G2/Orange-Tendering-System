using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;

namespace Orange_Tendering_System.Services;

// The single source of truth for the Role-Based Clarification Chat (human to human, separate from the AI Chat Assistant).
// Allowed conversations (both directions):
//   Requesting Department <-> Sourcing Officer <-> Supplier,  Sourcing Officer <-> Evaluation Committee <-> Sourcing Manager <-> Top Committee.
// Every other pair is refused, and each thread must be linked to a record both people legitimately belong to
// (purchase request of the department, bid of the supplier, tender the committee members are assigned to).
public static class ClarificationPolicy
{
    private static readonly (string A, string B)[] Pairs =
    [
        (SystemRoles.RequestingDepartment, SystemRoles.SourcingOfficer),
        (SystemRoles.SourcingOfficer, SystemRoles.Supplier),
        (SystemRoles.SourcingOfficer, SystemRoles.EvaluationCommittee),
        (SystemRoles.EvaluationCommittee, SystemRoles.SourcingManager),
        (SystemRoles.SourcingManager, SystemRoles.TopCommittee)
    ];
    private static readonly string[] Order = [SystemRoles.RequestingDepartment, SystemRoles.SourcingOfficer, SystemRoles.EvaluationCommittee, SystemRoles.SourcingManager, SystemRoles.TopCommittee, SystemRoles.Supplier];

    public static bool Allowed(string? a, string? b) => a != null && b != null && Pairs.Any(p => p.A == a && p.B == b || p.A == b && p.B == a);
    public static string? ChatRole(IEnumerable<string> roles) => Order.FirstOrDefault(roles.Contains);
    public static string Label(ClarificationStatus s) => s switch { ClarificationStatus.Open => "Pending", ClarificationStatus.Answered => "Answered", _ => "Resolved" };
}

public sealed record ClarificationOption(string RecipientId, string Recipient, string Role, string ContextType, int ContextId, string Context)
{
    public string Value => $"{RecipientId}|{ContextType}|{ContextId}";
}

public sealed class ClarificationService(ApplicationDbContext db, UserManager<ApplicationUser> users, TimeProvider clock)
{
    private async Task<(ApplicationUser User, string Role)> Actor(ClaimsPrincipal principal)
    {
        var user = await users.GetUserAsync(principal);
        if (user is not { IsActive: true }) throw new WorkflowException("An active authenticated account is required.");
        var role = ClarificationPolicy.ChatRole(await users.GetRolesAsync(user));
        if (role == null) throw new WorkflowException("Your account has no clarification role.");
        return (user, role);
    }

    private async Task<List<ApplicationUser>> ActiveInRole(string role) => (await users.GetUsersInRoleAsync(role)).Where(u => u.IsActive).ToList();
    private static string Name(ApplicationUser u) => string.IsNullOrWhiteSpace(u.FullName) ? u.Email ?? u.UserName ?? u.Id : u.FullName;

    // Everyone the caller may start a conversation with, together with the record that conversation would be about.
    public async Task<List<ClarificationOption>> Options(ClaimsPrincipal principal)
    {
        var (me, role) = await Actor(principal);
        var result = new List<ClarificationOption>();
        void Add(ApplicationUser u, string r, string type, int id, string label) { if (u.Id != me.Id && u.IsActive) result.Add(new(u.Id, Name(u), r, type, id, label)); }
        async Task<List<(ApplicationUser User, int TenderId)>> Seats(CommitteeType type, string requiredRole, IEnumerable<int> tenderIds, CommitteeResponsibility? responsibility = null)
        {
            var ids = tenderIds.ToList();
            var rows = await db.CommitteeMembers.AsNoTracking().Include(m => m.User)
                .Where(m => ids.Contains(m.TenderId) && m.CommitteeType == type && (responsibility == null ? m.Responsibility != CommitteeResponsibility.SourcingManager : m.Responsibility == responsibility)).ToListAsync();
            var list = new List<(ApplicationUser, int)>();
            foreach (var m in rows) if (m.User.IsActive && await users.IsInRoleAsync(m.User, requiredRole)) list.Add((m.User, m.TenderId));
            return list;
        }
        async Task<Dictionary<int, string>> Titles(IEnumerable<int> ids) { var l = ids.Distinct().ToList(); return await db.Tenders.AsNoTracking().Where(t => l.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Title); }

        switch (role)
        {
            case SystemRoles.RequestingDepartment:
            {
                var requests = await db.PurchaseRequests.AsNoTracking().Where(p => p.DepartmentId == me.DepartmentId && p.Status != PurchaseRequestStatus.Draft).ToListAsync();
                foreach (var officer in await ActiveInRole(SystemRoles.SourcingOfficer)) foreach (var p in requests) Add(officer, "Sourcing Officer", "PurchaseRequest", p.Id, "Request: " + p.Title);
                break;
            }
            case SystemRoles.SourcingOfficer:
            {
                foreach (var p in await db.PurchaseRequests.AsNoTracking().Include(p => p.CreatedByUser).Where(p => p.Status != PurchaseRequestStatus.Draft).ToListAsync())
                    if (p.CreatedByUser.IsActive && await users.IsInRoleAsync(p.CreatedByUser, SystemRoles.RequestingDepartment)) Add(p.CreatedByUser, "Requesting Department", "PurchaseRequest", p.Id, "Request: " + p.Title);
                foreach (var b in await db.Bids.AsNoTracking().Where(b => b.Status == BidStatus.Submitted).Select(b => new { b.Id, b.TenderInvitation.SupplierId, Title = b.TenderInvitation.TenderRound.Tender.Title }).ToListAsync())
                    foreach (var s in await db.Users.Where(u => u.SupplierId == b.SupplierId && u.IsActive).ToListAsync()) Add(s, "Supplier", "Bid", b.Id, "Offer on: " + b.Title);
                var tenderIds = await db.Tenders.AsNoTracking().Select(t => t.Id).ToListAsync(); var titles = await Titles(tenderIds);
                foreach (var (u, t) in await Seats(CommitteeType.Evaluation, SystemRoles.EvaluationCommittee, tenderIds)) Add(u, "Evaluation Committee", "Tender", t, "Tender: " + titles[t]);
                break;
            }
            case SystemRoles.EvaluationCommittee:
            {
                var mine = await db.CommitteeMembers.AsNoTracking().Where(m => m.UserId == me.Id && m.CommitteeType == CommitteeType.Evaluation).Select(m => m.TenderId).ToListAsync();
                var titles = await Titles(mine);
                foreach (var officer in await ActiveInRole(SystemRoles.SourcingOfficer)) foreach (var t in mine) Add(officer, "Sourcing Officer", "Tender", t, "Tender: " + titles[t]);
                foreach (var (u, t) in await Seats(CommitteeType.Evaluation, SystemRoles.SourcingManager, mine, CommitteeResponsibility.SourcingManager)) Add(u, "Sourcing Manager", "Tender", t, "Tender: " + titles[t]);
                break;
            }
            case SystemRoles.SourcingManager:
            {
                var mine = await db.CommitteeMembers.AsNoTracking().Where(m => m.UserId == me.Id && m.CommitteeType == CommitteeType.Evaluation && m.Responsibility == CommitteeResponsibility.SourcingManager).Select(m => m.TenderId).ToListAsync();
                var titles = await Titles(mine);
                foreach (var (u, t) in await Seats(CommitteeType.Evaluation, SystemRoles.EvaluationCommittee, mine)) Add(u, "Evaluation Committee", "Tender", t, "Tender: " + titles[t]);
                foreach (var (u, t) in await Seats(CommitteeType.Top, SystemRoles.TopCommittee, mine)) Add(u, "Top Committee", "Tender", t, "Tender: " + titles[t]);
                break;
            }
            case SystemRoles.TopCommittee:
            {
                var mine = await db.CommitteeMembers.AsNoTracking().Where(m => m.UserId == me.Id && m.CommitteeType == CommitteeType.Top).Select(m => m.TenderId).ToListAsync();
                var titles = await Titles(mine);
                foreach (var (u, t) in await Seats(CommitteeType.Evaluation, SystemRoles.SourcingManager, mine, CommitteeResponsibility.SourcingManager)) Add(u, "Sourcing Manager", "Tender", t, "Tender: " + titles[t]);
                break;
            }
            // Suppliers start their conversations from their own portal pages (Supplier area), always with the Officer.
        }
        return result;
    }

    public async Task<int> CreateAsync(ClaimsPrincipal principal, string recipientId, string contextType, int contextId, string subject, string message)
    {
        var (me, _) = await Actor(principal);
        subject = subject?.Trim() ?? ""; message = message?.Trim() ?? "";
        if (subject.Length is 0 or > 200 || message.Length is 0 or > 10000) throw new WorkflowException("Write a subject (up to 200 characters) and a message (up to 10,000 characters).");
        var allowed = (await Options(principal)).Any(o => o.RecipientId == recipientId && o.ContextType == contextType && o.ContextId == contextId);
        if (!allowed) throw new WorkflowException("You are not allowed to start this conversation. Clarifications follow the approved role chain only.");
        var now = clock.GetUtcNow().UtcDateTime;
        var thread = new Clarification { FromUserId = me.Id, ToUserId = recipientId, Subject = subject, Status = ClarificationStatus.Open, CreatedAt = now };
        if (contextType == "PurchaseRequest") thread.PurchaseRequestId = contextId; else if (contextType == "Tender") thread.TenderId = contextId; else thread.BidId = contextId;
        thread.Messages.Add(new ClarificationMessage { SenderUserId = me.Id, Message = message, SentAt = now });
        db.Clarifications.Add(thread);
        await db.SaveChangesAsync();
        db.Notifications.Add(new Notification { UserId = recipientId, Title = "New clarification", Message = $"{Name(me)} opened: {subject}", Link = $"/Clarifications/Details/{thread.Id}", CreatedAt = now });
        db.AuditLogs.Add(new AuditLog { UserId = me.Id, EntityName = "Clarification", EntityId = thread.Id.ToString(), Action = "ClarificationOpened", CreatedAt = now });
        await db.SaveChangesAsync();
        return thread.Id;
    }

    // A thread may only continue while its two participants are still an allowed pair (role changes or deactivation end it).
    public async Task<bool> CanContinue(Clarification thread)
    {
        var from = await users.FindByIdAsync(thread.FromUserId); var to = await users.FindByIdAsync(thread.ToUserId);
        if (from is not { IsActive: true } || to is not { IsActive: true }) return false;
        return ClarificationPolicy.Allowed(ClarificationPolicy.ChatRole(await users.GetRolesAsync(from)), ClarificationPolicy.ChatRole(await users.GetRolesAsync(to)));
    }

    public async Task<bool> ResolveAsync(string userId, int id)
    {
        var thread = await db.Clarifications.SingleOrDefaultAsync(c => c.Id == id && (c.FromUserId == userId || c.ToUserId == userId) && c.Status != ClarificationStatus.Closed);
        if (thread == null) return false;
        thread.Status = ClarificationStatus.Closed; thread.ClosedAt = clock.GetUtcNow().UtcDateTime;
        db.AuditLogs.Add(new AuditLog { UserId = userId, EntityName = "Clarification", EntityId = id.ToString(), Action = "ClarificationResolved", CreatedAt = thread.ClosedAt.Value });
        await db.SaveChangesAsync();
        return true;
    }
}
