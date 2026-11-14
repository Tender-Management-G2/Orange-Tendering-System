using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.ViewModels;

namespace Orange_Tendering_System.Areas.SourcingOfficer.Controllers;

[Area("SourcingOfficer")]
[Authorize(Roles = SystemRoles.SourcingOfficer)]
public class DashboardController(ApplicationDbContext db) : Controller
{
    public async Task<IActionResult> Index()
    {
        var officerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var request = await db.PurchaseRequests.AsNoTracking().Include(r => r.Department)
            .Where(r => r.Status == PurchaseRequestStatus.Submitted).OrderBy(r => r.SubmittedAt).FirstOrDefaultAsync();
        Tender? tender = null;
        var openRounds = await db.TenderRounds.AsNoTracking().Include(r => r.Tender)
            .Include(r => r.Invitations).ThenInclude(i => i.Bids)
            .Where(r => r.Status == TenderRoundStatus.Open && r.Tender.Status == TenderStatus.Bidding)
            .OrderBy(r => r.CreatedAt).ToListAsync();
        foreach (var round in openRounds)
        {
            var compliantSuppliers = round.Invitations
                .Where(i => i.Bids.Any(b => b.Status == BidStatus.Submitted && b.ComplianceStatus == ComplianceStatus.Compliant))
                .Select(i => i.SupplierId).Distinct().Count();
            var priorFailures = await db.TenderRounds.AsNoTracking()
                .Where(r => r.TenderId == round.TenderId && r.RoundNumber < round.RoundNumber && r.Status == TenderRoundStatus.Failed)
                .Include(r => r.Invitations).ThenInclude(i => i.Bids).ToListAsync();
            var failedCount = priorFailures.Count(r => r.Invitations.Count > 0 && r.Invitations
                .Where(i => i.Bids.Any(b => b.Status == BidStatus.Submitted))
                .Select(i => i.SupplierId).Distinct().Count() < 3);
            if (compliantSuppliers >= (failedCount >= 2 ? 2 : 3)) { tender = round.Tender; break; }
        }
        var model = new SourcingOfficerDashboardViewModel
        {
            DisplayName = User.Identity?.Name,
            SubmittedRequests = await db.PurchaseRequests.CountAsync(r => r.Status == PurchaseRequestStatus.Submitted),
            AcceptedRequestsWithoutTender = await db.PurchaseRequests.CountAsync(r => r.Status == PurchaseRequestStatus.Accepted && !r.Tenders.Any()),
            ActiveTenders = await db.Tenders.CountAsync(t => t.Status == TenderStatus.Bidding),
            OffersToReview = await db.Bids.CountAsync(b => b.Status == BidStatus.Submitted && b.ComplianceStatus == ComplianceStatus.NotChecked),
            ReportsToForward = await db.TenderReports.CountAsync(r => r.ReportType == ReportType.FullEvaluation && r.Status == ReportStatus.Submitted && r.CreatedByUserId != officerId),
            RequestToReview = request,
            TenderReadyForCommittee = tender
        };
        return View(model);
    }
}
