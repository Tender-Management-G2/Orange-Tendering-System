using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models.Entities;

namespace Orange_Tendering_System.Services;

// Global event spotlight: picks the signed-in user's own, real, not-yet-acknowledged notifications and classifies them.
// A tender win is shown only when SupplierResultDelivery records Outcome "Won" for this recipient, offer and company and
// the tender is Awarded, so the celebration can never be triggered by text alone or reveal another supplier's tender.
public sealed record SpotlightEvent(int Id, string Kind, string Title, string Message, string? Link, DateTime CreatedAt,
    string? TenderTitle = null, string? TenderReference = null);

public sealed class EventSpotlightService(ApplicationDbContext db)
{
    public const int WindowDays = 14;
    public const int MaxCards = 3;
    public static readonly string[] Kinds = ["win", "approval", "rejection", "message", "returned", "action"];

    public static string Classify(string title, string message)
    {
        var t = (title + " " + message).ToLowerInvariant();
        bool Has(params string[] words) => words.Any(t.Contains);
        if (Has("returned", "return for review", "re-tender", "retender", "sent back")) return "returned";
        if (Has("reject", "declin", "not selected", "unsuccessful", "cancel", "non-compliant", "refused")) return "rejection";
        if (Has("approv", "accepted", "awarded", "verified", "congratulations")) return "approval";
        if (Has("clarification", "reply", "message", "question", "answer")) return "message";
        if (Has("ready", "required", "awaiting", "please", "submitted", "review", "invitation", "invited", "deadline", "sign")) return "action";
        return "info";
    }

    // Only relative in-app links are returned to the browser.
    public static string? SafeLink(string? link) =>
        link is { Length: > 1 } && link[0] == '/' && link[1] != '/' && link[1] != '\\' ? link : null;

    public async Task<List<SpotlightEvent>> PendingAsync(string userId, CancellationToken ct = default)
    {
        var since = DateTime.UtcNow.AddDays(-WindowDays);
        var notes = await db.Notifications.AsNoTracking()
            .Where(n => n.UserId == userId && n.CreatedAt >= since
                && !db.NotificationAcknowledgements.Any(a => a.UserId == userId && a.NotificationId == n.Id))
            .OrderByDescending(n => n.CreatedAt).Take(30)
            .Select(n => new { n.Id, n.Title, n.Message, n.Link, n.IsRead, n.CreatedAt }).ToListAsync(ct);
        if (notes.Count == 0) return [];

        var supplierId = await db.Users.Where(u => u.Id == userId && u.IsActive).Select(u => u.SupplierId).SingleOrDefaultAsync(ct);
        var wins = supplierId == null ? [] : await (
            from d in db.SupplierResultDeliveries.AsNoTracking()
            join t in db.Tenders.AsNoTracking() on d.TenderId equals t.Id
            where d.RecipientUserId == userId && d.Outcome == "Won" && d.SupplierId == supplierId
                && t.Status == TenderStatus.Awarded && t.AwardedBidId == d.BidId
            select new { d.BidId, d.TenderId, t.Title }).ToListAsync(ct);

        var result = new List<SpotlightEvent>();
        foreach (var n in notes)
        {
            var win = n.Title == "Tender result" ? wins.FirstOrDefault(w => n.Link == $"/Supplier/Offers/Details/{w.BidId}") : null;
            if (win != null)
            {
                result.Add(new SpotlightEvent(n.Id, "win", n.Title, n.Message, $"/Supplier/Offers/Details/{win.BidId}", n.CreatedAt,
                    win.Title, $"T-{win.TenderId:0000}"));
                continue;
            }
            if (n.IsRead) continue;                 // already seen in the bell or history
            var kind = Classify(n.Title, n.Message);
            if (kind == "info") continue;
            result.Add(new SpotlightEvent(n.Id, kind, n.Title, n.Message, SafeLink(n.Link), n.CreatedAt));
        }
        // At most one celebration (the newest win), then a few event cards, newest first.
        var top = result.Where(e => e.Kind == "win").Take(1).ToList();
        top.AddRange(result.Where(e => e.Kind != "win").Take(MaxCards));
        return top;
    }

    public async Task<bool> AcknowledgeAsync(string userId, int notificationId, CancellationToken ct = default)
    {
        var note = await db.Notifications.AsNoTracking().Where(n => n.Id == notificationId && n.UserId == userId)
            .Select(n => new { n.Title, n.Message }).SingleOrDefaultAsync(ct);
        if (note == null) return false;              // not the caller's notification
        if (await db.NotificationAcknowledgements.AnyAsync(a => a.UserId == userId && a.NotificationId == notificationId, ct)) return true;
        var pending = await PendingAsync(userId, ct);
        var kind = pending.FirstOrDefault(p => p.Id == notificationId)?.Kind ?? Classify(note.Title, note.Message);
        db.NotificationAcknowledgements.Add(new NotificationAcknowledgement
        {
            UserId = userId, NotificationId = notificationId, Kind = kind.Length > 20 ? kind[..20] : kind, AcknowledgedAt = DateTime.UtcNow
        });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { /* concurrent acknowledgement from another tab: the unique index already holds it */ }
        return true;
    }
}
