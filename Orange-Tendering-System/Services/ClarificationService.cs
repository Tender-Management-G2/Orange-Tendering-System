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
    // The roles a chat role may talk to (for display; Allowed/CanContinue remain the enforcement).
    public static IReadOnlyList<string> Partners(string? role) => role == null ? [] : Pairs.Where(p => p.A == role || p.B == role).Select(p => p.A == role ? p.B : p.A).ToList();
    public static IReadOnlyList<(string A, string B)> AllPairs => Pairs;
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
        if (user is not { IsActive: true, EmailConfirmed: true }) throw new WorkflowException("An active authenticated account is required.");
        var role = ClarificationPolicy.ChatRole(await users.GetRolesAsync(user));
        if (role == null) throw new WorkflowException("Your account has no clarification role.");
        return (user, role);
    }

    private async Task<List<ApplicationUser>> ActiveInRole(string role) => (await users.GetUsersInRoleAsync(role)).Where(u => u.IsActive && u.EmailConfirmed).ToList();
    private static string Name(ApplicationUser u) => string.IsNullOrWhiteSpace(u.FullName) ? u.Email ?? u.UserName ?? u.Id : u.FullName;

    // Everyone the caller may start a conversation with, together with the record that conversation would be about.
    public async Task<List<ClarificationOption>> Options(ClaimsPrincipal principal)
    {
        var (me, role) = await Actor(principal);
        var result = new List<ClarificationOption>();
        void Add(ApplicationUser u, string r, string type, int id, string label) { if (u.Id != me.Id && u.IsActive && u.EmailConfirmed) result.Add(new(u.Id, Name(u), r, type, id, label)); }
        async Task<List<(ApplicationUser User, int TenderId)>> Seats(CommitteeType type, string requiredRole, IEnumerable<int> tenderIds, CommitteeResponsibility? responsibility = null)
        {
            var ids = tenderIds.ToList();
            var rows = await db.CommitteeMembers.AsNoTracking().Include(m => m.User)
                .Where(m => ids.Contains(m.TenderId) && m.CommitteeType == type && (responsibility == null ? m.Responsibility != CommitteeResponsibility.SourcingManager : m.Responsibility == responsibility)).ToListAsync();
            var list = new List<(ApplicationUser, int)>();
            foreach (var m in rows) if (m.User.IsActive && m.User.EmailConfirmed && await users.IsInRoleAsync(m.User, requiredRole)) list.Add((m.User, m.TenderId));
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
                // An invitation is already a shared record: the Officer may ask a supplier
                // for clarification before a bid is submitted, without inventing a bid context.
                var supplierUsers = (await ActiveInRole(SystemRoles.Supplier));
                var approvedSuppliers = await db.Suppliers.AsNoTracking().Where(s => s.Status == SupplierStatus.Approved).Select(s => s.Id).ToListAsync();
                foreach (var i in await db.TenderInvitations.AsNoTracking()
                    .Where(i => i.InvitedByUserId == me.Id && approvedSuppliers.Contains(i.SupplierId))
                    .Select(i => new { i.SupplierId, i.TenderRound.TenderId, Title = i.TenderRound.Tender.Title }).Distinct().ToListAsync())
                    foreach (var s in supplierUsers.Where(s => s.SupplierId == i.SupplierId))
                        Add(s, "Supplier", "Tender", i.TenderId, "Invited to: " + i.Title);
                foreach (var b in await db.Bids.AsNoTracking().Where(b => b.Status == BidStatus.Submitted && b.TenderInvitation.InvitedByUserId == me.Id && approvedSuppliers.Contains(b.TenderInvitation.SupplierId))
                    .Select(b => new { b.Id, b.TenderInvitation.SupplierId, Title = b.TenderInvitation.TenderRound.Tender.Title }).ToListAsync())
                    foreach (var s in supplierUsers.Where(s => s.SupplierId == b.SupplierId)) Add(s, "Supplier", "Bid", b.Id, "Offer on: " + b.Title);
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

    public sealed record PendingContact(string Id, string Name, string Role, string Reason);

    // Read-only, for display: people in the caller's approved partner roles who have no shared record with the caller yet,
    // with what is still missing. They cannot be messaged (Options/CreateAsync remain the only way to start a conversation).
    public async Task<List<PendingContact>> PendingContacts(ClaimsPrincipal principal, IEnumerable<ClarificationOption> available)
    {
        var (me, role) = await Actor(principal);
        var reachable = available.Select(o => o.RecipientId).ToHashSet();
        var approvedSuppliers = await db.Suppliers.AsNoTracking().Where(s => s.Status == SupplierStatus.Approved).Select(s => s.Id).ToListAsync();
        string Reason(string partner) => (role, partner) switch
        {
            (SystemRoles.RequestingDepartment, _) => "Available once your department submits a purchase request.",
            (SystemRoles.SourcingOfficer, SystemRoles.RequestingDepartment) => "Available once this department submits a purchase request.",
            (SystemRoles.SourcingOfficer, SystemRoles.Supplier) => "Available once you invite this supplier to a tender.",
            (SystemRoles.SourcingOfficer, SystemRoles.EvaluationCommittee) => "Available once this member is assigned to a tender's Evaluation Committee.",
            (SystemRoles.EvaluationCommittee, SystemRoles.SourcingOfficer) => "Available once you are assigned to a tender's Evaluation Committee.",
            _ => "Available once you are both assigned to the same tender."
        };
        var result = new List<PendingContact>();
        foreach (var partner in ClarificationPolicy.Partners(role))
            foreach (var u in (await ActiveInRole(partner)).Where(u => u.Id != me.Id && !reachable.Contains(u.Id)).OrderBy(Name))
            {
                if (partner == SystemRoles.Supplier && (u.SupplierId == null || !approvedSuppliers.Contains(u.SupplierId.Value))) continue;
                if (ClarificationPolicy.ChatRole(await users.GetRolesAsync(u)) != partner) continue;
                result.Add(new(u.Id, Name(u), partner, Reason(partner)));
                if (result.Count >= 60) return result;
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
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        // Recheck the context inside the same transaction that stores the thread, notice and audit.
        if (!(await Options(principal)).Any(o => o.RecipientId == recipientId && o.ContextType == contextType && o.ContextId == contextId))
            throw new WorkflowException("The conversation assignment changed. Reload before sending.");
        var now = clock.GetUtcNow().UtcDateTime;
        var thread = new Clarification { FromUserId = me.Id, ToUserId = recipientId, Subject = subject, Status = ClarificationStatus.Open, CreatedAt = now };
        if (contextType == "PurchaseRequest") thread.PurchaseRequestId = contextId; else if (contextType == "Tender") thread.TenderId = contextId; else thread.BidId = contextId;
        if (!await CanContinue(thread)) throw new WorkflowException("The recipient or record is no longer eligible for this conversation.");
        thread.Messages.Add(new ClarificationMessage { SenderUserId = me.Id, Message = message, SentAt = now });
        db.Clarifications.Add(thread);
        await db.SaveChangesAsync();
        db.Notifications.Add(new Notification { UserId = recipientId, Title = "New clarification", Message = $"{Name(me)} opened: {subject}", Link = $"/Clarifications/Details/{thread.Id}", CreatedAt = now });
        db.AuditLogs.Add(new AuditLog { UserId = me.Id, EntityName = "Clarification", EntityId = thread.Id.ToString(), Action = "ClarificationOpened", CreatedAt = now });
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return thread.Id;
    }

    // A thread may only continue while its two participants are still an allowed pair (role changes or deactivation end it).
    public async Task<bool> CanContinue(Clarification thread)
    {
        var from = await users.FindByIdAsync(thread.FromUserId); var to = await users.FindByIdAsync(thread.ToUserId);
        if (from is not { IsActive: true, EmailConfirmed: true } || to is not { IsActive: true, EmailConfirmed: true }) return false;
        var fromRole = ClarificationPolicy.ChatRole(await users.GetRolesAsync(from));
        var toRole = ClarificationPolicy.ChatRole(await users.GetRolesAsync(to));
        if (!ClarificationPolicy.Allowed(fromRole, toRole)) return false;
        var supplier = fromRole == SystemRoles.Supplier ? from : toRole == SystemRoles.Supplier ? to : null;
        if (supplier != null && !await db.Suppliers.AnyAsync(x => x.Id == supplier.SupplierId && x.Status == SupplierStatus.Approved)) return false;
        var officer = fromRole == SystemRoles.SourcingOfficer ? from : toRole == SystemRoles.SourcingOfficer ? to : null;
        if (thread.BidId.HasValue)
            return supplier != null && officer != null && thread.PurchaseRequestId == null && await db.Bids.AnyAsync(b => b.Id == thread.BidId && b.Status == BidStatus.Submitted &&
                b.TenderInvitation.SupplierId == supplier.SupplierId && b.TenderInvitation.InvitedByUserId == officer.Id &&
                (thread.TenderId == null || thread.TenderId == b.TenderInvitation.TenderRound.TenderId));
        if (thread.PurchaseRequestId.HasValue)
        {
            var department = fromRole == SystemRoles.RequestingDepartment ? from : toRole == SystemRoles.RequestingDepartment ? to : null;
            return thread.TenderId == null && officer != null && department?.DepartmentId != null &&
                await db.PurchaseRequests.AnyAsync(p => p.Id == thread.PurchaseRequestId && p.DepartmentId == department.DepartmentId);
        }
        if (fromRole == SystemRoles.RequestingDepartment || toRole == SystemRoles.RequestingDepartment) return false;
        if (thread.TenderId is not int tenderId) return false;
        if (supplier != null)
            return officer != null && await db.TenderInvitations.AnyAsync(i => i.TenderRound.TenderId == tenderId && i.SupplierId == supplier.SupplierId && i.InvitedByUserId == officer.Id);
        async Task<bool> Assigned(ApplicationUser u, string? role) => role == SystemRoles.SourcingOfficer || await db.CommitteeMembers.AnyAsync(m =>
            m.TenderId == tenderId && m.UserId == u.Id && (role == SystemRoles.TopCommittee ? m.CommitteeType == CommitteeType.Top :
                m.CommitteeType == CommitteeType.Evaluation && (role != SystemRoles.SourcingManager || m.Responsibility == CommitteeResponsibility.SourcingManager)));
        return await Assigned(from, fromRole) && await Assigned(to, toRole);
    }

    public async Task<bool> ReplyAsync(string userId, int id, string message)
    {
        message = message?.Trim() ?? "";
        if (message.Length is 0 or > 10000) return false;
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var thread = await db.Clarifications.FromSqlInterpolated($"SELECT * FROM Clarifications WITH (UPDLOCK,HOLDLOCK) WHERE Id={id}").SingleOrDefaultAsync();
        if (thread == null || thread.Status == ClarificationStatus.Closed || (thread.FromUserId != userId && thread.ToUserId != userId) || !await CanContinue(thread)) return false;
        var now = clock.GetUtcNow().UtcDateTime;
        db.ClarificationMessages.Add(new ClarificationMessage { ClarificationId = id, SenderUserId = userId, Message = message, SentAt = now });
        thread.Status = ClarificationStatus.Answered;
        db.Notifications.Add(new Notification { UserId = thread.FromUserId == userId ? thread.ToUserId : thread.FromUserId,
            Title = "Clarification reply", Message = $"{(await users.FindByIdAsync(userId) is { } sender ? Name(sender) : "The other participant")} replied: {thread.Subject}",
            Link = $"/Clarifications/Details/{id}", CreatedAt = now });
        db.AuditLogs.Add(new AuditLog { UserId = userId, EntityName = "Clarification", EntityId = id.ToString(), Action = "ClarificationReplied", CreatedAt = now });
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return true;
    }

    public async Task<bool> ResolveAsync(string userId, int id)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var thread = await db.Clarifications.FromSqlInterpolated($"SELECT * FROM Clarifications WITH (UPDLOCK,HOLDLOCK) WHERE Id={id}").SingleOrDefaultAsync();
        if (thread == null || thread.Status == ClarificationStatus.Closed || (thread.FromUserId != userId && thread.ToUserId != userId) || !await CanContinue(thread)) return false;
        thread.Status = ClarificationStatus.Closed; thread.ClosedAt = clock.GetUtcNow().UtcDateTime;
        db.AuditLogs.Add(new AuditLog { UserId = userId, EntityName = "Clarification", EntityId = id.ToString(), Action = "ClarificationResolved", CreatedAt = thread.ClosedAt.Value });
        db.Notifications.Add(new Notification { UserId = thread.FromUserId == userId ? thread.ToUserId : thread.FromUserId,
            Title = "Clarification resolved", Message = $"The conversation was resolved: {thread.Subject}", Link = $"/Clarifications/Details/{id}", CreatedAt = thread.ClosedAt.Value });
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return true;
    }
}
