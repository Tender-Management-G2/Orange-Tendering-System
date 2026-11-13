// ORANGE TENDER MANAGEMENT SYSTEM
// Owner: Haya
// Module: Sourcing Manager Review
// Purpose: MVC endpoints for the Reports pages of the Sourcing Manager workspace.
// Status: Scaffold only. GET actions return views; no POST actions, no database access, no business logic.
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orange_Tendering_System.Models;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Data;
using System.Security.Claims;
using Orange_Tendering_System.Models.Entities;

namespace Orange_Tendering_System.Areas.SourcingManager.Controllers
{
    [Area("SourcingManager")]
    [Authorize(Roles = SystemRoles.SourcingManager)]
    public class ReportsController : Controller
    {
        private readonly ApplicationDbContext _context;

        public ReportsController(ApplicationDbContext context)
        {
            _context = context;
        }
        // Evaluation Reports List — Owner: Haya · Trello: Evaluation Reports List (https://trello.com/c/CWgcYFbk)
        // GET /SourcingManager/Reports. TODO: query the data in a service and pass a view model.
        public async Task<IActionResult> Index()
        {
            var report=await _context.TenderReports.Include(r=>r.TenderRound).ThenInclude(tr=>tr.Tender).Include(r => r.CreatedByUser)
                .Where(r=>r.ReportType==ReportType.FullEvaluation).OrderByDescending(r=>r.SubmittedAt).AsNoTracking().ToListAsync();
            return View(report);
        }

        // Sidebar destination for reports that have arrived for managerial review.
        [HttpGet]
        public IActionResult Pending()
        {
            return RedirectToAction(nameof(Index), new { status = ReportStatus.Submitted.ToString() });
        }

        // Sidebar destination for accepted reports that can be sent to the Top Committee.
        [HttpGet]
        public IActionResult Forwarding()
        {
            return RedirectToAction(nameof(Index), new { status = ReportStatus.Accepted.ToString() });
        }

        // Review Unified Evaluation Report — Owner: Haya · Trello: Review Unified Evaluation Report (https://trello.com/c/IKQ7sZwo)
        // GET /SourcingManager/Reports/Review/{id?}. TODO: load the record by id (NotFound when missing) and check access.


        [HttpGet]
        public async Task<IActionResult> Review(int? id)
        {
            if (id == null)
            {
                return RedirectToAction(nameof(Index));
            }

            var report = await _context.TenderReports
                .Include(r => r.TenderRound)
                    .ThenInclude(tr => tr.Tender)
                .Include(r => r.RecommendedBid)
                .ThenInclude(b => b!.TenderInvitation)
                .ThenInclude(i => i.Supplier)
                .Include(r => r.CreatedByUser)
                .AsNoTracking()
                .FirstOrDefaultAsync(r =>
                    r.Id == id &&
                    r.ReportType == ReportType.FullEvaluation);

            if (report == null)
            {
                return NotFound();
            }

            return View(report);
        }



        // Approve Evaluation Report — Owner: Haya · Trello: Approve Evaluation Report (https://trello.com/c/tpvo9nZx)
        // GET /SourcingManager/Reports/Approve/{id?}. TODO: load the record by id (NotFound when missing) and check access.


        [HttpGet]
        public async Task<IActionResult> Approve(int id)
        {
            var report = await _context.TenderReports
                .Include(r => r.TenderRound)
                    .ThenInclude(tr => tr.Tender)
                .Include(r => r.CreatedByUser)
                .AsNoTracking()
                .FirstOrDefaultAsync(r =>
                    r.Id == id &&
                    r.ReportType == ReportType.FullEvaluation);

            if (report == null)
            {
                return NotFound();
            }

            if (report.Status != ReportStatus.Submitted)
            {
                TempData["Error"] = "Only submitted reports can be approved.";
                return RedirectToAction(nameof(Review), new { id });
            }

            return View(report);
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Approve(int id, string? comment)
        {
            return await SaveDecision( id,ReportStatus.Accepted,comment);
        }

        // Reject Evaluation Report — Owner: Haya · Trello: Reject Evaluation Report (https://trello.com/c/WE4m7Kej)
        // GET /SourcingManager/Reports/Reject/{id?}. TODO: load the record by id (NotFound when missing) and check access.


        [HttpGet]
        public async Task<IActionResult> Reject(int id)
        {
            var report = await _context.TenderReports
                .Include(r => r.TenderRound)
                    .ThenInclude(tr => tr.Tender)
                .Include(r => r.CreatedByUser)
                .AsNoTracking()
                .FirstOrDefaultAsync(r =>
                    r.Id == id &&
                    r.ReportType == ReportType.FullEvaluation);

            if (report == null)
            {
                return NotFound();
            }

            if (report.Status != ReportStatus.Submitted)
            {
                TempData["Error"] = "Only submitted reports can be rejected.";
                return RedirectToAction(nameof(Review), new { id });
            }

            return View(report);
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Reject( int id, string? comment)
        {
            if (string.IsNullOrWhiteSpace(comment))
            {
                TempData["Error"] = "Rejection reason is required.";
                return RedirectToAction(nameof(Reject), new { id });
            }

            return await SaveDecision( id,ReportStatus.Rejected,comment);
        }
        // Return Report for Review — Owner: Haya · Trello: Return Report for Review (https://trello.com/c/r1oVUbDr)
        // GET /SourcingManager/Reports/Return/{id?}. TODO: load the record by id (NotFound when missing) and check access.


        [HttpGet]
        [ActionName("Return")]
        public async Task<IActionResult> Return(int id)
        {
            var report = await _context.TenderReports
                .Include(r => r.TenderRound)
                    .ThenInclude(tr => tr.Tender)
                .Include(r => r.CreatedByUser)
                .AsNoTracking()
                .FirstOrDefaultAsync(r =>
                    r.Id == id &&
                    r.ReportType == ReportType.FullEvaluation);

            if (report == null)
            {
                return NotFound();
            }

            if (report.Status != ReportStatus.Submitted)
            {
                TempData["Error"] = "Only submitted reports can be returned.";
                return RedirectToAction(nameof(Review), new { id });
            }

            return View("Return", report);
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        [ActionName("Return")]
        public async Task<IActionResult> Return(int id, string? comment)
        {
            if (string.IsNullOrWhiteSpace(comment))
            {
                TempData["Error"] = "Return reason is required.";
                return RedirectToAction("Return", new { id });
            }

            return await SaveDecision(
                id,
                ReportStatus.Returned,
                comment
            );
        }


        private async Task<IActionResult> SaveDecision(
            int id,
            ReportStatus newStatus,
            string? comment)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (string.IsNullOrEmpty(userId))
                return Challenge();

            if (newStatus != ReportStatus.Accepted &&
                newStatus != ReportStatus.Rejected &&
                newStatus != ReportStatus.Returned)
                return BadRequest();

            comment = comment?.Trim();

            if ((newStatus == ReportStatus.Rejected ||
                 newStatus == ReportStatus.Returned) &&
                string.IsNullOrWhiteSpace(comment))
            {
                TempData["Error"] = "A reason is required.";
                return RedirectToAction(nameof(Review), new { id });
            }

            if (comment?.Length > 2000)
            {
                TempData["Error"] = "Comment cannot exceed 2000 characters.";
                return RedirectToAction(nameof(Review), new { id });
            }

            await using var transaction =
                await _context.Database.BeginTransactionAsync();

            var report = await _context.TenderReports
                .AsNoTracking()
                .FirstOrDefaultAsync(r =>
                    r.Id == id &&
                    r.ReportType == ReportType.FullEvaluation);

            if (report == null)
                return NotFound();

            var decisionDate = DateTime.UtcNow;

            // Update only if the report is still Submitted.
            var updatedRows = await _context.TenderReports
                .Where(r =>
                    r.Id == id &&
                    r.ReportType == ReportType.FullEvaluation &&
                    r.Status == ReportStatus.Submitted)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(r => r.Status, newStatus)
                    .SetProperty(r => r.ReviewComment, comment)
                    .SetProperty(r => r.ReviewedByUserId, userId)
                    .SetProperty(r => r.ReviewedAt, decisionDate));

            if (updatedRows == 0)
            {
                await transaction.RollbackAsync();

                TempData["Error"] =
                    "This report has already been reviewed.";

                return RedirectToAction(nameof(Review), new { id });
            }

            // Audit Log
            _context.AuditLogs.Add(new AuditLog
            {
                UserId = userId,
                Action = newStatus.ToString(),
                EntityName = "TenderReport",
                EntityId = id.ToString(),

                OldValues = System.Text.Json.JsonSerializer.Serialize(
                    new
                    {
                        Status = ReportStatus.Submitted.ToString()
                    }),

                NewValues = System.Text.Json.JsonSerializer.Serialize(
                    new
                    {
                        Status = newStatus.ToString(),
                        ReviewComment = comment,
                        ReviewedByUserId = userId,
                        ReviewedAt = decisionDate
                    }),

                CreatedAt = decisionDate
            });

            // Notification for the report creator
            if (!string.IsNullOrWhiteSpace(report.CreatedByUserId))
            {
                _context.Notifications.Add(new Notification
                {
                    UserId = report.CreatedByUserId,
                    Title = "Evaluation Report Decision",
                    Message = $"Your evaluation report #{id} " +
                              $"has been {newStatus.ToString().ToLowerInvariant()} " +
                              "by the Sourcing Manager.",
                    Link = null,
                    IsRead = false,
                    CreatedAt = decisionDate
                });
            }

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            TempData["Success"] = "Decision saved successfully.";

            return RedirectToAction(nameof(Index));
        }


        // Forward to Top Committee — Owner: Haya · Trello: Forward to Top Committee (https://trello.com/c/953NoZV0)
        // GET /SourcingManager/Reports/Forward/{id?}. TODO: load the record by id (NotFound when missing) and check access.


        [HttpGet]
        public async Task<IActionResult> Forward(int? id)
        {
            if (id == null)
            {
                return RedirectToAction(nameof(Index));
            }


            var report = await _context.TenderReports
                .Include(r => r.TenderRound)
                    .ThenInclude(tr => tr.Tender)
                        .ThenInclude(t => t.Items)
                .Include(r => r.RecommendedBid)
                    .ThenInclude(b => b!.TenderInvitation)
                        .ThenInclude(i => i.Supplier)
                .Include(r => r.RecommendedBid)
                    .ThenInclude(b => b!.Items)
                .AsNoTracking()
                .FirstOrDefaultAsync(r =>
                    r.Id == id &&
                    r.ReportType == ReportType.FullEvaluation);


            if (report == null)
            {
                return NotFound();
            }

            if (report.Status != ReportStatus.Accepted)
            {
                TempData["Error"] = "Only accepted reports can be forwarded.";

                return RedirectToAction(nameof(Review), new { id });
            }

            // Add Total Cost calculation here
            if (report.RecommendedBid != null)
            {
                var bid = report.RecommendedBid;

                var itemsTotal = bid.Items.Sum(item =>
                    item.Quantity * item.UnitPrice);

                ViewBag.TotalCost =
                    itemsTotal +
                    bid.ShippingCost +
                    bid.InstallationCost +
                    bid.MaintenanceCost +
                    bid.TaxAmount +
                    bid.OtherCost;
            }

            ViewBag.Summary = "";
            ViewBag.KeyComparisonPoints = "";
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (!string.IsNullOrEmpty(userId))
            {
                var draft = await _context.TenderReports
                    .AsNoTracking()
                    .FirstOrDefaultAsync(r =>
                        r.TenderRoundId == report.TenderRoundId &&
                        r.ReportType == ReportType.ExecutiveSummary &&
                        r.Status == ReportStatus.Draft &&
                        r.PreviousReportId == report.Id &&
                        r.CreatedByUserId == userId);

                if (draft != null)
                {
                    var separator = "\n\nKey Comparison Points:\n";
                    var content = draft.Content ?? "";

                    if (content.StartsWith("Summary:\n") &&
                        content.Contains(separator))
                    {
                        var parts = content.Substring("Summary:\n".Length)
                            .Split(new[] { separator }, 2, StringSplitOptions.None);

                        ViewBag.Summary = parts[0];
                        ViewBag.KeyComparisonPoints = parts[1];
                    }
                }
            }

            return View(report);
        }

        // Save Executive Summary Draft
        // Owner: Haya
        // Module: Sourcing Manager Review

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveSummaryDraft(
            int id,
            string? summary,
            string? keyComparisonPoints)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (string.IsNullOrEmpty(userId))
            {
                return Challenge();
            }

            if (string.IsNullOrWhiteSpace(summary) ||
                string.IsNullOrWhiteSpace(keyComparisonPoints))
            {
                TempData["Error"] =
                    "Summary and key comparison points are required.";

                return RedirectToAction(nameof(Forward), new { id });
            }

            var report = await _context.TenderReports
                .FirstOrDefaultAsync(r =>
                    r.Id == id &&
                    r.ReportType == ReportType.FullEvaluation);

            if (report == null)
            {
                return NotFound();
            }

            if (report.Status != ReportStatus.Accepted)
            {
                TempData["Error"] =
                    "Only accepted reports can have an executive summary.";

                return RedirectToAction(nameof(Review), new { id });
            }

     
            var draft = await _context.TenderReports
                .FirstOrDefaultAsync(r =>
                    r.TenderRoundId == report.TenderRoundId &&
                    r.ReportType == ReportType.ExecutiveSummary &&
                    r.PreviousReportId == report.Id &&
                    r.CreatedByUserId == userId &&
                    r.Status == ReportStatus.Draft);

            var content =
                "Summary:\n" + summary.Trim() +
                "\n\nKey Comparison Points:\n" +
                keyComparisonPoints.Trim();

            if (draft == null)
            {
                // 6. Find the next version number
                var latestVersion = await _context.TenderReports
                    .Where(r =>
                        r.TenderRoundId == report.TenderRoundId &&
                        r.ReportType == ReportType.ExecutiveSummary)
                    .MaxAsync(r => (int?)r.VersionNumber) ?? 0;

             
                draft = new TenderReport
                {
                    TenderRoundId = report.TenderRoundId,
                    ReportType = ReportType.ExecutiveSummary,
                    VersionNumber = latestVersion + 1,
                    PreviousReportId = report.Id,
                    Content = content,
                    RecommendedBidId = report.RecommendedBidId,
                    RankingSnapshotJson = report.RankingSnapshotJson,
                    Status = ReportStatus.Draft,
                    CreatedByUserId = userId,
                    CreatedAt = DateTime.UtcNow
                };

                _context.TenderReports.Add(draft);
            }
            else
            {
                
                draft.Content = content;
                draft.RecommendedBidId = report.RecommendedBidId;
                draft.RankingSnapshotJson = report.RankingSnapshotJson;
            }

            await _context.SaveChangesAsync();

            TempData["Success"] =
                "Executive summary draft saved successfully.";

            return RedirectToAction(nameof(Forward), new { id });
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SubmitToTopCommittee( int id,bool costAndItemsReviewed, bool supplierReviewed,bool readyForCommittee)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (string.IsNullOrEmpty(userId))
            {
                return Challenge();
            }

            if (!costAndItemsReviewed ||
                !supplierReviewed ||
                !readyForCommittee)
            {
                TempData["Error"] =
                    "Please confirm all forwarding requirements.";

                return RedirectToAction(nameof(Forward), new { id });
            }


            // 1. Find the accepted Full Evaluation report
            var report = await _context.TenderReports
                .Include(r => r.TenderRound)
                .FirstOrDefaultAsync(r =>
                    r.Id == id &&
                    r.ReportType == ReportType.FullEvaluation);

            if (report == null)
            {
                return NotFound();
            }

            if (report.Status != ReportStatus.Accepted)
            {
                TempData["Error"] =
                    "Only accepted evaluation reports can be forwarded.";

                return RedirectToAction(nameof(Review), new { id });
            }

            // 2. Find the saved Executive Summary draft
            var draft = await _context.TenderReports
                .FirstOrDefaultAsync(r =>
                    r.TenderRoundId == report.TenderRoundId &&
                    r.ReportType == ReportType.ExecutiveSummary &&
                    r.PreviousReportId == report.Id &&
                    r.CreatedByUserId == userId &&
                    r.Status == ReportStatus.Draft);

            if (draft == null || string.IsNullOrWhiteSpace(draft.Content))
            {
                TempData["Error"] =
                    "Please save the executive summary before forwarding.";

                return RedirectToAction(nameof(Forward), new { id });
            }

            // 3. Get the Top Committee members
            var members = await _context.CommitteeMembers
                .Where(m =>
                    m.TenderId == report.TenderRound.TenderId &&
                    m.CommitteeType == CommitteeType.Top)
                .Select(m => m.UserId)
                .ToListAsync();

            if (members.Count != 4 ||
                members.Distinct().Count() != 4)
            {
                TempData["Error"] =
                    "The Top Committee must have exactly four members.";

                return RedirectToAction(nameof(Forward), new { id });
            }

            // 4. Prevent forwarding the same summary twice

            var alreadyForwarded = await _context.CommitteeRounds
                .AnyAsync(r =>
                    r.CommitteeType == CommitteeType.Top &&
                    r.TenderReport != null &&
                    r.TenderReport.PreviousReportId == report.Id);


            if (alreadyForwarded)
            {
                TempData["Error"] =
                    "This executive summary has already been forwarded.";

                return RedirectToAction(nameof(Forward), new { id });
            }

            // 5. Save all related changes together
            await using var transaction =
                await _context.Database.BeginTransactionAsync();

            try
            {
                // Submit the Executive Summary
                draft.Status = ReportStatus.Submitted;
                draft.SubmittedAt = DateTime.UtcNow;

                // Find the next Top Committee round number
                var latestRound = await _context.CommitteeRounds
                    .Where(r =>
                        r.TenderRoundId == report.TenderRoundId &&
                        r.CommitteeType == CommitteeType.Top)
                    .MaxAsync(r => (int?)r.RoundNumber) ?? 0;

                // Create the Top Committee review round
                var committeeRound = new CommitteeRound
                {
                    TenderRoundId = report.TenderRoundId,
                    CommitteeType = CommitteeType.Top,
                    RoundNumber = latestRound + 1,
                    TenderReportId = draft.Id,
                    Status = CommitteeRoundStatus.InProgress,
                    StartedAt = DateTime.UtcNow
                };

                _context.CommitteeRounds.Add(committeeRound);

                // Notify the four Top Committee members
                foreach (var memberId in members)
                {
                    var notification = new Notification
                    {
                        UserId = memberId,
                        Title = "New Executive Summary",
                        Message =
                            "An executive summary has been forwarded " +
                            "to the Top Committee for review.",
                        IsRead = false,
                        CreatedAt = DateTime.UtcNow
                    };

                    _context.Notifications.Add(notification);
                }

                // Record the forwarding action
                var auditLog = new AuditLog
                {
                    UserId = userId,
                    Action = "Forward",
                    EntityName = "TenderReport",
                    EntityId = draft.Id.ToString(),
                    NewValues =
                        "{\"Status\":\"Submitted\"," +
                        "\"CommitteeType\":\"Top\"}",
                    CreatedAt = DateTime.UtcNow
                };

                _context.AuditLogs.Add(auditLog);

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                TempData["Success"] =
                    "Executive summary forwarded successfully.";

                return RedirectToAction(nameof(Index));
            }
            catch (DbUpdateException)
            {
                await transaction.RollbackAsync();

                TempData["Error"] =
                    "The executive summary could not be forwarded. " +
                    "Please try again.";

                return RedirectToAction(nameof(Forward), new { id });
            }
        }



        // Sourcing Manager Decision History — Owner: Haya · Trello: Sourcing Manager Decision History (https://trello.com/c/CSb7lOgK)
        // GET /SourcingManager/Reports/History. TODO: query the data in a service and pass a view model.

        [HttpGet]
        public async Task<IActionResult> History()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (string.IsNullOrEmpty(userId))
            {
                return Challenge();
            }

            var reports = await _context.TenderReports
                .Include(r => r.TenderRound)
                    .ThenInclude(tr => tr.Tender)
                .Where(r =>
                    r.ReportType == ReportType.FullEvaluation &&
                    r.ReviewedByUserId == userId &&
                    r.ReviewedAt != null)
                .OrderByDescending(r => r.ReviewedAt)
                .AsNoTracking()
                .ToListAsync();

            return View(reports);
        }

    }
}
