using System.Data;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.ViewModels;

namespace Orange_Tendering_System.Services;

// Same SupplierOfferService, split into a separate file to keep each step readable.
public sealed partial class SupplierOfferService
{
    private IQueryable<Bid> OwnOffers(string userId) => context.Bids.Where(b =>
        context.Users.Any(u => u.Id == userId && u.IsActive && u.Supplier != null
            && u.Supplier.Status == SupplierStatus.Approved
            && u.SupplierId == b.TenderInvitation.SupplierId));

    public async Task<List<SupplierOfferDetailsViewModel>> GetOffersAsync(string userId)
    {
        var bids = await OwnOffers(userId).AsNoTracking()
            .Include(b => b.TenderInvitation).ThenInclude(i => i.TenderRound).ThenInclude(r => r.Tender)
            .Include(b => b.Items).ThenInclude(i => i.TenderItem)
            .OrderByDescending(b => b.CreatedAt).ToListAsync();
        return bids.Select(ToDetails).ToList();
    }

    public async Task<SupplierOfferDetailsViewModel?> GetOfferAsync(string userId, int id)
    {
        var bid = await OwnOffers(userId).AsNoTracking()
            .Include(b => b.TenderInvitation).ThenInclude(i => i.TenderRound).ThenInclude(r => r.Tender)
            .Include(b => b.Items).ThenInclude(i => i.TenderItem)
            .Include(b => b.Documents).SingleOrDefaultAsync(b => b.Id == id);
        if (bid == null) return null;
        var model = ToDetails(bid);
        // The Officer publishes the approved result using this notification contract.
        // Until that notice exists, the supplier sees no inferred award outcome.
        var notice = await context.Notifications.AsNoTracking()
            .Where(n => n.UserId == userId && n.Title == "Tender result" && n.Link == $"/Supplier/Offers/Details/{id}")
            .OrderByDescending(n => n.CreatedAt).FirstOrDefaultAsync();
        var tender = bid.TenderInvitation.TenderRound.Tender;
        if (notice != null && tender.Status == TenderStatus.Awarded && tender.AwardedAt.HasValue && tender.AwardedBidId.HasValue)
        {
            var awardedToOwnInvitation = await context.Bids.AnyAsync(b => b.Id == tender.AwardedBidId
                && b.TenderInvitationId == bid.TenderInvitationId);
            model.Outcome = awardedToOwnInvitation ? "Selected" : "Not selected";
            model.ResultMessage = notice.Message;
            model.NotifiedAt = notice.CreatedAt;
        }
        return model;
    }

    public async Task<SupplierDraftOfferViewModel?> GetEditAsync(string userId, int id)
    {
        var details = await GetOfferAsync(userId, id);
        if (details == null || !details.CanEdit) return null;
        return new SupplierDraftOfferViewModel
        {
            BidId = id, InvitationId = details.InvitationId,
            TenderTitle = details.TenderTitle, Input = details.Terms
        };
    }

    public async Task<bool> UpdateDraftAsync(string userId, int id, SupplierDraftOfferInputModel input)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var tenderId = await OwnOffers(userId).Where(b => b.Id == id).Select(b => (int?)b.TenderInvitation.TenderRound.TenderId).SingleOrDefaultAsync();
        if (!tenderId.HasValue) return false;
        await context.Tenders.FromSqlInterpolated($"SELECT * FROM Tenders WITH (UPDLOCK,HOLDLOCK) WHERE Id={tenderId.Value}").SingleAsync();
        var bid = await OwnOffers(userId).Include(b => b.Items).SingleOrDefaultAsync(b => b.Id == id);
        if (bid == null || bid.Status != BidStatus.Draft) return false;
        if (!await AvailableInvitations(userId, bid.TenderInvitationId).AnyAsync()) return false;
        if (!bid.Items.Select(i => i.TenderItemId).OrderBy(i => i)
            .SequenceEqual(input.Items.Select(i => i.TenderItemId).OrderBy(i => i))) return false;

        ApplyTerms(bid, input);
        foreach (var item in bid.Items)
        {
            var posted = input.Items.Single(i => i.TenderItemId == item.TenderItemId);
            item.Quantity = decimal.Round(posted.Quantity, 3);
            item.UnitPrice = decimal.Round(posted.UnitPrice, 2);
            item.OfferedSpecifications = posted.OfferedSpecifications?.Trim();
        }
        AddOfferAudit(userId, id, "UpdateDraftOffer");
        await context.SaveChangesAsync();
        await transaction.CommitAsync();
        return true;
    }

    public async Task<string?> SubmitAsync(string userId, int id)
    {
        try
        {
            await workflow.SubmitDraft(id, new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
                [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, userId)], "SupplierPortal")));
            return null;
        }
        catch (WorkflowException e) { return e.Message; }
    }

    public async Task<List<SupplierHistoryRow>?> GetHistoryAsync(string userId, int id)
    {
        if (!await OwnOffers(userId).AnyAsync(b => b.Id == id)) return null;
        return await context.AuditLogs.AsNoTracking().Where(a => a.EntityName == nameof(Bid) && a.EntityId == id.ToString())
            .OrderBy(a => a.CreatedAt).Select(a => new SupplierHistoryRow(a.Action, a.CreatedAt)).ToListAsync();
    }

    private static void ApplyTerms(Bid bid, SupplierDraftOfferInputModel input)
    {
        bid.CurrencyCode = input.CurrencyCode;
        bid.DeliveryDays = input.DeliveryDays;
        bid.PaymentTerms = input.PaymentTerms?.Trim();
        bid.TechnicalProposal = input.TechnicalProposal?.Trim();
        bid.ValidUntil = input.ValidUntil;
        bid.ShippingCost = decimal.Round(input.ShippingCost, 2);
        bid.InstallationCost = decimal.Round(input.InstallationCost, 2);
        bid.MaintenanceCost = decimal.Round(input.MaintenanceCost, 2);
        bid.TaxAmount = decimal.Round(input.TaxAmount, 2);
        bid.OtherCost = decimal.Round(input.OtherCost, 2);
    }

    private void AddOfferAudit(string userId, int id, string action) => context.AuditLogs.Add(new AuditLog
    {
        UserId = userId, EntityName = nameof(Bid), EntityId = id.ToString(), Action = action, CreatedAt = DateTime.UtcNow
    });

    private SupplierOfferDetailsViewModel ToDetails(Bid bid)
    {
        var round = bid.TenderInvitation.TenderRound;
        var now = workflow.Now;
        return new SupplierOfferDetailsViewModel
        {
            Id = bid.Id, InvitationId = bid.TenderInvitationId, TenderTitle = round.Tender.Title,
            Revision = bid.RevisionNumber, Status = bid.Status, CreatedAt = bid.CreatedAt, SubmittedAt = bid.SubmittedAt,
            Deadline = round.Deadline,
            CanEdit = bid.Status == BidStatus.Draft && bid.TenderInvitation.Status == InvitationStatus.Accepted
                && round.Status == TenderRoundStatus.Open && round.Tender.Status == TenderStatus.Bidding
                && round.Deadline.HasValue && round.Deadline > now && (!round.OpensAt.HasValue || round.OpensAt <= now),
            MissingRequirements = bid.MissingRequirements, ComplianceStatus = bid.ComplianceStatus.ToString(),
            Terms = new SupplierDraftOfferInputModel
            {
                CurrencyCode = bid.CurrencyCode, TechnicalProposal = bid.TechnicalProposal, PaymentTerms = bid.PaymentTerms,
                DeliveryDays = bid.DeliveryDays, ValidUntil = bid.ValidUntil, ShippingCost = bid.ShippingCost,
                InstallationCost = bid.InstallationCost, MaintenanceCost = bid.MaintenanceCost, TaxAmount = bid.TaxAmount,
                OtherCost = bid.OtherCost,
                Items = bid.Items.OrderBy(i => i.Id).Select(i => new SupplierDraftItemInputModel
                {
                    TenderItemId = i.TenderItemId, Name = i.TenderItem.Name, RequiredQuantity = i.TenderItem.Quantity,
                    Quantity = i.Quantity, UnitPrice = i.UnitPrice, OfferedSpecifications = i.OfferedSpecifications
                }).ToList()
            },
            Documents = bid.Documents.Select(d => new SupplierDocumentViewModel
            {
                Id = d.Id, FileName = d.FileName, DocumentType = d.DocumentType, UploadedAt = d.UploadedAt, SizeBytes = d.SizeBytes
            }).ToList()
        };
    }
}
