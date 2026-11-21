// ORANGE TENDER MANAGEMENT SYSTEM
// Module: Evaluation meetings. "Join inside the system" for the Officer and the four Evaluation Committee members of a tender.
// The page is always reachable for them: before the start it is the meeting lobby (details, countdown, link to the evaluation
// workspace with offers, criteria, documents and AI analysis); the video room and the scoring actions open at the start time.
// Page access and permission to act are separate: scoring/voting are still enforced by TenderWorkflowService on the server.
// Jitsi meetings (created automatically, no credentials needed) are embedded with the Jitsi external API; Zoom meetings open the Zoom link.
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.Services;

namespace Orange_Tendering_System.Controllers
{
    [Authorize]
    public class MeetingsController(ApplicationDbContext db, TenderWorkflowService workflow) : Controller
    {
        public enum MeetingPhase { Before, Live, After }
        public sealed record MeetingInfo(int TenderId, string Title, string Url, string Provider, string? Room, DateTime StartsAt, DateTime EndsAt, string DisplayName,
            MeetingPhase Phase, bool IsEvaluator, bool IsOfficer, bool Submitted, int SubmittedCount, int EligibleOffers, string SessionState, DateTime UtcNow);

        [HttpGet]
        public async Task<IActionResult> Join(int id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var actor = await workflow.Actor(User);
            var officer = await workflow.IsOfficer(actor);
            var seat = await db.CommitteeMembers.AsNoTracking().SingleOrDefaultAsync(m => m.TenderId == id && m.UserId == userId && m.CommitteeType == CommitteeType.Evaluation);
            var evaluator = seat != null && (await workflow.Role(actor, SystemRoles.EvaluationCommittee) ||
                seat.Responsibility == CommitteeResponsibility.SourcingManager && await workflow.Role(actor, SystemRoles.SourcingManager));
            if (!officer && !evaluator) return NotFound();
            var round = await db.TenderRounds.AsNoTracking().Where(r => r.TenderId == id).OrderByDescending(r => r.RoundNumber).FirstOrDefaultAsync();
            if (round == null) return NotFound();
            var session = await db.Set<EvaluationSession>().AsNoTracking().Include(s => s.Invitations).Include(s => s.EligibleBids).FirstOrDefaultAsync(s => s.TenderRoundId == round.Id);
            if (session == null || string.IsNullOrWhiteSpace(session.MeetingUrl)) return NotFound();
            var title = await db.Tenders.AsNoTracking().Where(t => t.Id == id).Select(t => t.Title).SingleAsync();
            var name = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.FullName).SingleAsync();
            var now = workflow.Now;
            // Compared in UTC on the server (StartsAt/EndsAt are stored in UTC); displayed in Jordan time.
            var phase = now < session.StartsAt ? MeetingPhase.Before : now < session.EndsAt ? MeetingPhase.Live : MeetingPhase.After;
            var uri = new Uri(session.MeetingUrl);
            var provider = uri.Host == "meet.jit.si" ? "Jitsi" : "Zoom";
            var submitted = seat != null && session.Invitations.Any(x => x.CommitteeMemberId == seat.Id && x.SubmittedAt != null);
            Response.Headers.CacheControl = "no-store";
            return View(new MeetingInfo(id, title, session.MeetingUrl, provider, provider == "Jitsi" ? uri.AbsolutePath.Trim('/') : null, session.StartsAt, session.EndsAt,
                string.IsNullOrWhiteSpace(name) ? "Participant" : name, phase, evaluator, officer, submitted, session.Invitations.Count(x => x.SubmittedAt != null),
                session.EligibleBids.Count, session.State.ToString(), now));
        }
    }
}
