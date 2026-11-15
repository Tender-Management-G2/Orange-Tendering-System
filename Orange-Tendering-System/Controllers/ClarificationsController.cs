using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Services;

namespace Orange_Tendering_System.Controllers;

[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class ClarificationsController(ApplicationDbContext db, ClarificationService clarifications, IWebHostEnvironment environment) : Controller
{
    private string? CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    public async Task<IActionResult> Index(string? search, string? status)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Challenge();
        if (User.IsInRole(SystemRoles.Supplier)) return RedirectToAction(nameof(Index), "Clarifications", new { area = "Supplier" });

        IQueryable<Clarification> query = db.Clarifications.AsNoTracking()
            .Where(c => c.FromUserId == userId || c.ToUserId == userId)
            .Include(c => c.FromUser).Include(c => c.ToUser)
            .Include(c => c.Messages)
            .Include(c => c.PurchaseRequest).Include(c => c.Tender)
            .Include(c => c.Bid).ThenInclude(b => b!.TenderInvitation).ThenInclude(i => i.Supplier)
            .Include(c => c.Bid).ThenInclude(b => b!.TenderInvitation).ThenInclude(i => i.TenderRound).ThenInclude(r => r.Tender);

        search = search?.Trim();
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(c => c.Subject.Contains(search)
                || c.Messages.Any(m => m.Message.Contains(search))
                || c.FromUser.UserName!.Contains(search) || c.ToUser.UserName!.Contains(search)
                || (c.Tender != null && c.Tender.Title.Contains(search))
                || (c.PurchaseRequest != null && c.PurchaseRequest.Title.Contains(search))
                || (c.Bid != null && c.Bid.TenderInvitation.TenderRound.Tender.Title.Contains(search)));

        if (Enum.TryParse<ClarificationStatus>(status, true, out var parsedStatus))
            query = query.Where(c => c.Status == parsedStatus);

        ViewData["Search"] = search ?? "";
        ViewData["Status"] = status ?? "";
        return View(await query.OrderByDescending(c => c.Messages.Max(m => (DateTime?)m.SentAt) ?? c.CreatedAt).ToListAsync());
    }

    public async Task<IActionResult> Details(int? id)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Challenge();
        if (id is null or <= 0) return NotFound();
        if (User.IsInRole(SystemRoles.Supplier)) return RedirectToAction(nameof(Details), "Clarifications", new { area = "Supplier", id });

        var thread = await db.Clarifications.AsNoTracking()
            .Where(c => c.Id == id && (c.FromUserId == userId || c.ToUserId == userId))
            .Include(c => c.FromUser).Include(c => c.ToUser)
            .Include(c => c.Messages).ThenInclude(m => m.SenderUser)
            .Include(c => c.Messages).ThenInclude(m => m.Documents)
            .Include(c => c.PurchaseRequest).Include(c => c.Tender)
            .Include(c => c.Bid).ThenInclude(b => b!.TenderInvitation).ThenInclude(i => i.Supplier)
            .Include(c => c.Bid).ThenInclude(b => b!.TenderInvitation).ThenInclude(i => i.TenderRound).ThenInclude(r => r.Tender)
            .SingleOrDefaultAsync();
        return thread == null ? NotFound() : View(thread);
    }

    [HttpGet]
    public async Task<IActionResult> Download(int id)
    {
        var document = await db.Documents.AsNoTracking().Include(d => d.ClarificationMessage).ThenInclude(m => m!.Clarification)
            .SingleOrDefaultAsync(d => d.Id == id && d.ClarificationMessageId != null);
        var thread = document?.ClarificationMessage?.Clarification;
        if (thread == null || (thread.FromUserId != CurrentUserId && thread.ToUserId != CurrentUserId) || !await clarifications.CanContinue(thread)) return NotFound();
        string path;
        try { path = PrivateDocumentPaths.Resolve(environment.ContentRootPath, document!.StoragePath); }
        catch (WorkflowException) { return NotFound(); }
        if (!System.IO.File.Exists(path)) return NotFound();
        Response.Headers.CacheControl = "no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return PhysicalFile(path, "application/octet-stream", Path.GetFileName(document!.FileName));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Reply(int id, string message)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Challenge();
        if (string.IsNullOrWhiteSpace(message) || message.Length > 10000)
        {
            TempData["ClarificationError"] = "Write a message up to 10,000 characters.";
            return RedirectToAction(nameof(Details), new { id });
        }

        if (!await clarifications.ReplyAsync(userId, id, message)) return NotFound();
        TempData["ClarificationMessage"] = "Reply sent.";
        return RedirectToAction(nameof(Details), new { id });
    }

    // Start a new conversation: only the approved role chain is offered, and the server re-checks every choice.
    [HttpGet]
    public async Task<IActionResult> New()
    {
        if (User.IsInRole(SystemRoles.Supplier)) return RedirectToAction("Index", "Clarifications", new { area = "Supplier" });
        try { return View(await clarifications.Options(User)); }
        catch (WorkflowException e) { TempData["ClarificationError"] = e.Message; return RedirectToAction(nameof(Index)); }
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> New(string option, string subject, string message)
    {
        var parts = (option ?? "").Split('|');
        try
        {
            if (parts.Length != 3 || !int.TryParse(parts[2], out var contextId)) throw new WorkflowException("Choose who to ask.");
            var id = await clarifications.CreateAsync(User, parts[0], parts[1], contextId, subject, message);
            TempData["ClarificationMessage"] = "Clarification sent.";
            return RedirectToAction(nameof(Details), new { id });
        }
        catch (WorkflowException e) { TempData["ClarificationError"] = e.Message; return RedirectToAction(nameof(Index)); }
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Resolve(int id)
    {
        if (!await clarifications.ResolveAsync(CurrentUserId!, id)) return NotFound();
        TempData["ClarificationMessage"] = "Marked as resolved.";
        return RedirectToAction(nameof(Details), new { id });
    }
}
