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
        var tender = await db.Tenders.AsNoTracking().Include(t => t.Rounds)
            .Where(t => t.Status == TenderStatus.Bidding && t.Rounds.Any(r => r.Status == TenderRoundStatus.Open && r.Invitations.SelectMany(i => i.Bids).Count(b => b.Status == BidStatus.Submitted && b.ComplianceStatus == ComplianceStatus.Compliant) >= 3))
            .OrderBy(t => t.CreatedAt).FirstOrDefaultAsync();
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
