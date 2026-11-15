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
public class DashboardController(ApplicationDbContext db, UserManager<ApplicationUser> users) : Controller
{
    public async Task<IActionResult> Index()
    {
        var user = await users.GetUserAsync(User);
        if (user is not { IsActive: true, DepartmentId: not null }) return Forbid();
        var department = await db.Departments.AsNoTracking()
            .SingleOrDefaultAsync(d => d.Id == user.DepartmentId && d.IsActive);
        if (department is null) return Forbid();
        var service = new DepartmentRequestService(db, users);
        var query = service.Requests(department.Id).AsNoTracking();
        var groups = await query.Select(r => new
        {
            Status = r.Tenders.Any() ? PurchaseRequestStatus.ConvertedToTender :
                r.Status == PurchaseRequestStatus.Approved ? PurchaseRequestStatus.Accepted : r.Status
        }).GroupBy(r => r.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync();
        var counts = RequestDisplay.Statuses.ToDictionary(s => s, _ => 0);
        foreach (var group in groups)
            counts[RequestDisplay.Status(new PurchaseRequest { Status = group.Status })] = group.Count;
        return View(new DepartmentDashboard
        {
            DepartmentName = department.Name, UserId = user.Id, Counts = counts,
            Recent = await query.OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id).Take(8).ToListAsync(),
            ActionRequired = await query.Where(r => r.CreatedByUserId == user.Id && !r.Tenders.Any() &&
                (r.Status == PurchaseRequestStatus.Draft || r.Status == PurchaseRequestStatus.Returned))
                .OrderByDescending(r => r.Status == PurchaseRequestStatus.Returned).ThenByDescending(r => r.CreatedAt)
                .Take(10).ToListAsync(),
            RecentActivity = await db.PurchaseRequestReviews.AsNoTracking().Include(r => r.PurchaseRequest)
                .Where(r => r.PurchaseRequest.DepartmentId == department.Id)
                .OrderByDescending(r => r.ReviewedAt ?? r.SubmittedAt).Take(8).ToListAsync(),
            Notifications = await db.Notifications.AsNoTracking().Where(n => n.UserId == user.Id &&
                n.Link != null && n.Link.StartsWith("/Department/"))
                .OrderByDescending(n => n.CreatedAt).Take(10).ToListAsync()
        });
    }
}
