using System.Data;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.ViewModels;

namespace Orange_Tendering_System.Services;

public sealed class SupplierCommunicationService(ApplicationDbContext context)
{
    public IQueryable<ApplicationUser> Accounts(string userId) => context.Users.Where(u =>
        u.Id == userId && u.IsActive && u.Supplier != null && u.Supplier.Status == SupplierStatus.Approved);

    private IQueryable<string> Officers() => context.UserRoles.Where(ur => context.Roles
        .Any(r => r.Id == ur.RoleId && r.Name == SystemRoles.SourcingOfficer)).Select(ur => ur.UserId);

    // مورد مسجل في المحادثة، وموظف مرتبط بنفس المعاملة؛ لا تواصل مع اللجان أو المدير.
    public IQueryable<Clarification> Threads(string userId) => context.Clarifications.Where(c =>
        c.PurchaseRequestId == null && (c.FromUserId == userId || c.ToUserId == userId)
        && Officers().Contains(c.FromUserId == userId ? c.ToUserId : c.FromUserId)
        && Accounts(userId).Any(u =>
            (c.Bid != null && c.Bid.TenderInvitation.SupplierId == u.SupplierId
                && (c.TenderId == null || c.TenderId == c.Bid.TenderInvitation.TenderRound.TenderId)
                && c.Bid.TenderInvitation.InvitedByUserId == (c.FromUserId == userId ? c.ToUserId : c.FromUserId))
            || (c.BidId == null && c.TenderId != null && context.TenderInvitations.Any(i =>
                i.SupplierId == u.SupplierId && i.TenderRound.TenderId == c.TenderId
                && i.InvitedByUserId == (c.FromUserId == userId ? c.ToUserId : c.FromUserId)))));

    public async Task<List<SupplierClarificationViewModel>> GetThreadsAsync(string userId) =>
        await Threads(userId).AsNoTracking().OrderByDescending(c => c.CreatedAt)
            .Select(c => new SupplierClarificationViewModel
            {
                Id = c.Id, Subject = c.Subject,
                Status = c.Status == ClarificationStatus.Open ? "Pending" : c.Status == ClarificationStatus.Closed ? "Resolved" : "Answered",
                CanReply = c.Status != ClarificationStatus.Closed
            }).ToListAsync();

    public async Task<SupplierClarificationViewModel?> GetThreadAsync(string userId, int id)
    {
        var thread = await Threads(userId).AsNoTracking().Include(c => c.Messages).ThenInclude(m => m.Documents)
            .SingleOrDefaultAsync(c => c.Id == id);
        if (thread == null) return null;
        return new SupplierClarificationViewModel
        {
            Id = id, Subject = thread.Subject, CanReply = thread.Status != ClarificationStatus.Closed,
            Status = thread.Status == ClarificationStatus.Open ? "Pending" : thread.Status == ClarificationStatus.Closed ? "Resolved" : "Answered",
            Messages = thread.Messages.OrderBy(m => m.SentAt).ThenBy(m => m.Id).Select(m => new SupplierMessageViewModel(
                m.Id, m.SenderUserId == userId ? "You" : "Sourcing Officer", m.Message, m.SentAt,
                m.Documents.Select(d => new SupplierDocumentViewModel
                { Id = d.Id, FileName = d.FileName, UploadedAt = d.UploadedAt, SizeBytes = d.SizeBytes }).ToList())).ToList()
        };
    }

    public async Task<bool> CanCreateAsync(string userId, int? bidId, int? invitationId = null)
    {
        if (bidId.HasValue == invitationId.HasValue) return false;
        if (bidId.HasValue)
            return await context.Bids.AnyAsync(b => b.Id == bidId && b.Status == BidStatus.Submitted
                && Accounts(userId).Any(u => u.SupplierId == b.TenderInvitation.SupplierId)
                && Officers().Contains(b.TenderInvitation.InvitedByUserId));
        var now = DateTime.UtcNow;
        return await context.TenderInvitations.AnyAsync(i => i.Id == invitationId
            && Accounts(userId).Any(u => u.SupplierId == i.SupplierId) && Officers().Contains(i.InvitedByUserId)
            && i.TenderRound.Status == TenderRoundStatus.Open && i.TenderRound.Tender.Status == TenderStatus.Bidding
            && i.TenderRound.Deadline > now && (!i.TenderRound.OpensAt.HasValue || i.TenderRound.OpensAt <= now));
    }

    public async Task<int?> CreateAsync(string userId, SupplierClarificationInputModel input)
    {
        if (!await CanCreateAsync(userId, input.BidId, input.InvitationId)) return null;
        var invitation = input.BidId.HasValue
            ? await context.Bids.Where(b => b.Id == input.BidId)
                .Select(b => new { b.TenderInvitation.InvitedByUserId, b.TenderInvitation.TenderRound.TenderId }).SingleAsync()
            : await context.TenderInvitations.Where(i => i.Id == input.InvitationId)
                .Select(i => new { i.InvitedByUserId, i.TenderRound.TenderId }).SingleAsync();
        var officerId = invitation.InvitedByUserId;
        var thread = new Clarification
        {
            BidId = input.BidId, TenderId = input.BidId.HasValue ? null : invitation.TenderId,
            FromUserId = userId, ToUserId = officerId,
            Subject = input.Subject.Trim(), Status = ClarificationStatus.Open, CreatedAt = DateTime.UtcNow
        };
        thread.Messages.Add(new ClarificationMessage { SenderUserId = userId, Message = input.Message.Trim(), SentAt = DateTime.UtcNow });
        await using var transaction = await context.Database.BeginTransactionAsync();
        context.Clarifications.Add(thread);
        await context.SaveChangesAsync();
        AddNoticeAndAudit(userId, officerId, thread.Id, "CreateClarification");
        await context.SaveChangesAsync();
        await transaction.CommitAsync();
        return thread.Id;
    }

    public async Task<bool> ReplyAsync(string userId, int id, string message)
    {
        if (string.IsNullOrWhiteSpace(message) || message.Length > 10000) return false;
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var thread = await Threads(userId).SingleOrDefaultAsync(c => c.Id == id && c.Status != ClarificationStatus.Closed);
        if (thread == null) return false;
        context.ClarificationMessages.Add(new ClarificationMessage
        { ClarificationId = id, SenderUserId = userId, Message = message.Trim(), SentAt = DateTime.UtcNow });
        thread.Status = ClarificationStatus.Answered;
        AddNoticeAndAudit(userId, thread.FromUserId == userId ? thread.ToUserId : thread.FromUserId, id, "ReplyToClarification");
        await context.SaveChangesAsync();
        await transaction.CommitAsync();
        return true;
    }

    private void AddNoticeAndAudit(string userId, string officerId, int id, string action)
    {
        context.Notifications.Add(new Notification
        {
            UserId = officerId, Title = "Supplier clarification", Message = $"Supplier sent a message in clarification #{id}.",
            Link = $"/SourcingOfficer/Clarifications?id={id}", CreatedAt = DateTime.UtcNow
        });
        context.AuditLogs.Add(new AuditLog
        { UserId = userId, EntityName = nameof(Clarification), EntityId = id.ToString(), Action = action, CreatedAt = DateTime.UtcNow });
    }

    public async Task<List<SupplierNotificationViewModel>> GetNotificationsAsync(string userId) => await context.Notifications
        .AsNoTracking().Where(n => n.UserId == userId && Accounts(userId).Any())
        .OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id)
        .Select(n => new SupplierNotificationViewModel(n.Id, n.Title, n.Message, n.CreatedAt, n.IsRead, n.Link)).ToListAsync();

    public async Task<bool> MarkReadAsync(string userId, int id)
    {
        var notification = await context.Notifications.SingleOrDefaultAsync(n => n.Id == id && n.UserId == userId && Accounts(userId).Any());
        if (notification == null) return false;
        if (notification.IsRead) return true;
        notification.IsRead = true;
        context.AuditLogs.Add(new AuditLog
        { UserId = userId, EntityName = nameof(Notification), EntityId = id.ToString(), Action = "ReadNotification", CreatedAt = DateTime.UtcNow });
        await context.SaveChangesAsync();
        return true;
    }
}
