// ORANGE TENDER MANAGEMENT SYSTEM
// Owner: Saja
// Module: Supplier Portal
// Purpose: MVC endpoints for the Invitations pages of the Supplier workspace.
// Status: Own invitation list, details and accept/decline responses.
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Services;
using Orange_Tendering_System.Models.Entities;

namespace Orange_Tendering_System.Areas.Supplier.Controllers
{
    [Area("Supplier")]
    [Authorize(Roles = SystemRoles.Supplier)]
    public class InvitationsController(SupplierInvitationService invitationService) : Controller
    {
        // GET /Supplier/Invitations: عرض دعوات شركة المستخدم فقط.
        public async Task<IActionResult> Index(string? search, string? status)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Challenge();
            }

            if (search?.Length > 200) return BadRequest();
            var model = await invitationService.GetInvitationsAsync(userId, search, status);
            if (model.AccessMessage != null)
            {
                Response.StatusCode = StatusCodes.Status403Forbidden;
            }

            return View(model);
        }

        // تفاصيل دعوة تخص شركة المستخدم فقط.
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null || id <= 0)
            {
                return NotFound();
            }

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Challenge();
            }

            var model = await invitationService.GetDetailsAsync(userId, id.Value);
            if (model == null)
            {
                return NotFound();
            }

            return View(model);
        }

        // Accept / Decline Invitation — Owner: Saja
        // GET: preview response options for an owned invitation; POST: save the response.
        [HttpGet]
        public async Task<IActionResult> Respond(int? id)
        {
            if (id == null || id <= 0)
                return NotFound();

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Challenge();

            var model = await invitationService.GetDetailsAsync(userId, id.Value);
            if (model == null)
                return NotFound();

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Respond(int id, InvitationStatus decision)
        {
            if (!ModelState.IsValid || id <= 0
                || (decision != InvitationStatus.Accepted && decision != InvitationStatus.Declined))
                return BadRequest();

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Challenge();

            var invitation = await invitationService.GetDetailsAsync(userId, id);
            if (invitation == null)
                return NotFound();

            bool saved = await invitationService.RespondAsync(userId, id, decision);
            if (saved == false)
            {
                TempData["InvitationError"] = "Response not saved. The invitation has already been answered or is not open for responses.";
                return RedirectToAction("Details", new { id });
            }

            TempData["InvitationSuccess"] = decision == InvitationStatus.Accepted
                ? "Invitation accepted. Submitting an offer is a separate step."
                : "Invitation declined.";
            return RedirectToAction("Details", new { id });
        }
    }
}
