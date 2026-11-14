using System.Data;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.ViewModels;

namespace Orange_Tendering_System.Services;

// Owner: Saja. First step: create one draft for an accepted invitation.
public sealed partial class SupplierOfferService(ApplicationDbContext context, TenderWorkflowService workflow)
{
    // شروط السماح بالإنشاء تستخدم عند فتح الصفحة وعند الحفظ أيضا.
    private IQueryable<TenderInvitation> AvailableInvitations(string userId, int invitationId)
    {
        var now = workflow.Now;
        return context.TenderInvitations.Where(i => i.Id == invitationId
            && i.Status == InvitationStatus.Accepted
            && i.TenderRound.Status == TenderRoundStatus.Open
            && i.TenderRound.Tender.Status == TenderStatus.Bidding
            && context.Set<WeightRound>().Any(w => w.TenderId == i.TenderRound.TenderId && w.State == WeightState.Approved)
            && i.TenderRound.Deadline.HasValue && i.TenderRound.Deadline > now
            && (!i.TenderRound.OpensAt.HasValue || i.TenderRound.OpensAt <= now)
            && context.Users.Any(u => u.Id == userId && u.IsActive
                && u.SupplierId == i.SupplierId && u.Supplier != null
                && u.Supplier.Status == SupplierStatus.Approved));
    }

    public async Task<SupplierDraftOfferViewModel?> GetCreateAsync(string userId, int invitationId)
    {
        return await AvailableInvitations(userId, invitationId).AsNoTracking()
            .Where(i => !i.Bids.Any())
            .Select(i => new SupplierDraftOfferViewModel
            {
                InvitationId = i.Id,
                TenderTitle = i.TenderRound.Tender.Title,
                Input = new SupplierDraftOfferInputModel
                {
                    Items = i.TenderRound.Tender.Items.OrderBy(item => item.Id)
                        .Select(item => new SupplierDraftItemInputModel
                        {
                            TenderItemId = item.Id,
                            Name = item.Name,
                            RequiredQuantity = item.Quantity,
                            Quantity = item.Quantity
                        }).ToList()
                }
            }).SingleOrDefaultAsync();
    }

    public async Task<bool> CreateDraftAsync(string userId, int invitationId, SupplierDraftOfferInputModel input)
    {
        // المعاملة تمنع إنشاء نسختين لنفس الدعوة عند تكرار الضغط.
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var tenderId = await context.TenderInvitations.Where(i => i.Id == invitationId).Select(i => (int?)i.TenderRound.TenderId).SingleOrDefaultAsync();
        if (!tenderId.HasValue) return false;
        await context.Tenders.FromSqlInterpolated($"SELECT * FROM Tenders WITH (UPDLOCK,HOLDLOCK) WHERE Id={tenderId.Value}").SingleAsync();
        var model = await GetCreateAsync(userId, invitationId);
        if (model == null)
            return false;

        // نرفض الأصناف المكررة أو التابعة لعطاء آخر أو الناقصة.
        var expectedIds = model.Input.Items.Select(item => item.TenderItemId).OrderBy(id => id);
        var postedIds = input.Items.Select(item => item.TenderItemId).OrderBy(id => id);
        if (!expectedIds.SequenceEqual(postedIds) || input.Items.Count == 0)
            return false;

        // 1. ننشئ العرض بحالة Draft من السيرفر.
        var bid = new Bid
        {
            TenderInvitationId = invitationId,
            RevisionNumber = 1,
            Status = BidStatus.Draft,
            CurrencyCode = input.CurrencyCode,
            DeliveryDays = input.DeliveryDays,
            PaymentTerms = input.PaymentTerms?.Trim(),
            TechnicalProposal = input.TechnicalProposal?.Trim(),
            CreatedAt = DateTime.UtcNow
        };
        ApplyTerms(bid, input);

        // 2. نضيف أسعار وكميات الأصناف.
        foreach (var item in input.Items)
        {
            bid.Items.Add(new BidItem
            {
                TenderItemId = item.TenderItemId,
                Quantity = decimal.Round(item.Quantity, 3),
                UnitPrice = decimal.Round(item.UnitPrice, 2),
                OfferedSpecifications = item.OfferedSpecifications?.Trim()
            });
        }

        // 3. نضيف العرض ثم نحفظه في قاعدة البيانات.
        context.Bids.Add(bid);
        await context.SaveChangesAsync();

        context.AuditLogs.Add(new AuditLog
        {
            UserId = userId,
            Action = "CreateDraftOffer",
            EntityName = nameof(Bid),
            EntityId = bid.Id.ToString(),
            NewValues = "{\"Status\":\"Draft\",\"RevisionNumber\":1}",
            CreatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();
        await transaction.CommitAsync();
        return true;
    }
}
