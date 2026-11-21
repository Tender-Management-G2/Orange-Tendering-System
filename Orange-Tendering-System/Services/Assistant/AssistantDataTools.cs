using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;

namespace Orange_Tendering_System.Services.Assistant;

// Controlled, read-only query tools behind the AI assistant. Each tool is a fixed EF Core query over AssistantAccess scopes,
// bounded with Take(); the model never writes SQL, never receives DbContext, and never decides who may see what.
public sealed class AssistantDataTools(ApplicationDbContext db, AssistantAccess access, InternalDashboardService dashboard, TimeProvider clock)
{
    public const int ExportLimit = 5000;
    private static readonly TenderStatus[] Finished = [TenderStatus.Awarded, TenderStatus.Rejected, TenderStatus.Cancelled];

    // Which topics each role may ask for. Anything else gets an explicit "not available for your role" answer.
    private static readonly AssistantTopic[] TenderActions = [AssistantTopic.LatestTender, AssistantTopic.SearchTenders, AssistantTopic.DraftTenders, AssistantTopic.AwaitingApproval, AssistantTopic.TenderAttachments];
    public static AssistantTopic[] Allowed(AssistantUser user) =>
        user.Role is SystemRoles.SourcingOfficer or SystemRoles.SourcingManager or SystemRoles.TopCommittee or SystemRoles.EvaluationCommittee or SystemRoles.RequestingDepartment
            ? [.. Base(user), .. TenderActions]
            : user.Role == SystemRoles.Supplier ? [.. Base(user), AssistantTopic.LatestTender] : Base(user);

    private static AssistantTopic[] Base(AssistantUser user) => user.Role switch
    {
        SystemRoles.SourcingOfficer => [AssistantTopic.TenderDetail, AssistantTopic.OpenTenders, AssistantTopic.ActiveTenders, AssistantTopic.ClosedTenders, AssistantTopic.AllTenders, AssistantTopic.Deadlines,
            AssistantTopic.PurchaseRequests, AssistantTopic.DelayedRequests, AssistantTopic.PendingApprovals, AssistantTopic.Notifications, AssistantTopic.Suppliers,
            AssistantTopic.SupplierPerformance, AssistantTopic.ActivitySummary, AssistantTopic.DepartmentSpending, AssistantTopic.EvaluationResults],
        SystemRoles.SourcingManager or SystemRoles.TopCommittee => [AssistantTopic.TenderDetail, AssistantTopic.OpenTenders, AssistantTopic.ActiveTenders, AssistantTopic.ClosedTenders, AssistantTopic.AllTenders,
            AssistantTopic.Deadlines, AssistantTopic.PendingApprovals, AssistantTopic.Notifications, AssistantTopic.SupplierPerformance, AssistantTopic.ActivitySummary,
            AssistantTopic.DepartmentSpending, AssistantTopic.EvaluationResults],
        SystemRoles.EvaluationCommittee => [AssistantTopic.TenderDetail, AssistantTopic.OpenTenders, AssistantTopic.ActiveTenders, AssistantTopic.ClosedTenders, AssistantTopic.AllTenders,
            AssistantTopic.Deadlines, AssistantTopic.PendingApprovals, AssistantTopic.Notifications, AssistantTopic.ActivitySummary, AssistantTopic.EvaluationResults],
        SystemRoles.RequestingDepartment => [AssistantTopic.TenderDetail, AssistantTopic.OpenTenders, AssistantTopic.ActiveTenders, AssistantTopic.ClosedTenders, AssistantTopic.AllTenders,
            AssistantTopic.Deadlines, AssistantTopic.PurchaseRequests, AssistantTopic.DelayedRequests, AssistantTopic.PendingApprovals, AssistantTopic.Notifications, AssistantTopic.ActivitySummary],
        SystemRoles.Admin => [AssistantTopic.PendingApprovals, AssistantTopic.Notifications, AssistantTopic.Suppliers, AssistantTopic.ActivitySummary],
        SystemRoles.Supplier => [AssistantTopic.MyInvitations, AssistantTopic.MyOffers, AssistantTopic.Deadlines, AssistantTopic.PendingApprovals, AssistantTopic.Notifications],
        _ => []
    };

    public async Task<AssistantDataset> RunAsync(AssistantUser user, ClaimsPrincipal principal, AssistantIntent intent, int limit, CancellationToken ct)
    {
        limit = Math.Clamp(limit, 1, ExportLimit);
        var ar = intent.Arabic;
        var (from, to, periodLabel) = Period(intent.Period, ar);
        var own = intent.OwnDepartment == true;
        var scope = new List<string> { Labels.Role(user.Role, ar) };
        if (periodLabel != null) scope.Add(periodLabel);
        if (own) scope.Add(user.DepartmentName is { } d ? (ar ? "قسم " : "Department: ") + d : ar ? "لا يوجد قسم مرتبط بحسابك" : "No department on your account");
        else if (user.SeesAllDepartments && intent.OwnDepartment == false) scope.Add(ar ? "جميع الأقسام المصرّح بها" : "All authorized departments");

        AssistantDataset New(string en, string arTitle) => new() { Topic = intent.Topic, Title = ar ? arTitle : en, Arabic = ar, ScopeNote = string.Join(" · ", scope) };
        var tenders = access.Tenders(user);
        var requests = access.Requests(user);
        if (own) { tenders = tenders.Where(t => t.PurchaseRequest.DepartmentId == user.DepartmentId); requests = requests.Where(p => p.DepartmentId == user.DepartmentId); }
        var now = clock.GetUtcNow().UtcDateTime;

        switch (intent.Topic)
        {
            case AssistantTopic.OpenTenders:
            case AssistantTopic.ActiveTenders:
            case AssistantTopic.ClosedTenders:
            case AssistantTopic.AllTenders:
            {
                var (en, a) = intent.Topic switch
                {
                    AssistantTopic.OpenTenders => ("Open tenders (bidding)", "المناقصات المفتوحة للتقديم"),
                    AssistantTopic.ActiveTenders => ("Active tenders", "المناقصات الجارية"),
                    AssistantTopic.ClosedTenders => ("Closed tenders", "المناقصات المغلقة"),
                    _ => ("Tenders", "المناقصات")
                };
                var q = intent.Topic switch
                {
                    AssistantTopic.OpenTenders => tenders.Where(t => t.Status == TenderStatus.Bidding),
                    AssistantTopic.ActiveTenders => tenders.Where(t => !Finished.Contains(t.Status) && t.Status != TenderStatus.Draft),
                    AssistantTopic.ClosedTenders => tenders.Where(t => Finished.Contains(t.Status)),
                    _ => tenders
                };
                if (from != null)
                    q = intent.Topic == AssistantTopic.ClosedTenders
                        ? q.Where(t => (t.ClosedAt ?? t.AwardedAt ?? t.CreatedAt) >= from && (t.ClosedAt ?? t.AwardedAt ?? t.CreatedAt) < to)
                        : q.Where(t => t.CreatedAt >= from && t.CreatedAt < to);
                var ds = New(en, a);
                await TenderRows(ds, q, user, limit, ct);
                return ds;
            }
            case AssistantTopic.DraftTenders:
            case AssistantTopic.AwaitingApproval:
            case AssistantTopic.SearchTenders:
            {
                var ds = intent.Topic switch
                {
                    AssistantTopic.DraftTenders => New("Draft tenders", "مسودات المناقصات"),
                    AssistantTopic.AwaitingApproval => New("Tenders awaiting approval (Manager or Top Committee review)", "مناقصات بانتظار الاعتماد (مراجعة المدير أو اللجنة العليا)"),
                    _ => New($"Tenders matching \"{intent.SearchText}\"", $"المناقصات المطابقة لـ «{intent.SearchText}»")
                };
                if (intent.Topic == AssistantTopic.SearchTenders && string.IsNullOrWhiteSpace(intent.SearchText))
                { ds.Empty = ar ? "عن ماذا أبحث؟ مثال: «ابحث عن عطاء أجهزة كمبيوتر»." : "What should I search for? For example: \"search tenders for laptops\"."; return ds; }
                var q = intent.Topic switch
                {
                    AssistantTopic.DraftTenders => tenders.Where(t => t.Status == TenderStatus.Draft),
                    AssistantTopic.AwaitingApproval => tenders.Where(t => t.Status == TenderStatus.ManagerReview || t.Status == TenderStatus.TopCommitteeReview),
                    _ => tenders.Where(t => t.Title.Contains(intent.SearchText!) || t.Description.Contains(intent.SearchText!))
                };
                await TenderRows(ds, q, user, limit, ct);
                return ds;
            }
            case AssistantTopic.TenderAttachments:
            {
                var ds = New($"Attachments of tender T-{intent.RecordId:0000}", $"مرفقات المناقصة T-{intent.RecordId:0000}");
                var t = await tenders.Where(x => x.Id == intent.RecordId).Select(x => new { x.Id, x.Title, x.PurchaseRequestId }).SingleOrDefaultAsync(ct);
                if (t == null) { ds.Empty = ar ? "لم أجد هذه المناقصة ضمن السجلات المصرّح لك بعرضها." : "I couldn't find that tender among the records you are authorized to view."; return ds; }
                // Requirement documents of the tender only; suppliers' bid documents stay on the confidential bid pages.
                var docs = await db.Documents.AsNoTracking().Where(d => d.TenderId == t.Id).OrderBy(d => d.UploadedAt).ThenBy(d => d.Id)
                    .Select(d => new { d.Id, d.FileName, d.SizeBytes, d.UploadedAt }).Take(limit).ToListAsync(ct);
                ds.Columns.AddRange([new(ar ? "الملف" : "File", ColumnKind.Text, 40), new(ar ? "الحجم (KB)" : "Size (KB)", ColumnKind.Number, 12), new(ar ? "تاريخ الرفع" : "Uploaded", ColumnKind.DateTime, 18)]);
                foreach (var d in docs) ds.Rows.Add([d.FileName, Math.Round(d.SizeBytes / 1024.0, 1), d.UploadedAt]);
                if (docs.Count == 0) ds.Empty = ar ? $"لا توجد مرفقات متطلبات لهذه المناقصة (T-{t.Id:0000})." : $"Tender T-{t.Id:0000} has no requirement attachments.";
                ds.TotalCount = docs.Count;
                var area = user.Role switch { SystemRoles.SourcingOfficer => "SourcingOfficer", SystemRoles.EvaluationCommittee => "EvaluationCommittee", SystemRoles.SourcingManager => "SourcingManager", SystemRoles.TopCommittee => "TopCommittee", _ => null };
                if (area != null) foreach (var d in docs.Take(5)) ds.Links.Add($"/{area}/Workflow/Download/{d.Id}");
                ds.Links.Insert(0, AssistantActions.TenderUrl(user, t.Id, t.PurchaseRequestId));
                return ds;
            }
            case AssistantTopic.TenderDetail:
            {
                var ds = New($"Tender T-{intent.RecordId:0000}", $"المناقصة T-{intent.RecordId:0000}");
                var t = await tenders.Where(x => x.Id == intent.RecordId).Select(x => new
                {
                    x.Id, x.Title, x.Description, x.Status, x.CreatedAt, x.EstimatedValue, x.Currency, x.IsCritical, x.AwardedAt,
                    Department = x.PurchaseRequest.Department.Name, x.PurchaseRequestId,
                    Round = x.Rounds.OrderByDescending(r => r.RoundNumber).Select(r => new { r.RoundNumber, r.Status, r.Deadline, Invited = r.Invitations.Count(), Accepted = r.Invitations.Count(i => i.Status == InvitationStatus.Accepted) }).FirstOrDefault(),
                    Items = x.Items.Count()
                }).SingleOrDefaultAsync(ct);
                if (t == null) { ds.Empty = ar ? "لم أجد هذه المناقصة ضمن السجلات المصرّح لك بعرضها." : "I couldn't find that tender among the records you are authorized to view."; return ds; }
                ds.Columns.AddRange([new(ar ? "البند" : "Field", ColumnKind.Text, 30), new(ar ? "القيمة" : "Value", ColumnKind.Text, 70)]);
                void Row(string en2, string ar2, object? v) => ds.Rows.Add([ar ? ar2 : en2, v]);
                Row("Reference", "المرجع", $"T-{t.Id:0000}");
                Row("Title", "العنوان", t.Title);
                Row("Status", "الحالة", Labels.Tender(t.Status, ar));
                Row("Department", "القسم", t.Department);
                Row("Purchase request", "طلب الشراء", $"PR-{t.PurchaseRequestId:0000}");
                Row("Created", "تاريخ الإنشاء", JordanTime.Display(t.CreatedAt));
                if (t.Round != null)
                {
                    Row("Current round", "الجولة الحالية", $"{t.Round.RoundNumber} · {Labels.Round(t.Round.Status, ar)}");
                    Row("Deadline", "الموعد النهائي", t.Round.Deadline is { } dl ? JordanTime.Display(dl) : "—");
                    if (user.Role != SystemRoles.RequestingDepartment) Row("Invited / accepted suppliers", "الموردون المدعوون / القابلون", $"{t.Round.Invited} / {t.Round.Accepted}");
                }
                Row("Items", "عدد البنود", t.Items);
                Row("Critical", "حرجة", t.IsCritical ? (ar ? "نعم" : "Yes") : (ar ? "لا" : "No"));
                if (user.SeesFinancials) Row("Estimated value", "القيمة التقديرية", $"{t.EstimatedValue:N2} {t.Currency}");
                if (t.AwardedAt is { } aw) Row("Awarded", "تاريخ الإرساء", JordanTime.Display(aw));
                Row("Description", "الوصف", t.Description.Length > 600 ? t.Description[..600] + "…" : t.Description);
                ds.TotalCount = 1;
                ds.Links.Add(user.Role == SystemRoles.RequestingDepartment ? "/Department/PurchaseRequests/Details/" + t.PurchaseRequestId : "/Workflow/Details/" + t.Id);
                return ds;
            }
            case AssistantTopic.Deadlines:
            {
                var ds = New("Tenders approaching their bidding deadline (next 14 days)", "المناقصات التي يقترب موعد إغلاقها (خلال 14 يومًا)");
                var until = now.AddDays(14);
                if (user.Role == SystemRoles.Supplier)
                {
                    var rows = await db.TenderInvitations.AsNoTracking()
                        .Where(i => i.SupplierId == user.SupplierId && i.Status != InvitationStatus.Declined && i.TenderRound.Status == TenderRoundStatus.Open && i.TenderRound.Deadline >= now && i.TenderRound.Deadline <= until)
                        .OrderBy(i => i.TenderRound.Deadline).Take(limit + 1)
                        .Select(i => new { i.Id, i.TenderRound.TenderId, i.TenderRound.Tender.Title, i.Status, i.TenderRound.Deadline }).ToListAsync(ct);
                    ds.Columns.AddRange([new(ar ? "المرجع" : "Ref", ColumnKind.Text, 10), new(ar ? "المناقصة" : "Tender", ColumnKind.Text, 40), new(ar ? "حالة الدعوة" : "Invitation", ColumnKind.Text, 14), new(ar ? "الموعد النهائي" : "Deadline", ColumnKind.DateTime, 18), new(ar ? "الوقت المتبقي" : "Time left", ColumnKind.Text, 14)]);
                    foreach (var r in rows.Take(limit)) ds.Rows.Add([$"T-{r.TenderId:0000}", r.Title, Labels.Invitation(r.Status, ar), r.Deadline, Left(r.Deadline!.Value - now, ar)]);
                    Finish(ds, rows.Count, limit); ds.Links.Add("/Supplier/Invitations");
                    return ds;
                }
                var list = await tenders.Select(t => new { t.Id, t.Title, Department = t.PurchaseRequest.Department.Name, Round = t.Rounds.OrderByDescending(r => r.RoundNumber).Select(r => new { r.RoundNumber, r.Status, r.Deadline }).FirstOrDefault() })
                    .Where(x => x.Round != null && x.Round.Status == TenderRoundStatus.Open && x.Round.Deadline >= now && x.Round.Deadline <= until)
                    .OrderBy(x => x.Round!.Deadline).Take(limit + 1).ToListAsync(ct);
                ds.Columns.AddRange([new(ar ? "المرجع" : "Ref", ColumnKind.Text, 10), new(ar ? "المناقصة" : "Tender", ColumnKind.Text, 38), new(ar ? "القسم" : "Department", ColumnKind.Text, 18), new(ar ? "الجولة" : "Round", ColumnKind.Number, 8), new(ar ? "الموعد النهائي" : "Deadline", ColumnKind.DateTime, 18), new(ar ? "الوقت المتبقي" : "Time left", ColumnKind.Text, 14)]);
                foreach (var r in list.Take(limit)) ds.Rows.Add([$"T-{r.Id:0000}", r.Title, r.Department, r.Round!.RoundNumber, r.Round.Deadline, Left(r.Round.Deadline!.Value - now, ar)]);
                Finish(ds, list.Count, limit);
                return ds;
            }
            case AssistantTopic.PurchaseRequests:
            case AssistantTopic.DelayedRequests:
            {
                var delayed = intent.Topic == AssistantTopic.DelayedRequests;
                var ds = delayed ? New("Delayed purchase requests (needed-by date passed, not yet converted to a tender)", "طلبات الشراء المتأخرة (تجاوزت التاريخ المطلوب ولم تتحول إلى مناقصة)") : New("Purchase requests", "طلبات الشراء");
                var q = requests;
                if (delayed) q = q.Where(p => p.NeededBy != null && p.NeededBy < now && p.Status != PurchaseRequestStatus.ConvertedToTender && p.Status != PurchaseRequestStatus.Rejected && p.Status != PurchaseRequestStatus.Draft);
                if (from != null) q = q.Where(p => p.CreatedAt >= from && p.CreatedAt < to);
                var total = await q.CountAsync(ct);
                var rows = await q.OrderByDescending(p => p.CreatedAt).Take(limit)
                    .Select(p => new { p.Id, p.Title, Department = p.Department.Name, p.Priority, p.Status, p.NeededBy, p.SubmittedAt, p.CreatedAt, Items = p.Items.Count() }).ToListAsync(ct);
                ds.Columns.AddRange([new(ar ? "المرجع" : "Ref", ColumnKind.Text, 10), new(ar ? "العنوان" : "Title", ColumnKind.Text, 36), new(ar ? "القسم" : "Department", ColumnKind.Text, 18), new(ar ? "الأولوية" : "Priority", ColumnKind.Text, 10),
                    new(ar ? "الحالة" : "Status", ColumnKind.Text, 16), new(ar ? "مطلوب بتاريخ" : "Needed by", ColumnKind.Date, 12), new(ar ? "تاريخ الإرسال" : "Submitted", ColumnKind.Date, 12), new(ar ? "البنود" : "Items", ColumnKind.Number, 8)]);
                foreach (var p in rows) ds.Rows.Add([$"PR-{p.Id:0000}", p.Title, p.Department, Labels.Priority(p.Priority, ar), Labels.Request(p.Status, ar), p.NeededBy, p.SubmittedAt, p.Items]);
                ds.TotalCount = total; ds.Truncated = total > rows.Count;
                foreach (var g in rows.GroupBy(p => p.Status).OrderByDescending(g => g.Count())) ds.Chart.Add((Labels.Request(g.Key, ar), g.Count()));
                ds.ChartTitle = ar ? "حسب الحالة" : "By status";
                ds.Links.Add(user.Role == SystemRoles.RequestingDepartment ? "/Department/PurchaseRequests" : "/SourcingOfficer/PurchaseRequests");
                return ds;
            }
            case AssistantTopic.PendingApprovals:
            {
                var ds = New("Items that need your action", "عناصر بانتظار إجرائك");
                ds.Columns.AddRange([new(ar ? "المهمة" : "Task", ColumnKind.Text, 28), new(ar ? "السجل" : "Record", ColumnKind.Text, 46), new(ar ? "الحالة" : "Status", ColumnKind.Text, 12), new(ar ? "الصفحة" : "Page", ColumnKind.Text, 30)]);
                if (user.Role == SystemRoles.Supplier)
                {
                    var rows = await db.TenderInvitations.AsNoTracking().Where(i => i.SupplierId == user.SupplierId && i.Status == InvitationStatus.Pending && i.TenderRound.Status == TenderRoundStatus.Open)
                        .OrderBy(i => i.TenderRound.Deadline).Take(limit).Select(i => new { i.Id, i.TenderRound.TenderId, i.TenderRound.Tender.Title, i.TenderRound.Deadline }).ToListAsync(ct);
                    foreach (var r in rows) ds.Rows.Add([ar ? "الرد على دعوة" : "Respond to invitation", $"T-{r.TenderId:0000} – {r.Title}", ar ? "بانتظار" : "Pending", "/Supplier/Invitations/Details/" + r.Id]);
                }
                else
                    foreach (var task in (await dashboard.TasksAsync(user.Role, principal)).Take(limit))
                        ds.Rows.Add([task.Title, task.Context, task.Status, task.Link]);
                ds.TotalCount = ds.Rows.Count;
                ds.Links.Add(user.Role == SystemRoles.Supplier ? "/Supplier/Invitations" : "/Tasks");
                return ds;
            }
            case AssistantTopic.Notifications:
            {
                var ds = New("Your latest notifications", "أحدث إشعاراتك");
                var q = db.Notifications.AsNoTracking().Where(n => n.UserId == user.Id);
                if (from != null) q = q.Where(n => n.CreatedAt >= from && n.CreatedAt < to);
                var unread = await q.CountAsync(n => !n.IsRead, ct);
                var rows = await q.OrderByDescending(n => n.CreatedAt).Take(Math.Min(limit, 200)).Select(n => new { n.Title, n.Message, n.IsRead, n.CreatedAt, n.Link }).ToListAsync(ct);
                ds.Columns.AddRange([new(ar ? "العنوان" : "Title", ColumnKind.Text, 26), new(ar ? "الرسالة" : "Message", ColumnKind.Text, 50), new(ar ? "مقروء" : "Read", ColumnKind.Text, 8), new(ar ? "التاريخ" : "Date", ColumnKind.DateTime, 18)]);
                foreach (var n in rows) ds.Rows.Add([n.Title, n.Message, n.IsRead ? (ar ? "نعم" : "Yes") : (ar ? "لا" : "No"), n.CreatedAt]);
                ds.Summary.Add((ar ? "غير المقروءة" : "Unread", unread.ToString()));
                ds.TotalCount = rows.Count; ds.Links.Add("/Notifications");
                return ds;
            }
            case AssistantTopic.Suppliers:
            {
                var ds = New("Suppliers", "الموردون");
                var q = db.Suppliers.AsNoTracking();
                if (from != null) q = q.Where(s => s.CreatedAt >= from && s.CreatedAt < to);
                var total = await q.CountAsync(ct);
                var byStatus = await q.GroupBy(s => s.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
                var rows = await q.OrderBy(s => s.CompanyName).Take(limit).Select(s => new { s.CompanyName, s.Category, s.Status, s.RegistrationNumber, s.Email, s.CreatedAt }).ToListAsync(ct);
                ds.Columns.AddRange([new(ar ? "الشركة" : "Company", ColumnKind.Text, 32), new(ar ? "الفئة" : "Category", ColumnKind.Text, 18), new(ar ? "الحالة" : "Status", ColumnKind.Text, 12), new(ar ? "رقم السجل" : "Registration no.", ColumnKind.Text, 16), new(ar ? "البريد" : "Email", ColumnKind.Text, 28), new(ar ? "تاريخ التسجيل" : "Registered", ColumnKind.Date, 12)]);
                foreach (var s in rows) ds.Rows.Add([s.CompanyName, s.Category, Labels.Supplier(s.Status, ar), s.RegistrationNumber, s.Email, s.CreatedAt]);
                foreach (var g in byStatus.OrderByDescending(g => g.Count)) { ds.Summary.Add((Labels.Supplier(g.Key, ar), g.Count.ToString())); ds.Chart.Add((Labels.Supplier(g.Key, ar), g.Count)); }
                ds.ChartTitle = ar ? "الموردون حسب الحالة" : "Suppliers by status";
                ds.TotalCount = total; ds.Truncated = total > rows.Count;
                ds.Links.Add(user.Role == SystemRoles.Admin ? "/Admin/Suppliers" : "/SourcingOfficer/Invitations");
                return ds;
            }
            case AssistantTopic.SupplierPerformance:
            {
                var ds = New("Supplier performance (invitations, offers, compliance and awards)", "أداء الموردين (الدعوات والعروض والامتثال والإرساء)");
                var ids = tenders.Select(t => t.Id);
                var flat = await db.TenderInvitations.AsNoTracking().Where(i => ids.Contains(i.TenderRound.TenderId))
                    .Where(i => from == null || (i.InvitedAt >= from && i.InvitedAt < to))
                    .OrderByDescending(i => i.Id).Take(20000)
                    .Select(i => new
                    {
                        i.SupplierId, i.Supplier.CompanyName, i.Status,
                        Submitted = i.Bids.Any(b => b.Status == BidStatus.Submitted),
                        Compliant = i.Bids.Any(b => b.Status == BidStatus.Submitted && b.ComplianceStatus == ComplianceStatus.Compliant),
                        Won = i.Bids.Any(b => db.Tenders.Any(t => t.AwardedBidId == b.Id))
                    }).ToListAsync(ct);
                var groups = flat.GroupBy(x => new { x.SupplierId, x.CompanyName }).Select(g => new
                {
                    g.Key.CompanyName, Invited = g.Count(), Accepted = g.Count(x => x.Status == InvitationStatus.Accepted), Declined = g.Count(x => x.Status == InvitationStatus.Declined),
                    Submitted = g.Count(x => x.Submitted), Compliant = g.Count(x => x.Compliant), Won = g.Count(x => x.Won)
                }).OrderByDescending(g => g.Won).ThenByDescending(g => g.Submitted).ThenBy(g => g.CompanyName).ToList();
                ds.Columns.AddRange([new(ar ? "المورد" : "Supplier", ColumnKind.Text, 32), new(ar ? "الدعوات" : "Invited", ColumnKind.Number, 9), new(ar ? "مقبولة" : "Accepted", ColumnKind.Number, 9), new(ar ? "مرفوضة" : "Declined", ColumnKind.Number, 9),
                    new(ar ? "عروض مقدمة" : "Offers submitted", ColumnKind.Number, 11), new(ar ? "عروض مطابقة" : "Compliant offers", ColumnKind.Number, 11), new(ar ? "مرات الإرساء" : "Awards", ColumnKind.Number, 9), new(ar ? "نسبة الاستجابة %" : "Response rate %", ColumnKind.Number, 11)]);
                foreach (var g in groups.Take(limit)) ds.Rows.Add([g.CompanyName, g.Invited, g.Accepted, g.Declined, g.Submitted, g.Compliant, g.Won, g.Invited == 0 ? 0 : Math.Round(g.Submitted * 100.0 / g.Invited, 1)]);
                foreach (var g in groups.Take(8)) ds.Chart.Add((g.CompanyName, g.Submitted));
                ds.ChartTitle = ar ? "العروض المقدمة لكل مورد" : "Offers submitted per supplier";
                ds.Summary.Add((ar ? "موردون مشاركون" : "Suppliers involved", groups.Count.ToString()));
                ds.Summary.Add((ar ? "إجمالي الدعوات" : "Total invitations", flat.Count.ToString()));
                Finish(ds, groups.Count, limit);
                return ds;
            }
            case AssistantTopic.ActivitySummary:
            {
                (from, to, periodLabel) = from == null ? Period("month", ar) : (from, to, periodLabel);
                var ds = New("Procurement activity summary", "ملخص نشاط المشتريات");
                if (periodLabel != null && !ds.ScopeNote.Contains(periodLabel)) ds.Summary.Add((ar ? "الفترة" : "Period", periodLabel));
                ds.Columns.AddRange([new(ar ? "المؤشر" : "Metric", ColumnKind.Text, 60), new(ar ? "القيمة" : "Value", ColumnKind.Number, 20)]);
                void Metric(string en, string a, double v) => ds.Rows.Add([ar ? a : en, v]);
                if (user.Role != SystemRoles.Admin)
                {
                    Metric("Tenders created in period", "مناقصات أُنشئت خلال الفترة", await tenders.CountAsync(t => t.CreatedAt >= from && t.CreatedAt < to, ct));
                    Metric("Tenders awarded in period", "مناقصات أُرسيت خلال الفترة", await tenders.CountAsync(t => t.AwardedAt >= from && t.AwardedAt < to, ct));
                    Metric("Tenders currently open for bidding", "مناقصات مفتوحة للتقديم حاليًا", await tenders.CountAsync(t => t.Status == TenderStatus.Bidding, ct));
                    Metric("Tenders currently in progress", "مناقصات جارية حاليًا", await tenders.CountAsync(t => !Finished.Contains(t.Status) && t.Status != TenderStatus.Draft, ct));
                    var byStatus = await tenders.GroupBy(t => t.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
                    foreach (var g in byStatus.OrderByDescending(g => g.Count)) ds.Chart.Add((Labels.Tender(g.Key, ar), g.Count));
                    ds.ChartTitle = ar ? "المناقصات حسب الحالة الحالية" : "Tenders by current status";
                }
                if (user.Role is SystemRoles.SourcingOfficer or SystemRoles.RequestingDepartment)
                {
                    Metric("Purchase requests created in period", "طلبات شراء أُنشئت خلال الفترة", await requests.CountAsync(p => p.CreatedAt >= from && p.CreatedAt < to, ct));
                    Metric("Purchase requests submitted in period", "طلبات شراء أُرسلت خلال الفترة", await requests.CountAsync(p => p.SubmittedAt >= from && p.SubmittedAt < to, ct));
                    Metric("Purchase requests awaiting review", "طلبات شراء بانتظار المراجعة", await requests.CountAsync(p => p.Status == PurchaseRequestStatus.Submitted || p.Status == PurchaseRequestStatus.UnderReview, ct));
                }
                if (user.Role == SystemRoles.SourcingOfficer)
                    Metric("Supplier offers submitted in period", "عروض موردين قُدّمت خلال الفترة", await db.Bids.AsNoTracking().CountAsync(b => b.Status == BidStatus.Submitted && b.SubmittedAt >= from && b.SubmittedAt < to, ct));
                if (user.Role is SystemRoles.Admin or SystemRoles.SourcingOfficer)
                {
                    Metric("Supplier registrations in period", "تسجيلات موردين خلال الفترة", await db.Suppliers.AsNoTracking().CountAsync(s => s.CreatedAt >= from && s.CreatedAt < to, ct));
                    Metric("Supplier registrations awaiting review", "تسجيلات موردين بانتظار المراجعة", await db.Suppliers.AsNoTracking().CountAsync(s => s.Status == SupplierStatus.Pending, ct));
                }
                if (user.Role == SystemRoles.Admin)
                    Metric("Active user accounts", "حسابات مستخدمين فعّالة", await db.Users.AsNoTracking().CountAsync(u => u.IsActive, ct));
                if (user.SeesFinancials)
                    foreach (var c in await tenders.Where(t => t.AwardedAt >= from && t.AwardedAt < to).GroupBy(t => t.Currency).Select(g => new { g.Key, Sum = g.Sum(t => t.EstimatedValue) }).ToListAsync(ct))
                        ds.Summary.Add((ar ? $"القيمة التقديرية للمناقصات المرساة ({c.Key})" : $"Estimated value of awarded tenders ({c.Key})", c.Sum.ToString("N2")));
                ds.TotalCount = ds.Rows.Count;
                return ds;
            }
            case AssistantTopic.DepartmentSpending:
            {
                var ds = New("Procurement by department (estimated tender value)", "المشتريات حسب القسم (القيمة التقديرية للمناقصات)");
                var q = from == null ? tenders : tenders.Where(t => t.CreatedAt >= from && t.CreatedAt < to);
                var rows = await q.GroupBy(t => new { Department = t.PurchaseRequest.Department.Name, t.Currency })
                    .Select(g => new
                    {
                        g.Key.Department, g.Key.Currency, Count = g.Count(), Awarded = g.Count(t => t.Status == TenderStatus.Awarded),
                        Total = g.Sum(t => t.EstimatedValue), AwardedValue = g.Sum(t => t.Status == TenderStatus.Awarded ? t.EstimatedValue : 0m)
                    }).OrderByDescending(r => r.Total).Take(limit).ToListAsync(ct);
                ds.Columns.AddRange([new(ar ? "القسم" : "Department", ColumnKind.Text, 26), new(ar ? "العملة" : "Currency", ColumnKind.Text, 9), new(ar ? "المناقصات" : "Tenders", ColumnKind.Number, 10), new(ar ? "المرساة" : "Awarded", ColumnKind.Number, 10),
                    new(ar ? "إجمالي القيمة التقديرية" : "Total estimated value", ColumnKind.Money, 20), new(ar ? "قيمة المرساة" : "Awarded estimated value", ColumnKind.Money, 20)]);
                foreach (var r in rows) ds.Rows.Add([r.Department, r.Currency, r.Count, r.Awarded, r.Total, r.AwardedValue]);
                foreach (var r in rows.Take(8)) ds.Chart.Add(($"{r.Department} ({r.Currency})", (double)r.Total));
                ds.ChartTitle = ar ? "القيمة التقديرية حسب القسم" : "Estimated value by department";
                ds.Summary.Add((ar ? "ملاحظة" : "Note", ar ? "القيم تقديرية من سجلات المناقصات وليست مبالغ مدفوعة." : "Values are tender estimates recorded in the system, not paid amounts."));
                ds.TotalCount = rows.Count;
                return ds;
            }
            case AssistantTopic.EvaluationResults:
            {
                var ds = New("Evaluation results you are authorized to view", "نتائج التقييم المصرّح لك بعرضها");
                var ids = tenders.Select(t => t.Id);
                var q = db.TenderReports.AsNoTracking().Where(r => ids.Contains(r.TenderRound.TenderId) && r.SupersededAt == null && r.Status != ReportStatus.Draft);
                if (from != null) q = q.Where(r => r.CreatedAt >= from && r.CreatedAt < to);
                var rows = await q.OrderByDescending(r => r.CreatedAt).Take(limit + 1).Select(r => new
                {
                    r.TenderRound.TenderId, r.TenderRound.Tender.Title, r.TenderRound.RoundNumber, r.ReportType, r.VersionNumber, r.Status, r.CreatedAt,
                    Recommended = r.RecommendedBid != null ? r.RecommendedBid.TenderInvitation.Supplier.CompanyName : null, TenderStatus = r.TenderRound.Tender.Status
                }).ToListAsync(ct);
                ds.Columns.AddRange([new(ar ? "المرجع" : "Ref", ColumnKind.Text, 10), new(ar ? "المناقصة" : "Tender", ColumnKind.Text, 30), new(ar ? "الجولة" : "Round", ColumnKind.Number, 7), new(ar ? "نوع التقرير" : "Report", ColumnKind.Text, 16),
                    new(ar ? "الإصدار" : "Version", ColumnKind.Number, 7), new(ar ? "حالة التقرير" : "Report status", ColumnKind.Text, 12), new(ar ? "المورد الموصى به" : "Recommended supplier", ColumnKind.Text, 24), new(ar ? "حالة المناقصة" : "Tender status", ColumnKind.Text, 16), new(ar ? "التاريخ" : "Date", ColumnKind.Date, 12)]);
                foreach (var r in rows.Take(limit)) ds.Rows.Add([$"T-{r.TenderId:0000}", r.Title, r.RoundNumber, Labels.Words(r.ReportType.ToString()), r.VersionNumber, Labels.Words(r.Status.ToString()), r.Recommended ?? "—", Labels.Tender(r.TenderStatus, ar), r.CreatedAt]);
                ds.Summary.Add((ar ? "ملاحظة" : "Note", ar ? "الدرجات التفصيلية والأسعار متاحة في صفحة سير العمل المصرّح بها فقط." : "Detailed scores and prices remain on the authorized workflow pages."));
                Finish(ds, rows.Count, limit);
                return ds;
            }
            case AssistantTopic.MyInvitations:
            {
                var ds = New("Your tender invitations", "دعوات المناقصات الخاصة بك");
                var q = db.TenderInvitations.AsNoTracking().Where(i => i.SupplierId == user.SupplierId);
                if (from != null) q = q.Where(i => i.InvitedAt >= from && i.InvitedAt < to);
                var rows = await q.OrderByDescending(i => i.InvitedAt).Take(limit + 1)
                    .Select(i => new { i.TenderRound.TenderId, i.TenderRound.Tender.Title, i.TenderRound.RoundNumber, i.Status, RoundStatus = i.TenderRound.Status, i.TenderRound.Deadline, i.InvitedAt }).ToListAsync(ct);
                ds.Columns.AddRange([new(ar ? "المرجع" : "Ref", ColumnKind.Text, 10), new(ar ? "المناقصة" : "Tender", ColumnKind.Text, 36), new(ar ? "الجولة" : "Round", ColumnKind.Number, 7), new(ar ? "حالة الدعوة" : "Invitation", ColumnKind.Text, 12), new(ar ? "حالة الجولة" : "Round status", ColumnKind.Text, 14), new(ar ? "الموعد النهائي" : "Deadline", ColumnKind.DateTime, 18), new(ar ? "تاريخ الدعوة" : "Invited", ColumnKind.Date, 12)]);
                foreach (var r in rows.Take(limit)) ds.Rows.Add([$"T-{r.TenderId:0000}", r.Title, r.RoundNumber, Labels.Invitation(r.Status, ar), Labels.Round(r.RoundStatus, ar), r.Deadline, r.InvitedAt]);
                Finish(ds, rows.Count, limit); ds.Links.Add("/Supplier/Invitations");
                return ds;
            }
            case AssistantTopic.MyOffers:
            {
                var ds = New("Your offers", "عروضك");
                var q = db.Bids.AsNoTracking().Where(b => b.TenderInvitation.SupplierId == user.SupplierId && b.Status != BidStatus.Superseded);
                if (from != null) q = q.Where(b => b.CreatedAt >= from && b.CreatedAt < to);
                var rows = await q.OrderByDescending(b => b.CreatedAt).Take(limit + 1).Select(b => new
                {
                    b.TenderInvitation.TenderRound.TenderId, b.TenderInvitation.TenderRound.Tender.Title, b.RevisionNumber, b.Status, b.SubmittedAt, b.CurrencyCode,
                    Total = b.Items.Sum(i => i.Quantity * i.UnitPrice) + b.ShippingCost + b.InstallationCost + b.MaintenanceCost + b.TaxAmount + b.OtherCost
                }).ToListAsync(ct);
                ds.Columns.AddRange([new(ar ? "المرجع" : "Ref", ColumnKind.Text, 10), new(ar ? "المناقصة" : "Tender", ColumnKind.Text, 36), new(ar ? "النسخة" : "Version", ColumnKind.Number, 8), new(ar ? "الحالة" : "Status", ColumnKind.Text, 12), new(ar ? "تاريخ التقديم" : "Submitted", ColumnKind.DateTime, 18), new(ar ? "العملة" : "Currency", ColumnKind.Text, 8), new(ar ? "إجمالي عرضك" : "Your offer total", ColumnKind.Money, 16)]);
                foreach (var r in rows.Take(limit)) ds.Rows.Add([$"T-{r.TenderId:0000}", r.Title, r.RevisionNumber, Labels.Words(r.Status.ToString()), r.SubmittedAt, r.CurrencyCode, r.Total]);
                Finish(ds, rows.Count, limit); ds.Links.Add("/Supplier/Offers");
                return ds;
            }
            default:
                return New("No data", "لا توجد بيانات");
        }
    }

    private static async Task TenderRows(AssistantDataset ds, IQueryable<Tender> q, AssistantUser user, int limit, CancellationToken ct)
    {
        var ar = ds.Arabic;
        var total = await q.CountAsync(ct);
        var byStatus = await q.GroupBy(t => t.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        var rows = await q.OrderByDescending(t => t.CreatedAt).Take(limit).Select(t => new
        {
            t.Id, t.Title, t.Status, Department = t.PurchaseRequest.Department.Name, t.CreatedAt, t.EstimatedValue, t.Currency,
            Round = t.Rounds.OrderByDescending(r => r.RoundNumber).Select(r => new { r.RoundNumber, r.Deadline }).FirstOrDefault()
        }).ToListAsync(ct);
        ds.Columns.AddRange([new(ar ? "المرجع" : "Ref", ColumnKind.Text, 10), new(ar ? "العنوان" : "Title", ColumnKind.Text, 36), new(ar ? "الحالة" : "Status", ColumnKind.Text, 16), new(ar ? "القسم" : "Department", ColumnKind.Text, 18),
            new(ar ? "الجولة" : "Round", ColumnKind.Number, 7), new(ar ? "الموعد النهائي" : "Deadline", ColumnKind.DateTime, 17), new(ar ? "تاريخ الإنشاء" : "Created", ColumnKind.Date, 12)]);
        if (user.SeesFinancials) ds.Columns.AddRange([new(ar ? "القيمة التقديرية" : "Estimated value", ColumnKind.Money, 16), new(ar ? "العملة" : "Currency", ColumnKind.Text, 8)]);
        foreach (var t in rows)
        {
            object?[] row = [$"T-{t.Id:0000}", t.Title, Labels.Tender(t.Status, ar), t.Department, t.Round?.RoundNumber, t.Round?.Deadline, t.CreatedAt];
            ds.Rows.Add(user.SeesFinancials ? [.. row, t.EstimatedValue, t.Currency] : row);
        }
        foreach (var g in byStatus.OrderByDescending(g => g.Count)) ds.Chart.Add((Labels.Tender(g.Key, ar), g.Count));
        ds.ChartTitle = ar ? "حسب الحالة" : "By status";
        if (user.SeesFinancials && rows.Count > 0)
            foreach (var c in rows.GroupBy(r => r.Currency)) ds.Summary.Add((ar ? $"مجموع القيمة التقديرية للصفوف المعروضة ({c.Key})" : $"Estimated value of listed rows ({c.Key})", c.Sum(r => r.EstimatedValue).ToString("N2")));
        ds.TotalCount = total; ds.Truncated = total > rows.Count;
        ds.Links.Add(user.Role switch
        {
            SystemRoles.SourcingOfficer => "/SourcingOfficer/Workflow", SystemRoles.RequestingDepartment => "/Department/PurchaseRequests",
            SystemRoles.EvaluationCommittee => "/EvaluationCommittee/Evaluations", SystemRoles.SourcingManager => "/SourcingManager/Workflow", _ => "/TopCommittee/Workflow"
        });
    }

    private static void Finish(AssistantDataset ds, int fetched, int limit) { ds.Truncated = fetched > limit; ds.TotalCount = Math.Min(fetched, limit); if (ds.Truncated) ds.TotalCount = limit; }

    private static string Left(TimeSpan span, bool ar) => span.TotalHours < 24
        ? (ar ? $"{Math.Max(0, (int)span.TotalHours)} ساعة" : $"{Math.Max(0, (int)span.TotalHours)} h")
        : (ar ? $"{(int)span.TotalDays} يوم" : $"{(int)span.TotalDays} days");

    // Period boundaries are calendar periods in Jordan time, converted back to UTC for the stored UTC columns.
    public static (DateTime? From, DateTime? To, string? Label) Period(string? key, bool ar)
    {
        if (key == null) return (null, null, null);
        var local = JordanTime.Now.Date;
        DateTime start, end; string en, a;
        switch (key)
        {
            case "today": start = local; end = local.AddDays(1); en = "Today"; a = "اليوم"; break;
            case "week": start = local.AddDays(-(int)local.DayOfWeek); end = start.AddDays(7); en = "This week"; a = "هذا الأسبوع"; break;
            case "month": start = new DateTime(local.Year, local.Month, 1); end = start.AddMonths(1); en = start.ToString("MMMM yyyy", System.Globalization.CultureInfo.InvariantCulture); a = "الشهر الحالي " + start.ToString("MM/yyyy"); break;
            case "lastmonth": end = new DateTime(local.Year, local.Month, 1); start = end.AddMonths(-1); en = start.ToString("MMMM yyyy", System.Globalization.CultureInfo.InvariantCulture); a = "الشهر الماضي " + start.ToString("MM/yyyy"); break;
            case "year": start = new DateTime(local.Year, 1, 1); end = start.AddYears(1); en = "Year " + local.Year; a = "عام " + local.Year; break;
            default:
                if (key.StartsWith('d') && int.TryParse(key[1..], out var days)) { start = local.AddDays(-days + 1); end = local.AddDays(1); en = $"Last {days} days"; a = $"آخر {days} يومًا"; break; }
                return (null, null, null);
        }
        return (TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(start, DateTimeKind.Unspecified), JordanTime.Zone),
            TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(end, DateTimeKind.Unspecified), JordanTime.Zone), ar ? a : en);
    }
}

// Display labels for stored enum values (English words or Arabic).
public static partial class Labels
{
    public static string Words(string pascal) => SplitWords().Replace(pascal, " ");
    [GeneratedRegex("(?<=[a-z])(?=[A-Z])")] private static partial Regex SplitWords();

    public static string Tender(TenderStatus s, bool ar) => !ar ? Words(s.ToString()) : s switch
    {
        TenderStatus.Draft => "مسودة", TenderStatus.Bidding => "مفتوحة للتقديم", TenderStatus.Evaluation => "قيد التقييم", TenderStatus.OfficerReview => "مراجعة مسؤول التوريد",
        TenderStatus.ManagerReview => "مراجعة المدير", TenderStatus.TopCommitteeReview => "مراجعة اللجنة العليا", TenderStatus.Awarded => "مُرساة", TenderStatus.Rejected => "مرفوضة", _ => "ملغاة"
    };
    public static string Request(PurchaseRequestStatus s, bool ar) => !ar ? Words(s.ToString()) : s switch
    {
        PurchaseRequestStatus.Draft => "مسودة", PurchaseRequestStatus.Submitted => "مُرسل", PurchaseRequestStatus.Returned => "مُعاد للتعديل", PurchaseRequestStatus.Rejected => "مرفوض",
        PurchaseRequestStatus.Accepted => "مقبول", PurchaseRequestStatus.UnderReview => "قيد المراجعة", PurchaseRequestStatus.Approved => "معتمد", _ => "تحول إلى مناقصة"
    };
    public static string Round(TenderRoundStatus s, bool ar) => !ar ? Words(s.ToString()) : s switch
    {
        TenderRoundStatus.Draft => "مسودة", TenderRoundStatus.Open => "مفتوحة", TenderRoundStatus.Closed => "مغلقة", TenderRoundStatus.Failed => "فاشلة", _ => "أُرسلت للتقييم"
    };
    public static string Invitation(InvitationStatus s, bool ar) => !ar ? s.ToString() : s switch { InvitationStatus.Pending => "بانتظار الرد", InvitationStatus.Accepted => "مقبولة", _ => "مرفوضة" };
    public static string Supplier(SupplierStatus s, bool ar) => !ar ? s.ToString() : s switch { SupplierStatus.Pending => "بانتظار المراجعة", SupplierStatus.Approved => "معتمد", SupplierStatus.Rejected => "مرفوض", _ => "موقوف" };
    public static string Priority(RequestPriority p, bool ar) => !ar ? p.ToString() : p switch { RequestPriority.Low => "منخفضة", RequestPriority.Normal => "عادية", RequestPriority.High => "مرتفعة", _ => "عاجلة" };
    public static string Role(string role, bool ar) => !ar ? role : role switch
    {
        SystemRoles.Admin => "مدير النظام", SystemRoles.RequestingDepartment => "القسم الطالب", SystemRoles.SourcingOfficer => "مسؤول التوريد", SystemRoles.Supplier => "مورد",
        SystemRoles.SourcingManager => "مدير التوريد", SystemRoles.EvaluationCommittee => "لجنة التقييم الفني والمالي", SystemRoles.TopCommittee => "اللجنة العليا", _ => role
    };
}
