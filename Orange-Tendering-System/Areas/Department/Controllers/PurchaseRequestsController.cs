// Owner: Doulat — Requesting Department only.
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Areas.Department.Services;
using Orange_Tendering_System.Areas.Department.ViewModels;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;

namespace Orange_Tendering_System.Areas.Department.Controllers;

[Area("Department")]
[Authorize(Roles = SystemRoles.RequestingDepartment)]
public class PurchaseRequestsController : Controller
{
    private readonly ApplicationDbContext db;
    private readonly UserManager<ApplicationUser> users;
    private readonly DepartmentRequestService service;
    private readonly string storageRoot;
    private readonly ILogger<PurchaseRequestsController> logger;

    public PurchaseRequestsController(ApplicationDbContext db, UserManager<ApplicationUser> users,
        IWebHostEnvironment environment, ILogger<PurchaseRequestsController> logger)
    {
        this.db = db;
        this.users = users;
        this.logger = logger;
        service = new(db, users);
        storageRoot = Path.Combine(environment.ContentRootPath, "Areas", "Department", "App_Data", "Uploads");
    }

    private async Task<ApplicationUser?> DepartmentUser()
    {
        var user = await users.GetUserAsync(User);
        return user is { IsActive: true, DepartmentId: not null } &&
            await db.Departments.AnyAsync(d => d.Id == user.DepartmentId && d.IsActive) ? user : null;
    }

    public async Task<IActionResult> Index(string? search, string? status, RequestPriority? priority, int page = 1)
    {
        var user = await DepartmentUser();
        if (user is null) return Forbid();
        var query = service.Requests(user.DepartmentId!.Value).AsNoTracking();
        search = search?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            var reference = search.StartsWith("PR-", StringComparison.OrdinalIgnoreCase) ? search[3..] : search;
            int.TryParse(reference, out var id);
            query = query.Where(r => r.Title.Contains(search) || r.Id == id);
        }
        if (!string.IsNullOrEmpty(status))
        {
            if (!RequestDisplay.Statuses.Contains(status)) return BadRequest();
            query = status switch
            {
                "Converted to Tender" => query.Where(r => r.Tenders.Any() || r.Status == PurchaseRequestStatus.ConvertedToTender),
                "Accepted" => query.Where(r => !r.Tenders.Any() && (r.Status == PurchaseRequestStatus.Accepted || r.Status == PurchaseRequestStatus.Approved)),
                _ => query.Where(r => !r.Tenders.Any() && r.Status == Enum.Parse<PurchaseRequestStatus>(status.Replace(" ", "")))
            };
        }
        if (priority.HasValue)
        {
            if (!Enum.IsDefined(priority.Value)) return BadRequest();
            query = query.Where(r => r.Priority == priority);
        }
        var total = await query.CountAsync();
        page = Math.Clamp(page, 1, Math.Max(1, (int)Math.Ceiling(total / 20m)));
        var model = new RequestList
        {
            Search = search, Status = status, Priority = priority, Page = page, Total = total, UserId = user.Id,
            Requests = await query.OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id)
                .Skip((page - 1) * 20).Take(20).ToListAsync()
        };
        if (string.Equals(Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase))
        {
            Response.Headers.CacheControl = "no-store";
            return PartialView("_MyRequestResults", model);
        }
        return View(model);
    }

    public async Task<IActionResult> Details(int id)
    {
        var user = await DepartmentUser();
        if (user is null) return Forbid();
        var request = await service.Load(user.DepartmentId!.Value, id);
        if (request is null) return NotFound();
        return View(new RequestDetails
        {
            Request = request, CanEdit = RequestDisplay.Editable(request, user.Id),
            Clarifications = await db.Clarifications.AsNoTracking().Include(c => c.Messages)
                .Where(c => c.PurchaseRequestId == id && (c.FromUserId == user.Id || c.ToUserId == user.Id))
                .OrderByDescending(c => c.CreatedAt).ToListAsync()
        });
    }

    public async Task<IActionResult> Create()
    {
        if (await DepartmentUser() is null) return Forbid();
        return View(new RequestForm());
    }

    public async Task<IActionResult> Edit(int id)
    {
        var user = await DepartmentUser();
        if (user is null) return Forbid();
        var request = await service.Load(user.DepartmentId!.Value, id);
        if (request is null) return NotFound();
        if (!RequestDisplay.Editable(request, user.Id)) return Forbid();
        return View(DepartmentRequestService.Form(request));
    }

    [HttpPost, ValidateAntiForgeryToken]
    [RequestSizeLimit(12 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 12 * 1024 * 1024)]
    public Task<IActionResult> Create(RequestForm form, string intent) => Save(form, intent, false, 0);

    [HttpPost, ValidateAntiForgeryToken]
    [RequestSizeLimit(12 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 12 * 1024 * 1024)]
    public Task<IActionResult> Edit(int id, RequestForm form, string intent) => Save(form, intent, true, id);

    private async Task<IActionResult> Save(RequestForm form, string intent, bool edit, int id)
    {
        var user = await DepartmentUser();
        if (user is null) return Forbid();
        if ((!edit && form.Id != 0) || (edit && (id <= 0 || form.Id != id))) return BadRequest();
        if (intent is not ("save" or "submit")) return BadRequest();
        if (edit)
        {
            var request = await service.Load(user.DepartmentId!.Value, id);
            if (request is null) return NotFound();
            if (!RequestDisplay.Editable(request, user.Id)) return Forbid();
            form.ExistingDocuments = request.Documents.ToList();
            form.ReturnComment = DepartmentRequestService.Form(request).ReturnComment;
            db.ChangeTracker.Clear();
        }
        form.Items ??= [];
        form.Attachments ??= [];
        if (form.Items.Count is < 1 or > 100)
            ModelState.AddModelError("Items", "Add between 1 and 100 items.");
        for (var i = 0; i < form.Items.Count; i++)
            if (decimal.Round(form.Items[i].Quantity, 3) != form.Items[i].Quantity)
                ModelState.AddModelError($"Items[{i}].Quantity", "Use at most three decimal places.");
        if (form.NeededBy?.Date < DateTime.UtcNow.Date && intent == "submit")
            ModelState.AddModelError("NeededBy", "Choose today or a future needed-by date when submitting.");
        await ValidateAttachments(form.Attachments);
        if (!ModelState.IsValid) return View(edit ? "Edit" : "Create", form);
        try
        {
            var savedId = await service.Save(user, form, intent == "submit", storageRoot);
            TempData["DepartmentMessage"] = intent == "submit"
                ? "Request submitted directly to the Sourcing Officer." : "Changes saved.";
            return RedirectToAction(nameof(Details), new { id = savedId });
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "Department request could not be saved");
            ModelState.AddModelError("", "The request could not be saved. It may have changed; reload it and try again.");
        }
        catch (Exception ex) when (ex is DbUpdateException or System.Data.Common.DbException or IOException)
        {
            logger.LogError(ex, "Department request save failed");
            ModelState.AddModelError("", "Saving failed. Your changes were not committed. Please try again.");
        }
        return View(edit ? "Edit" : "Create", form);
    }

    private async Task ValidateAttachments(List<IFormFile> files)
    {
        if (files.Count > 5 || files.Sum(f => f.Length) > 10 * 1024 * 1024)
        {
            ModelState.AddModelError("Attachments", "Upload at most five files, up to 10 MB combined.");
            return;
        }
        foreach (var file in files)
        {
            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (file.Length is <= 0 or > 5 * 1024 * 1024 ||
                Path.GetFileName(file.FileName).Length > 260 ||
                extension is not (".pdf" or ".png" or ".jpg" or ".jpeg" or ".docx" or ".xlsx"))
            {
                ModelState.AddModelError("Attachments", "Use PDF, PNG, JPEG, DOCX or XLSX files up to 5 MB each.");
                continue;
            }
            await using var stream = file.OpenReadStream();
            var bytes = new byte[8];
            var count = await stream.ReadAtLeastAsync(bytes, 8, throwOnEndOfStream: false);
            var valid = extension switch
            {
                ".pdf" => count >= 5 && bytes.AsSpan(0, 5).SequenceEqual("%PDF-"u8),
                ".png" => count == 8 && bytes.AsSpan().SequenceEqual(new byte[] {137,80,78,71,13,10,26,10}),
                ".jpg" or ".jpeg" => count >= 3 && bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255,
                _ => count >= 4 && bytes[0] == 80 && bytes[1] == 75 && bytes[2] == 3 && bytes[3] == 4
            };
            if (!valid) ModelState.AddModelError("Attachments", "A file's content does not match its extension.");
        }
    }

    public async Task<IActionResult> Download(int id)
    {
        var user = await DepartmentUser();
        if (user is null) return Forbid();
        var document = await db.Documents.AsNoTracking().SingleOrDefaultAsync(d => d.Id == id &&
            d.PurchaseRequest != null && d.PurchaseRequest.DepartmentId == user.DepartmentId);
        if (document is null) return NotFound();
        // Only files managed by this area are served; shared storage needs a team download contract.
        if (document.DocumentType != "DepartmentSupportingDocument" ||
            Path.GetFileName(document.StoragePath) != document.StoragePath) return NotFound();
        var path = Path.Combine(storageRoot, document.StoragePath);
        if (!System.IO.File.Exists(path)) return NotFound();
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return PhysicalFile(path, "application/octet-stream", document.FileName);
    }
}
