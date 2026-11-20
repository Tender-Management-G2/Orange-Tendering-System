// ORANGE TENDER MANAGEMENT SYSTEM
// Module: Administration — procurement categories used by purchase request items and tender items.
// A category is never deleted (items keep their history); it is renamed or deactivated instead.
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.Services;

namespace Orange_Tendering_System.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = SystemRoles.Admin)]
    [AutoValidateAntiforgeryToken]
    public class CategoriesController(ApplicationDbContext db) : Controller
    {
        public sealed record CategoryRow(ProcurementCategory Category, int RequestItems, int TenderItems);

        public async Task<IActionResult> Index()
        {
            var rows = await db.ProcurementCategories.AsNoTracking().OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
                .Select(c => new CategoryRow(c, db.PurchaseRequestItems.Count(i => i.CategoryId == c.Id), db.TenderItems.Count(i => i.CategoryId == c.Id))).ToListAsync();
            return View(rows);
        }

        [HttpPost]
        public async Task<IActionResult> Save(int id, string? name, string? description, int sortOrder, bool isActive = false)
        {
            name = name?.Trim(); description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
            if (string.IsNullOrEmpty(name) || name.Length > 100 || (description?.Length ?? 0) > 300 || sortOrder is < 0 or > 10000)
            { TempData["FlashError"] = "Enter a name (up to 100 characters), an optional description (up to 300) and an order from 0 to 10000."; return RedirectToAction(nameof(Index)); }
            if (await db.ProcurementCategories.AnyAsync(c => c.Name == name && c.Id != id))
            { TempData["FlashError"] = $"A category named \"{name}\" already exists."; return RedirectToAction(nameof(Index)); }
            var category = id == 0 ? new ProcurementCategory { CreatedAt = DateTime.UtcNow } : await db.ProcurementCategories.SingleOrDefaultAsync(c => c.Id == id);
            if (category == null) return NotFound();
            var old = id == 0 ? null : new { category.Name, category.Description, category.IsActive, category.SortOrder };
            category.Name = name; category.Description = description; category.SortOrder = sortOrder; category.IsActive = isActive;
            if (id == 0) db.ProcurementCategories.Add(category);
            await db.SaveChangesAsync();
            db.AuditLogs.Add(new AuditLog { UserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, EntityName = nameof(ProcurementCategory), EntityId = category.Id.ToString(),
                Action = id == 0 ? "CategoryCreated" : "CategoryUpdated", OldValues = old == null ? null : System.Text.Json.JsonSerializer.Serialize(old),
                NewValues = System.Text.Json.JsonSerializer.Serialize(new { category.Name, category.Description, category.IsActive, category.SortOrder }), CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
            TempData.Orange(OrangeFeedback.Toast(id == 0 ? "Category added" : "Category saved", $"\"{category.Name}\" is {(category.IsActive ? "available" : "hidden")} in new requests."));
            return RedirectToAction(nameof(Index));
        }
    }
}
