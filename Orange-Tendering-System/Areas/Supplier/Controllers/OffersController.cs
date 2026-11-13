// ORANGE TENDER MANAGEMENT SYSTEM
// Owner: Saja
// Module: Supplier Portal
// Purpose: MVC endpoints for the Offers pages of the Supplier workspace.
// Status: Own offers, draft creation/editing, final submission, details, history and summary.
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Services;
using Orange_Tendering_System.ViewModels;

namespace Orange_Tendering_System.Areas.Supplier.Controllers
{
    [Area("Supplier")]
    [Authorize(Roles = SystemRoles.Supplier)]
    public class OffersController(SupplierOfferService offerService) : Controller
    {
        // My Submitted Offers — Owner: Saja · No Trello card
        // GET /Supplier/Offers. Ownership is checked in the supplier service.
        public async Task<IActionResult> Index()
        {
            return View(await offerService.GetOffersAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!));
        }

        // Create Supplier Offer — Owner: Saja
        // GET: open an accepted invitation. POST: save a new draft only.
        [HttpGet]
        public async Task<IActionResult> Create(int? id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Challenge();
            if (id == null || id <= 0)
                return NotFound();

            var model = await offerService.GetCreateAsync(userId, id.Value);
            if (model == null)
                return NotFound();

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(int id,
            [Bind(Prefix = "Input")] SupplierDraftOfferInputModel input)
        {
            if (input.Items == null || input.Items.Any(item => item == null))
                return BadRequest();
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Challenge();
            if (id <= 0)
                return NotFound();

            var model = await offerService.GetCreateAsync(userId, id);
            if (model == null)
                return NotFound();

            if (!ModelState.IsValid)
                return View(model);

            bool saved = await offerService.CreateDraftAsync(userId, id, input);
            if (saved == false)
            {
                ModelState.AddModelError("", "Draft not saved. Check the invitation, deadline and tender items.");
                return View(model);
            }

            TempData["OfferSuccess"] = "Your draft offer was saved. It has not been submitted.";
            return RedirectToAction("Index");
        }

        // Edit Draft Offer — Owner: Saja · No Trello card
        // GET /Supplier/Offers/Edit/{id?}. Ownership is checked in the supplier service.
        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var model = await offerService.GetEditAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!, id);
            return model == null ? NotFound() : View("Edit", model);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind(Prefix = "Input")] SupplierDraftOfferInputModel input)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var model = await offerService.GetEditAsync(userId, id);
            if (model == null) return NotFound();
            if (input.Items == null || input.Items.Any(i => i == null)) return BadRequest();
            if (!ModelState.IsValid) return View("Edit", model);
            if (!await offerService.UpdateDraftAsync(userId, id, input))
            {
                ModelState.AddModelError("", "Draft not saved. Check the items and submission deadline.");
                return View("Edit", model);
            }
            TempData["OfferSuccess"] = "Draft updated.";
            return RedirectToAction("Details", new { id });
        }

        // Offer Details — Owner: Saja · No Trello card
        // GET /Supplier/Offers/Details/{id?}. Ownership is checked in the supplier service.
        public async Task<IActionResult> Details(int id)
        {
            var model = await offerService.GetOfferAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!, id);
            return model == null ? NotFound() : View(model);
        }

        // Supplier Offer History — Owner: Saja · No Trello card
        // GET /Supplier/Offers/History/{id?}. Ownership is checked in the supplier service.
        public async Task<IActionResult> History(int id)
        {
            var model = await offerService.GetHistoryAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!, id);
            return model == null ? NotFound() : View(model);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Submit(int id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
            if (await offerService.GetOfferAsync(userId, id) == null) return NotFound();
            var error = await offerService.SubmitAsync(userId, id);
            TempData[error == null ? "OfferSuccess" : "OfferError"] = error ?? "Offer submitted successfully. It can no longer be edited.";
            return RedirectToAction("Details", new { id });
        }

        public async Task<IActionResult> DownloadSummary()
        {
            var offers = await offerService.GetOffersAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var lines = offers.Select(o => $"Offer #{o.Id} | {o.TenderTitle} | Revision {o.Revision} | {o.Status} | {o.Total:0.00} {o.Terms.CurrencyCode}");
            var summary = "My offer summary (UTC)\n" + string.Join("\n", lines);
            return File(System.Text.Encoding.UTF8.GetBytes(summary), "text/plain; charset=utf-8", "my-offers-summary.txt");
        }
    }
}
