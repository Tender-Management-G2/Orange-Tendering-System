// ORANGE TENDER MANAGEMENT SYSTEM
// Owner: Saja
// Module: Supplier Portal
// Purpose: MVC endpoints for the Clarifications pages of the Supplier workspace.
// Status: Supplier-only data and workflow, with server-side ownership checks.
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Services;
using Orange_Tendering_System.ViewModels;
using System.Security.Claims;

namespace Orange_Tendering_System.Areas.Supplier.Controllers
{
    [Area("Supplier")]
    [Authorize(Roles = SystemRoles.Supplier)]
    public class ClarificationsController(SupplierCommunicationService communication) : Controller
    {
        // Supplier Clarifications — Owner: Saja · No Trello card
        // GET /Supplier/Clarifications. Ownership is checked in the supplier service.
        public async Task<IActionResult> Index()
        {
            return View(await communication.GetThreadsAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!));
        }

        public async Task<IActionResult> Details(int id)
        {
            var model = await communication.GetThreadAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!, id);
            return model == null ? NotFound() : View(model);
        }

        [HttpGet]
        public async Task<IActionResult> Create(int? bidId, int? invitationId)
        {
            if (!await communication.CanCreateAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!, bidId, invitationId)) return NotFound();
            return View(new SupplierClarificationInputModel { BidId = bidId, InvitationId = invitationId });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(SupplierClarificationInputModel input)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
            if (!await communication.CanCreateAsync(userId, input.BidId, input.InvitationId)) return NotFound();
            if (!ModelState.IsValid) return View(input);
            var id = await communication.CreateAsync(userId, input);
            if (id == null) return NotFound();
            return RedirectToAction("Details", new { id });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Reply(int id, string message)
        {
            if (!ModelState.IsValid || !await communication.ReplyAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!, id, message))
            {
                TempData["ClarificationMessage"] = "Reply not saved. Check the message and whether this clarification is resolved.";
                return RedirectToAction("Details", new { id });
            }
            TempData["ClarificationMessage"] = "Reply sent to the Sourcing Officer.";
            return RedirectToAction("Details", new { id });
        }
    }
}
