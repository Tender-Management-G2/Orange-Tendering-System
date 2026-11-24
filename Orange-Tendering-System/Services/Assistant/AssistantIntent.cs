using System.Text;
using System.Text.RegularExpressions;

namespace Orange_Tendering_System.Services.Assistant;

public enum AssistantTopic
{
    None, TenderDetail, OpenTenders, ActiveTenders, ClosedTenders, AllTenders, Deadlines, PurchaseRequests, DelayedRequests, PendingApprovals,
    Notifications, Suppliers, SupplierPerformance, ActivitySummary, DepartmentSpending, EvaluationResults, MyInvitations, MyOffers,
    // Tender actions: navigation and the confirmed draft-creation conversation (AssistantActions).
    LatestTender, DraftTenders, AwaitingApproval, SearchTenders, TenderAttachments, CreateTender
}

public enum ReportFormat { None, Xlsx, Pdf, Docx, Pptx }

// A structured, validated request. Built by deterministic parsing on the server, so the model can never choose what data is read.
public sealed record AssistantIntent(AssistantTopic Topic, ReportFormat Format, string? Period, bool? OwnDepartment, bool CountOnly, bool WantsReport, bool Arabic, int? RecordId = null,
    bool Navigate = false, string? SearchText = null, decimal? Amount = null, string? Currency = null)
{
    public AssistantIntent Merge(AssistantIntent pending) => this with
    {
        Topic = Topic == AssistantTopic.None ? pending.Topic : Topic,
        RecordId = Topic == AssistantTopic.None ? pending.RecordId : RecordId,
        Navigate = Topic == AssistantTopic.None ? pending.Navigate : Navigate,
        SearchText = Topic == AssistantTopic.None ? pending.SearchText : SearchText,
        Format = Format == ReportFormat.None ? pending.Format : Format,
        Period = Period ?? pending.Period,
        OwnDepartment = OwnDepartment ?? pending.OwnDepartment,
        WantsReport = WantsReport || pending.WantsReport
    };
}

public static partial class AssistantIntentParser
{
    public static bool IsArabic(string text) => text.Any(c => c >= '؀' && c <= 'ۿ');

    // Lower-case, strip Arabic diacritics/tatweel and unify alef/yaa/taa-marbuta spellings so keywords match common writing.
    public static string Normalize(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text.ToLowerInvariant())
        {
            if (c is >= 'ً' and <= 'ٟ' or 'ٰ' or 'ـ') continue;
            sb.Append(c switch { 'أ' or 'إ' or 'آ' => 'ا', 'ى' => 'ي', 'ة' => 'ه', 'ؤ' => 'و', 'ئ' => 'ي', >= '٠' and <= '٩' => (char)('0' + (c - '٠')), _ => c });
        }
        return sb.ToString();
    }

    private static bool Any(string t, params string[] words) => words.Any(t.Contains);

    public static AssistantIntent Parse(string message, bool isSupplier)
    {
        var t = " " + Normalize(Strip(message)) + " ";
        var format = Any(t, "excel", "xlsx", "اكسل", "اكسيل", "سبريدشيت", "spreadsheet", "جدول بيانات") ? ReportFormat.Xlsx
            : Any(t, "pdf", "بي دي اف", "بدف") ? ReportFormat.Pdf
            : Any(t, "word", "docx", "وورد", " ورد ") ? ReportFormat.Docx
            : Any(t, "powerpoint", "power point", "pptx", "ppt", "slides", "presentation", "بوربوينت", "باوربوينت", "عرض تقديمي", "شرائح", "برزنتيشن") ? ReportFormat.Pptx
            : ReportFormat.None;
        var wantsReport = format != ReportFormat.None || Any(t, " export", "download", "generate", "report", "صدر", "تصدير", "نزل", "تقرير", "ملف", "طلعلي", "طلع لي", "جهز", "اعمل لي", "اعملي");

        // "tinder"/"tander"/"tenda": observed or common speech-recognition spellings of "tender" (local Whisper heard "tinder").
        var tender = Any(t, "tender", "tinder", "tander", "tenda ", "tendr", "مناقص", "عطاء", "عطاءات", "عطا ");
        var request = Any(t, "purchase request", " pr ", "pr-", "requests", "request ", "طلب شراء", "طلبات الشراء", "طلبات شراء", "طلبات", "طلب ");
        var supplier = Any(t, "supplier", "vendor", "مورد", "موردين", "الموردين");
        AssistantTopic topic; int? recordId = null;
        var open = Any(t, " open", "show me", "take me", "go to", "navigate", "bring up", "افتح", "افتحلي", "افتحي", " فتح", "وديني", "ودني", "خذني", "روح على", "روحني", "فرجيني", "اعرضلي", "اعرض لي");
        var latest = Any(t, "latest", "newest", " last ", "most recent", "احدث", " اخر ", "اجدد");
        var create = Any(t, "create", "new tender", "make a tender", "start a tender", "draft a tender", "انشي", "انشاء", "اعمل", "اعملي", "سوي", "سويلي", "جديد", "ابدا");
        var reference = TenderRef().Match(t);
        string? searchText = null, currency = null; decimal? amount = null;
        if (tender && create && format == ReportFormat.None && !Any(t, "how do", "how to", "how can", "كيف", "summary", "summar", "ملخص", "لخص", "تقرير", "report", "export", "list", "قائمه"))
        {
            topic = AssistantTopic.CreateTender;
            var purchaseReference = Regex.Match(t, @"(?:\bpr[- ]|طلب شراء\s*(?:رقم\s*)?)\s*0*(\d{1,9})\b");
            if (int.TryParse(purchaseReference.Groups[1].Value, out var requestId)) recordId = requestId;
            if (Budget().Match(t) is { Success: true } b && decimal.TryParse(b.Groups[1].Value.Replace(",", ""), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var value))
            {
                amount = b.Groups[2].Success && b.Groups[2].Value.Length > 0 ? value * 1000 : value;
                currency = CurrencyCode(b.Groups[3].Value);
            }
        }
        else if (reference.Success && Any(t, "attachment", "document", "files", "مرفق", "مرفقات", "مستند", "ملفات"))
        { topic = AssistantTopic.TenderAttachments; recordId = int.Parse(reference.Groups[1].Value + reference.Groups[2].Value); }
        else if (reference.Success)
        { topic = AssistantTopic.TenderDetail; recordId = int.Parse(reference.Groups[1].Value + reference.Groups[2].Value); }
        else if (tender && latest) { topic = AssistantTopic.LatestTender; open = true; }
        else if (tender && Any(t, "search", "find", "look for", "ابحث", "بحث", "دور على", "دورلي", "فتش")) { topic = AssistantTopic.SearchTenders; searchText = SearchTerm(message); }
        else if (tender && Any(t, "draft", "مسود")) topic = AssistantTopic.DraftTenders;
        else if (tender && Any(t, "awaiting approval", "pending approval", "waiting for approval", "need approval", "بانتظار الموافق", "بانتظار الاعتماد", "تنتظر الموافق", "تنتظر الاعتماد", "بحاجه لموافق", "بحاجه الى موافق"))
            topic = AssistantTopic.AwaitingApproval;
        else if (Any(t, "delay", " late", "overdue", "متاخر", "تاخر", "تاخير")) topic = AssistantTopic.DelayedRequests;
        else if (Any(t, "spend", "spending", "expenditure", "انفاق", "مصروف", "مصاريف") || (Any(t, "department", "قسم", "اقسام", "ادارات") && Any(t, "compare", "across", "by department", "per department", "مقارن", "حسب", "لكل")))
            topic = AssistantTopic.DepartmentSpending;
        else if (supplier && Any(t, "performance", "rating", "scorecard", "اداء", "تقييم الموردين")) topic = AssistantTopic.SupplierPerformance;
        else if (Any(t, "evaluation result", "evaluation outcome", "ranking", "recommended supplier", "recommendation", "نتائج التقييم", "نتيجه التقييم", "نتائج تقييم", "ترتيب الموردين", "المورد الموصى")) topic = AssistantTopic.EvaluationResults;
        else if (Any(t, "deadline", "due soon", "closing soon", "approaching", "expir", "موعد", "مواعيد", "تنتهي", "ينتهي", "اقتراب", "قريب", "تقترب")) topic = AssistantTopic.Deadlines;
        else if (Any(t, "notification", "alert", "اشعار", "تنبيه", "تنبيهات")) topic = AssistantTopic.Notifications;
        else if (Any(t, "approval", "approve", "task", "attention", "to do", "todo", "my work", "موافق", "اعتماد", "مهام", "مهمه", "انتباه", "تحتاج", "بانتظار", "معلق")
                 || (Any(t, "pending") && !tender && !request)) topic = AssistantTopic.PendingApprovals;
        else if (isSupplier && Any(t, "my offer", "my bid", "offers", "bids", "عروضي", "عرضي", "العروض")) topic = AssistantTopic.MyOffers;
        else if (isSupplier && (tender || Any(t, "invitation", "دعوه", "دعوات"))) topic = AssistantTopic.MyInvitations;
        else if (supplier) topic = AssistantTopic.Suppliers;
        else if (Any(t, "summary", "summarize", "summarise", "activity", "overview", "kpi", "ملخص", "لخص", "نشاط", "احصائ", "مؤشرات")) topic = AssistantTopic.ActivitySummary;
        else if (tender)
            topic = Any(t, "closed", "finished", "completed", "awarded", "مغلق", "منتهي", "مقفل", "مرسا", "مكتمل") ? AssistantTopic.ClosedTenders
                : Any(t, "open", "bidding", "مفتوح", "مفتوحه") ? AssistantTopic.OpenTenders
                : Any(t, "active", "in progress", "ongoing", "current", "جاري", "قائم", "حالي", "نشط") ? AssistantTopic.ActiveTenders
                : AssistantTopic.AllTenders;
        else if (request) topic = AssistantTopic.PurchaseRequests;
        else topic = AssistantTopic.None;

        // A plain question such as "how do I create a tender?" is guidance, not a data request.
        if (format == ReportFormat.None && Any(t, "how do", "how to", "how can", "how should", "what is", "why ", "explain", "workflow", "prerequisite", "steps", "process", "what should", "am i ", "can i ", "may i ",
                "كيف", "لماذا", "ليش", "اشرح", "ما هو", "ما هي", "شو يعني", "خطوات", "مراحل", "هل استطيع", "هل يمكنني", "ما الفرق"))
        { topic = AssistantTopic.None; wantsReport = false; }

        var count = Any(t, "how many", "count", "number of", "total number", "كم ", "عدد");
        string? period = Any(t, "last month", "previous month", "الشهر الماضي", "الشهر السابق") ? "lastmonth"
            : Any(t, "this month", "current month", "monthly", "هذا الشهر", "الشهر الحالي", "الشهر") ? "month"
            : Any(t, "this week", "هذا الاسبوع", "الاسبوع") ? "week"
            : Any(t, "today", "اليوم") ? "today"
            : Any(t, "this year", "هذه السنه", "هذا العام", "السنه", "العام") ? "year"
            : LastDays().Match(t) is { Success: true } m ? "d" + Math.Clamp(int.Parse(m.Groups[1].Value + m.Groups[2].Value), 1, 366) : null;
        bool? own = Any(t, "my department", "our department", "only mine", "قسمي", "قسمنا", "ادارتي", "دائرتي", "فقط قسم") ? true
            : Any(t, "all departments", "every department", "all of them", "كل الاقسام", "جميع الاقسام", "كل الادارات", "الكل") ? false : null;
        return new AssistantIntent(topic, format, period, own, count, wantsReport, IsArabic(message), recordId,
            open && topic is AssistantTopic.TenderDetail or AssistantTopic.LatestTender, searchText, amount, currency);
    }

    [GeneratedRegex(@"\bt-0*(\d{1,7})\b|(?:tender|مناقصه|عطاء)\s*(?:#|رقم|no\.?)?\s*0*(\d{1,7})\b")]
    private static partial Regex TenderRef();

    // "Hey Orange," / "يا Orange،" wake phrases are not part of the command.
    public static string Strip(string message) => WakeWord().Replace(message.Trim(), "").Trim();
    [GeneratedRegex(@"^(?:hey|hi|ok|okay|يا|اهلا|مرحبا)?\s*(?:orange|اورنج|أورنج)\s*(?:ai)?\s*[,،:!.]?\s*", RegexOptions.IgnoreCase)]
    private static partial Regex WakeWord();

    [GeneratedRegex(@"(?:budget|value|worth|estimated|ميزانيه|ميزانيتها|ميزانيته|بقيمه|قيمته|قيمتها|بمبلغ)\D{0,25}?(\d[\d,]*(?:\.\d+)?)\s*(k|الف|thousand)?\s*([a-z]{3}\b|دينار|دولار|يورو|ريال|درهم)?")]
    private static partial Regex Budget();
    public static string? CurrencyCode(string? text) => Normalize(text ?? "").Trim() switch
    {
        "jod" or "دينار" => "JOD", "usd" or "دولار" => "USD", "eur" or "يورو" => "EUR", "sar" or "ريال" => "SAR", "aed" or "درهم" => "AED", "egp" => "EGP", _ => null
    };
    private static readonly char[] TrimChars = [' ', '"', '\'', '«', '»', '.', '؟', '?', '،', ','];
    private static string? SearchTerm(string message)
    {
        var m = SearchAfter().Match(Strip(message));
        var term = m.Success ? m.Groups[1].Value.Trim(TrimChars) : null;
        return string.IsNullOrWhiteSpace(term) || term.Length > 100 ? null : term;
    }
    [GeneratedRegex(@"(?:for|about|named|called|titled|containing|عن|على|باسم|اسمه|بعنوان|فيها|فيه)\s+(?:(?:a|the|tenders?|مناقصات|مناقصه|مناقصة|عطاءات|عطاء)\s+)*(.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex SearchAfter();

    [GeneratedRegex(@"last\s+(\d{1,3})\s+days|اخر\s+(\d{1,3})\s+يوم")]
    private static partial Regex LastDays();
}
