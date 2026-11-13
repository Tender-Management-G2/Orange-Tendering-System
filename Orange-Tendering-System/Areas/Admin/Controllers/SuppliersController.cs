// ORANGE TENDER MANAGEMENT SYSTEM
// Owner: Rjoub
// Module: Administration
// Purpose: MVC endpoints for the Suppliers pages of the Admin workspace.
// Status: Scaffold only. GET actions return views; no POST actions, no database access, no business logic.



using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.Services;
using Orange_Tendering_System.ViewModels;

namespace Orange_Tendering_System.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = SystemRoles.Admin)]
    public class SuppliersController : Controller
    {
        private readonly AdminService _adminService;
        private readonly UserManager<ApplicationUser> _userManager;

        public SuppliersController(
            AdminService adminService,
            UserManager<ApplicationUser> userManager)
        {
            _adminService = adminService;
            _userManager = userManager;
        }

        // GET: /Admin/Suppliers
        [HttpGet]
        public async Task<IActionResult> Index(
            string? search,
            string? statusFilter,
            int page = 1)
        {
            var model = await _adminService.GetSuppliersAsync(
                search, statusFilter, page);

            return View(model);
        }

        // GET: /Admin/Suppliers/Details/5
        [HttpGet]
        public async Task<IActionResult> Details(int? id)
        {
            if (!id.HasValue || id <= 0)
                return NotFound();

            var model = await _adminService
                .GetSupplierDetailsAsync(id.Value);

            if (model == null)
                return NotFound();

            return View(model);
        }

        // GET: /Admin/Suppliers/Review/5
        [HttpGet]
        public async Task<IActionResult> Review(int? id)
        {
            if (!id.HasValue || id <= 0)
                return NotFound();

            var model = await _adminService
                .GetSupplierDetailsAsync(id.Value);

            if (model == null)
                return NotFound();

            return View(model);
        }

        // POST: Verify legal document
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> VerifyDocument(
            SupplierDocumentReviewViewModel model)
        {
            if (!ModelState.IsValid)
            {
                TempData["ErrorMessage"] =
                    "Please enter valid document review details.";

                return RedirectToAction(nameof(Review),
                    new { id = model.SupplierId });
            }

            var adminId = _userManager.GetUserId(User);

            if (string.IsNullOrEmpty(adminId))
                return Challenge();

            var result = await _adminService
                .ReviewSupplierDocumentAsync(model, adminId);

            TempData[result == "Success"
                ? "SuccessMessage"
                : "ErrorMessage"] = result switch
                {
                    "Success" => "Document review saved successfully.",
                    "NotFound" => "Document or supplier not found.",
                    "ReasonRequired" => "A rejection reason is required.",
                    "DocumentExpired" =>
                        "An expired document cannot be verified.",
                    "SupplierApproved" =>
                        "Suspend the supplier before changing document verification.",
                    _ => "Unable to review the document."
                };

            return RedirectToAction(nameof(Review),
                new { id = model.SupplierId });
        }

        // POST: Approve / Reject / Suspend Supplier
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SubmitReview(
            SupplierReviewDecisionViewModel model)
        {
            if (!ModelState.IsValid)
            {
                TempData["ErrorMessage"] =
                    "Please enter valid supplier review details.";

                return RedirectToAction(nameof(Review),
                    new { id = model.SupplierId });
            }

            var adminId = _userManager.GetUserId(User);

            if (string.IsNullOrEmpty(adminId))
                return Challenge();

            var result = await _adminService
                .ReviewSupplierAsync(model, adminId);

            TempData[result == "Success"
                ? "SuccessMessage"
                : "ErrorMessage"] = result switch
                {
                    "Success" =>
                        "Supplier review completed successfully.",

                    "NotFound" =>
                        "Supplier not found.",

                    "ReasonRequired" =>
                        "A reason is required for rejection or suspension.",

                    "DocumentsRequired" =>
                        "Supplier must upload legal documents before approval.",

                    "DocumentsNotVerified" =>
                        "All submitted documents must be verified and valid.",

                    "InvalidTransition" =>
                        "This status change is not allowed.",

                    _ => "Unable to complete supplier review."
                };

            return RedirectToAction(nameof(Review),
                new { id = model.SupplierId });
        }
    }
}
