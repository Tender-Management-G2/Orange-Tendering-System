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
        private readonly SupplierReviewService _supplierReviewService;
        private readonly UserManager<ApplicationUser> _userManager;

        public SuppliersController(
            AdminService adminService,
            SupplierReviewService supplierReviewService,
            UserManager<ApplicationUser> userManager)
        {
            _adminService = adminService;
            _supplierReviewService = supplierReviewService;
            _userManager = userManager;
        }

        [HttpGet]
        public async Task<IActionResult> Document(int id)
        {
            try
            {
                var (path, name) = await _supplierReviewService.Document(id, User);
                var contentType = Path.GetExtension(name).ToLowerInvariant() switch
                {
                    ".pdf" => "application/pdf",
                    ".png" => "image/png",
                    ".jpg" or ".jpeg" => "image/jpeg",
                    _ => "application/octet-stream"
                };
                Response.Headers["X-Content-Type-Options"] = "nosniff";
                Response.Headers["Content-Security-Policy"] = "default-src 'none'; sandbox";
                return PhysicalFile(path, contentType, enableRangeProcessing: false);
            }
            catch (WorkflowException)
            {
                return NotFound();
            }
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
            var wantsJson = Request.Headers.Accept == "application/json";
            if (!ModelState.IsValid)
            {
                const string validationMessage = "Please enter valid document review details.";
                if (wantsJson)
                    return BadRequest(new { message = validationMessage });

                TempData["ErrorMessage"] = validationMessage;

                return RedirectToAction(nameof(Review),
                    new { id = model.SupplierId });
            }

            var adminId = _userManager.GetUserId(User);

            if (string.IsNullOrEmpty(adminId))
                return Challenge();

            var result = await _adminService
                .ReviewSupplierDocumentAsync(model, adminId);

            var message = result switch
                {
                    "Success" => "Document review saved successfully.",
                    "NotFound" => "Document or supplier not found.",
                    "ReasonRequired" => "A rejection reason is required.",
                    "DocumentExpired" =>
                        "An expired document cannot be verified.",
                    "EmailUnverified" =>
                        "The supplier must verify its contact email before review.",
                    "SupplierApproved" =>
                        "Suspend the supplier before changing document verification.",
                    _ => "Unable to review the document."
                };

            if (wantsJson)
            {
                if (result != "Success")
                    return BadRequest(new { message });

                return Ok(new { message, status = model.Status.ToString() });
            }

            if (result == "Success")
            {
                TempData.Remove("SuccessMessage");
                TempData.Orange(model.Status == DocumentVerificationStatus.Verified
                    ? OrangeFeedback.Toast("Document verified", "The document review is saved.", OrangeVariant.Approve)
                    : OrangeFeedback.Toast("Document review saved", $"The document is marked {model.Status}.", OrangeVariant.Return));
            }
            else
            {
                TempData["ErrorMessage"] = message;
            }

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
                        "A commercial registration document is required before approval.",

                    "DocumentsUnavailable" =>
                        "A submitted document is unavailable. Ask the supplier to upload it again before approval.",

                    "DocumentsNotVerified" =>
                        "An expired document must be replaced before approval.",

                    "DocumentsRejected" =>
                        "A rejected document must be reviewed and verified before approval.",

                    "InvalidTransition" =>
                        "This status change is not allowed.",
                    "EmailUnverified" =>
                        "The supplier must verify its contact email before review.",

                    _ => "Unable to complete supplier review."
                };
            if (result == "Success")
            {
                TempData.Remove("SuccessMessage");
                TempData.Orange(model.Decision switch
                {
                    SupplierStatus.Approved => OrangeFeedback.Popup("Supplier approved!", "The supplier can now be invited to tenders.", OrangeVariant.Approve),
                    SupplierStatus.Rejected => OrangeFeedback.Popup("Supplier rejected", "The decision and its reason are recorded.", OrangeVariant.Return),
                    SupplierStatus.Suspended => OrangeFeedback.Popup("Supplier suspended", "The supplier cannot take part in tenders until reinstated.", OrangeVariant.Return),
                    _ => OrangeFeedback.Toast("Supplier review saved", null)
                });
            }

            return RedirectToAction(nameof(Review),
                new { id = model.SupplierId });
        }
    }
}
