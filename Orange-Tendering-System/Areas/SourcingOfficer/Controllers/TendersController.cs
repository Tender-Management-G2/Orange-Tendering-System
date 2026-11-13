using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.ViewModels;

namespace Orange_Tendering_System.Areas.SourcingOfficer.Controllers;

[Area("SourcingOfficer")]
[Authorize(Roles = SystemRoles.SourcingOfficer)]
public class TendersController(ApplicationDbContext db) : Controller
{
    public async Task<IActionResult> Index() => View(await db.Tenders.AsNoTracking().Include(t => t.PurchaseRequest).Include(t => t.Rounds).OrderByDescending(t => t.CreatedAt).ToListAsync());

    public async Task<IActionResult> Create(int id)
    {
        var request = await db.PurchaseRequests.AsNoTracking().Include(r => r.Items).SingleOrDefaultAsync(r => r.Id == id && r.Status == PurchaseRequestStatus.Accepted && !r.Tenders.Any());
        if (request == null) return NotFound();
        return View(new CreateOfficerTenderModel { PurchaseRequestId = id, RequestTitle = request.Title, Title = request.Title, Description = request.Justification, OpensAt = DateTime.Now.AddDays(1), Deadline = DateTime.Now.AddDays(8), Items = request.Items.ToList() });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateOfficerTenderModel model)
    {
        var request = await db.PurchaseRequests.Include(r => r.Items).SingleOrDefaultAsync(r => r.Id == model.PurchaseRequestId && r.Status == PurchaseRequestStatus.Accepted && !r.Tenders.Any());
        if (request == null) return NotFound();
        if (model.Deadline <= model.OpensAt) ModelState.AddModelError(nameof(model.Deadline), "Deadline must be after opening time.");
        if (!ModelState.IsValid) { model.Items = request.Items.ToList(); model.RequestTitle = request.Title; return View(model); }
        var now = DateTime.UtcNow;
        var tender = new Tender { PurchaseRequestId = request.Id, Title = model.Title.Trim(), Description = model.Description.Trim(), Requirements = model.Requirements?.Trim(), CreatedByUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value, CreatedAt = now, Status = TenderStatus.Draft };
        foreach (var item in request.Items) tender.Items.Add(new TenderItem { Name = item.Name, Specifications = item.Specifications, Quantity = item.Quantity, Unit = item.Unit });
        tender.Rounds.Add(new TenderRound { RoundNumber = 1, Status = TenderRoundStatus.Draft, OpensAt = model.OpensAt, Deadline = model.Deadline, CreatedByUserId = tender.CreatedByUserId, CreatedAt = now });
        db.Tenders.Add(tender); await db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Details(int id)
    {
        var tender = await db.Tenders.AsNoTracking().Include(t => t.PurchaseRequest).Include(t => t.Items)
            .Include(t => t.Rounds).ThenInclude(r => r.Invitations).ThenInclude(i => i.Supplier)
            .Include(t => t.Rounds).ThenInclude(r => r.Invitations).ThenInclude(i => i.Bids)
            .SingleOrDefaultAsync(t => t.Id == id);
        return tender == null ? NotFound() : View(tender);
    }

    public async Task<IActionResult> SelectSuppliers(int id, string? category = null)
    {
        return await SelectSuppliersView(id, category);
    }

    private async Task<IActionResult> SelectSuppliersView(int id, string? category)
    {
        var round = await db.TenderRounds.AsNoTracking().Include(r => r.Tender)
            .Include(r => r.Invitations).ThenInclude(i => i.Supplier).SingleOrDefaultAsync(r => r.Id == id);
        if (round == null) return NotFound();
        var approvedSuppliers = await db.Suppliers.AsNoTracking().Where(s => s.Status == SupplierStatus.Approved).OrderBy(s => s.Category).ThenBy(s => s.CompanyName).ToListAsync();
        if (!string.IsNullOrWhiteSpace(category) && !SupplierCategories.All.Contains(category)) return NotFound();
        var canInvite = round.Status is TenderRoundStatus.Draft or TenderRoundStatus.Open
            && round.Tender.Status is TenderStatus.Draft or TenderStatus.Bidding;
        var model = new SelectOfficerSuppliersModel
        {
            RoundId = id,
            TenderId = round.TenderId,
            TenderTitle = round.Tender.Title,
            SelectedCategory = category,
            CanInvite = canInvite,
            InviteUnavailableReason = canInvite ? null : "This round is no longer accepting invitations. You can still review its supplier history below.",
            Categories = SupplierCategories.All.Select(name => new SupplierCategoryRowViewModel(name, approvedSuppliers.Count(s => s.Category == name))).ToList(),
            Suppliers = category == null ? [] : approvedSuppliers.Where(s => s.Category == category).ToList(),
            Invitations = round.Invitations.OrderByDescending(i => i.InvitedAt).ToList()
        };
        return View(model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> InviteSupplier(int roundId, int supplierId, string category)
    {
        if (!SupplierCategories.All.Contains(category)) return BadRequest();
        var round = await db.TenderRounds.Include(r => r.Tender).Include(r => r.Invitations).SingleOrDefaultAsync(r => r.Id == roundId);
        if (round == null) return NotFound();
        if (round.Status is not (TenderRoundStatus.Draft or TenderRoundStatus.Open) || round.Tender.Status is not (TenderStatus.Draft or TenderStatus.Bidding))
        {
            TempData["SupplierInviteError"] = "This round is no longer accepting invitations.";
            return RedirectToAction(nameof(SelectSuppliers), new { id = roundId, category });
        }
        var alreadyInvited = round.Invitations.Any(i => i.SupplierId == supplierId);
        var supplier = await db.Suppliers.SingleOrDefaultAsync(s => s.Id == supplierId && s.Status == SupplierStatus.Approved && s.Category == category);
        if (supplier == null) return NotFound();
        if (alreadyInvited)
        {
            TempData["SupplierInviteMessage"] = "This supplier has already been invited to this round.";
            return RedirectToAction(nameof(SelectSuppliers), new { id = roundId, category });
        }
        var now = DateTime.UtcNow;
        round.Invitations.Add(new TenderInvitation { SupplierId = supplier.Id, InvitedByUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value, InvitedAt = now });
        var accounts = await db.Users.Where(u => u.IsActive && u.SupplierId == supplier.Id).ToListAsync();
        foreach (var user in accounts)
            db.Notifications.Add(new Notification { UserId = user.Id, Title = "Tender invitation", Message = $"You are invited to submit an offer for {round.Tender.Title}.", Link = "/Supplier/Invitations", CreatedAt = now });
        round.Status = TenderRoundStatus.Open; round.Tender.Status = TenderStatus.Bidding; await db.SaveChangesAsync();
        TempData["SupplierInviteMessage"] = $"Invitation sent to {supplier.CompanyName}.";
        return RedirectToAction(nameof(SelectSuppliers), new { id = roundId, category });
    }
}
