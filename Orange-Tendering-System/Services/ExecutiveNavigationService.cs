using System.Globalization;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.ViewModels;

namespace Orange_Tendering_System.Services;

public sealed record ExecutivePage(string Id, string Label, string Area, string Url, string[] Aliases);
public sealed record ExecutiveNavigationResult(string State, string? Url, string Message, ExecutivePage[] Choices);

// Site knowledge is grounded in the real navigation, registered GET actions and current
// Identity permissions. Neither a transcript nor a model can supply a destination URL.
public sealed class ExecutiveNavigationService(TenderWorkflowService workflow, UserManager<ApplicationUser> users,
    ApplicationDbContext db, IActionDescriptorCollectionProvider descriptors, LinkGenerator links,
    IAuthorizationPolicyProvider policies, IAuthorizationService authorization)
{
    public async Task<ExecutivePage[]> Catalog(ClaimsPrincipal principal, string? workspaceRole = null)
    {
        var actor = await workflow.Actor(principal);
        if (!actor.EmailConfirmed || actor.MustChangePassword || await users.IsLockedOutAsync(actor)) throw new AgentException("Complete account verification first.", 403);
        var roles = await users.GetRolesAsync(actor);
        var current = new ClaimsPrincipal(new ClaimsIdentity(principal.Claims.Where(c => c.Type != ClaimTypes.Role)
            .Concat(roles.Select(r => new Claim(ClaimTypes.Role, r))), principal.Identity?.AuthenticationType ?? "Identity.Application"));
        var pages = new List<ExecutivePage>();
        var available = WorkspaceNavigation.Workspaces.Where(w => roles.Contains(w.Role) && (workspaceRole == null || workspaceRole == w.Role)).ToArray();
        foreach (var workspace in available)
            foreach (var item in workspace.Items) await Add(workspace.Area, item);
        foreach (var item in WorkspaceNavigation.SharedItems) await Add("", item);
        await Add("", new("Device passkey", "Biometrics", "Fingerprint", "user", ""));
        await Add("", new("How It Works", "Home", "HowItWorks", "home", ""));
        return pages.DistinctBy(p => p.Id).ToArray();

        async Task Add(string defaultArea, NavItem item)
        {
            var area = item.Area ?? defaultArea;
            var candidates = descriptors.ActionDescriptors.Items.OfType<ControllerActionDescriptor>()
                .Where(a => a.ControllerName == item.Controller && a.ActionName == item.Action && (a.RouteValues.TryGetValue("area", out var actionArea) ? actionArea ?? "" : "") == area).ToArray();
            foreach (var action in candidates)
            {
                var methods = action.ActionConstraints?.OfType<Microsoft.AspNetCore.Mvc.ActionConstraints.HttpMethodActionConstraint>().SelectMany(c => c.HttpMethods).ToArray() ?? [];
                if (methods.Length > 0 && !methods.Contains("GET", StringComparer.OrdinalIgnoreCase)) continue;
                var data = action.ControllerTypeInfo.GetCustomAttributes<AuthorizeAttribute>(true)
                    .Concat(action.MethodInfo.GetCustomAttributes<AuthorizeAttribute>(true)).Cast<IAuthorizeData>().ToArray();
                var policy = await AuthorizationPolicy.CombineAsync(policies, data);
                if (policy != null && !(await authorization.AuthorizeAsync(current, null, policy)).Succeeded) continue;
                var url = links.GetPathByAction(item.Action, item.Controller, new { area });
                if (url == null || !url.StartsWith('/') || url.StartsWith("//")) continue;
                pages.Add(new($"{area}:{item.Controller}:{item.Action}", item.Label, area, url, Aliases(item.Label)));
                break;
            }
        }
    }

    public async Task<ExecutiveNavigationResult> Resolve(string? command, string? area, ClaimsPrincipal principal, string? pageId = null)
    {
        if (command?.Length > 2000 || pageId?.Length > 150) throw new AgentException("Navigation command is too long.");
        var pages = await Catalog(principal);
        var result = pageId == null ? Match(command ?? "", area, pages) : pages.FirstOrDefault(p => p.Id == pageId) is { } page
            ? new ExecutiveNavigationResult("Navigate", page.Url, $"Opening {page.Label} / فتح الصفحة", [page])
            : new ExecutiveNavigationResult("Unavailable", null, "هذه الصفحة غير متاحة لحسابك. / This page is unavailable for your account.", []);
        var actorId = users.GetUserId(principal);
        db.AuditLogs.Add(new AuditLog { UserId = actorId, EntityName = "ExecutiveNavigation", EntityId = Guid.NewGuid().ToString(), Action = "VoiceNavigation" + result.State,
            NewValues = System.Text.Json.JsonSerializer.Serialize(new { result.State, result.Url, PageIds = result.Choices.Select(p => p.Id), CommandHash = Reports.ReportCryptography.Hash(command ?? pageId ?? "") }), CreatedAt = workflow.Now });
        await db.SaveChangesAsync();
        return result;
    }

    public static ExecutiveNavigationResult Match(string command, string? currentArea, IEnumerable<ExecutivePage> pages)
    {
        var key = Normalize(command);
        if (key.Length == 0) return new("Unrecognized", null, "احكي اسم الصفحة المطلوبة. / Say the page name.", []);
        var matches = pages.Where(p => p.Aliases.Append(p.Label).Any(a => Normalize(a) == key)).DistinctBy(p => p.Url).ToArray();
        if (matches.Length > 1 && !string.IsNullOrEmpty(currentArea))
        {
            var local = matches.Where(p => p.Area.Equals(currentArea, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (local.Length > 0) matches = local;
        }
        return matches.Length switch {
            1 => new("Navigate", matches[0].Url, $"Opening {matches[0].Label} / فتح الصفحة", matches),
            > 1 => new("Choose", null, "أي صفحة تقصد؟ اختَر من الصفحات المسموحة. / Choose the page you meant.", matches),
            _ => new("Unrecognized", null, "لم يتم تحديد صفحة مسموحة بشكل مؤكد. / No permitted page was identified with certainty.", []) };
    }

    // Spoken English page names written in Arabic letters by speech recognition (e.g. "الانفيتيشن"). Used only by MatchLoose.
    private static readonly Dictionary<string, string[]> Spoken = new()
    {
        ["Invitations"] = ["انفيتيشن", "انفيتيشنز", "انفتيشن", "انفتيشنز", "انفكتشن", "انفيكيشن", "انفيتيشين", "دعوه", "دعوات"],
        ["My Offers"] = ["اوفرز", "اوفر", "عرض", "عروض"],
        ["Dashboard"] = ["داشبورد", "داش بورد", "رئيسيه"],
        ["My Tasks"] = ["تاسكات", "تاسك", "مهام"],
        ["Notifications"] = ["نوتيفيكيشن", "نوتيفيكيشنز", "اشعار", "اشعارات"],
        ["Reports"] = ["ريبورت", "ريبورتس", "تقارير"],
        ["Profile"] = ["بروفايل", "بروفيل"],
        ["Clarifications"] = ["كلاريفيكيشن", "كلاريفيكيشنز", "استفسار", "استفسارات"],
        ["Received Bids"] = ["بيدز", "عروض الموردين"],
        ["Suppliers"] = ["سبلايرز", "موردين"],
        ["Users"] = ["يوزرز", "مستخدمين"],
        ["Tender"] = ["تندر", "تندرات", "مناقصات", "عطاءات"],
        ["My Requests"] = ["ريكويست", "ريكويستس", "طلبات"],
        ["Request"] = ["ريكويست", "ريكويستس", "طلبات"],
        ["Final Decisions"] = ["قرارات", "ديسيجن"],
        ["Settings"] = ["سيتينجز", "اعدادات"],
    };

    // Tolerant matching for the chat/voice agent: exact first (Match), then a page name contained in the sentence, then a near
    // spelling (speech recognition). Still only pages from the user's authorized Catalog; never a URL from the transcript.
    public static ExecutiveNavigationResult MatchLoose(string command, string? currentArea, IReadOnlyList<ExecutivePage> pages)
    {
        var exact = Match(command, currentArea, pages);
        if (exact.State != "Unrecognized") return exact;
        var key = " " + Normalize(command) + " ";
        var tokens = key.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var scored = new List<(ExecutivePage Page, int Score)>();
        foreach (var page in pages)
        {
            var names = page.Aliases.Append(page.Label).Concat(Spoken.GetValueOrDefault(page.Label) ?? []).Select(Normalize).Where(n => n.Length >= 3).Distinct();
            var best = 0;
            foreach (var name in names)
            {
                if (key.Contains(" " + name + " ")) best = Math.Max(best, 100 + name.Length);
                else if (!name.Contains(' ') && name.Length >= 5 && tokens.Any(t => t.Length >= 5 && Distance(t, name) <= (name.Length >= 8 ? 2 : 1))) best = Math.Max(best, 50 + name.Length);
            }
            if (best > 0) scored.Add((page, best));
        }
        if (scored.Count == 0) return exact;
        var top = scored.Max(s => s.Score);
        var winners = scored.Where(s => s.Score == top).Select(s => s.Page).DistinctBy(p => p.Url).ToArray();
        if (winners.Length > 1 && !string.IsNullOrEmpty(currentArea))
        {
            var local = winners.Where(p => p.Area.Equals(currentArea, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (local.Length > 0) winners = local;
        }
        return winners.Length == 1
            ? new("Navigate", winners[0].Url, $"Opening {winners[0].Label} / فتح الصفحة", winners)
            : new("Choose", null, "أي صفحة تقصد؟ اختَر من الصفحات المسموحة. / Choose the page you meant.", winners);
    }

    private static int Distance(string a, string b)
    {
        if (Math.Abs(a.Length - b.Length) > 2) return 99;
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) d[0, j] = j;
        for (var i = 1; i <= a.Length; i++)
            for (var j = 1; j <= b.Length; j++)
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
        return d[a.Length, b.Length];
    }

    public static string Normalize(string value)
    {
        var text = new string(value.Normalize(NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray())
            .Normalize(NormalizationForm.FormC).ToLowerInvariant().Replace('أ','ا').Replace('إ','ا').Replace('آ','ا').Replace('ٱ','ا').Replace('ى','ي').Replace('ة','ه').Replace("ـ", "");
        text = Regex.Replace(text, @"[^\p{L}\p{N}\s]", " ");
        text = Regex.Replace(text, @"\b(take me to|go to|show me|can you|افتح لي|اعرض لي|لو سمحت|افتحلي|افتحلنا|اعرضلي|وديني ع|خذني ع|روح ع|وديني|خذني|افتح|اعرض|روح|بدي|ممكن|صفحه|لصفحه|شاشه|على|علي|الي|لي|please|open|show|page|screen|the)\b", " ");
        return string.Join(' ', text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(t => t.StartsWith("لل") && t.Length > 3 ? t[2..] : t.StartsWith("ال") && t.Length > 3 ? t[2..] : t));
    }

    public static string[] Aliases(string label) => label switch {
        "Dashboard" => ["الرئيسية", "الداشبورد", "لوحة التحكم", "لوحة المعلومات", "home", "my dashboard"],
        "Users" => ["المستخدمين", "المستخدمون", "ادارة المستخدمين", "الموظفين", "حسابات الموظفين", "user management", "staff accounts"],
        "Roles" => ["الادوار", "الصلاحيات", "ادارة الادوار"],
        "Departments" => ["الاقسام", "الادارات", "اقسام الشركة", "departments"],
        "Suppliers" => ["الموردين", "الموردون", "الشركات", "ادارة الموردين", "تسجيلات الموردين", "supplier management", "supplier registrations"],
        "Settings" => ["الاعدادات", "اعدادات الموقع"],
        "Audit Logs" => ["سجل التدقيق", "سجل العمليات", "السجلات", "مين عدل", "audit", "audit log", "activity log"],
        "Analytics & Exports" => ["الاحصائيات", "التحليلات", "التصدير", "analytics", "exports"],
        "New Request" => ["طلب شراء جديد", "انشاء طلب شراء", "نموذج طلب شراء", "نموذج طلب جديد", "new purchase request", "create purchase request", "purchase request form"],
        "My Requests" => ["طلباتي", "طلبات الشراء", "الطلبات", "purchase requests", "requests"],
        "Request" => ["الطلبات", "طلبات الشراء", "طلب شراء", "requests", "purchase requests"],
        "Tender" or "Weighted Workflow" => ["العطاءات", "العطايات", "المناقصات", "العطاء", "مناقصة", "عطاء", "ادارة العطاءات", "تندر", "التندرات", "tenders", "tender", "bidding", "tender management"],
        "Received Bids" => ["العروض الواردة", "العروض المستلمة", "عروض الموردين", "العروض المقدمة", "bids", "received offers", "received bids"],
        "Reports" => ["التقارير", "تقرير", "reports", "report"],
        "Final Decisions" => ["القرارات النهائية", "القرار النهائي", "final decisions"],
        "Clarifications" or "Manager Clarifications" => ["الاستفسارات", "التوضيحات", "المحادثات", "الاسئلة", "المراسلات", "clarifications", "messages", "conversations"],
        "Invitations" => ["الدعوات", "دعواتي", "دعوات العطاءات", "invitations", "my invitations"],
        "My Offers" => ["عروضي", "العروض", "عروض شركتي", "offers", "my bids", "my offers"],
        "Profile" => ["ملفي", "الملف الشخصي", "حسابي", "profile", "my profile"],
        "Notifications" => ["الاشعارات", "التنبيهات", "notifications"],
        "Assigned Evaluations" => ["التقييمات المسندة", "التقييمات", "تقييماتي", "العطاءات المسندة", "evaluations", "assigned tenders", "my evaluations"],
        "Criteria & Weights" => ["المعايير والاوزان", "الاوزان", "المعايير", "مقترحات الاوزان", "weights", "criteria", "weight proposals"],
        "Technical Evaluation" => ["التقييم الفني", "التقييم التقني", "technical evaluation"],
        "Financial Evaluation" => ["التقييم المالي", "financial evaluation"],
        "Scores" => ["العلامات", "الدرجات", "النتائج", "scores"],
        "AI Analysis" => ["تحليل الذكاء الاصطناعي", "تحليل العروض", "التحليل الذكي", "ai analysis"],
        "Unified Report" => ["التقرير الموحد", "تقرير اللجنة", "unified report", "committee report"],
        "History" or "Decision History" => ["سجل القرارات", "التاريخ", "السجل السابق", "history", "decision history"],
        "Workflow Overview" => ["سير العمل", "ملخص سير العمل", "workflow overview"],
        "Pending Reports" => ["التقارير المعلقة", "التقارير بانتظار المراجعة", "تقارير بانتظار الاعتماد", "pending reports", "reports awaiting review"],
        "Report Review" => ["مراجعة التقارير", "التقارير", "reports", "report review"],
        "Forwarding" => ["تحويل التقارير", "التحويل", "forwarding"],
        "Evaluation Summary" => ["ملخص التقييم", "التقارير", "تقرير التقييم", "evaluation summary", "reports"],
        "Voting" => ["التصويت", "التصويتات", "voting", "vote"],
        "Management Statistics" => ["احصائيات الادارة", "الاحصائيات", "statistics"],
        "My Tasks" => ["مهامي", "المهام", "شغلي", "المطلوب مني", "tasks", "my tasks", "my work"],
        "Device passkey" => ["مفتاح المرور", "البصمة", "التحقق من الهوية", "passkeys", "device passkey"],
        "How It Works" => ["كيف يعمل الموقع", "شرح الموقع", "طريقة عمل الموقع", "how it works"],
        _ => []
    };
}
