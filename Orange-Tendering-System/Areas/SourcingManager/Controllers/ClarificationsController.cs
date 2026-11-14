using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.ViewModels;

namespace Orange_Tendering_System.Areas.SourcingManager.Controllers;

[Area("SourcingManager")]
[Authorize(Roles = SystemRoles.SourcingManager + "," + SystemRoles.EvaluationCommittee + "," + SystemRoles.TopCommittee)]
public sealed class ClarificationsController(ApplicationDbContext context) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(string? search, string? status)
    {
        var userId = CurrentUserId();
        var threads = await context.Clarifications
            .AsNoTracking()
            .Include(thread => thread.FromUser)
            .Include(thread => thread.ToUser)
            .Include(thread => thread.Tender)
            .Include(thread => thread.Messages)
            .Where(thread => thread.FromUserId == userId || thread.ToUserId == userId)
            .OrderByDescending(thread => thread.CreatedAt)
            .ToListAsync();

        var rows = threads.Select(thread =>
        {
            var other = thread.FromUserId == userId ? thread.ToUser : thread.FromUser;
            return new SourcingManagerClarificationRow(
                thread.Id,
                thread.Subject,
                other.FullName.Length > 0 ? other.FullName : other.Email ?? "—",
                thread.Tender?.Title ?? "—",
                thread.Status,
                thread.CreatedAt,
                thread.Messages.MaxBy(message => message.SentAt)?.SentAt);
        }).AsEnumerable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            rows = rows.Where(row => row.Subject.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                                     row.TenderTitle.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                                     row.OtherParticipant.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        if (Enum.TryParse<ClarificationStatus>(status, true, out var selectedStatus))
            rows = rows.Where(row => row.Status == selectedStatus);

        return View(new SourcingManagerClarificationsIndexViewModel
        {
            Search = search,
            Status = status,
            Threads = rows.ToList()
        });
    }

    [Authorize(Roles = SystemRoles.SourcingManager)]
    [HttpGet]
    public async Task<IActionResult> Create(int? tenderId)
    {
        return View(await CreateViewModel(tenderId));
    }

    [Authorize(Roles = SystemRoles.SourcingManager)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(int tenderId, string toUserId, string subject, string message)
    {
        var managerId = CurrentUserId();
        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(message))
        {
            ModelState.AddModelError(string.Empty, "Subject and message are required.");
            return View(await CreateViewModel(tenderId));
        }

        if (!await IsAllowedRecipient(tenderId, toUserId))
        {
            ModelState.AddModelError(string.Empty, "Choose an Evaluation Committee or Top Committee member assigned to this tender.");
            return View(await CreateViewModel(tenderId));
        }

        var now = DateTime.UtcNow;
        var thread = new Clarification
        {
            TenderId = tenderId,
            FromUserId = managerId,
            ToUserId = toUserId,
            Subject = subject.Trim(),
            Status = ClarificationStatus.Open,
            CreatedAt = now
        };
        context.Clarifications.Add(thread);
        await context.SaveChangesAsync();

        context.ClarificationMessages.Add(new ClarificationMessage
        {
            ClarificationId = thread.Id,
            SenderUserId = managerId,
            Message = message.Trim(),
            SentAt = now
        });
        context.Notifications.Add(new Notification
        {
            UserId = toUserId,
            Title = "New clarification from Sourcing Manager",
            Message = subject.Trim(),
            Link = $"/SourcingManager/Clarifications/Details/{thread.Id}",
            IsRead = false,
            CreatedAt = now
        });
        await context.SaveChangesAsync();

        TempData["Success"] = "Clarification thread opened and the recipient was notified.";
        return RedirectToAction(nameof(Details), new { id = thread.Id });
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var thread = await FindThreadForCurrentUser(id);
        if (thread == null)
            return NotFound();

        return View(ToDetailsViewModel(thread));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reply(int id, string message)
    {
        var thread = await FindThreadForCurrentUser(id);
        if (thread == null)
            return NotFound();
        if (thread.Status == ClarificationStatus.Closed)
        {
            TempData["Error"] = "This clarification is closed.";
            return RedirectToAction(nameof(Details), new { id });
        }
        if (string.IsNullOrWhiteSpace(message))
        {
            TempData["Error"] = "Write a message before sending.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var userId = CurrentUserId();
        var recipientId = thread.FromUserId == userId ? thread.ToUserId : thread.FromUserId;
        var now = DateTime.UtcNow;
        context.ClarificationMessages.Add(new ClarificationMessage
        {
            ClarificationId = thread.Id,
            SenderUserId = userId,
            Message = message.Trim(),
            SentAt = now
        });
        thread.Status = ClarificationStatus.Answered;
        context.Notifications.Add(new Notification
        {
            UserId = recipientId,
            Title = "New reply to clarification",
            Message = thread.Subject,
            Link = $"/SourcingManager/Clarifications/Details/{thread.Id}",
            IsRead = false,
            CreatedAt = now
        });
        await context.SaveChangesAsync();

        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize(Roles = SystemRoles.SourcingManager)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Close(int id)
    {
        var thread = await context.Clarifications
            .FirstOrDefaultAsync(item => item.Id == id && item.FromUserId == CurrentUserId());
        if (thread == null)
            return NotFound();

        thread.Status = ClarificationStatus.Closed;
        thread.ClosedAt = DateTime.UtcNow;
        await context.SaveChangesAsync();
        TempData["Success"] = "Clarification closed.";
        return RedirectToAction(nameof(Details), new { id });
    }

    private async Task<SourcingManagerClarificationCreateViewModel> CreateViewModel(int? tenderId)
    {
        // Materialize the simple projection before removing duplicate tender rows.
        // SQL Server can translate the join, while Distinct over the view-model constructor cannot.
        var tenderRows = await context.TenderReports
            .AsNoTracking()
            .Where(report => report.ReportType == ReportType.FullEvaluation)
            .Select(report => new { report.TenderRound.TenderId, report.TenderRound.Tender.Title })
            .ToListAsync();
        var tenders = tenderRows
            .DistinctBy(tender => tender.TenderId)
            .OrderBy(tender => tender.Title)
            .Select(tender => new SourcingManagerTenderOption(tender.TenderId, tender.Title))
            .ToList();
        var selectedTenderId = tenderId ?? tenders.FirstOrDefault()?.Id;
        var recipients = selectedTenderId == null
            ? []
            : await context.CommitteeMembers
                .AsNoTracking()
                .Include(member => member.User)
                .Where(member => member.TenderId == selectedTenderId &&
                    (member.CommitteeType == CommitteeType.Evaluation || member.CommitteeType == CommitteeType.Top))
                .OrderBy(member => member.CommitteeType)
                .ThenBy(member => member.User.FullName)
                .Select(member => new SourcingManagerRecipientOption(
                    member.UserId,
                    member.User.FullName.Length > 0 ? member.User.FullName : member.User.Email!,
                    member.CommitteeType == CommitteeType.Top ? "Top Committee" : "Evaluation Committee"))
                .ToListAsync();

        return new SourcingManagerClarificationCreateViewModel
        {
            TenderId = selectedTenderId,
            Tenders = tenders,
            Recipients = recipients
        };
    }

    private Task<bool> IsAllowedRecipient(int tenderId, string userId) =>
        context.CommitteeMembers.AnyAsync(member => member.TenderId == tenderId &&
            member.UserId == userId &&
            (member.CommitteeType == CommitteeType.Evaluation || member.CommitteeType == CommitteeType.Top));

    private async Task<Clarification?> FindThreadForCurrentUser(int id)
    {
        var userId = CurrentUserId();
        return await context.Clarifications
            .Include(thread => thread.FromUser)
            .Include(thread => thread.ToUser)
            .Include(thread => thread.Tender)
            .Include(thread => thread.Messages)
                .ThenInclude(message => message.SenderUser)
            .FirstOrDefaultAsync(thread => thread.Id == id &&
                (thread.FromUserId == userId || thread.ToUserId == userId));
    }

    private SourcingManagerClarificationDetailsViewModel ToDetailsViewModel(Clarification thread)
    {
        var userId = CurrentUserId();
        var other = thread.FromUserId == userId ? thread.ToUser : thread.FromUser;
        return new SourcingManagerClarificationDetailsViewModel
        {
            Id = thread.Id,
            Subject = thread.Subject,
            TenderTitle = thread.Tender?.Title ?? "—",
            OtherParticipant = other.FullName.Length > 0 ? other.FullName : other.Email ?? "—",
            Status = thread.Status,
            CanReply = thread.Status != ClarificationStatus.Closed,
            CanClose = User.IsInRole(SystemRoles.SourcingManager) && thread.FromUserId == userId && thread.Status != ClarificationStatus.Closed,
            Messages = thread.Messages
                .OrderBy(message => message.SentAt)
                .Select(message => new SourcingManagerClarificationMessageRow(
                    message.SenderUser.FullName.Length > 0 ? message.SenderUser.FullName : message.SenderUser.Email ?? "—",
                    message.Message,
                    message.SentAt))
                .ToList()
        };
    }

    private string CurrentUserId() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new InvalidOperationException("The signed-in user has no identifier.");
}
