// ORANGE TENDER MANAGEMENT SYSTEM
// Module: Evaluation meetings. "Join inside the system" for the Officer and the four Evaluation Committee members of a tender.
// Jitsi meetings (created automatically, no credentials needed) are embedded with the Jitsi external API (see meetingZoom sample);
// Zoom meetings open the Zoom link. Nobody else can open the page, so the room link is not shown to other roles.
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;

namespace Orange_Tendering_System.Controllers
{
    [Authorize]
    public class MeetingsController(ApplicationDbContext db) : Controller
    {
        public sealed record MeetingInfo(int TenderId, string Title, string Url, string Provider, string? Room, DateTime StartsAt, DateTime EndsAt, string DisplayName);

        [HttpGet]
        public async Task<IActionResult> Join(int id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var allowed = User.IsInRole(SystemRoles.SourcingOfficer) ||
                await db.CommitteeMembers.AnyAsync(m => m.TenderId == id && m.UserId == userId && m.CommitteeType == CommitteeType.Evaluation);
            if (!allowed) return NotFound();
            var round = await db.TenderRounds.AsNoTracking().Where(r => r.TenderId == id).OrderByDescending(r => r.RoundNumber).FirstOrDefaultAsync();
            if (round == null) return NotFound();
            var session = await db.Set<EvaluationSession>().AsNoTracking().FirstOrDefaultAsync(s => s.TenderRoundId == round.Id);
            if (session == null || string.IsNullOrWhiteSpace(session.MeetingUrl)) return NotFound();
            var title = await db.Tenders.AsNoTracking().Where(t => t.Id == id).Select(t => t.Title).SingleAsync();
            var name = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.FullName).SingleAsync();
            var uri = new Uri(session.MeetingUrl);
            if (uri.Host != "meet.jit.si") return Redirect(session.MeetingUrl);   // Zoom: open the Zoom link
            var room = uri.AbsolutePath.Trim('/');
            return View(new MeetingInfo(id, title, session.MeetingUrl, "Jitsi", room, session.StartsAt, session.EndsAt, string.IsNullOrWhiteSpace(name) ? "Participant" : name));
        }
    }
}
