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
public sealed class DashboardController(ApplicationDbContext db, UserManager<ApplicationUser> users) : TopCommitteeControllerBase(db, users)
{
    public async Task<IActionResult> Index()
    {
        var user = await ActiveUser();
        if (user is null) return Forbid();
        var groups = await Committee.AssignedRounds(user.Id).GroupBy(r => r.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Status, g => g.Count);
        var pending = new List<RoundDetails>();
        var awaiting = 0;
        var activeIds = await Committee.AssignedRounds(user.Id).Where(r => r.Status == CommitteeRoundStatus.InProgress)
            .OrderByDescending(r => r.StartedAt).Select(r => r.Id).ToListAsync();
        foreach (var id in activeIds)
        {
            var details = await Committee.Load(user.Id, id);
            if (details is null) continue;
            if (details.CanVote) awaiting++;
            if (pending.Count < 20) pending.Add(details);
        }
        return View(new CommitteeDashboard
        {
            DisplayName = string.IsNullOrWhiteSpace(user.FullName) ? user.UserName ?? "Committee member" : user.FullName,
            Active = groups.GetValueOrDefault(CommitteeRoundStatus.InProgress),
            Approved = groups.GetValueOrDefault(CommitteeRoundStatus.Approved),
            Rejected = groups.GetValueOrDefault(CommitteeRoundStatus.Rejected), AwaitingVote = awaiting, Pending = pending,
            Recent = await Committee.RoundList(user.Id).AsNoTracking().Where(r => r.Status != CommitteeRoundStatus.InProgress)
                .OrderByDescending(r => r.CompletedAt).Take(8).AsSplitQuery().ToListAsync()
        });
    }
}
