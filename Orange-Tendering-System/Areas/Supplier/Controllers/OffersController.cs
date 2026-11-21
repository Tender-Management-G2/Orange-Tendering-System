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

        // Save and submit use the same form; only submission requires all fields and documents.
        [HttpGet]
        public async Task<IActionResult> Create(int? id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId)) return Challenge();
            if (id == null || id <= 0) return NotFound();
            var model = await offerService.GetOfferFormAsync(userId, id.Value);
            if (model == null) return View("Unavailable", await offerService.GetCreateBlockReasonAsync(userId, id.Value));
            return View(model);
        }

        [HttpPost, ValidateAntiForgeryToken]
        [RequestSizeLimit(45 * 1024 * 1024), RequestFormLimits(MultipartBodyLengthLimit = 45 * 1024 * 1024)]
        public async Task<IActionResult> Create(int id, [Bind(Prefix = "Input")] SupplierDraftOfferInputModel input)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId)) return Challenge();
            if (id <= 0 || input.Items == null || input.Items.Any(item => item == null)) return BadRequest();
            var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => string.IsNullOrWhiteSpace(e.ErrorMessage) ? "A field has an invalid value." : e.ErrorMessage).Distinct().ToList();
            var (files, fileErrors) = await ReadFilesAsync(input);
            errors.AddRange(fileErrors);
            if (errors.Count == 0)
            {
                var (bidId, problems) = await offerService.SubmitOfferAsync(userId, id, input, files);
                if (bidId is int submitted)
                {
                    TempData.Orange(OrangeFeedback.Popup("Offer submitted!", "Your offer and its documents were sent to the Sourcing Officer. This version can no longer be changed.", OrangeVariant.Send));
                    return RedirectToAction("Details", new { id = submitted });
                }
                errors.AddRange(problems);
            }
            var model = await offerService.GetOfferFormAsync(userId, id);
            if (model == null) return View("Unavailable", await offerService.GetCreateBlockReasonAsync(userId, id));
            model.Input = input; model.Errors = errors;
            // Files are never kept across a refused submission; the page asks the supplier to choose them again.
            return View(model);
        }

        [HttpPost, ValidateAntiForgeryToken]
        [RequestSizeLimit(45 * 1024 * 1024), RequestFormLimits(MultipartBodyLengthLimit = 45 * 1024 * 1024)]
        public async Task<IActionResult> SaveDraft(int id, [Bind(Prefix = "Input")] SupplierDraftOfferInputModel input)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId)) return Challenge();
            if (id <= 0 || input.Items == null || input.Items.Any(item => item == null)) return BadRequest();
            // An empty price or cost is allowed in a draft; an invalid nonempty value is not.
            var errors = ModelState.Where(entry => !string.IsNullOrWhiteSpace(entry.Value?.AttemptedValue))
                .SelectMany(entry => entry.Value!.Errors)
                .Select(error => string.IsNullOrWhiteSpace(error.ErrorMessage) ? "A field has an invalid value." : error.ErrorMessage)
                .Distinct().ToList();
            var (files, fileErrors) = await ReadFilesAsync(input);
            errors.AddRange(fileErrors);
            if (errors.Count == 0)
            {
                var (bidId, problems) = await offerService.SaveDraftAsync(userId, id, input, files);
                if (bidId != null)
                {
                    TempData["OfferDraftSaved"] = "Draft saved. You can return and complete it before the submission deadline.";
                    return RedirectToAction(nameof(Create), new { id });
                }
                errors.AddRange(problems);
            }
            var model = await offerService.GetOfferFormAsync(userId, id);
            if (model == null) return View("Unavailable", await offerService.GetCreateBlockReasonAsync(userId, id));
            model.Input = input; model.Errors = errors;
            return View("Create", model);
        }

        private async Task<(List<SupplierOfferService.UploadedOfferFile> Files, List<string> Errors)> ReadFilesAsync(SupplierDraftOfferInputModel input)
        {
            var files = new List<SupplierOfferService.UploadedOfferFile>();
            var errors = new List<string>();
            foreach (var file in Request.Form.Files)
            {
                if (!file.Name.StartsWith("doc_", StringComparison.Ordinal)) continue;
                var documentType = file.Name[4..];
                var storedName = file.FileName;
                var itemImage = documentType.StartsWith("ReferenceImage_", StringComparison.Ordinal);
                if (itemImage)
                {
                    if (!int.TryParse(documentType["ReferenceImage_".Length..], out var tenderItemId)
                        || input.Items.All(item => item.TenderItemId != tenderItemId))
                    { errors.Add("A product image refers to an item outside this offer."); continue; }
                    documentType = "ReferenceImage";
                    storedName = $"Item-{tenderItemId}-{Path.GetFileName(file.FileName)}";
                }
                if (file.Length > SupplierOfferService.MaxFileBytes) { errors.Add($"{Path.GetFileName(file.FileName)}: files must be 10 MB or smaller."); continue; }
                await using var stream = new MemoryStream();
                await file.CopyToAsync(stream);
                var (upload, error) = SupplierOfferService.ReadUpload(documentType, storedName, stream.ToArray());
                if (upload != null && (!itemImage || upload.ContentType.StartsWith("image/", StringComparison.Ordinal))) files.Add(upload);
                else errors.Add(upload != null ? "Product images must be valid PNG or JPEG files." : error!);
            }
            return (files, errors);
        }

        // Older "Edit draft" links: the draft opens in the offer form for its invitation.
        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var offer = await offerService.GetOfferAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!, id);
            return offer == null ? NotFound() : offer.CanEdit ? RedirectToAction(nameof(Create), new { id = offer.InvitationId }) : RedirectToAction(nameof(Details), new { id });
        }

        // Offer Details — Owner: Saja · No Trello card
        // GET /Supplier/Offers/Details/{id?}. Ownership is checked in the supplier service.
        public async Task<IActionResult> Details(int id)
        {
            var model = await offerService.GetOfferAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!, id);
            if (model == null) return NotFound();
            // The My Offers page opens details in a popup; direct links (notifications, bookmarks) keep the full page.
            if (string.Equals(Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase))
            {
                Response.Headers.CacheControl = "no-store";
                return PartialView("_OfferDetailsPopup", model);
            }
            return View(model);
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
            if (error == null) TempData.Orange(OrangeFeedback.Popup("Offer submitted!", "Your offer is recorded and can no longer be edited."));
            else TempData["OfferError"] = error;
            return RedirectToAction("Details", new { id });
        }

        public async Task<IActionResult> DownloadSummary()
        {
            var offers = await offerService.GetOffersAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var lines = offers.Select(o => $"Offer #{o.Id} | {o.TenderTitle} | Revision {o.Revision} | {o.Status} | {o.Total:0.00} {o.Terms.CurrencyCode}");
            var summary = "My offer summary (Jordan time)\n" + string.Join("\n", lines);
            return File(System.Text.Encoding.UTF8.GetBytes(summary), "text/plain; charset=utf-8", "my-offers-summary.txt");
        }
    }
}
