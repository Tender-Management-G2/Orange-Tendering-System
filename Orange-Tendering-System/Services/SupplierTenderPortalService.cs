using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.ViewModels;
namespace Orange_Tendering_System.Services;

public sealed class SupplierTenderPortalService(ApplicationDbContext db, SupplierInvitationService invitations, SupplierOfferService offers)
{
    public async Task<SupplierTenderPortalViewModel?> GetAsync(string userId, int tenderId)
    {
        var invitationId = await db.TenderInvitations.AsNoTracking().Where(i => i.TenderRound.TenderId == tenderId
            && db.Users.Any(u => u.Id == userId && u.IsActive && u.SupplierId == i.SupplierId && u.Supplier != null && u.Supplier.Status == SupplierStatus.Approved))
            .OrderByDescending(i => i.TenderRound.RoundNumber).Select(i => (int?)i.Id).FirstOrDefaultAsync();
        if (!invitationId.HasValue) return null;
        var invitation = await invitations.GetDetailsAsync(userId, invitationId.Value);
        if (invitation is null) return null;
        var myOffers = (await offers.GetOffersAsync(userId)).Where(o => o.InvitationId == invitation.Id).ToList();
        var ready = !invitation.HasOffer && await offers.GetCreateAsync(userId, invitation.Id) != null;
        return new()
        {
            TenderId = tenderId, InvitationId = invitation.Id, Title = invitation.TenderTitle, Description = invitation.Description,
            Requirements = invitation.Requirements, RoundNumber = invitation.RoundNumber, OpensAt = invitation.OpensAt, Deadline = invitation.Deadline,
            Availability = invitation.IsBiddingOpen ? "Open" : invitation.OpensAt > DateTime.UtcNow ? "Upcoming" : "Closed",
            InvitationStatus = invitation.Status, CanRespond = invitation.CanRespond, CanCreateOffer = ready,
            Items = invitation.Items, Documents = invitation.Documents,
            Offers = myOffers.Select(o => new SupplierPortalOffer(o.Id, o.Status.ToString(), o.CanEdit)).ToList()
        };
    }
}
