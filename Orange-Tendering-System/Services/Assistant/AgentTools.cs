using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Areas.Department.Services;
using Orange_Tendering_System.Areas.Department.ViewModels;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.ViewModels;

namespace Orange_Tendering_System.Services.Assistant;

// One step of an agent plan: a registered tool, its collected (validated) values and, after execution, the verified result.
public sealed record AgentStep
{
    public string Tool { get; init; } = "";
    public Dictionary<string, string> Values { get; init; } = new();
    public string Status { get; init; } = "Pending";   // Pending | Done | Failed
    public string? ResultRef { get; init; }            // e.g. the created department ID, user ID or PR ID
    public string? ResultText { get; init; }
}

// Server-written conversation state. ActionId is the pending-action identifier: a confirmation applies only to the plan that carries it.
public sealed record AgentPlan
{
    public Guid ActionId { get; init; } = Guid.NewGuid();
    public bool Arabic { get; init; }
    public List<AgentStep> Steps { get; init; } = [];
    public int Current { get; init; }
    public string? Asking { get; init; }
    public List<string> KnowledgeSources { get; init; } = [];
    public string State { get; init; } = "Collecting";   // Collecting | Confirming | Done | Partial | Failed | Cancelled
    public DateTime ExpiresAt { get; init; }
    public int? ConversationId { get; init; }
    public string? WorkspaceRole { get; init; }
}

public sealed record ToolSlot(string Key, string En, string Ar, bool Optional = false);

// Registry of the business tools the agent may execute. Each tool: permission rule, typed slots with validation, a summary for
// confirmation, execution through the EXISTING application service, and verification by reading the database afterwards.
// High-impact operations (delete, approve, publish, security policy, Admin role grants) are intentionally not registered.
public sealed partial class AgentTools(ApplicationDbContext db, UserManager<ApplicationUser> users, AdminService admin, EmployeeProvisioning provisioning,
    IHttpContextAccessor http, IWebHostEnvironment environment, ILogger<AgentTools> logger, ProcurementCategoryService categories, PurchaseRequestService requests,
    TenderWorkflowService workflow, EvaluationSchedulingService scheduling, SupplierOfferService supplierOffers, AiChatFileStore chatFiles)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static readonly TimeSpan PlanLifetime = TimeSpan.FromMinutes(30);

    public sealed record ToolInfo(string Id, string En, string Ar, string Kind, string Permission, string Service, bool Confirmation, bool Reversible);
    // The capability registry (also rendered in docs/AI_AGENT_TOOL_MATRIX.md).
    public static readonly ToolInfo[] Registry =
    [
        .. ProcurementRegistry,
        new("create_department", "Create department", "إنشاء قسم", "create", "Admin", "AdminService.CreateDepartmentAsync", true, true),
        new("create_category", "Create procurement category", "إنشاء تصنيف مشتريات", "create", "Admin", "ProcurementCategoryService.SaveAsync", true, true),
        new("rename_department", "Rename department", "تعديل اسم قسم", "update", "Admin", "AdminService.UpdateDepartmentAsync", true, true),
        new("list_departments", "List departments", "عرض الأقسام", "read", "Admin", "ApplicationDbContext.Departments (read-only)", false, true),
        new("list_users", "List employee accounts", "عرض حسابات الموظفين", "read", "Admin", "AdminService.GetUsersAsync (read-only)", false, true),
        new("find_user", "Find an employee account", "البحث عن حساب موظف", "read", "Admin", "AdminService.GetUsersAsync (read-only)", false, true),
        new("get_user_details", "Show employee account details", "عرض تفاصيل حساب موظف", "read", "Admin", "AdminService.GetUserDetailsAsync (read-only)", false, true),
        new("prepare_user_role_change", "Prepare a role change summary for the protected Admin page", "تجهيز ملخص تغيير دور لصفحة الإدارة المحمية", "read", "Admin", "AdminService current role and membership state (read-only)", false, true),
        new("update_user_name", "Update employee name", "تعديل اسم موظف", "update", "Admin", "AdminService.UpdateUserProfileAsync", true, true),
        new("update_user_phone", "Update employee phone", "تعديل هاتف موظف", "update", "Admin", "AdminService.UpdateUserProfileAsync", true, true),
        new("change_user_department", "Move employee to department", "نقل موظف إلى قسم", "update", "Admin", "AdminService.UpdateUserProfileAsync", true, true),
        new("deactivate_user", "Deactivate employee account", "تعطيل حساب موظف", "update", "Admin", "AdminService.UpdateUserProfileAsync", true, true),
        new("reactivate_user", "Reactivate employee account", "إعادة تفعيل حساب موظف", "update", "Admin", "AdminService.UpdateUserProfileAsync", true, true),
        new("create_user", "Create employee account (activation link)", "إنشاء حساب موظف (رابط تفعيل)", "create", "Admin", "EmployeeProvisioning.CreateAsync", true, false),
        new("create_purchase_request", "Create purchase request draft", "إنشاء مسودة طلب شراء", "create", "Requesting Department (own active department)", "DepartmentRequestService.Save(submit: false)", true, true),
    ];

    public static ToolSlot[] Slots(string tool) => tool switch
    {
        "create_department" => [new("name", "What is the new department's name?", "شو اسم القسم الجديد؟"), new("description", "Add a short description, or say \"skip\".", "اكتب وصفًا قصيرًا للقسم، أو قل «تخطي».", true)],
        "create_category" => [new("name", "What should I name the new procurement category?", "شو اسم تصنيف المشتريات الجديد؟")],
        "rename_department" => [new("department", "Which department do you want to rename?", "أي قسم بدك تغيّر اسمه؟"), new("name", "What is the new name?", "شو الاسم الجديد؟")],
        "update_user_name" => [new("user", "Which employee? Give a name or email.", "أي موظف؟ اكتب الاسم أو البريد."), new("fullName", "What is the employee's new full name?", "شو الاسم الكامل الجديد للموظف؟")],
        "update_user_phone" => [new("user", "Which employee? Give a name or email.", "أي موظف؟ اكتب الاسم أو البريد."), new("phone", "What is the new phone number?", "شو رقم الهاتف الجديد؟")],
        "change_user_department" => [new("user", "Which employee? Give a name or email.", "أي موظف؟ اكتب الاسم أو البريد."), new("department", "Which active department should they move to?", "لأي قسم نشط بدك تنقله؟")],
        "deactivate_user" or "reactivate_user" => [new("user", "Which employee? Give a name or email.", "أي موظف؟ اكتب الاسم أو البريد.")],
        "create_user" => [new("fullName", "What is the employee's full name?", "شو الاسم الكامل للموظف؟"), new("email", "What is the employee's email address?", "شو البريد الإلكتروني للموظف؟"),
            new("role", "Which role? (e.g. Requesting Department, Sourcing Manager, Technical, Finance, Department Representative)", "شو الدور؟ (مثلًا: القسم الطالب، مدير التوريد، فني، مالي، ممثل القسم)"),
            new("department", "Which department does the employee belong to?", "لأي قسم بيتبع الموظف؟")],
        "create_purchase_request" => [new("item", "What item do you need?", "شو الصنف المطلوب؟"), new("category", "Which active procurement category?", "شو تصنيف المشتريات للصنف؟"), new("quantity", "How many?", "كم الكمية؟"), new("unit", "What unit? (e.g. Piece, Box, License)", "شو الوحدة؟ (مثلًا: قطعة، علبة، رخصة)"),
            new("specifications", "What specifications are required?", "شو المواصفات المطلوبة؟"),
            new("justification", "Why is it needed? (justification)", "ليش محتاجينه؟ (المبرر)"), new("neededBy", "When is it needed? (e.g. 2026-12-31, or \"in 30 days\")", "إمتى بدكم ياه؟ (مثلًا 2026-12-31 أو «بعد 30 يوم»)")],
        _ => ProcurementSlots(tool)
    };

    // ---------------- permissions (decided from the authenticated user, re-checked right before execution) ----------------

    public async Task<string?> Denial(string tool, AssistantUser user, bool ar)
    {
        if (ProcurementRole(tool) is { } role) return user.Is(role) ? null : ar ? "هذا الإجراء غير متاح في مساحة عملك." : "This action is unavailable in your workspace.";
        switch (tool)
        {
            case "create_category" or "create_department" or "rename_department" or "list_departments" or "list_users" or "find_user" or "get_user_details" or "prepare_user_role_change"
                or "update_user_name" or "update_user_phone" or "change_user_department" or "deactivate_user" or "reactivate_user" or "create_user":
                return user.Is(SystemRoles.Admin) ? null : ar ? "هذا الإجراء متاح لمدير النظام فقط." : "This action is available to the Admin only.";
            case "create_purchase_request":
                if (!user.Is(SystemRoles.RequestingDepartment)) return ar ? "إنشاء طلبات الشراء متاح لموظفي القسم الطالب فقط." : "Only Requesting Department users can create purchase requests.";
                var ok = user.DepartmentId != null && await db.Departments.AnyAsync(d => d.Id == user.DepartmentId && d.IsActive);
                return ok ? null : ar ? "حسابك غير مرتبط بقسم نشط." : "Your account is not linked to an active department.";
            default: return ar ? "هذا الإجراء غير مسجّل." : "This action is not registered.";
        }
    }

    // ---------------- detection and initial extraction ----------------

    private static string N(string s) => " " + AssistantIntentParser.Normalize(AssistantIntentParser.Strip(s)) + " ";
    private static readonly Regex CreateVerb = new(@"\b(create|add|make|new|register|open a)\b|انشي|انشاء|اضف|اضافه|ضيف|ضيفلي|اعمل|اعملي|سوي|سويلي|سجل|جديد|افتح", RegexOptions.IgnoreCase);
    private static readonly Regex DeptWord = new(@"\bdepartments?\b|قسم|اقسام|اداره|ادارات|دائره", RegexOptions.IgnoreCase);
    private static readonly Regex UserWord = new(@"\b(user|users|employee|account|staff)\b|يوزر|مستخدم|موظف|حساب", RegexOptions.IgnoreCase);
    private static readonly Regex EmployeeCreation = new(@"\b(?:create|add|make|register|new)\s+(?:(?:an?|the|new|employee)\s+)*(?:user|employee|account)\b|(?:انشي|انشاء|اضف|ضيف|اعمل|سوي|سجل)\s*(?:لي\s*)?(?:يوزر|مستخدم|موظف|حساب)|(?:يوزر|مستخدم|موظف|حساب)\s+جديد", RegexOptions.IgnoreCase);
    private static readonly Regex PrWord = new(@"purchase request|\bpr\b|طلب شراء|طلب شرا", RegexOptions.IgnoreCase);
    private const string CategoryWords = @"\b(?:procurement\s+)?categor(?:y|ies)\b|كات[اأ]?[غجق]وري|كاتاغوري|كتغوري|تصنيف(?:\s+مشتريات)?|فئ[هة](?:\s+مشتريات)?";
    private static readonly Regex CategoryName = new(@"(?:named|called|باسم|بإسم|اسمها|اسمه)\s+(.+)$", RegexOptions.IgnoreCase);
    private static readonly Regex CreationFollowUp = new(
        @"^(?:(?:انا\s+)?بدي\s+)?(?:(?:انت|انتي)\s+)?(?:بدك\s+)?(?:ت?عمل(?:ها|لي|ه|و|لها|ليها)|ت?نشي(?:ها|ه)?|انشاء|سويها)(?:\s+(?:انت|انتي|انشاء))?$|^(?:please\s+)?(?:create|add|make)\s+it(?:\s+for me)?$",
        RegexOptions.IgnoreCase);
    private static readonly Regex Question = new(@"\s(how|why|what is|explain|can i|كيف|ليش|لماذا|اشرح|شو يعني|هل)\s", RegexOptions.IgnoreCase);

    public static bool IsCreationFollowUp(string message) =>
        CreationFollowUp.IsMatch(N(message).Trim().TrimEnd('.', '!', '؟', '?').Trim());

    public static string? CreationTypeCommand(string message, bool arabic) =>
        N(message).Trim().TrimEnd('.', '!', '؟', '?').Trim() switch
        {
            "مستخدم" or "يوزر" or "موظف" or "حساب موظف" or "user" or "employee" or "employee account"
                => arabic ? "أنشئ مستخدم" : "Create a user",
            "قسم" or "اداره" or "department" => arabic ? "أنشئ قسم" : "Create a department",
            "تصنيف" or "كاتاغوري" or "تصنيف مشتريات" or "فئه" or "category" or "procurement category"
                => arabic ? "أنشئ تصنيف مشتريات" : "Create a procurement category",
            "طلب شراء" or "مسوده طلب شراء" or "purchase request" => arabic ? "أنشئ طلب شراء" : "Create a purchase request",
            "عطاء" or "مناقصه" or "مسوده عطاء" or "tender" or "tender draft" => arabic ? "أنشئ عطاء" : "Create a tender",
            _ => null
        };

    public static (List<string> Tools, string Request)? RecoverCreation(IReadOnlyList<string> recent)
    {
        if (recent.Count == 0) return null;
        for (var i = 0; i < recent.Count; i++)
        {
            if (Detect(recent[i]) is not { Count: > 0 } tools ||
                tools.Any(id => !Registry.Any(tool => tool.Id == id && tool.Kind == "create"))) continue;
            var request = tools is ["create_category"] ? RecoverCategoryRequest(recent) ?? recent[i] : recent[i];
            return (tools, request);
        }
        return null;
    }

    public static string? RecoverCategoryRequest(IReadOnlyList<string> recentUserMessages)
    {
        if (recentUserMessages.Count == 0) return null;
        var latest = recentUserMessages[0];
        if (Detect(latest) is ["create_category"]) return latest;
        if (recentUserMessages.Count < 2 || Detect(recentUserMessages[1]) is not ["create_category"]) return null;
        var request = recentUserMessages[1];
        if (CategoryName.IsMatch(request)) return request;

        // Only reuse a short user-supplied name. Assistant prose and uploaded content never supply action details.
        var match = CategoryName.Match(latest);
        var name = match.Success ? match.Groups[1].Value : latest;
        name = name.Trim().Trim('"', '\'', '«', '»');
        return Regex.IsMatch(name, @"^[\p{L}\p{N}][\p{L}\p{N} &()._-]{0,99}$") &&
            !Regex.IsMatch(N(name), @"\b(?:yes|no|ok|confirm|cancel)\b|(?:^|\s)(?:نعم|لا|تمام|اكد|الغاء)(?:\s|$)")
            ? request + " باسم " + name
            : request;
    }

    // Returns the planned tool sequence for a message, or null when the message is not a tool command.
    public static List<string>? Detect(string message)
    {
        var t = N(message);
        if (Question.IsMatch(t)) return null;
        if (DetectProcurement(t) is { } procurement) return [procurement];
        var create = CreateVerb.IsMatch(t);
        var employee = EmployeeCreation.Match(t);
        if (employee.Success && !Regex.IsMatch(t[..employee.Index], @"\b(?:then)\b|ثم|وبعدين|وبعدها", RegexOptions.IgnoreCase))
            return ["create_user"];
        if (create && Regex.IsMatch(t, CategoryWords, RegexOptions.IgnoreCase)) return ["create_category"];
        var plan = new List<string>();
        var phoneChange = Regex.IsMatch(t, @"\b(change|update|edit|set)\b.*\b(phone|mobile|number)\b|غير.*(هاتف|تلفون|موبايل|رقم)|عدل.*(هاتف|تلفون|موبايل|رقم)", RegexOptions.IgnoreCase);
        var roleChange = Regex.IsMatch(t, @"\b(assign|grant|add|give|remove|revoke|change|update)\b.*\b(role|permission)\b|امنح|اعط.*(دور|صلاح)|اضف.*(دور|صلاح)|عين.*(دور|صلاح)|خصص.*(دور|صلاح)|غير.*(صلاح|دور)|احذف.*(صلاح|دور)|ازل.*(صلاح|دور)|شيل.*(صلاح|دور)", RegexOptions.IgnoreCase);
        var departmentChange = Regex.IsMatch(t, @"\b(move|transfer)\b.+\b(from|to|into)\b|\b(change|update)\b.*\bdepartment\b|انقل|نقل|حول.*(قسم|اداره)", RegexOptions.IgnoreCase);
        var nameChange = Regex.IsMatch(t, @"\b(change|update|edit|rename)\b.*\b(name|full name)\b|غير.*(اسم|اسمه|اسمها)|عدل.*(اسم|اسمه|اسمها)", RegexOptions.IgnoreCase);
        var userTarget = Regex.IsMatch(t, @"\b(users?|employees?|accounts?|staff|details|profile)\b|يوزر|مستخدم|موظف|حساب|تفاصيل|بيانات|ملف شخصي", RegexOptions.IgnoreCase) || phoneChange || departmentChange || nameChange || roleChange;
        if (userTarget && roleChange) return ["prepare_user_role_change"];
        if (userTarget && Regex.IsMatch(t, @"\b(details|profile)\b|تفاصيل|بيانات|ملف شخصي", RegexOptions.IgnoreCase)) return ["get_user_details"];
        if (userTarget && Regex.IsMatch(t, @"\b(list|show|all|view)\b|اعرض|عرض|شوف|فرج|كل|جميع", RegexOptions.IgnoreCase)) return ["list_users"];
        if (userTarget && Regex.IsMatch(t, @"\b(find|search|look up|who is|details|profile)\b|ابحث|دور|فتش|مين|تفاصيل|بيانات|ملف شخصي", RegexOptions.IgnoreCase))
            return [Regex.IsMatch(t, @"\b(details|profile)\b|تفاصيل|بيانات|ملف شخصي", RegexOptions.IgnoreCase) ? "get_user_details" : "find_user"];
        if (userTarget && phoneChange) return ["update_user_phone"];
        if (userTarget && departmentChange) return ["change_user_department"];
        if (userTarget && Regex.IsMatch(t, @"\b(deactivate|disable|suspend)\b|عطّل|عطل|وقف|أوقف", RegexOptions.IgnoreCase)) return ["deactivate_user"];
        if (userTarget && Regex.IsMatch(t, @"\b(reactivate|enable|activate)\b|فعّل|فعل|نشّط|نشط", RegexOptions.IgnoreCase)) return ["reactivate_user"];
        if (userTarget && nameChange) return ["update_user_name"];
        var wantsDept = create && DeptWord.IsMatch(t) && !PrWord.IsMatch(t) && Regex.IsMatch(t, @"(create|add|make|new|انشي|انشاء|اضف|ضيف|اعمل|سوي|جديد)\W+(?:\w+\W+){0,3}(an?\s+|the\s+)?(?:\w+\s+){0,3}?(department|قسم|اداره)|(department|قسم)\s+جديد|new\s+(\w+\s+)?department", RegexOptions.IgnoreCase);
        var wantsUser = create && UserWord.IsMatch(t);
        if (create && PrWord.IsMatch(t)) return ["create_purchase_request"];
        if (wantsDept) plan.Add("create_department");
        if (wantsUser) plan.Add("create_user");
        if (plan.Count > 0) return plan;
        if (Regex.IsMatch(t, @"\b(rename|change|update|edit)\b.*\b(department)\b|(عدل|غير|بدل|عدلي|غيرلي)\s.*(اسم)\s.*(قسم|اداره)|(عدل|غير)\s+(اسم\s+)?(القسم|قسم)") ) return ["rename_department"];
        if (Regex.IsMatch(t, @"\b(show|list|all|view)\b.*\bdepartments\b|(اعرض|عرض|اعرضلي|شوفلي|فرجيني|كل|جميع)\s+(ال)?اقسام|^\s*(ال)?اقسام\s*$")) return ["list_departments"];
        return null;
    }

    public async Task<Dictionary<string, string>> ExtractAsync(string tool, string message, AssistantUser user, bool firstOfPlan)
    {
        var v = new Dictionary<string, string>();
        var raw = AssistantIntentParser.Strip(message);
        if (ProcurementRole(tool) != null) return await ExtractProcurement(tool, raw, user);
        switch (tool)
        {
            case "create_category":
            {
                var match = CategoryName.Match(raw);
                if (match.Success) v["name"] = match.Groups[1].Value.Trim(' ', '"', '\'', '«', '»');
                break;
            }
            case "create_department":
            {
                var m = Regex.Match(raw, @"(?:department|قسم|إدارة|ادارة)\s+(?:جديد\s+)?(?:named|called|باسم|بإسم|اسمه|اسمها)\s+[""'«]?(.+?)[""'»]?(?=\s*(?:,|،|\.|\bthen\b|\band\b|ثم|وبعدين|وبعدها|و\s*(?:اعمل|انشئ|أنشئ|ضيف|أضف|سجل)|$))", RegexOptions.IgnoreCase);
                if (!m.Success) m = Regex.Match(raw, @"\b(?:create|add|make|new)\s+(?:an?\s+|the\s+)?(?:new\s+)?([A-Za-z][\w&\- ]{0,60}?)\s+department\b", RegexOptions.IgnoreCase);
                if (!m.Success) m = Regex.Match(raw, @"قسم\s+(?:جديد\s+)?(?:لل|ال)?([\p{L}A-Za-z&\- ]{2,60}?)(?=\s*(?:,|،|\.|\bthen\b|ثم|وبعدين|وبعدها|و\s*(?:اعمل|انشئ|أنشئ|ضيف|أضف|سجل)|$))");
                var name = m.Success ? m.Groups[1].Value.Trim(' ', '"', '\'', '«', '»') : null;
                if (name != null && !Regex.IsMatch(AssistantIntentParser.Normalize(name), @"^(جديد|new|a|an|the)$") && name.Length is >= 2 and <= 100) v["name"] = name;
                break;
            }
            case "create_user":
            {
                var email = Regex.Match(raw, @"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}");
                if (email.Success) v["email"] = email.Value;
                var name = Regex.Match(raw, @"(?:named|called|name\s*(?:is|:)|الاسم\s*:|اسمه|اسمها|باسم)\s*(.+?)(?=\s+(?:with|and|email|as|in|to|role)\b|\s+(?:إيميل|ايميل|بريد|ودوره|دوره|الدور|بقسم|في قسم)|[,،\r\n]|$)", RegexOptions.IgnoreCase);
                if (name.Success) v["fullName"] = name.Groups[1].Value.Trim(' ', '"', '\'', '«', '»');
                var roleInput = Regex.Match(raw, @"(?:\b(?:as|role)\s*:?\s+|(?:بدور|ودوره|دوره|الدور)\s*:?\s*)(.+?)(?=\s+(?:in|for|department)\s+|\s+(?:بقسم|في قسم|لقسم)|[,،\r\n]|$)", RegexOptions.IgnoreCase);
                if (roleInput.Success) v["role"] = roleInput.Groups[1].Value.Trim();
                var dept = Regex.Match(raw, @"(?:\bdepartment\s*:\s*|بقسم\s+|في قسم\s+|على قسم\s+|لقسم\s+|القسم\s*:\s*)(.+?)(?=\s+(?:بدور|ودوره|دوره|الدور)\b|[,،\r\n]|$)|\b(?:in|to|for|into)\s+(?:the\s+)?([\p{L}\p{N}&\- ]{2,80}?)\s+department\b", RegexOptions.IgnoreCase);
                if (!firstOfPlan) break;   // in "create a department then a user in it" the department comes from step 1
                if (dept.Success) v["department"] = (dept.Groups[1].Value + dept.Groups[2].Value).Trim();
                break;
            }
            case "update_user_name":
            case "update_user_phone":
            case "change_user_department":
            case "deactivate_user":
            case "reactivate_user":
            {
                if (ExtractEmployeeQuery(raw, tool) is { Length: > 0 } target) v["user"] = target;
                if (tool == "update_user_name")
                {
                    var m = Regex.Match(raw, @"(?:\bname\s+(?:to|as)|\bto\s+|(?:اسمه|اسمها|الاسم الجديد)\s+)([\p{L}][\p{L}' .\-]{1,100}?)(?=\s+(?:phone|department|email)\b|,|،|\.|$)", RegexOptions.IgnoreCase);
                    if (m.Success) v["fullName"] = m.Groups[1].Value.Trim();
                }
                else if (tool == "update_user_phone")
                {
                    var m = Regex.Match(raw, @"(?:\b(?:to|as)\s*|(?:رقم(?:ه)?|الهاتف الجديد|لـ)\s*)(\+?[0-9][0-9() .\-]{4,30})", RegexOptions.IgnoreCase);
                    if (m.Success) v["phone"] = m.Groups[1].Value.Trim();
                }
                else if (tool == "change_user_department")
                {
                    var m = Regex.Match(raw, @"(?:\b(?:to|into)\s+(?:the\s+)?|(?:الى|إلى|على|لقسم|قسم)\s+)([\p{L}A-Za-z0-9&\- ]{2,60}?)(?=\s+(?:department|قسم)|[,،.]|$)", RegexOptions.IgnoreCase);
                    if (m.Success && await ResolveDepartment(m.Groups[1].Value.Trim()) is ({ } id, null)) v["department"] = id.ToString();
                }
                break;
            }
            case "create_purchase_request":
            {
                var t = AssistantIntentParser.Normalize(raw);
                var m = Regex.Match(t, @"(?:for|of|ل|ب)?\s*(\d+(?:\.\d{1,3})?)\s+([\p{L}][\p{L}\- ]{1,60}?)(?=\s*(?:,|،|\.|\bfor\b|\bwith\b|\bneeded\b|ل|$))");
                if (m.Success) { v["quantity"] = m.Groups[1].Value; v["item"] = m.Groups[2].Value.Trim(); v["unit"] = AssistantIntentParser.IsArabic(m.Groups[2].Value) ? "قطعة" : "pcs"; }
                break;
            }
        }
        return v;
    }

    private static string? ExtractEmployeeQuery(string raw, string tool)
    {
        Match m = tool switch
        {
            "update_user_phone" => Regex.Match(raw, @"(?:change|update|edit|set)\s+(.+?)['’]?s?\s+(?:phone|mobile|number)|(?:غير|عدل)\s+(.+?)\s+(?:هاتف|تلفون|موبايل|رقم)", RegexOptions.IgnoreCase),
            "change_user_department" => Regex.Match(raw, @"(?:move|transfer|change)\s+(.+?)\s+(?:from\s+.+?\s+)?(?:to|into)\s+.+?(?:\s+department)?$|(?:انقل|نقل|حول)\s+(.+?)\s+(?:من\s+.+?\s+)?(?:الى|على)\s+.+$", RegexOptions.IgnoreCase),
            "deactivate_user" or "reactivate_user" => Regex.Match(raw, @"(?:deactivate|disable|suspend|reactivate|enable|activate)\s+(?:user|employee|account)?\s*(.+?)\s*$|(?:عطل|وقف|اوقف|فعل|نشط)\s+(?:حساب)?\s*(.+?)\s*$", RegexOptions.IgnoreCase),
            _ => Regex.Match(raw, @"(?:change|update|edit|rename)\s+(.+?)['’]?s?\s+(?:name|full name)|(?:غير|عدل)\s+(.+?)\s+(?:اسم|اسمه|اسمها)", RegexOptions.IgnoreCase)
        };
        return m.Success ? (m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value).Trim(' ', '\'', '’', '"', '،', ',') : null;
    }

    public static string? ResolveRole(string text)
    {
        var t = N(text);
        (string Role, string[] Words)[] map =
        [
            (SystemRoles.SourcingManager, ["sourcing manager", "مدير التوريد", "مدير المشتريات"]),
            (SystemRoles.SourcingOfficer, ["sourcing officer", "مسؤول التوريد", "مسوول التوريد", "procurement officer"]),
            (SystemRoles.DepartmentRepresentative, ["department representative", "ممثل القسم", "ممثل الاداره"]),
            (SystemRoles.EvaluationCommittee, ["evaluation committee", "لجنة التقييم"]),
            (SystemRoles.TopCommittee, ["top committee", "اللجنة العليا"]),
            (SystemRoles.Supplier, ["supplier", "مورد"]),
            (SystemRoles.RequestingDepartment, ["requesting department", "department user", "department employee", "القسم الطالب", "موظف قسم", "طالب شراء"]),
            (SystemRoles.Technical, ["technical", "technical representative", "فني", "الفني", "ممثل فني"]),
            (SystemRoles.Finance, ["finance", "financial", "مالي", "المالي", "ممثل مالي"]),
            (SystemRoles.ChiefExecutiveOfficer, [" ceo ", "chief executive", "الرئيس التنفيذي", "المدير التنفيذي"]),
            (SystemRoles.ChiefFinancialOfficer, [" cfo ", "chief financial", "المدير المالي"]),
            (SystemRoles.ChiefTechnologyOfficer, [" cto ", "chief technology", "المدير التقني"]),
            (SystemRoles.ChiefOperationsOfficer, [" coo ", "chief operations", "مدير العمليات"]),
            (SystemRoles.Admin, ["admin", "administrator", "مدير النظام", "ادمن"]),
        ];
        foreach (var (role, words) in map) if (words.Any(w => t.Contains(AssistantIntentParser.Normalize(w)))) return role;
        return null;
    }

    // Roles the agent may assign: the Admin's own allow-list (PublicRoles without Supplier) minus Admin (privilege escalation is
    // not delegated to AI; an Admin account is created in Users → Create).
    public static readonly string[] AssignableRoles = SystemRoles.PublicRoles.Where(r => r is not (SystemRoles.Supplier or SystemRoles.Admin)).ToArray();

    private async Task<(int? Id, string? Problem)> ResolveDepartment(string text, bool ar = false)
    {
        text = text.Trim();
        if (text.Length == 0) return (null, ar ? "اكتب اسم القسم." : "Give the department name.");
        var active = await db.Departments.AsNoTracking().Where(d => d.IsActive).Select(d => new { d.Id, d.Name }).ToListAsync();
        if (int.TryParse(AssistantIntentParser.Normalize(text).Replace("#", ""), out var id) && active.Any(d => d.Id == id)) return (id, null);
        var key = AssistantIntentParser.Normalize(Regex.Replace(text, @"^(the|قسم|ال)\s*", "", RegexOptions.IgnoreCase)).Trim();
        var exact = active.Where(d => AssistantIntentParser.Normalize(d.Name).Trim() == key || AssistantIntentParser.Normalize(d.Name).Trim() == "ال" + key).ToList();
        var matches = exact.Count > 0 ? exact : active.Where(d => AssistantIntentParser.Normalize(d.Name).Contains(key) || key.Contains(AssistantIntentParser.Normalize(d.Name))).ToList();
        return matches.Count switch
        {
            1 => (matches[0].Id, null),
            0 => (null, ar ? "لم أجد قسمًا نشطًا بهذا الاسم. الأقسام: " + string.Join("، ", active.Select(d => d.Name).Take(12)) : "No active department matches. Departments: " + string.Join(", ", active.Select(d => d.Name).Take(12))),
            _ => (null, (ar ? "في أكثر من قسم مطابق: " : "Several departments match: ") + string.Join(ar ? "، " : ", ", matches.Select(d => $"{d.Name} (#{d.Id})")))
        };
    }

    // ---------------- slot validation ----------------

    public async Task<(string? Value, string? Problem)> Validate(string tool, string key, string answer, AgentPlan plan, AssistantUser user, bool ar)
    {
        answer = answer.Trim();
        if (ProcurementRole(tool) != null) return await ValidateProcurement(tool, key, answer, plan, user, ar);
        var skip = Regex.IsMatch(N(answer), @"\s(skip|none|no|تخطي|تخطى|لا|بدون|ما في|مافي)\s");
        string? Len(int min, int max) => answer.Length < min || answer.Length > max ? (ar ? $"الطول المسموح {min}–{max} حرفًا." : $"Use {min}–{max} characters.") : null;
        switch (tool, key)
        {
            case ("create_category", "name"):
                if (Len(1, 100) is { } categoryError) return (null, categoryError);
                return await db.ProcurementCategories.AnyAsync(c => c.Name == answer)
                    ? (null, ar ? "هذا التصنيف موجود. اختر اسمًا آخر." : "This category already exists. Choose another name.")
                    : (answer, null);
            case ("create_department", "name"):
            case ("rename_department", "name"):
            {
                if (Len(2, 100) is { } p) return (null, p);
                var exists = await db.Departments.AnyAsync(d => d.Name == answer);
                return exists ? (null, ar ? $"يوجد قسم باسم «{answer}» مسبقًا. اختر اسمًا آخر." : $"A department named \"{answer}\" already exists. Choose another name.") : (answer, null);
            }
            case ("create_department", "description"): return skip ? ("", null) : Len(0, 160) is { } d ? (null, d) : (answer, null);
            case ("rename_department", "department"):
            {
                var all = await db.Departments.AsNoTracking().Select(x => new { x.Id, x.Name }).ToListAsync();
                var key2 = AssistantIntentParser.Normalize(Regex.Replace(answer, @"^(the|قسم)\s*", "", RegexOptions.IgnoreCase)).Trim();
                var hit = all.Where(x => x.Id.ToString() == key2 || AssistantIntentParser.Normalize(x.Name).Trim() == key2).ToList();
                if (hit.Count == 0) hit = all.Where(x => AssistantIntentParser.Normalize(x.Name).Contains(key2)).ToList();
                return hit.Count == 1 ? (hit[0].Id.ToString(), null) : (null, (ar ? "حدّد القسم: " : "Which one: ") + string.Join(", ", (hit.Count == 0 ? all : hit).Take(12).Select(x => $"{x.Name} (#{x.Id})")));
            }
            case ("create_user", "fullName"): return Len(2, 150) is { } f ? (null, f) : (answer, null);
            case ("create_user", "email"):
            {
                var m = Regex.Match(answer, @"^[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}$");
                var email = m.Success ? m.Value : Regex.Match(answer, @"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}").Value;
                if (email.Length == 0 || email.Length > 256) return (null, ar ? "اكتب بريدًا إلكترونيًا صحيحًا." : "Enter a valid email address.");
                if (await db.Users.AnyAsync(u => u.NormalizedEmail == email.ToUpperInvariant())) return (null, ar ? "هذا البريد مسجّل مسبقًا." : "This email is already registered.");
                return (email, null);
            }
            case ("create_user", "role"):
            {
                var role = ResolveRole(answer) ?? AssignableRoles.FirstOrDefault(r => string.Equals(r, answer, StringComparison.OrdinalIgnoreCase));
                if (role == SystemRoles.Admin) return (null, ar ? "إنشاء حساب مدير النظام غير متاح من الشات. اختر دور موظف للمتابعة." : "Creating an Admin account is unavailable in chat. Choose an employee role to continue.");
                if (role == null || !AssignableRoles.Contains(role)) return (null, (ar ? "اختر دورًا من: " : "Choose one of: ") + string.Join(", ", AssignableRoles.Select(r => Labels.Role(r, ar))));
                if (role == SystemRoles.SourcingOfficer && (await users.GetUsersInRoleAsync(SystemRoles.SourcingOfficer)).Any(u => u.IsActive))
                    return (null, ar ? "يوجد مسؤول توريد نشط واحد مسموح فقط. عطّل الحالي أولًا." : "Only one active Sourcing Officer is allowed. Deactivate the current officer first.");
                return (role, null);
            }
            case ("create_user", "department"):
            {
                var (id, problem) = await ResolveDepartment(answer, ar);
                return id != null ? (id.ToString(), null) : (null, problem);
            }
            case ("update_user_name", "user"):
            case ("update_user_phone", "user"):
            case ("change_user_department", "user"):
            case ("deactivate_user", "user"):
            case ("reactivate_user", "user"):
            {
                var (id, problem) = await ResolveEmployee(answer, ar, default);
                return id != null ? (id, null) : (null, problem);
            }
            case ("update_user_name", "fullName"): return Len(2, 150) is { } n ? (null, n) : (answer, null);
            case ("update_user_phone", "phone"):
                return answer.Length is < 6 or > 32 || answer.Any(c => !char.IsDigit(c) && c is not ('+' or '(' or ')' or '-' or ' ' or '.'))
                    ? (null, ar ? "اكتب رقم هاتف من 6 إلى 32 محرفًا باستخدام الأرقام وعلامات الهاتف المعتادة." : "Enter a 6–32 character phone number using digits and common phone punctuation.")
                    : (answer, null);
            case ("change_user_department", "department"):
            {
                var (id, problem) = await ResolveDepartment(answer, ar);
                return id != null ? (id.ToString(), null) : (null, problem);
            }
            case ("create_purchase_request", "item"): return Len(2, 200) is { } i ? (null, i) : (answer, null);
            case ("create_purchase_request", "category"):
            {
                var options = await db.ProcurementCategories.AsNoTracking().Where(c => c.IsActive)
                    .OrderBy(c => c.SortOrder).ThenBy(c => c.Name).Select(c => new { c.Id, c.Name }).ToListAsync();
                var keyText = AssistantIntentParser.Normalize(answer).TrimStart('#');
                var match = options.FirstOrDefault(c => c.Id.ToString() == keyText || AssistantIntentParser.Normalize(c.Name) == keyText);
                return match != null ? (match.Id.ToString(), null) : (null,
                    (ar ? "اختر تصنيفًا نشطًا، مثل: " : "Choose an active category, such as: ") +
                    string.Join(", ", options.Take(12).Select(c => $"{c.Name} (#{c.Id})")));
            }
            case ("create_purchase_request", "quantity"):
            {
                return decimal.TryParse(AssistantIntentParser.Normalize(answer), NumberStyles.None, CultureInfo.InvariantCulture, out var q) && q is >= 1 and <= 1_000_000
                    ? (q.ToString(CultureInfo.InvariantCulture), null) : (null, ar ? "اكتب كمية موجبة بعدد صحيح." : "Enter a positive whole-number quantity.");
            }
            case ("create_purchase_request", "unit"):
            {
                var input = AssistantIntentParser.Normalize(answer);
                var unit = input switch { "pcs" or "قطعه" or "قطعة" => "Piece", "علبه" or "علبة" => "Box", "رخصه" or "رخصة" => "License",
                    _ => UnitsOfMeasure.All.FirstOrDefault(u => AssistantIntentParser.Normalize(u) == input) };
                return unit != null ? (unit, null) : (null,
                    (ar ? "اختر وحدة من: " : "Choose a unit from: ") + string.Join(", ", UnitsOfMeasure.All));
            }
            case ("create_purchase_request", "specifications"): return Len(3, 10000) is { } s ? (null, s) : (answer, null);
            case ("create_purchase_request", "justification"): return Len(5, 10000) is { } j ? (null, j) : (answer, null);
            case ("create_purchase_request", "neededBy"):
            {
                var date = ParseDate(answer);
                var today = JordanTime.Now.Date;
                return date == null ? (null, ar ? "اكتب تاريخًا مثل 2026-12-31 أو «بعد 30 يوم»." : "Give a date like 2026-12-31 or \"in 30 days\".")
                    : date < today ? (null, ar ? "التاريخ لازم يكون اليوم أو بعده." : "The date must be today or later.") : (date.Value.ToString("yyyy-MM-dd"), null);
            }
        }
        return (null, ar ? "قيمة غير مدعومة." : "Unsupported value.");
    }

    public static DateTime? ParseDate(string text)
    {
        var t = AssistantIntentParser.Normalize(text);
        foreach (var f in new[] { "yyyy-MM-dd", "yyyy/MM/dd", "d/M/yyyy", "dd/MM/yyyy", "d-M-yyyy" })
            if (DateTime.TryParseExact(Regex.Match(t, @"\d{1,4}[-/]\d{1,2}[-/]\d{1,4}").Value, f, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return d.Date;
        var days = Regex.Match(t, @"(?:in|after|بعد|خلال)\s*(\d{1,3})\s*(day|days|يوم|ايام)");
        if (days.Success) return JordanTime.Now.Date.AddDays(int.Parse(days.Groups[1].Value));
        var weeks = Regex.Match(t, @"(?:in|after|بعد|خلال)\s*(\d{1,2})\s*(week|weeks|اسبوع|اسابيع)");
        if (weeks.Success) return JordanTime.Now.Date.AddDays(7 * int.Parse(weeks.Groups[1].Value));
        var months = Regex.Match(t, @"(?:in|after|بعد|خلال)\s*(\d{1,2})?\s*(month|months|شهر|اشهر|شهرين)");
        if (months.Success) return JordanTime.Now.Date.AddMonths(months.Groups[2].Value == "شهرين" ? 2 : months.Groups[1].Success && months.Groups[1].Value != "" ? int.Parse(months.Groups[1].Value) : 1);
        return null;
    }

    // ---------------- summaries ----------------

    public async Task<string> Describe(AgentStep step, int index, AgentPlan plan, bool ar)
    {
        if (ProcurementRole(step.Tool) != null) return await DescribeProcurement(step, plan, ar);
        var v = step.Values; string Val(string k) => v.TryGetValue(k, out var x) && x.Length > 0 ? x : "—";
        async Task<string> Dept(string k)
        {
            if (!v.TryGetValue(k, out var x)) return "—";
            if (x.StartsWith('@')) return ar ? $"القسم الجديد من الخطوة {int.Parse(x[1..]) + 1} ({plan.Steps[int.Parse(x[1..])].Values.GetValueOrDefault("name")})" : $"the new department from step {int.Parse(x[1..]) + 1} ({plan.Steps[int.Parse(x[1..])].Values.GetValueOrDefault("name")})";
            return int.TryParse(x, out var id) ? await db.Departments.Where(d => d.Id == id).Select(d => d.Name).SingleOrDefaultAsync() ?? "—" : "—";
        }
        async Task<string> Category()
        {
            if (!int.TryParse(v.GetValueOrDefault("category"), out var categoryId)) return "—";
            return await db.ProcurementCategories.AsNoTracking().Where(c => c.Id == categoryId)
                .Select(c => c.Name).SingleOrDefaultAsync() ?? "—";
        }
        var head = $"**{index + 1}. {(ar ? Registry.First(r => r.Id == step.Tool).Ar : Registry.First(r => r.Id == step.Tool).En)}**";
        async Task<string> DescribeEmployee(AgentStep currentStep, string enField, string arField, string newEn, string newAr)
        {
            var id = v.GetValueOrDefault("user");
            var target = id == null ? null : await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == id && u.SupplierId == null);
            if (target == null) return head + "\n- " + (ar ? "الحساب: —" : "Account: —");
            var currentDepartment = target.DepartmentId == null ? "—" : await db.Departments.AsNoTracking().Where(d => d.Id == target.DepartmentId).Select(d => d.Name).SingleOrDefaultAsync() ?? "—";
            var oldValue = currentStep.Tool switch
            {
                "update_user_name" => target.FullName,
                "update_user_phone" => "••••" + (target.PhoneNumber ?? "")[^Math.Min(4, (target.PhoneNumber ?? "").Length)..],
                "change_user_department" => currentDepartment,
                _ => target.IsActive ? (ar ? "نشط" : "Active") : (ar ? "غير نشط" : "Inactive")
            };
            var roles = string.Join(", ", await users.GetRolesAsync(target));
            return head + "\n- " + (ar ? "الحساب: " : "Account: ") + target.FullName + " (" + target.Email + ")" +
                "\n- " + (ar ? arField + ": " + oldValue + " ← " + newAr : enField + ": " + oldValue + " → " + newEn) +
                "\n- " + (ar ? "الأدوار: " : "Roles: ") + roles +
                "\n- " + (ar ? "لن تتغير الأدوار." : "Roles will not change.");
        }
        return step.Tool switch
        {
            "create_department" => $"{head}\n- {(ar ? "الاسم" : "Name")}: {Val("name")}\n- {(ar ? "الوصف" : "Description")}: {Val("description")}\n- {(ar ? "الحالة" : "Status")}: {(ar ? "نشط" : "Active")}",
            "create_category" => $"{head}\n- {(ar ? "الاسم" : "Name")}: {Val("name")}\n- {(ar ? "الحالة: نشط" : "Status: Active")}",
            "rename_department" => $"{head}\n- {(ar ? "القسم" : "Department")}: {await Dept("department")}\n- {(ar ? "الاسم الجديد" : "New name")}: {Val("name")}\n- {(ar ? "الموظفون والوصف والحالة تبقى كما هي." : "Employees, description and status stay unchanged.")}",
            "create_user" => $"{head}\n- {(ar ? "الاسم" : "Name")}: {Val("fullName")}\n- {(ar ? "البريد" : "Email")}: {Val("email")}\n- {(ar ? "الدور" : "Role")}: {(v.TryGetValue("role", out var r) ? Labels.Role(r, ar) : "—")}\n- {(ar ? "القسم" : "Department")}: {await Dept("department")}\n- {(ar ? "لن تُنشأ كلمة مرور: سيصل الموظف رابط تفعيل آمن (صالح ساعة) ليختار كلمة مروره بنفسه." : "No password is created: the employee receives a secure activation link (valid one hour) to set their own password.")}",
            "update_user_name" => await DescribeEmployee(step, "Name", "الاسم", Val("fullName"), Val("fullName")),
            "update_user_phone" => await DescribeEmployee(step, "Phone", "الهاتف", "••••" + Val("phone")[^Math.Min(4, Val("phone").Length)..], "••••" + Val("phone")[^Math.Min(4, Val("phone").Length)..]),
            "change_user_department" => await DescribeEmployee(step, "Department", "القسم", await Dept("department"), await Dept("department")),
            "deactivate_user" => await DescribeEmployee(step, "Status", "الحالة", "Inactive", "غير نشط"),
            "reactivate_user" => await DescribeEmployee(step, "Status", "الحالة", "Active", "نشط"),
            "create_purchase_request" => $"{head}\n- {(ar ? "التصنيف" : "Category")}: {await Category()}\n- {(ar ? "الصنف" : "Item")}: {Val("item")} × {Val("quantity")} {Val("unit")}\n- {(ar ? "المواصفات" : "Specifications")}: {Val("specifications")}\n- {(ar ? "المبرر" : "Justification")}: {Val("justification")}\n- {(ar ? "مطلوب بتاريخ" : "Needed by")}: {Val("neededBy")}\n- {(ar ? "يُحفظ كمسودة في قسمك؛ لا يُرسل لمسؤول التوريد حتى ترسله أنت." : "Saved as a draft in your department; it is not sent to the Sourcing Officer until you submit it.")}",
            _ => head
        };
    }

    // ---------------- execution (existing services) + verification ----------------

    public async Task<(bool Ok, string Text, string? Ref, string? Url)> Execute(AgentStep step, AgentPlan plan, AssistantUser user, ClaimsPrincipal principal, bool ar, CancellationToken ct)
    {
        if (ProcurementRole(step.Tool) != null) return await ExecuteProcurement(step, plan, user, principal, ar, ct);
        var v = new Dictionary<string, string>(step.Values);
        if (v.TryGetValue("department", out var dRef) && dRef.StartsWith('@'))
        {
            var source = plan.Steps[int.Parse(dRef[1..])];
            if (source.Status != "Done" || source.ResultRef == null) return (false, ar ? "القسم المطلوب من الخطوة السابقة لم يُنشأ." : "The department from the previous step was not created.", null, null);
            v["department"] = source.ResultRef;
        }
        switch (step.Tool)
        {
            case "create_category":
            {
                var result = await categories.SaveAsync(0, v["name"], null, null, true, user.Id, ct);
                if (result.Category is not { } category)
                {
                    var error = result.Error == "Duplicate"
                        ? (ar ? "هذا التصنيف موجود مسبقًا." : "This category already exists.")
                        : (ar ? "اسم التصنيف غير صالح." : "The category name is invalid.");
                    return (false, error, null, null);
                }
                if (!await db.ProcurementCategories.AsNoTracking().AnyAsync(c => c.Id == category.Id && c.Name == v["name"] && c.IsActive, ct))
                    return (false, ar ? "تعذّر التحقق من التصنيف." : "Could not verify the new category.", null, null);
                return (true, ar ? $"تم إنشاء تصنيف المشتريات «{category.Name}»." : $"Procurement category \"{category.Name}\" created.", category.Id.ToString(), "/Admin/Categories");
            }
            case "create_department":
            {
                var result = await admin.CreateDepartmentAsync(new CreateDepartmentViewModel { Name = v["name"], Description = v.GetValueOrDefault("description") is { Length: > 0 } d ? d : null, IsActive = true }, user.Id);
                if (result != "Success") return (false, Explain(result, ar), null, null);
                var created = await db.Departments.AsNoTracking().Where(x => x.Name == v["name"]).Select(x => (int?)x.Id).SingleOrDefaultAsync(ct);   // verification
                return created == null ? (false, ar ? "لم أتمكن من التحقق من إنشاء القسم." : "Could not verify the new department.", null, null)
                    : (true, ar ? $"تم إنشاء القسم «{v["name"]}» (#{created})." : $"Department \"{v["name"]}\" created (#{created}).", created.ToString(), $"/Admin/Departments/Details/{created}");
            }
            case "rename_department":
            {
                var id = int.Parse(v["department"]);
                var current = await db.Departments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
                if (current == null) return (false, ar ? "القسم لم يعد موجودًا." : "The department no longer exists.", null, null);
                var members = await db.Users.AsNoTracking().Where(u => u.DepartmentId == id).Select(u => u.Id).ToListAsync(ct);   // keep every current employee
                var result = await admin.UpdateDepartmentAsync(new EditDepartmentViewModel { Id = id, Name = v["name"], Description = current.Description, IsActive = current.IsActive, SelectedEmployeeIds = members }, user.Id);
                if (result != "Success") return (false, Explain(result, ar), null, null);
                var name = await db.Departments.AsNoTracking().Where(x => x.Id == id).Select(x => x.Name).SingleAsync(ct);
                var kept = await db.Users.AsNoTracking().CountAsync(u => u.DepartmentId == id, ct);
                return name == v["name"] && kept == members.Count
                    ? (true, ar ? $"تم تغيير اسم القسم #{id} من «{current.Name}» إلى «{name}»؛ الموظفون ({kept}) لم يتغيروا." : $"Department #{id} renamed from \"{current.Name}\" to \"{name}\"; its {kept} employees are unchanged.", id.ToString(), $"/Admin/Departments/Details/{id}")
                    : (false, ar ? "لم أتمكن من التحقق من التعديل." : "Could not verify the change.", null, null);
            }
            case "create_user":
            {
                var context = http.HttpContext ?? throw new InvalidOperationException("No HTTP context for account provisioning.");
                var role = v["role"];
                if (!AssignableRoles.Contains(role)) return (false, ar ? "دور غير مسموح." : "Role not permitted.", null, null);
                var deptId = int.Parse(v["department"]);
                if (!await db.Departments.AnyAsync(d => d.Id == deptId && d.IsActive, ct)) return (false, ar ? "القسم غير نشط." : "The department is not active.", null, null);
                if (role == SystemRoles.SourcingOfficer && (await users.GetUsersInRoleAsync(SystemRoles.SourcingOfficer)).Any(u => u.IsActive))
                    return (false, ar ? "يوجد مسؤول توريد نشط." : "An active Sourcing Officer already exists.", null, null);
                IdentityResult created;
                try { created = await provisioning.CreateAsync(context, v["fullName"], v["email"], deptId, [role]); }
                catch (Exception e) when (e is InvalidOperationException or UnauthorizedAccessException)
                {
                    db.ChangeTracker.Clear();
                    logger.LogWarning(e, "Agent user provisioning failed");
                    return (false, e is UnauthorizedAccessException ? (ar ? "غير مسموح." : "Not permitted.") : (ar ? "تعذر إرسال رابط التفعيل، لذلك لم يُنشأ الحساب." : "The activation email could not be sent, so the account was not created."), null, null);
                }
                if (!created.Succeeded) { db.ChangeTracker.Clear(); return (false, string.Join(" ", created.Errors.Select(e => e.Description)), null, null); }
                var account = await users.FindByEmailAsync(v["email"]);   // verification
                if (account == null || !await users.IsInRoleAsync(account, role)) return (false, ar ? "لم أتمكن من التحقق من الحساب." : "Could not verify the new account.", null, null);
                return (true, ar ? $"تم إنشاء حساب {account.FullName} ({account.Email}) بدور {Labels.Role(role, true)}، وأُرسل له رابط التفعيل. الحساب يتطلب تعيين كلمة مرور عند أول دخول." : $"Account created for {account.FullName} ({account.Email}) with role {role}; the activation link was sent. A password must be set at first sign-in.",
                    account.Id, $"/Admin/Users/Details/{account.Id}");
            }
            case "update_user_name":
            case "update_user_phone":
            case "change_user_department":
            case "deactivate_user":
            case "reactivate_user":
            {
                var targetId = v["user"];
                var before = await db.Users.AsNoTracking().Where(u => u.Id == targetId && u.SupplierId == null)
                    .Select(u => new { u.FullName, u.Email, u.PhoneNumber, u.DepartmentId, u.IsActive }).SingleOrDefaultAsync(ct);
                if (before == null) return (false, ar ? "حساب الموظف لم يعد متاحًا." : "The employee account is no longer available.", null, null);
                var rolesBefore = (await users.FindByIdAsync(targetId)) is { } beforeUser ? (await users.GetRolesAsync(beforeUser)).Order().ToArray() : [];
                string? fullName = step.Tool == "update_user_name" ? v["fullName"] : null;
                string? phone = step.Tool == "update_user_phone" ? v["phone"] : null;
                int? departmentId = step.Tool == "change_user_department" ? int.Parse(v["department"]) : null;
                bool? active = step.Tool == "deactivate_user" ? false : step.Tool == "reactivate_user" ? true : null;
                var update = await admin.UpdateUserProfileAsync(targetId, fullName, phone, departmentId, active, user.Id);
                if (update != "Success") return (false, Explain(update, ar), null, null);

                var after = await db.Users.AsNoTracking().Where(u => u.Id == targetId)
                    .Select(u => new { u.FullName, u.Email, u.PhoneNumber, u.DepartmentId, u.IsActive }).SingleOrDefaultAsync(ct);
                var afterUser = await users.FindByIdAsync(targetId);
                var rolesAfter = afterUser == null ? [] : (await users.GetRolesAsync(afterUser)).Order().ToArray();
                if (after == null || afterUser == null || !rolesBefore.SequenceEqual(rolesAfter) ||
                    fullName != null && after.FullName != fullName ||
                    phone != null && after.PhoneNumber != phone ||
                    departmentId != null && after.DepartmentId != departmentId ||
                    active != null && after.IsActive != active)
                    return (false, ar ? "لم أتمكن من التحقق من التغيير أو من بقاء الأدوار كما هي." : "Could not verify the requested change and unchanged roles.", null, null);

                var field = step.Tool switch { "update_user_name" => ar ? $"الاسم: {before.FullName} ← {after.FullName}" : $"name: {before.FullName} → {after.FullName}",
                    "update_user_phone" => ar ? "تم تحديث الهاتف (يظهر آخر 4 أرقام فقط)." : "phone updated (only the last 4 digits are shown)",
                    "change_user_department" => ar ? $"القسم: {await DepartmentName(before.DepartmentId, ct)} ← {await DepartmentName(after.DepartmentId, ct)}" : $"department: {await DepartmentName(before.DepartmentId, ct)} → {await DepartmentName(after.DepartmentId, ct)}",
                    _ => ar ? $"الحالة: {(after.IsActive ? "نشط" : "غير نشط")}" : $"status: {(after.IsActive ? "Active" : "Inactive")}" };
                var maskedPhone = "••••" + (after.PhoneNumber ?? "")[^Math.Min(4, (after.PhoneNumber ?? "").Length)..];
                var message = ar ? $"تم تحديث حساب {after.FullName} ({after.Email}) بنجاح: {field}{(step.Tool == "update_user_phone" ? " " + maskedPhone : "")}. لم تتغير أدواره." :
                    $"Updated {after.FullName} ({after.Email}) successfully: {field}{(step.Tool == "update_user_phone" ? " " + maskedPhone : "")}. Roles are unchanged.";
                return (true, message, targetId, $"/Admin/Users/Details/{targetId}");
            }
            case "create_purchase_request":
            {
                var actor = await db.Users.SingleAsync(u => u.Id == user.Id, ct);
                var form = new RequestForm
                {
                    Justification = v["justification"], Priority = RequestPriority.Normal, NeededBy = DateTime.ParseExact(v["neededBy"], "yyyy-MM-dd", CultureInfo.InvariantCulture),
                    Items = [new RequestItemForm { Name = v["item"], CategoryId = int.Parse(v["category"]), Quantity = decimal.Parse(v["quantity"], CultureInfo.InvariantCulture), Unit = v["unit"], Specifications = v["specifications"] }]
                };
                if (!await db.ProcurementCategories.AnyAsync(c => c.Id == form.Items[0].CategoryId && c.IsActive, ct))
                    return (false, ar ? "تصنيف الصنف غير نشط." : "The item category is no longer active.", null, null);
                var errors = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
                if (!System.ComponentModel.DataAnnotations.Validator.TryValidateObject(form, new(form), errors, true) ||
                    !System.ComponentModel.DataAnnotations.Validator.TryValidateObject(form.Items[0], new(form.Items[0]), errors, true))
                    return (false, string.Join(" ", errors.Select(e => e.ErrorMessage)), null, null);
                int id;
                try { id = await new DepartmentRequestService(db, users).Save(actor, form, false, Path.Combine(environment.ContentRootPath, "Areas", "Department", "App_Data", "Uploads")); }
                catch (InvalidOperationException e) { db.ChangeTracker.Clear(); return (false, e.Message, null, null); }
                var saved = await db.PurchaseRequests.AsNoTracking().AnyAsync(p => p.Id == id && p.DepartmentId == user.DepartmentId && p.Status == PurchaseRequestStatus.Draft, ct);   // verification
                return saved ? (true, ar ? $"تم حفظ طلب الشراء PR-{id:0000} كمسودة في قسمك." : $"Purchase request PR-{id:0000} saved as a draft in your department.", id.ToString(), $"/Department/PurchaseRequests/Details/{id}")
                    : (false, ar ? "لم أتمكن من التحقق من حفظ الطلب." : "Could not verify the saved request.", null, null);
            }
        }
        return (false, ar ? "إجراء غير مسجّل." : "Unregistered action.", null, null);

        async Task<string> DepartmentName(int? id, CancellationToken token) => id == null ? "—" :
            await db.Departments.AsNoTracking().Where(d => d.Id == id).Select(d => d.Name).SingleOrDefaultAsync(token) ?? "—";
    }

    private static string Explain(string code, bool ar) => code switch
    {
        "Duplicate" => ar ? "يوجد قسم بنفس الاسم." : "A department with that name already exists.",
        "InvalidName" => ar ? "الاسم يجب أن يكون 2–100 حرف." : "The name must be 2–100 characters.",
        "InvalidDescription" => ar ? "الوصف حتى 160 حرفًا." : "The description is limited to 160 characters.",
        "InvalidPhone" => ar ? "رقم الهاتف غير صالح." : "The phone number is invalid.",
        "InvalidDepartment" => ar ? "القسم غير نشط أو غير موجود." : "The department is inactive or does not exist.",
        "DepartmentRequired" => ar ? "هذا الدور يتطلب ربط المستخدم بقسم." : "This role requires a department.",
        "OfficerAlreadyActive" => ar ? "يوجد مسؤول توريد نشط بالفعل." : "An active Sourcing Officer already exists.",
        "SelfDeactivate" => ar ? "لا يمكنك تعطيل حسابك بنفسك." : "You cannot deactivate your own account.",
        "ProtectedAdmin" => ar ? "تعطيل حساب مدير النظام غير متاح عبر المساعد. استخدم مسار إدارة المستخدمين المحمي." : "Admin accounts cannot be deactivated through the assistant. Use the protected user-management workflow.",
        "SupplierAccount" => ar ? "حسابات الموردين تدار من مسار منفصل." : "Supplier accounts are managed through a separate workflow.",
        "NotFound" => ar ? "السجل المطلوب غير موجود." : "The requested record was not found.",
        _ => (ar ? "رفض النظام العملية: " : "The system rejected the operation: ") + code
    };

    public async Task<string> ListDepartments(bool ar, CancellationToken ct)
    {
        var rows = await db.Departments.AsNoTracking().OrderBy(d => d.Name)
            .Select(d => new { d.Id, d.Name, d.IsActive, Employees = db.Users.Count(u => u.DepartmentId == d.Id), Requests = d.PurchaseRequests.Count() }).Take(200).ToListAsync(ct);
        var head = ar ? "| # | القسم | الحالة | الموظفون | طلبات الشراء |\n|---|---|---|---|---|" : "| # | Department | Status | Employees | Purchase requests |\n|---|---|---|---|---|";
        return (ar ? $"### الأقسام ({rows.Count})\n" : $"### Departments ({rows.Count})\n") + head + "\n" + string.Join("\n", rows.Select(r => $"| {r.Id} | {r.Name.Replace("|", "/")} | {(r.IsActive ? (ar ? "نشط" : "Active") : (ar ? "غير نشط" : "Inactive"))} | {r.Employees} | {r.Requests} |")) + "\n\n/Admin/Departments";
    }

    public async Task<string> ReadUsers(string tool, string message, bool ar, CancellationToken ct)
    {
        if (tool == "list_users")
        {
            var page = await admin.GetUsersAsync(null, null, null, "All", 1);
            var rows = page.Users.Where(u => u.SupplierStatus == null).ToList();
            if (rows.Count == 0) return ar ? "لا توجد حسابات موظفين." : "There are no employee accounts.";
            var list = string.Join("\n", rows.Select(u => $"- {u.FullName} · {u.Email} · {u.Roles} · {u.DepartmentOrCompany} · {(u.IsActive ? (ar ? "نشط" : "Active") : (ar ? "غير نشط" : "Inactive"))}"));
            return (ar ? $"### حسابات الموظفين (حتى {rows.Count})\n" : $"### Employee accounts (up to {rows.Count})\n") + list + "\n\n/Admin/Users";
        }

        var query = ExtractUserSearch(message);
        var (id, problem) = await ResolveEmployee(query, ar, ct);
        if (id == null) return problem ?? (ar ? "اكتب اسم الموظف أو بريده الإلكتروني." : "Give an employee name or email address.");
        if (tool == "find_user")
        {
            var row = await db.Users.AsNoTracking().Where(u => u.Id == id && u.SupplierId == null)
                .Select(u => new { u.FullName, u.Email, u.DepartmentId, Department = u.Department == null ? "—" : u.Department.Name, u.IsActive }).SingleOrDefaultAsync(ct);
            if (row == null) return ar ? "لم أجد حساب موظف بهذا الاسم." : "No employee account matched that name.";
            var account = await users.FindByIdAsync(id);
            if (account == null) return ar ? "لم أجد حساب موظف بهذا الاسم." : "No employee account matched that name.";
            var roles = await users.GetRolesAsync(account);
            return ar
                ? $"وجدت حسابًا واحدًا:\n- {row.FullName} ({row.Email})\n- الدور: {string.Join(", ", roles)}\n- القسم: {row.Department}\n- الحالة: {(row.IsActive ? "نشط" : "غير نشط")}\n\nافتح: /Admin/Users/Details/{id}"
                : $"Found one account:\n- {row.FullName} ({row.Email})\n- Role: {string.Join(", ", roles)}\n- Department: {row.Department}\n- Status: {(row.IsActive ? "Active" : "Inactive")}\n\nOpen: /Admin/Users/Details/{id}";
        }
        var details = await admin.GetUserDetailsAsync(id);
        if (details == null) return ar ? "لم أجد حساب الموظف." : "Employee account was not found.";
        var phone = await db.Users.AsNoTracking().Where(u => u.Id == id).Select(u => u.PhoneNumber).SingleOrDefaultAsync(ct);
        return ar
            ? $"### {details.FullName}\n- البريد: {details.Email}\n- الهاتف: {phone ?? "—"}\n- الأدوار: {details.Roles}\n- القسم: {details.DepartmentName}\n- الحالة: {details.StatusLabel}\n- البريد مؤكد: {(details.EmailConfirmed ? "نعم" : "لا")}\n\nافتح: /Admin/Users/Details/{id}"
            : $"### {details.FullName}\n- Email: {details.Email}\n- Phone: {phone ?? "—"}\n- Roles: {details.Roles}\n- Department: {details.DepartmentName}\n- Status: {details.StatusLabel}\n- Email confirmed: {(details.EmailConfirmed ? "Yes" : "No")}\n\nOpen: /Admin/Users/Details/{id}";
    }

    // Role writes deliberately remain in the protected Admin pages: that workflow currently has no strong-auth step that
    // the Chat confirmation pipeline can safely reuse. This prepares a real, validated before/after summary only.
    public async Task<string> PrepareUserRoleChange(string message, AssistantUser actor, bool ar, CancellationToken ct)
    {
        var raw = AssistantIntentParser.Strip(message).Trim();
        var assign = Regex.IsMatch(N(raw), @"\b(assign|grant|add|give|change|update)\b|امنح|اعط|اضف|عين|خصص", RegexOptions.IgnoreCase);
        var targetQuery = (string?)null;
        var role = ResolveRole(raw);
        var patterns = assign
            ? new[]
            {
                @"(?:assign|grant|add|give)\s+(?:the\s+)?(.+?)\s+role\s+to\s+(.+)$",
                @"(?:change|update)\s+(.+?)['’]?s\s+(?:role|permission)\s+(?:to|as)\s+(.+)$",
                @"(?:assign|grant|add|give)\s+(.+?)\s+(?:the\s+)?(.+?)\s+role\s*$",
                @"(?:امنح|اعط|اعطي|اضف|عين|خصص)\s+(?:دور|صلاحية)\s+(.+?)\s+(?:ل|لل|الى|إلى)\s+(.+)$"
            }
            : new[]
            {
                @"(?:remove|revoke)\s+(?:the\s+)?(.+?)\s+role\s+from\s+(.+)$",
                @"(?:remove|revoke)\s+(.+?)['’]?s\s+(.+?)\s+role\s*$",
                @"(?:احذف|ازل|الغ|شيل)\s+(?:دور|صلاحية)\s+(.+?)\s+(?:من|لدى|عن)\s+(.+)$"
            };
        for (var patternIndex = 0; patternIndex < patterns.Length; patternIndex++)
        {
            var pattern = patterns[patternIndex];
            var m = Regex.Match(raw, pattern, RegexOptions.IgnoreCase);
            if (!m.Success) continue;
            var roleFirst = assign ? patternIndex is 0 or 3 : patternIndex == 0;
            if (roleFirst)
            {
                role ??= ResolveRole(m.Groups[1].Value);
                targetQuery = m.Groups[2].Value;
            }
            else
            {
                targetQuery = m.Groups[1].Value;
                role ??= ResolveRole(m.Groups[2].Value);
            }
            break;
        }
        if (role == null || targetQuery == null)
        {
            return ar
                ? "أقدر أجهز ملخص تغيير الدور فقط؛ التغيير نفسه يتم من صفحة الإدارة المحمية. اكتب اسم الموظف والدور المطلوب، مثل: **Assign the Requesting Department role to Ahmad**."
                : "I can prepare a role-change summary, but the change itself must be completed in the protected Admin page. Give the employee and exact role, for example: **Assign the Requesting Department role to Ahmad**.";
        }
        var (targetId, ambiguity) = await ResolveEmployee(targetQuery, ar, ct);
        if (targetId == null) return ambiguity ?? (ar ? "حدّد الموظف بالاسم الكامل أو البريد." : "Identify the employee by full name or email.");
        var target = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == targetId && u.SupplierId == null, ct);
        if (target == null) return ar ? "حساب الموردين يدار عبر مسار منفصل." : "Supplier accounts use a separate management workflow.";
        var before = (await users.GetRolesAsync(target)).ToHashSet(StringComparer.Ordinal);
        var remove = !assign;
        var roleLabel = Labels.Role(role, ar);
        string validation;
        var after = new HashSet<string>(before, StringComparer.Ordinal);
        if (role is SystemRoles.Admin or SystemRoles.Supplier || !SystemRoles.PublicRoles.Contains(role))
            validation = ar ? "لا يمكن تنفيذ هذا الدور من خلال المساعد؛ استخدم صفحة إدارة الأدوار." : "This role cannot be changed through the assistant; use the Admin Roles page.";
        else if (!remove && before.Contains(role)) validation = ar ? "الدور موجود مسبقًا." : "The role is already assigned.";
        else if (remove && !before.Contains(role)) validation = ar ? "الدور غير مسند لهذا الحساب." : "The role is not assigned to this account.";
        else if (!remove && role is SystemRoles.RequestingDepartment or SystemRoles.DepartmentRepresentative && !target.DepartmentId.HasValue)
            validation = ar ? "يلزم ربط الحساب بقسم أولًا." : "The account must belong to a department first.";
        else if (remove && role == SystemRoles.Admin && target.IsActive && target.Id == actor.Id)
            validation = ar ? "لا يمكن إزالة دور مدير النظام من حسابك." : "You cannot remove Admin from your own account.";
        else if (remove && role == SystemRoles.Admin && target.IsActive && (await users.GetUsersInRoleAsync(SystemRoles.Admin)).Count(u => u.IsActive && u.Id != target.Id) == 0)
            validation = ar ? "لا يمكن إزالة آخر مدير نظام نشط." : "The last active Admin cannot be removed.";
        else if (!remove && role == SystemRoles.SourcingOfficer && target.IsActive && (await users.GetUsersInRoleAsync(SystemRoles.SourcingOfficer)).Any(u => u.IsActive && u.Id != target.Id))
            validation = ar ? "قد يؤدي هذا لتجاوز قيد مسؤول التوريد النشط الواحد." : "This could violate the single-active Sourcing Officer constraint.";
        else
        {
            validation = ar ? "البيانات الحالية تسمح بمتابعة المراجعة." : "Current account data passes the known pre-checks.";
            if (remove) after.ExceptWith(SystemRoles.RolesToRemove(before, role));
            else after.UnionWith(SystemRoles.AccountRoles(role));
        }
        var rolesBefore = string.Join(", ", before.Where(SystemRoles.PublicRoles.Contains).Order(StringComparer.Ordinal));
        var rolesAfter = string.Join(", ", after.Where(SystemRoles.PublicRoles.Contains).Order(StringComparer.Ordinal));
        var verb = remove ? (ar ? "إزالة" : "Remove") : (ar ? "إضافة" : "Assign");
        var result = ar
            ? $"### ملخص تغيير دور مُجهّز فقط — لم يتغير أي شيء\n- الموظف: {target.FullName} ({target.Email})\n- الإجراء المطلوب: {verb} **{roleLabel}**\n- الأدوار الحالية: {rolesBefore}\n- الأدوار المتوقعة بعد التغيير: {rolesAfter}\n- الفحص: {validation}\n\nلإكماله يدويًا، راجع صفحة إدارة الأدوار: /Admin/Roles. لن أعدّل الأدوار عبر المحادثة لأن مسار التأكيد الحالي لا يطلب إعادة مصادقة قوية لهذا التغيير."
            : $"### Prepared role-change summary only — nothing was changed\n- Employee: {target.FullName} ({target.Email})\n- Requested: {verb} **{roleLabel}**\n- Current roles: {rolesBefore}\n- Expected roles after change: {rolesAfter}\n- Pre-check: {validation}\n\nTo continue manually, review Admin Roles: /Admin/Roles. I will not change roles in chat because the current confirmation flow does not require strong re-authentication for this security change.";
        db.AuditLogs.Add(new AuditLog { UserId = actor.Id, EntityName = "AiAssistantAction", EntityId = target.Id, Action = "AiRoleChangePrepared", NewValues = JsonSerializer.Serialize(new { Role = role, Remove = remove, QueryHash = Orange_Tendering_System.Services.Reports.ReportCryptography.Hash(raw), Validation = validation }), CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync(ct);
        return result;
    }

    private async Task<(string? Id, string? Problem)> ResolveEmployee(string? text, bool ar, CancellationToken ct)
    {
        text = text?.Trim().TrimEnd('.', '؟', '?', ',', '،');
        if (string.IsNullOrWhiteSpace(text)) return (null, ar ? "اكتب اسم الموظف أو بريده الإلكتروني." : "Give the employee's name or email.");
        var rows = await db.Users.AsNoTracking().Where(u => u.SupplierId == null &&
                (u.Id == text || u.Email == text || u.FullName.Contains(text) || (u.Email != null && u.Email.Contains(text))))
            .OrderBy(u => u.FullName).Take(21).Select(u => new { u.Id, u.FullName, u.Email, Department = u.Department == null ? "—" : u.Department.Name }).ToListAsync(ct);
        var exact = rows.Where(u => u.Id == text || string.Equals(u.Email, text, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(AssistantIntentParser.Normalize(u.FullName), AssistantIntentParser.Normalize(text), StringComparison.Ordinal)).ToList();
        var matches = exact.Count > 0 ? exact : rows;
        if (matches.Count == 1) return (matches[0].Id, null);
        if (matches.Count == 0) return (null, ar ? "لم أجد حساب موظف مطابقًا. استخدم الاسم الكامل أو البريد الإلكتروني." : "No employee account matched. Use the full name or email address.");
        var choices = string.Join(ar ? "؛ " : "; ", matches.Take(10).Select(u => $"{u.FullName} ({u.Email}) — {u.Department}"));
        return (null, ar ? "وجدت أكثر من موظف. حدّد واحدًا: " + choices : "More than one employee matched. Choose one: " + choices);
    }

    private static string? ExtractUserSearch(string message)
    {
        var text = AssistantIntentParser.Strip(message).Trim();
        text = Regex.Replace(text, @"^(?:find|search(?:\s+for)?|look\s+up|who\s+is|show\s+(?:me\s+)?(?:the\s+)?details\s+(?:for|of)?|get\s+(?:the\s+)?details\s+(?:for|of)?|ابحث\s+(?:عن)?|دور\s+(?:على)?|فتش\s+(?:عن)?|مين|تفاصيل\s+(?:عن|الموظف)?)\s+(?:(?:user|employee|account|staff)\s+)?", "", RegexOptions.IgnoreCase).Trim();
        return text.Length is > 0 and <= 150 ? text.Trim(' ', '\'', '’', '"', '،', ',') : null;
    }
}
