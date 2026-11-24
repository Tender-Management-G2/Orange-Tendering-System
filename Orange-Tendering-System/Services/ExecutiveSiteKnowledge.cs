using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Orange_Tendering_System.Models;

namespace Orange_Tendering_System.Services;

// Reviewed procedural knowledge, never record contents or a grant of authority.
// Destinations come exclusively from the registered, authorized navigation catalog.
public static class ExecutiveSiteKnowledge
{
    public const string Instruction = " Use the server-loaded reviewed site guide for procedural answers; site_help can reread it if needed. Distinguish explain/open/prepare/execute: explaining or opening must never prepare an approval. Only listed tools are connected. Other site tasks must use the existing permitted page/form; explicitly say that opening a form saves nothing. Never substitute a similar prepare_action operation for an unsupported action. If a record is ambiguous, ask which one; never choose the first search hit. If a command is unclear, ask one focused question. If asked to train or know everything, do not claim fine-tuning, complete coverage or zero errors.";
    public static bool IsProcedural(string command) => System.Text.RegularExpressions.Regex.IsMatch(command, @"\b(how|why|explain|workflow|كيف|اشرح|اشرحلي|ليش|خطوات)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    public static string IdentityPrompt(IEnumerable<string> roles) => "Authenticated roles supplied by the server: " + string.Join("; ", roles.Where(SystemRoles.All.Contains).Select(r => r + " / " + RoleLabel(r))) +
        ". The user's role is known. For a general question about my role/workflow, site_help topic can simply be 'workflow'; do not ask the user to specify a task before consulting the guide.";
    public static string CommandInstruction(string command) => Instruction + (command.Any(c => c is >= '\u0600' and <= '\u06ff')
        ? " Current user's command is Arabic. Write your explanation in Arabic, even if tool guides contain English."
        : " Current user's command is English. Write your explanation in English, translating Arabic guide text. Keep exact tool names and page IDs unchanged.");
    public static string ContextPrompt(ClaimsPrincipal currentUser, ExecutivePage[] pages, string command)
    {
        var roles = SystemRoles.All.Where(currentUser.IsInRole).ToArray();
        return IdentityPrompt(roles) + (IsProcedural(command)
            ? " Reviewed guide automatically loaded by the SERVER, not a model tool invocation: " + ModelJson(Read(currentUser, pages, command))
            : NavigationPrompt(pages) + " Reviewed role facts: " + string.Join(" ", roles.Select(RoleGuide))) + CommandInstruction(command);
    }
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    // This JSON is model-message data, never HTML. Keep Arabic as Unicode rather than
    // six-character escapes that needlessly consume the model's fixed context window.
    public static string ModelJson(object value) => JsonSerializer.Serialize(value, Json);
    public static string RoleLabel(string role) => role switch {
        SystemRoles.Admin => "مدير النظام",
        SystemRoles.RequestingDepartment => "القسم الطالب للشراء",
        SystemRoles.SourcingOfficer => "مسؤول التوريد",
        SystemRoles.Supplier => "المورد",
        SystemRoles.EvaluationCommittee => "عضو لجنة التقييم الفني والمالي",
        SystemRoles.SourcingManager => "مدير التوريد",
        SystemRoles.TopCommittee => "عضو اللجنة العليا",
        _ => "غير محدد"
    };
    public static string RoleGuide(string role) => role switch {
        SystemRoles.Admin => "مدير النظام: إدارة المستخدمين والأدوار والأقسام ومراجعة الموردين والإعدادات والتدقيق والتحليلات. الإدارة لا تمنحك حق التقييم أو التصويت بدلاً من اللجان. التعديلات تتم في نماذج الإدارة الحالية.",
        SystemRoles.RequestingDepartment => "القسم الطالب للشراء: أنشئ طلب شراء بأصنافه وكمياته ومواصفاته وتبريره ثم أرسله وتابع طلبات قسمك. صحح المعاد حسب الحالة وتواصل مع مسؤول التوريد عبر الاستفسارات. لا تنشئ عطاءً ولا تراجع عروض المنافسين.",
        SystemRoles.SourcingOfficer => "مسؤول التوريد: راجع طلب الشراء المقبول وأنشئ العطاء، واطلب مقترحات الأوزان من المقيمين الأربعة. النظام يحسب المتوسط؛ في TenderWorkflowService الحالي أنت المسؤول عن اعتماد المتوسط وقفل الأوزان قبل فتح التقديم، وليس المدير. أدر الدعوات والموعد والإغلاق والامتثال والجولات والتحليل والاجتماع. بعد تسليم الأربعة علاماتهم أنهِ التقييم لتوليد التقرير الواحد ثم يوقعه الأربعة، وحوّله للمدير. اتبع القرار والتحويل الإلزامي قبل الإرساء وإشعار الموردين.",
        SystemRoles.Supplier => "المورد: راجع دعوتك ومتطلبات العطاء واقبل الدعوة أولاً، ثم جهّز عرضك وأسعار الأصناف والشروط والمرفقات وأرسله قبل الموعد. تابع عروضي والإشعارات واستفسر من مسؤول التوريد. لا تطلع على عروض المنافسين ولا تقترح أوزان التقييم ولا توقع تقارير اللجان.",
        SystemRoles.EvaluationCommittee => "عضو لجنة التقييم الفني والمالي: تعمل في العطاءات المسندة لمقعدك. قدّم مقترح أوزان مالي وفني مجموعه 100% قبل التقديم. أثناء الفترة المحددة قيّم كل مورد مؤهل مالياً وفنياً مع التبرير. وقّع عند تسليم العلامات. النظام يولّد التقرير بعد رابع تسليم موقّع ويرسله لمسؤول التوريد.",
        SystemRoles.SourcingManager => "مدير التوريد: أنت واحد من المقيمين الأربعة؛ تقترح الأوزان وتسلم علاماتك وتوقع بصفتك مقيماً. في TenderWorkflowService الحالي مسؤول التوريد هو من يعتمد متوسط الأوزان؛ لا تقل إن اعتماد تقرير المدير يقفل الأوزان. بعد توقيعات اللجنة وتحويل المسؤول، تراجع التقرير بصفتك مديراً وفق إجراءات الموافقة أو الإعادة أو الرفض والتوقيع. تقر وتحول القرار النهائي للمسؤول عبر مسار التحويل الحالي.",
        SystemRoles.TopCommittee => "عضو اللجنة العليا: راجع التقرير المعتمد وتوقيع المدير والنسخة الحالية، ثم سجّل صوتك وتوقيعك بصفتك العضو المسند وفق السياسة المجمدة. لا تفترض إجماعاً أو نصاباً غير مقروء من السياسة. أداة top_vote تستخدم TenderWorkflowService: Reject أو Retender يوقف الموافقات اللاحقة ويسجل قراراً ينتظر تحويل المدير وإجراء مسؤول التوريد. Reject ليس دليلاً على وصول إشعارات الموردين، وRetender ليس إنشاء جولة جديدة تلقائياً. صفحات اللجان الأخرى قد تستخدم مسارها الخاص؛ اعتمد نتيجة الخدمة الفعلية ولا تخلط المسارات.",
        _ => "لا توجد إرشادات دور مؤكد؛ اطلب توضيحاً ولا تفترض صلاحيات."
    };
    public static string[] CandidateOperations(IEnumerable<string> roles) => roles.SelectMany(role => role switch {
        SystemRoles.SourcingOfficer => new[] { "request_weights", "approve_weights", "forward_report", "notify_suppliers" },
        SystemRoles.EvaluationCommittee => new[] { "sign_evaluation_report" },
        SystemRoles.SourcingManager => new[] { "sign_evaluation_report", "approve_manager_report", "forward_decision" },
        SystemRoles.TopCommittee => new[] { "top_vote" },
        _ => Array.Empty<string>()
    }).Distinct().ToArray();
    public static object Read(ClaimsPrincipal currentUser, ExecutivePage[] pages, string topic)
    {
        var roles = SystemRoles.All.Where(currentUser.IsInRole).ToArray();
        return new {
            source = "Reviewed site guide + registered authorized page catalog; not model training or live record status",
            topicHash = Reports.ReportCryptography.Hash(topic),
            scope = "Explain ONLY these authenticated roles as the user's roles. For a question about my role, name my exact role once. Other roles in the workflow are handoff recipients, never the user's identity.",
            roleGuides = roles.Select(role => new { role, arabicRole = RoleLabel(role), guide = RoleGuide(role) }).ToArray(),
            workflow = "Purchase request -> Officer review/accept -> tender requirements -> four evaluator weight proposals -> average and Sourcing Officer approval/lock in current TenderWorkflowService -> bidding/invitations -> close/participation gate -> compliance and reviewed AI analysis -> scheduled scoring -> four signed evaluations produce one report and send it to the Officer -> Officer forwarding -> separate Manager review/sign -> configured Top review -> Manager decision relay -> Officer outcome/award -> supplier notifications. Current services and the report's frozen policy govern availability; never assume a quorum or current record state.",
            rules = new[] {
                "Evaluation has four assigned seats: Manager, Department, Financial and Technical representatives. Manager is one of the four; Top is a separate assigned committee.",
                "Weight proposals precede bidding; scores follow bidding. Members sign once with their evaluation submission. Four signed submissions generate the report. Weight approval and Manager report approval are different operations.",
                "Normally three distinct submitted suppliers in the current round; the two-offer exception needs three prior failed rounds and server validation. Invitations are not bids. Prior rounds remain history.",
                "Read the exact current report before signing. Revisions preserve old evidence and invalidate obsolete signatures. Never alter locked bids, weights, scores or signed versions.",
                "For the connected TenderWorkflowService Top action: Reject means company no longer needs tender (cancellation decision); Retender means offers unacceptable. A non-approval stops further approval for this version and creates the Manager-to-Officer relay. Never promise supplier notification or a fresh round merely because a vote was recorded. Follow the actual result of the selected module.",
                "Department ownership, supplier ownership, tender assignment, stage, deadlines and current policy are enforced by existing services. Admin is not automatically an evaluator or voter.",
                "Account activation, OTP, reset password and passkeys use existing Identity pages. Never ask for credentials in chat. Clarifications are human communication, not AI chat. Notifications and emails require actual delivery evidence."
            },
            capabilities = new {
                read = new[] { "search_records: permitted records only", "tender_status: live permitted tender status", "open_report: authorized current report link", "navigate_page: permitted pageId only", "site_help: procedural guide, no record access" },
                formOnly = roles.Contains(SystemRoles.SourcingOfficer) ? "draft_tender opens Create Tender at the selected accepted purchase request. The wizard inherits the request's title, description and items. The officer completes the applicable requirements and assignments, then reviews and confirms." : "Use the existing authorized form for writes. draft_tender is Officer-only and unavailable to this role.",
                prepareOnly = CandidateOperations(roles),
                authorization = "These are candidate operations by role, NOT a grant: active Officer identity, tender seat, current stage and all backend authorization must still pass. Admin, Department and Supplier have no connected prepare_action mutations.",
                confirmation = "prepare_action creates pending review only. Execute only through the separate Confirm endpoint with explicit consent and current service authorization. Approvals, signatures, votes/cancellation and supplier notifications require existing user-verified passkey signing. Voice or chat saying yes is not identity proof. Never call an approval for a request to explain or open it."
            },
            pages = pages.Select(p => new { pageId = p.Id, p.Label, p.Area, guide = PageGuide(p.Label) }).ToArray()
        };
    }

    public static string NavigationPrompt(ExecutivePage[] pages) =>
        "Actual registered role-authorized pages. Use navigate_page with the exact pageId, never a guessed URL. Names and aliases are navigation labels, not permission or proof of execution. " +
        JsonSerializer.Serialize(pages.Select(p => new { pageId = p.Id, p.Label, p.Area, p.Aliases }), Json);

    public static string PageGuide(string label) => label switch {
        "Dashboard" => "ملخص دورك ومؤشراته / Role dashboard; live figures require its actual data.",
        "Users" => "إنشاء الموظفين وتفعيلهم وتحديد أدوارهم عبر نماذج الإدارة / Manage internal accounts in existing Admin forms.",
        "Roles" => "إدارة الأدوار والصلاحيات / Manage roles; does not grant record access by itself.",
        "Departments" => "إدارة أقسام الشركة / Manage department master data.",
        "Suppliers" => "مراجعة تسجيل المورد ووثائقه وحالته / Review supplier registration, documents and eligibility.",
        "Settings" => "إعدادات النظام المتاحة للمدير / Existing administrative settings; never reveal secrets.",
        "Audit Logs" => "سجل من غيّر ماذا ومتى / Authorized audit trail.",
        "Analytics & Exports" => "المؤشرات والتصدير / Authorized analytics and exports, filtered by existing access.",
        "New Request" => "نموذج طلب شراء: العنوان والتبرير والأولوية والموعد والأصناف والكميات والمواصفات / Create or submit through the existing department form.",
        "My Requests" => "متابعة طلبات قسمك وتصحيح المعاد حسب حالته / Track owned requests and permitted returned corrections.",
        "Request" => "مراجعة طلب الشراء وقبوله أو إعادته أو رفضه مع السبب / Officer request review using the current form.",
        "Tender" or "Weighted Workflow" => "دورة العطاء: المتطلبات والأوزان والدعوات والمواعيد والعروض والامتثال والجولات والاجتماع والتقييم والتحويل والإرساء / Open the relevant tender after resolving its record; each action has a separate stage gate.",
        "Received Bids" => "العروض المستلمة والامتثال والمقارنة / Review received offers and supporting documents.",
        "Reports" => "التقارير الحالية والإصدارات والمراجعة / Review authorized reports and versions.",
        "Final Decisions" => "القرارات المعتمدة والتحويل والإرساء والإشعارات / Track the required decision relay before award and notification.",
        "Clarifications" or "Manager Clarifications" => "استفسارات بشرية للأطراف المسموحة مع موضوع ورسالة وسياق / Existing clarification forms, recipients and thread rules apply.",
        "Invitations" => "راجع دعوتك ومتطلبات العطاء ثم اقبل أو ارفض / Own supplier invitations; acceptance precedes offer submission.",
        "My Offers" => "جهّز أسعار الأصناف والشروط والمرفقات ثم أرسل قبل الموعد / Own draft and submitted offers; competitors remain private.",
        "Profile" => "بيانات شركتك ووسائل الاتصال / Own supplier profile using existing validation.",
        "Notifications" => "الإشعارات المرتبطة بحسابك / Read your notifications; not proof an external email arrived.",
        "Assigned Evaluations" => "العطاءات المسندة لمقعدك / Assigned evaluations only; global role is insufficient.",
        "Criteria & Weights" => "مقترح مالي وفني مجموعه 100% قبل التقديم؛ المتوسط ثم الاعتماد والقفل / Propose weights, not supplier scores.",
        "Technical Evaluation" => "علامات فنية لكل مورد مؤهل وتبرير أثناء الفترة المحددة / Technical scoring in the existing authorized form.",
        "Financial Evaluation" => "علامات مالية لكل مورد مؤهل وتبرير أثناء الفترة المحددة / Financial scoring in the existing authorized form.",
        "Scores" => "مراجعة العلامات المسلمة والنتيجة الموزونة / Review permitted scores; AI cannot replace a member's assessment.",
        "AI Analysis" => "تحليل مساعد يراجعه الإنسان / Advisory bid analysis, not an autonomous approval.",
        "Unified Report" => "تقرير واحد من التقييمات الأربعة الموقّعة / Unified report from four signed evaluations.",
        "History" or "Decision History" => "السجل التاريخي للقرارات أو التقييمات / Preserve earlier rounds and evidence.",
        "Workflow Overview" => "متابعة تقدم العطاءات ومراحلها / Authorized workflow overview.",
        "Pending Reports" => "التقارير بانتظار مراجعة المدير / Pending Manager review, separate from weight approval.",
        "Report Review" => "راجع التقرير الحالي للموافقة أو الرفض أو الإعادة مع السبب والتوقيع حسب السياسة / Existing Manager review form.",
        "Forwarding" => "تحويل التقرير أو القرار بعد اكتمال المتطلبات / Existing authorized report/decision relay.",
        "Evaluation Summary" => "ملخص التقرير المعتمد ومرفقاته المسموحة / Top Committee authorized evidence review.",
        "Voting" => "صوت العضو وتوقيعه في النسخة الحالية وفق السياسة المجمدة / Own assigned Top vote only; never infer consent.",
        "Management Statistics" => "إحصائيات الإدارة المسموحة / Authorized management statistics.",
        "My Tasks" => "مهام حسابك والعناصر المنتظرة / Your tasks and pending work.",
        "Device passkey" => "إعداد مفتاح المرور والتحقق عبر جهازك / Existing Identity passkey enrollment and verification.",
        "How It Works" => "شرح دورة الموقع والأدوار / General site workflow guide.",
        _ => "Use the existing authorized page; unsupported details require clarification, not a guessed procedure."
    };
}
