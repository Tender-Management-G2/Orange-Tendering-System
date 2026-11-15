using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.ViewModels;

namespace Orange_Tendering_System.Services;

public sealed class SupplierDashboardService(ApplicationDbContext context, SupplierCommunicationService communication)
{
    public async Task<WorkspaceDashboardViewModel?> GetAsync(string userId)
    {
        var user = await communication.Accounts(userId).AsNoTracking().Include(u => u.Supplier).SingleOrDefaultAsync();
        if (user == null) return null;
        var now = DateTime.UtcNow;
        var invitations = await context.TenderInvitations.AsNoTracking().Where(i => i.SupplierId == user.SupplierId)
            .Include(i => i.TenderRound).ThenInclude(r => r.Tender).OrderByDescending(i => i.InvitedAt).ToListAsync();
        var bids = await context.Bids.AsNoTracking().Where(b => b.TenderInvitation.SupplierId == user.SupplierId)
            .Select(b => b.Status).ToListAsync();
        var notices = await communication.GetNotificationsAsync(userId);
        var months = Enumerable.Range(0, 6).Select(i => new DateTime(now.Year, now.Month, 1).AddMonths(i - 5)).ToList();
        var submitted = await context.Bids.AsNoTracking().Where(b => b.TenderInvitation.SupplierId == user.SupplierId && b.SubmittedAt != null && b.SubmittedAt >= months[0])
            .Select(b => b.SubmittedAt!.Value).ToListAsync();
        return new WorkspaceDashboardViewModel
        {
            Monthly = months.Select(m => new DashboardPoint(m.ToString("MMM"), submitted.Count(d => d.Year == m.Year && d.Month == m.Month))).ToList(),
            DataConnected = true, DisplayName = user.Supplier!.CompanyName,
            Metrics = new Dictionary<string, int>
            {
                ["active-invitations"] = invitations.Count(i => i.Status == InvitationStatus.Pending
                    && i.TenderRound.Status == TenderRoundStatus.Open && i.TenderRound.Tender.Status == TenderStatus.Bidding
                    && i.TenderRound.Deadline > now && (!i.TenderRound.OpensAt.HasValue || i.TenderRound.OpensAt <= now)),
                ["submitted-offers"] = bids.Count(s => s == BidStatus.Submitted),
                ["awarded-tenders"] = await context.Bids.CountAsync(b => b.TenderInvitation.SupplierId == user.SupplierId
                    && b.TenderInvitation.TenderRound.Tender.Status == TenderStatus.Awarded
                    && b.TenderInvitation.TenderRound.Tender.AwardedAt != null
                    && b.TenderInvitation.TenderRound.Tender.AwardedBidId == b.Id
                    && context.Notifications.Any(n => n.UserId == userId && n.Title == "Tender result"
                        && n.Link == "/Supplier/Offers/Details/" + b.Id)),
                ["pending-clarifications"] = await communication.Threads(userId).CountAsync(c => c.Status == ClarificationStatus.Open),
                ["offers-total"] = bids.Count
            },
            Invitations = invitations.Take(5).Select(i => new DashboardInvitation($"#{i.Id}", i.TenderRound.Tender.Title,
                i.TenderRound.Deadline?.ToString("yyyy-MM-dd HH:mm") ?? "—",
                i.Status == InvitationStatus.Pending && i.TenderRound.Deadline <= now ? "Expired" : i.Status.ToString())).ToList(),
            Statuses = bids.GroupBy(s => s).Select(g => new DashboardStatus(g.Key.ToString(), g.Count(), g.Key == BidStatus.Draft ? "#ff7900" : "#2196f3")).ToList(),
            Activities = notices.Take(5).Select(n => new DashboardActivity(n.Title, n.Message, n.At.ToString("yyyy-MM-dd HH:mm"))).ToList()
        };
    }
}
