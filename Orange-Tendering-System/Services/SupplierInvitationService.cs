using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.ViewModels;

namespace Orange_Tendering_System.Services;

// Owner: Saja. Own invitations, details and participation responses.
public sealed class SupplierInvitationService(ApplicationDbContext context)
{
    public async Task<SupplierInvitationsViewModel> GetInvitationsAsync(string userId, string? search = null, string? status = null)
    {
        // نحدد الشركة من حساب المستخدم، وليس من رقم يرسله المتصفح.
        var user = await context.Users.AsNoTracking()
            .Include(u => u.Supplier)
            .SingleOrDefaultAsync(u => u.Id == userId);

        if (user == null || user.IsActive == false)
        {
            return new() { AccessMessage = "Your account is unavailable or inactive. Please contact the administrator." };
        }

        var supplier = user.Supplier;
        if (supplier == null || supplier.Status != SupplierStatus.Approved)
        {
            return new() { AccessMessage = "Your account must be linked to an approved company to view invitations." };
        }

        // شرط الملكية: نجلب دعوات هذه الشركة فقط.
        var query = context.TenderInvitations.AsNoTracking().Where(i => i.SupplierId == supplier.Id);
        search = search?.Trim();
        if (!string.IsNullOrEmpty(search))
            query = query.Where(i => i.TenderRound.Tender.Title.Contains(search));
        var now = DateTime.UtcNow;
        if (status == "Expired")
            query = query.Where(i => i.Status == InvitationStatus.Pending && i.TenderRound.Deadline <= now);
        else if (Enum.TryParse<InvitationStatus>(status, out var parsed) && Enum.IsDefined(parsed))
        {
            query = query.Where(i => i.Status == parsed);
            if (parsed == InvitationStatus.Pending)
                query = query.Where(i => !i.TenderRound.Deadline.HasValue || i.TenderRound.Deadline > now);
        }
        var invitations = await query
            .OrderByDescending(i => i.InvitedAt)
            .ThenByDescending(i => i.Id)
            .Select(i => new SupplierInvitationRowViewModel
            {
                Id = i.Id,
                TenderTitle = i.TenderRound.Tender.Title,
                RoundNumber = i.TenderRound.RoundNumber,
                InvitedAt = i.InvitedAt,
                Deadline = i.TenderRound.Deadline,
                Status = i.Status
            })
            .ToListAsync();

        return new SupplierInvitationsViewModel { Invitations = invitations, Search = search, StatusFilter = status };
    }

    public async Task<SupplierInvitationDetailsViewModel?> GetDetailsAsync(string userId, int invitationId)
    {
        // نحدد شركة المستخدم الفعال والمعتمد من حسابه فقط.
        var supplierId = await context.Users.AsNoTracking()
            .Where(u => u.Id == userId && u.IsActive && u.Supplier != null
                && u.Supplier.Status == SupplierStatus.Approved)
            .Select(u => u.SupplierId)
            .SingleOrDefaultAsync();

        if (supplierId == null)
        {
            return null;
        }

        // حتى لو تغير رقم الدعوة بالرابط، لا نعرض دعوة شركة أخرى.
        return await context.TenderInvitations.AsNoTracking()
            .Where(i => i.Id == invitationId && i.SupplierId == supplierId)
            .Select(i => new SupplierInvitationDetailsViewModel
            {
                Id = i.Id,
                HasOffer = i.Bids.Any(),
                WeightsApproved = context.Set<WeightRound>().Any(w => w.TenderId == i.TenderRound.TenderId && w.State == WeightState.Approved),
                RoundStatus = i.TenderRound.Status,
                TenderStatus = i.TenderRound.Tender.Status,
                TenderTitle = i.TenderRound.Tender.Title,
                Description = i.TenderRound.Tender.Description,
                Requirements = i.TenderRound.Tender.Requirements,
                RoundNumber = i.TenderRound.RoundNumber,
                OpensAt = i.TenderRound.OpensAt,
                Deadline = i.TenderRound.Deadline,
                Status = i.Status,
                Documents = i.TenderRound.Tender.Documents.OrderBy(d => d.Id).Select(d => new SupplierDocumentViewModel
                {
                    Id = d.Id, FileName = d.FileName, DocumentType = d.DocumentType,
                    SizeBytes = d.SizeBytes, UploadedAt = d.UploadedAt
                }).ToList(),
                Items = i.TenderRound.Tender.Items.OrderBy(item => item.Id)
                    .Select(item => new SupplierTenderItemViewModel
                    {
                        Name = item.Name,
                        Specifications = item.Specifications,
                        Quantity = item.Quantity,
                        Unit = item.Unit
                    }).ToList()
            })
            .SingleOrDefaultAsync();
    }

    public async Task<bool> RespondAsync(string userId, int invitationId, InvitationStatus decision)
    {
        if (decision != InvitationStatus.Accepted && decision != InvitationStatus.Declined)
            return false;

        var ownInvitation = context.TenderInvitations.Where(i => i.Id == invitationId
            && context.Users.Any(u => u.Id == userId && u.IsActive
                && u.SupplierId == i.SupplierId && u.Supplier != null
                && u.Supplier.Status == SupplierStatus.Approved));
        var invitation = await ownInvitation.AsNoTracking()
            .Select(i => new { i.InvitedByUserId, i.TenderRound.TenderId })
            .SingleOrDefaultAsync();
        if (invitation == null)
            return false;

        await using var transaction = await context.Database.BeginTransactionAsync();
        var now = DateTime.UtcNow;

        // تحديث مشروط يمنع الرد المكرر حتى لو ضغط المستخدم الزر مرتين.
        int updated = await ownInvitation
            .Where(i => i.Status == InvitationStatus.Pending
                && i.TenderRound.Status == TenderRoundStatus.Open
                && i.TenderRound.Tender.Status == TenderStatus.Bidding
                && i.TenderRound.Deadline.HasValue && i.TenderRound.Deadline > now
                && (!i.TenderRound.OpensAt.HasValue || i.TenderRound.OpensAt <= now))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(i => i.Status, decision)
                .SetProperty(i => i.RespondedAt, (DateTime?)now));
        if (updated == 0)
            return false;

        context.Notifications.Add(new Notification
        {
            UserId = invitation.InvitedByUserId,
            Title = "Supplier invitation response",
            Message = $"Invitation #{invitationId} was {decision.ToString().ToLowerInvariant()} by the supplier.",
            Link = $"/SourcingOfficer/Invitations?id={invitation.TenderId}",
            CreatedAt = now
        });
        context.AuditLogs.Add(new AuditLog
        {
            UserId = userId,
            Action = "RespondToInvitation",
            EntityName = nameof(TenderInvitation),
            EntityId = invitationId.ToString(),
            OldValues = JsonSerializer.Serialize(new { Status = InvitationStatus.Pending.ToString() }),
            NewValues = JsonSerializer.Serialize(new { Status = decision.ToString(), RespondedAt = now }),
            CreatedAt = now
        });

        // نحفظ الإشعار والسجل والرد في معاملة واحدة. لا ننشئ عرضا هنا.
        await context.SaveChangesAsync();
        await transaction.CommitAsync();
        return true;
    }
}
