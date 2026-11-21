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

        // Summary cards: all of this user's conversations, independent of the current search/filter.
        var byStatus = await db.Clarifications.AsNoTracking().Where(c => c.FromUserId == userId || c.ToUserId == userId)
            .GroupBy(c => c.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync();
        int Count(ClarificationStatus s) => byStatus.Where(x => x.Key == s).Sum(x => x.Count);
        ViewBag.Counts = new[] { Count(ClarificationStatus.Open), Count(ClarificationStatus.Answered), Count(ClarificationStatus.Closed) };

        // Unread: the user's own unread notifications that point at one of their conversations (new thread or reply).
        var unreadLinks = await db.Notifications.AsNoTracking()
            .Where(n => n.UserId == userId && !n.IsRead && n.Link != null && n.Link.StartsWith("/Clarifications/Details/"))
            .Select(n => n.Link!).ToListAsync();
        var unread = unreadLinks.Select(l => int.TryParse(l["/Clarifications/Details/".Length..], out var tid) ? tid : 0).Where(tid => tid > 0)
            .GroupBy(tid => tid).ToDictionary(g => g.Key, g => g.Count());
        var unreadThreads = unread.Count == 0 ? [] : await db.Clarifications.AsNoTracking()
            .Where(c => unread.Keys.Contains(c.Id) && (c.FromUserId == userId || c.ToUserId == userId))
            .Select(c => new { c.Id, Other = c.FromUserId == userId ? c.ToUser : c.FromUser }).ToListAsync();
        unread = unread.Where(u => unreadThreads.Any(t => t.Id == u.Key)).ToDictionary(u => u.Key, u => u.Value);
        ViewBag.Unread = unread;
        ViewBag.UnreadFrom = unreadThreads.Select(t => string.IsNullOrWhiteSpace(t.Other.FullName) ? t.Other.Email ?? "—" : t.Other.FullName).Distinct().ToList();

        // People for the Create window next to the table (internal roles only; suppliers and Admin do not start chats here).
        if (!User.IsInRole(SystemRoles.Admin))
        {
            try
            {
                var options = await clarifications.Options(User);
                ViewBag.CreateForm = new Orange_Tendering_System.ViewModels.ClarificationCreateModel(options, await clarifications.PendingContacts(User, options));
            }
            catch (WorkflowException) { /* No clarification role: the Create button is not shown. */ }
        }
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
        if (thread == null) return NotFound();
        // Opening the conversation reads it: its new-thread/reply notifications for this user are marked as read.
        var link = $"/Clarifications/Details/{thread.Id}";
        var unread = await db.Notifications.Where(n => n.UserId == userId && !n.IsRead && n.Link == link).ToListAsync();
        if (unread.Count > 0) { unread.ForEach(n => n.IsRead = true); await db.SaveChangesAsync(); }
        return View(thread);
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
        TempData.Orange(OrangeFeedback.Toast("Reply sent", "The other participant was notified.", OrangeVariant.Send));
        return RedirectToAction(nameof(Details), new { id });
    }

    // Start a new conversation: the Create window on the list page (people table + message). Old links keep working.
    [HttpGet]
    public IActionResult New() => User.IsInRole(SystemRoles.Supplier)
        ? RedirectToAction("Index", "Clarifications", new { area = "Supplier" })
        : RedirectToAction(nameof(Index), new { compose = 1 });

    // One or more ticked people ("options"; the older single "option" field still works). Each person gets a separate
    // two-person conversation; ClarificationService re-checks the role pair and the shared record for every one.
    // People ticked from "Not available yet" ("pending") share no record with the caller: they are refused by name,
    // because every conversation must stay linked to a request, tender or bid.
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> New(string[]? options, string? option, string[]? pending, string? subject, string message)
    {
        var chosen = (options ?? []).Append(option).Where(o => !string.IsNullOrWhiteSpace(o)).Select(o => o!).Distinct().Take(50).ToList();
        var unlinkedIds = (pending ?? []).Where(p => !string.IsNullOrWhiteSpace(p)).Distinct().Take(60).ToHashSet();
        string? unlinked = null;
        if (unlinkedIds.Count > 0)
        {
            // Names are looked up on the server (only people this user may see in the list); anything else is ignored.
            var names = (await clarifications.PendingContacts(User, await clarifications.Options(User)))
                .Where(p => unlinkedIds.Contains(p.Id)).Select(p => p.Name).ToList();
            if (names.Count > 0)
                unlinked = $"No conversation was created with {string.Join(", ", names)}: you do not share a request, tender or bid with {(names.Count == 1 ? "this person" : "them")} yet.";
        }
        if (chosen.Count == 0)
        {
            TempData["ClarificationError"] = unlinked ?? "Choose at least one person.";
            return RedirectToAction(nameof(Index), new { compose = 1 });
        }
        message = message?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(subject))
        {
            var firstLine = message.Split('\n', 2)[0].Trim();
            subject = firstLine.Length > 80 ? firstLine[..77] + "..." : firstLine;
        }
        var created = new List<int>(); string? failure = null;
        foreach (var value in chosen)
        {
            var parts = value.Split('|');
            try
            {
                if (parts.Length != 3 || !int.TryParse(parts[2], out var contextId)) throw new WorkflowException("Choose who to ask.");
                created.Add(await clarifications.CreateAsync(User, parts[0], parts[1], contextId, subject, message));
            }
            catch (WorkflowException e) { failure ??= e.Message; }
        }
        if (created.Count == 0) { TempData["ClarificationError"] = string.Join(" ", new[] { failure ?? "The conversation could not be created.", unlinked }.Where(x => x != null)); return RedirectToAction(nameof(Index), new { compose = 1 }); }
        var problems = new List<string>();
        if (failure != null) problems.Add($"{chosen.Count - created.Count} of {chosen.Count} conversations were not created: {failure}");
        if (unlinked != null) problems.Add(unlinked);
        if (problems.Count > 0) TempData["ClarificationError"] = string.Join(" ", problems);
        TempData.Orange(OrangeFeedback.Toast(created.Count == 1 ? "Clarification sent!" : $"{created.Count} clarifications sent!",
            created.Count == 1 ? "The recipient was notified." : "Each recipient was notified in their own conversation.", OrangeVariant.Send));
        // The new conversations appear in the table of the list page.
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Resolve(int id)
    {
        if (!await clarifications.ResolveAsync(CurrentUserId!, id)) return NotFound();
        TempData.Orange(OrangeFeedback.Toast("Clarification resolved", "The conversation is marked as resolved.", OrangeVariant.Approve));
        return RedirectToAction(nameof(Details), new { id });
    }
}
