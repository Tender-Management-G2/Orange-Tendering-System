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
    public class CategoriesController(ApplicationDbContext db, ProcurementCategoryService categories) : Controller
    {
        public sealed record CategoryRow(ProcurementCategory Category, int RequestItems, int TenderItems);

        public async Task<IActionResult> Index()
        {
            var rows = await db.ProcurementCategories.AsNoTracking().OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
                .Select(c => new CategoryRow(c, db.PurchaseRequestItems.Count(i => i.CategoryId == c.Id), db.TenderItems.Count(i => i.CategoryId == c.Id))).ToListAsync();
            return View(rows);
        }

        [HttpPost]
        public async Task<IActionResult> Save(int id, string? name, string? description, int sortOrder, bool isActive = false, CancellationToken ct = default)
        {
            var result = await categories.SaveAsync(id, name, description, sortOrder, isActive,
                User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, ct);
            if (result.Error == "NotFound") return NotFound();
            if (result.Category is not { } category)
            {
                TempData["FlashError"] = result.Error == "Duplicate"
                    ? $"A category named \"{name?.Trim()}\" already exists."
                    : "Enter a name (up to 100 characters), an optional description (up to 300) and an order from 0 to 10000.";
                return RedirectToAction(nameof(Index));
            }
            TempData.Orange(OrangeFeedback.Toast(id == 0 ? "Category added" : "Category saved", $"\"{category.Name}\" is {(category.IsActive ? "available" : "hidden")} in new requests."));
            return RedirectToAction(nameof(Index));
        }
    }
}
