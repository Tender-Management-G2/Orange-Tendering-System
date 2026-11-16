using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orange_Tendering_System.Services;

namespace Orange_Tendering_System.Controllers;

// Global event spotlight endpoints (all signed-in roles). Every query is scoped to the caller's own notifications.
// Rejections carry a JSON body so the HTML status-page middleware does not re-execute the POST (which would become 405).
[Authorize, Route("Spotlight"), AgentAntiforgeryFailure]
public sealed class SpotlightController(EventSpotlightService spotlight) : Controller
{
    [HttpGet("Pending")]
    public async Task<IActionResult> Pending(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var events = await spotlight.PendingAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!, ct);
        return Json(events.Select(e => new
        {
            id = e.Id, kind = e.Kind, title = e.Title, message = e.Message, link = e.Link,
            createdAt = DateTime.SpecifyKind(e.CreatedAt, DateTimeKind.Utc), tenderTitle = e.TenderTitle, tenderReference = e.TenderReference
        }));
    }

    [HttpPost("Acknowledge/{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Acknowledge(int id, CancellationToken ct) =>
        await spotlight.AcknowledgeAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!, id, ct) ? Json(new { ok = true }) : NotFound(new { error = "Notification not found." });
}
