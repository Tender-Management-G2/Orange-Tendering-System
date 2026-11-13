using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Areas.TopCommittee.ViewModels;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;

namespace Orange_Tendering_System.Areas.TopCommittee.Controllers;

[Area("TopCommittee"), Authorize(Roles = SystemRoles.TopCommittee)]
public sealed class DecisionsController(ApplicationDbContext db, UserManager<ApplicationUser> users,
    IWebHostEnvironment environment, ILogger<DecisionsController> logger) : TopCommitteeControllerBase(db, users)
{
    public async Task<IActionResult> ReviewEvaluation(int? id, string? search = null, CommitteeRoundStatus? status = null, int page = 1)
    {
        var user = await ActiveUser();
        if (user is null) return Forbid();
        if (!id.HasValue) return await SelectRound(user.Id, nameof(ReviewEvaluation), search, status, page);
        var details = await Committee.Load(user.Id, id.Value);
        return details is null ? NotFound() : View(details);
    }
    public async Task<IActionResult> Decision(int? id, string? search = null, CommitteeRoundStatus? status = null, int page = 1)
    {
        var user = await ActiveUser();
        if (user is null) return Forbid();
        if (!id.HasValue) return await SelectRound(user.Id, nameof(Decision), search, status, page);
        var details = await Committee.Load(user.Id, id.Value);
        return details is null ? NotFound() : View(details);
    }

    private async Task<IActionResult> SelectRound(string userId, string targetAction, string? search, CommitteeRoundStatus? status, int page)
    {
        if (status.HasValue && !Enum.IsDefined(status.Value)) return BadRequest();
        var query = Committee.RoundList(userId).AsNoTracking();
        search = search?.Trim();
        if (!string.IsNullOrEmpty(search))
            query = query.Where(r => r.TenderRound.Tender.Title.Contains(search) || r.TenderRound.TenderId.ToString() == search);
        if (status.HasValue) query = query.Where(r => r.Status == status);
        var total = await query.CountAsync();
        page = Math.Clamp(page, 1, Math.Max(1, (int)Math.Ceiling(total / 20m)));
        return View("SelectRound", new RoundSelection
        {
            TargetAction = targetAction, UserId = userId,
            Listing = new DecisionHistory
            {
                Search = search, Status = status, Total = total, Page = page,
                Rounds = await query.OrderByDescending(r => r.StartedAt).ThenByDescending(r => r.Id)
                    .Skip((page - 1) * 20).Take(20).AsSplitQuery().ToListAsync()
            }
        });
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Vote(VoteInput input)
    {
        var user = await ActiveUser();
        if (user is null) return Forbid();
        if (input.Decision == VoteDecision.Reject && string.IsNullOrWhiteSpace(input.Comment))
            ModelState.AddModelError(nameof(input.Comment), "A written rejection reason is required.");
        if (!ModelState.IsValid)
        {
            var details = await Committee.Load(user.Id, input.RoundId);
            return details is null ? NotFound() : View("Decision", details);
        }
        try
        {
            await Committee.Vote(user.Id, input);
            TempData["TopMessage"] = "Your vote was saved. The final result is processed automatically after all four members vote.";
        }
        catch (InvalidOperationException exception) { TempData["TopError"] = exception.Message; }
        catch (Exception exception) when (exception is DbUpdateException or System.Data.Common.DbException)
        {
            logger.LogWarning(exception, "Committee vote could not be committed");
            TempData["TopError"] = "The vote could not be committed. Refresh to check the latest votes before retrying.";
        }
        return RedirectToAction(nameof(Decision), new { id = input.RoundId });
    }
    public async Task<IActionResult> History(int? id, string? search, CommitteeRoundStatus? status, int? tenderId, int page = 1)
    {
        var user = await ActiveUser();
        if (user is null) return Forbid();
        if (status.HasValue && !Enum.IsDefined(status.Value)) return BadRequest();
        var query = Committee.RoundList(user.Id).AsNoTracking();
        search = search?.Trim();
        if (!string.IsNullOrEmpty(search)) query = query.Where(r => r.TenderRound.Tender.Title.Contains(search) || r.TenderRound.TenderId.ToString() == search);
        if (status.HasValue) query = query.Where(r => r.Status == status);
        if (tenderId.HasValue) query = query.Where(r => r.TenderRound.TenderId == tenderId);
        var total = await query.CountAsync();
        page = Math.Clamp(page, 1, Math.Max(1, (int)Math.Ceiling(total / 20m)));
        var selected = id.HasValue ? await Committee.Load(user.Id, id.Value) : null;
        if (id.HasValue && selected is null) return NotFound();
        return View(new DecisionHistory
        {
            Search = search, Status = status, TenderId = tenderId, Page = page, Total = total, Selected = selected,
            Rounds = await query.OrderByDescending(r => r.StartedAt).ThenByDescending(r => r.Id)
                .Skip((page - 1) * 20).Take(20).AsSplitQuery().ToListAsync()
        });
    }
    public async Task<IActionResult> Download(int id, int roundId)
    {
        var user = await ActiveUser();
        if (user is null) return Forbid();
        var details = await Committee.Load(user.Id, roundId);
        var document = details?.Documents.SingleOrDefault(d => d.Id == id);
        if (document is null || document.DocumentType != "DepartmentSupportingDocument" ||
            Path.GetFileName(document.StoragePath) != document.StoragePath) return NotFound();
        // This is the sole implemented storage convention in the current project. Other handlers are a team integration.
        var path = Path.Combine(environment.ContentRootPath, "Areas", "Department", "App_Data", "Uploads", document.StoragePath);
        if (!System.IO.File.Exists(path)) return NotFound();
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return PhysicalFile(path, "application/octet-stream", document.FileName);
    }
}
