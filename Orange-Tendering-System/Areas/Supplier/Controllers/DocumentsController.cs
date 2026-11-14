// ORANGE TENDER MANAGEMENT SYSTEM
// Owner: Saja
// Module: Supplier Portal
// Purpose: MVC endpoints for the Documents pages of the Supplier workspace.
// Status: Supplier-only data and workflow, with server-side ownership checks.
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Services;
using System.Security.Claims;

namespace Orange_Tendering_System.Areas.Supplier.Controllers
{
    [Area("Supplier")]
    [Authorize(Roles = SystemRoles.Supplier)]
    public class DocumentsController(SupplierDocumentService documents) : Controller
    {
        // Supplier Documents — Owner: Saja · No Trello card
        // GET /Supplier/Documents. Ownership is checked in the supplier service.
        public async Task<IActionResult> Index()
        {
            return View(await documents.GetCompanyDocumentsAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!));
        }

        public async Task<IActionResult> Download(int id)
        {
            var file = await documents.GetDownloadAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!, id);
            if (file == null) return NotFound();
            return PhysicalFile(file.Value.Path, file.Value.Type, file.Value.Name);
        }

        [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(12 * 1024 * 1024)]
        public async Task<IActionResult> UploadOffer(int id, IFormFile file)
        {
            var error = await documents.UploadAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!, file, bidId: id);
            TempData[error == null ? "OfferSuccess" : "OfferError"] = error ?? "Attachment uploaded.";
            return RedirectToAction("Details", "Offers", new { id });
        }

        [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(12 * 1024 * 1024)]
        public async Task<IActionResult> UploadCompany(IFormFile file, bool returnToProfile = false)
        {
            var error = await documents.UploadAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!, file);
            TempData["DocumentMessage"] = error ?? "Document uploaded for administrator review.";
            return returnToProfile ? RedirectToAction("Index", "Profile") : RedirectToAction("Index");
        }

        [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(12 * 1024 * 1024)]
        public async Task<IActionResult> UploadClarification(int messageId, IFormFile file)
        {
            var error = await documents.UploadAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!, file, messageId: messageId);
            TempData["ClarificationMessage"] = error ?? "Clarification attachment uploaded.";
            return RedirectToAction("Index", "Clarifications");
        }
    }
}
