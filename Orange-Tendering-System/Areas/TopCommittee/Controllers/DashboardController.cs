<<<<<<< HEAD
// ORANGE TENDER MANAGEMENT SYSTEM
// Owner: Doulat
// Module: Top Committee
// Purpose: MVC endpoints for the Dashboard pages of the Top Committee workspace.
// Status: Dashboard data comes from InternalDashboardService (real, role-scoped SQL Server counts).
=======
using Orange_Tendering_System.Areas.TopCommittee.Services;
>>>>>>> be32000 (temp)
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.ViewModels;

<<<<<<< HEAD
namespace Orange_Tendering_System.Areas.TopCommittee.Controllers
=======
namespace Orange_Tendering_System.Areas.TopCommittee.Controllers;

[Area("TopCommittee"), Authorize(Roles = TopExecutiveRoles.Allowed)]
public sealed class DashboardController(ApplicationDbContext db, UserManager<ApplicationUser> users, IServiceProvider services) : TopCommitteeControllerBase(db, users)
>>>>>>> be32000 (temp)
{
    [Area("TopCommittee")]
    [Authorize(Roles = SystemRoles.TopCommittee)]
    public class DashboardController : Controller
    {
<<<<<<< HEAD
        // Top Committee Dashboard — Owner: Doulat · Trello: Top Committee Dashboard (https://trello.com/c/8Z6bnMsb)
        // GET /TopCommittee. TODO: build the view model from real data in a service.
        public async Task<IActionResult> Index([FromServices] Orange_Tendering_System.Services.InternalDashboardService dashboard) => View(await dashboard.GetAsync(SystemRoles.TopCommittee, User));
=======
        var user = await ActiveUser();
        if (user is null) return Forbid();
        var groups = await Committee.AssignedRounds(user.Id).GroupBy(r => r.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Status, g => g.Count);
        var pending = new List<RoundDetails>();
        var awaiting = 0;
        var signedReportRounds = new HashSet<int>();
        var activeIds = await Committee.AssignedRounds(user.Id).Where(r => r.Status == CommitteeRoundStatus.InProgress)
            .OrderByDescending(r => r.StartedAt).ThenByDescending(r => r.Id).Select(r => r.Id).ToListAsync();
        foreach (var id in activeIds)
        {
            var details = await Committee.Load(user.Id, id);
            if (details is null) continue;
            if (details.Round.TenderReport is { ReportType: ReportType.FullEvaluation } report)
            {
                try
                {
                    var review = await TopCommitteeWorkflow.Create(services).ReviewReport(report.Id, User);
                    if (review.CanVoteTop && details.MyVote is null)
                    {
                        awaiting++;
                        signedReportRounds.Add(id);
                    }
                }
                catch (WorkflowException) { /* Invalid/stale documents remain reviewable with an explicit workflow error. */ }
            }
            else if (details.CanVote) awaiting++;
            if ((details.CanVote || details.MyVote is not null) && pending.Count < 20) pending.Add(details);
        }
        ViewData["SignedReportRounds"] = signedReportRounds;
        var executiveTitle = TopExecutiveRoles.Title(await Users.GetRolesAsync(user));
        return View(new CommitteeDashboard
        {
            DisplayName = executiveTitle is not null ? executiveTitle + " · " + (string.IsNullOrWhiteSpace(user.FullName) ? user.UserName : user.FullName) : string.IsNullOrWhiteSpace(user.FullName) ? user.UserName ?? "Committee member" : user.FullName,
            Active = groups.GetValueOrDefault(CommitteeRoundStatus.InProgress),
            Approved = groups.GetValueOrDefault(CommitteeRoundStatus.Approved),
            Rejected = groups.GetValueOrDefault(CommitteeRoundStatus.Rejected), AwaitingVote = awaiting, Pending = pending,
            Recent = await Committee.RoundList(user.Id).AsNoTracking().Where(r => r.Status != CommitteeRoundStatus.InProgress)
                .OrderByDescending(r => r.StartedAt).ThenByDescending(r => r.Id).Take(8).AsSplitQuery().ToListAsync()
        });
>>>>>>> be32000 (temp)
    }
}
