using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.ViewModels;

namespace Orange_Tendering_System.Services;

// Builds the six internal role dashboards (and the My Tasks list) from SQL Server. Every number is a real count scoped to what the
// signed-in user's role may see; nothing is invented. Supplier has its own SupplierDashboardService (supplier isolation).
public sealed class InternalDashboardService(ApplicationDbContext db)
{
    private static readonly TenderStatus[] Finished = [TenderStatus.Awarded, TenderStatus.Rejected, TenderStatus.Cancelled];

    public async Task<WorkspaceDashboardViewModel> GetAsync(string role, ClaimsPrincipal principal)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == userId);
        var tenders = ScopedTenders(role, user);
        var prs = ScopedRequests(role, user);
        var statusRows = await tenders.GroupBy(t => t.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync();
        var count = (TenderStatus s) => statusRows.Where(r => r.Key == s).Sum(r => r.Count);
        var tendersTotal = statusRows.Sum(r => r.Count);
        var active = statusRows.Where(r => !Finished.Contains(r.Key) && r.Key != TenderStatus.Draft).Sum(r => r.Count);
        var inEvaluation = count(TenderStatus.Evaluation) + count(TenderStatus.OfficerReview);

        var since = DateTime.UtcNow.AddDays(-30); var before = DateTime.UtcNow.AddDays(-60);
        async Task<decimal?> Trend<T>(IQueryable<T> q, System.Linq.Expressions.Expression<Func<T, DateTime>> created)
        {
            var recent = await q.Where(Combine(created, d => d >= since)).CountAsync();
            var prior = await q.Where(Combine(created, d => d >= before && d < since)).CountAsync();
            return prior == 0 ? null : Math.Round((recent - prior) * 100m / prior, 0);
        }

        List<DashboardKpi> kpis;
        string subtitle;
        switch (role)
        {
            case SystemRoles.Admin:
                subtitle = "Manage system access, users, suppliers and configuration.";
                kpis =
                [
                    new("Active Users", await db.Users.CountAsync(u => u.IsActive), "users", "orange", "/Admin/Users"),
                    new("Registered Suppliers", await db.Suppliers.CountAsync(s => s.Status == SupplierStatus.Approved), "layers", "blue", "/Admin/Suppliers", await Trend(db.Suppliers.AsNoTracking(), s => s.CreatedAt)),
                    new("Purchase Requests", await prs.CountAsync(), "request", "green", null, await Trend(prs, p => p.CreatedAt)),
                    new("Tenders", tendersTotal, "clipboard", "purple", null, await Trend(tenders, t => t.CreatedAt))
                ];
                break;
            case SystemRoles.RequestingDepartment:
                subtitle = "Create purchase requests and track their review status.";
                kpis =
                [
                    new("My Requests", await prs.CountAsync(), "request", "orange", "/Department/PurchaseRequests", await Trend(prs, p => p.CreatedAt)),
                    new("Under Review", await prs.CountAsync(p => p.Status == PurchaseRequestStatus.Submitted), "clipboard", "blue", "/Department/PurchaseRequests"),
                    new("Accepted", await prs.CountAsync(p => p.Status == PurchaseRequestStatus.Accepted), "check", "green", "/Department/PurchaseRequests"),
                    new("Returned / Rejected", await prs.CountAsync(p => p.Status == PurchaseRequestStatus.Returned || p.Status == PurchaseRequestStatus.Rejected), "alert", "purple", "/Department/PurchaseRequests")
                ];
                break;
            case SystemRoles.SourcingOfficer:
                subtitle = "Manage sourcing activities, tenders, and supplier interactions efficiently.";
                kpis =
                [
                    new("Active Tenders", count(TenderStatus.Bidding), "clipboard", "orange", "/SourcingOfficer/Tenders", await Trend(tenders, t => t.CreatedAt)),
                    new("Purchase Requests", await prs.CountAsync(p => p.Status == PurchaseRequestStatus.Submitted), "layers", "blue", "/SourcingOfficer/PurchaseRequests", await Trend(prs, p => p.CreatedAt)),
                    new("Registered Suppliers", await db.Suppliers.CountAsync(s => s.Status == SupplierStatus.Approved), "users", "green", null, await Trend(db.Suppliers.AsNoTracking(), s => s.CreatedAt)),
                    new("Pending Evaluations", inEvaluation, "check", "purple", "/SourcingOfficer/Bids")
                ];
                break;
            case SystemRoles.EvaluationCommittee:
                subtitle = "Evaluate offers against the approved criteria and weights.";
                kpis =
                [
                    new("Assigned Tenders", tendersTotal, "clipboard", "orange", "/EvaluationCommittee/Evaluations"),
                    new("Pending Evaluations", count(TenderStatus.Evaluation), "check", "purple", "/EvaluationCommittee/Evaluations"),
                    new("Awaiting Review", count(TenderStatus.OfficerReview) + count(TenderStatus.ManagerReview), "layers", "blue", "/EvaluationCommittee/Reports"),
                    new("Awarded", count(TenderStatus.Awarded), "trophy", "green", null)
                ];
                break;
            case SystemRoles.SourcingManager:
                subtitle = "Review unified evaluation reports and decide on recommendations.";
                kpis =
                [
                    new("Awaiting My Review", count(TenderStatus.ManagerReview), "check", "orange", "/SourcingManager/Reports"),
                    new("Under Evaluation", inEvaluation, "layers", "blue", null),
                    new("Forwarded to Top Committee", count(TenderStatus.TopCommitteeReview), "users", "purple", null),
                    new("Awarded", count(TenderStatus.Awarded), "trophy", "green", null)
                ];
                break;
            default:
                subtitle = "Take the final group decision on high-value and critical tenders.";
                kpis =
                [
                    new("Awaiting Decision", count(TenderStatus.TopCommitteeReview), "check", "orange", "/TopCommittee/Decisions"),
                    new("Awarded", count(TenderStatus.Awarded), "trophy", "green", "/TopCommittee/Decisions"),
                    new("Rejected", count(TenderStatus.Rejected), "alert", "purple", null),
                    new("All Tenders", tendersTotal, "clipboard", "blue", "/TopCommittee/Statistics")
                ];
                break;
        }

        var ongoing = await tenders.Where(t => !Finished.Contains(t.Status) && t.Status != TenderStatus.Draft)
            .OrderByDescending(t => t.CreatedAt).FirstOrDefaultAsync();
        var tasks = await Tasks(role, user, tenders, prs);
        var activities = role == SystemRoles.Admin
            ? (await db.AuditLogs.AsNoTracking().OrderByDescending(a => a.CreatedAt).Take(6).ToListAsync())
                .Select(a => new DashboardActivity(Words(a.Action), $"{a.EntityName} #{a.EntityId}", Ago(a.CreatedAt))).ToList()
            : (await db.Notifications.AsNoTracking().Where(n => n.UserId == userId).OrderByDescending(n => n.CreatedAt).Take(6).ToListAsync())
                .Select(n => new DashboardActivity(n.Title, n.Message, Ago(n.CreatedAt), n.Link)).ToList();

        var palette = new[] { "#ff8a00", "#098fff", "#00bf81", "#c6cbd2" };
        var statuses = statusRows.OrderByDescending(r => r.Count).Select((r, i) => new DashboardStatus(Words(r.Key.ToString()), r.Count, palette[i % palette.Length])).ToList();

        return new WorkspaceDashboardViewModel
        {
            DataConnected = true, DisplayName = string.IsNullOrWhiteSpace(user.FullName) ? role : user.FullName,
            Subtitle = subtitle, Kpis = kpis,
            Metrics = new Dictionary<string, int> { ["tenders-total"] = tendersTotal, ["active-tenders"] = active },
            Statuses = statuses, Tasks = tasks, Activities = activities,
            OngoingTender = ongoing == null ? null : new DashboardTender($"T-{ongoing.Id:0000}", ongoing.Title, Words(ongoing.Status.ToString()), Progress(ongoing.Status)),
            CurrentWorkflowStage = ongoing == null ? null : Stage(ongoing.Status)
        };
    }

    // Full task list (the My Tasks page).
    public async Task<IReadOnlyList<DashboardTask>> TasksAsync(string role, ClaimsPrincipal principal)
    {
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
        return await Tasks(role, user, ScopedTenders(role, user), ScopedRequests(role, user), 100);
    }

    public sealed record SearchHit(string Type, string Title, string Subtitle, string Url);

    // Global workspace search: only rows the role may already open (same scoping as the dashboards).
    public async Task<List<SearchHit>> SearchAsync(string role, ClaimsPrincipal principal, string q)
    {
        q = q.Trim();
        if (q.Length < 2) return [];
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var hits = new List<SearchHit>();
        foreach (var t in await ScopedTenders(role, user).Where(t => t.Title.Contains(q) || t.Description.Contains(q) || ("T-" + t.Id).Contains(q)).OrderByDescending(t => t.CreatedAt).Take(6).ToListAsync())
            hits.Add(new("Tender", t.Title, $"T-{t.Id:0000} · {Words(t.Status.ToString())}", "/Workflow/Details/" + t.Id));
        var prArea = role == SystemRoles.RequestingDepartment ? "/Department/PurchaseRequests/Details/" : role == SystemRoles.SourcingOfficer ? "/SourcingOfficer/PurchaseRequests/Review/" : null;
        if (prArea != null)
            foreach (var p in await ScopedRequests(role, user).Where(p => p.Title.Contains(q)).OrderByDescending(p => p.CreatedAt).Take(5).ToListAsync())
                hits.Add(new("Purchase request", p.Title, $"PR-{p.Id:0000} · {p.Status}", prArea + p.Id));
        if (role is SystemRoles.Admin or SystemRoles.SourcingOfficer)
            foreach (var s in await db.Suppliers.AsNoTracking().Where(s => s.CompanyName.Contains(q) || s.Email.Contains(q)).OrderBy(s => s.CompanyName).Take(5).ToListAsync())
                hits.Add(new("Supplier", s.CompanyName, s.Status.ToString(), role == SystemRoles.Admin ? "/Admin/Suppliers/Review/" + s.Id : "/SourcingOfficer/Invitations"));
        if (role == SystemRoles.Admin)
            foreach (var u in await db.Users.AsNoTracking().Where(u => u.Email!.Contains(q) || u.FullName.Contains(q)).OrderBy(u => u.Email).Take(5).ToListAsync())
                hits.Add(new("User", string.IsNullOrWhiteSpace(u.FullName) ? u.Email! : u.FullName, u.Email!, "/Admin/Users/Details/" + u.Id));
        return hits;
    }

    private IQueryable<Tender> ScopedTenders(string role, ApplicationUser user)
    {
        var q = db.Tenders.AsNoTracking();
        return role switch
        {
            SystemRoles.RequestingDepartment => q.Where(t => t.PurchaseRequest.DepartmentId == user.DepartmentId),
            SystemRoles.EvaluationCommittee =>
                q.Where(t => db.CommitteeMembers.Any(m => m.TenderId == t.Id && m.UserId == user.Id)),
            _ => q
        };
    }

    private IQueryable<PurchaseRequest> ScopedRequests(string role, ApplicationUser user)
    {
        var q = db.PurchaseRequests.AsNoTracking();
        return role == SystemRoles.RequestingDepartment ? q.Where(p => p.DepartmentId == user.DepartmentId) : q.Where(p => p.Status != PurchaseRequestStatus.Draft || p.CreatedByUserId == user.Id);
    }

    private async Task<List<DashboardTask>> Tasks(string role, ApplicationUser user, IQueryable<Tender> tenders, IQueryable<PurchaseRequest> prs, int take = 5)
    {
        var list = new List<DashboardTask>();
        async Task AddTenders(TenderStatus status, string title, string pill, string path)
        {
            foreach (var t in await tenders.Where(t => t.Status == status).OrderBy(t => t.CreatedAt).Take(take).ToListAsync())
                list.Add(new DashboardTask(title, $"T-{t.Id:0000} – {t.Title}", pill, path + t.Id));
        }
        switch (role)
        {
            case SystemRoles.Admin:
                foreach (var s in await db.Suppliers.AsNoTracking().Where(s => s.Status == SupplierStatus.Pending &&
                    (!db.Users.Any(u => u.SupplierId == s.Id) || db.Users.Any(u => u.SupplierId == s.Id && u.EmailConfirmed)))
                    .OrderBy(s => s.CreatedAt).Take(take).ToListAsync())
                    list.Add(new DashboardTask("Review supplier registration", s.CompanyName, "Pending", "/Admin/Suppliers"));
                break;
            case SystemRoles.RequestingDepartment:
                foreach (var p in await prs.Where(p => p.Status == PurchaseRequestStatus.Returned || p.Status == PurchaseRequestStatus.Draft).OrderBy(p => p.CreatedAt).Take(take).ToListAsync())
                    list.Add(new DashboardTask(p.Status == PurchaseRequestStatus.Returned ? "Revise returned request" : "Complete draft request", $"PR-{p.Id:0000} – {p.Title}", p.Status.ToString(), "/Department/PurchaseRequests"));
                break;
            case SystemRoles.SourcingOfficer:
                foreach (var p in await prs.Where(p => p.Status == PurchaseRequestStatus.Submitted).OrderBy(p => p.CreatedAt).Take(take).ToListAsync())
                    list.Add(new DashboardTask("Review purchase request", $"PR-{p.Id:0000} – {p.Title}", "Pending", "/SourcingOfficer/PurchaseRequests"));
                await AddTenders(TenderStatus.OfficerReview, "Review evaluation outcome", "In Progress", "/Workflow/Details/");
                foreach (var c in await db.Clarifications.AsNoTracking().Where(c => c.ToUserId == user.Id && c.Status == ClarificationStatus.Open).Take(take).ToListAsync())
                    list.Add(new DashboardTask("Answer supplier clarification", c.Subject, "Pending", "/SourcingOfficer/Clarifications"));
                break;
            case SystemRoles.EvaluationCommittee:
                await AddTenders(TenderStatus.Evaluation, "Score submitted offers", "Pending", "/Workflow/Details/");
                break;
            case SystemRoles.SourcingManager:
                await AddTenders(TenderStatus.ManagerReview, "Review unified report", "Pending", "/Workflow/Details/");
                break;
            default:
                await AddTenders(TenderStatus.TopCommitteeReview, "Cast committee decision", "Pending", "/Workflow/Details/");
                break;
        }
        return list.Take(take).ToList();
    }

    private static System.Linq.Expressions.Expression<Func<T, bool>> Combine<T>(System.Linq.Expressions.Expression<Func<T, DateTime>> selector, System.Linq.Expressions.Expression<Func<DateTime, bool>> predicate)
    {
        var body = new Replace(predicate.Parameters[0], selector.Body).Visit(predicate.Body)!;
        return System.Linq.Expressions.Expression.Lambda<Func<T, bool>>(body, selector.Parameters);
    }
    private sealed class Replace(System.Linq.Expressions.ParameterExpression from, System.Linq.Expressions.Expression to) : System.Linq.Expressions.ExpressionVisitor
    {
        protected override System.Linq.Expressions.Expression VisitParameter(System.Linq.Expressions.ParameterExpression node) => node == from ? to : base.VisitParameter(node);
    }

    private static int Progress(TenderStatus s) => s switch
    {
        TenderStatus.Bidding => 30, TenderStatus.Evaluation => 50, TenderStatus.OfficerReview => 62,
        TenderStatus.ManagerReview => 75, TenderStatus.TopCommitteeReview => 90, TenderStatus.Awarded => 100, _ => 10
    };
    private static int Stage(TenderStatus s) => s switch
    {
        TenderStatus.Bidding => 2, TenderStatus.Evaluation or TenderStatus.OfficerReview => 3,
        TenderStatus.ManagerReview => 4, TenderStatus.TopCommitteeReview => 5, TenderStatus.Awarded => 6, _ => 1
    };
    private static string Words(string pascal) => System.Text.RegularExpressions.Regex.Replace(pascal, "(?<=[a-z])(?=[A-Z])", " ");
    private static string Ago(DateTime at)
    {
        var d = DateTime.UtcNow - at;
        return d.TotalMinutes < 1 ? "Just now" : d.TotalHours < 1 ? $"{(int)d.TotalMinutes} min ago" : d.TotalDays < 1 ? $"{(int)d.TotalHours} h ago" : d.TotalDays < 2 ? "Yesterday" : at.ToString("yyyy-MM-dd");
    }
}
