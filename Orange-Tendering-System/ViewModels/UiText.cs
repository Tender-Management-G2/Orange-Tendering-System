namespace Orange_Tendering_System.ViewModels;

// Arabic text for the shared workspace shell (sidebar items, role labels, top bar) of every role.
// English is the key; anything without a translation stays English. Language: UiLanguage (cookie "orange-lang").
public static class UiText
{
    private static readonly Dictionary<string, string> Arabic = new(StringComparer.Ordinal)
    {
        // Shell
        ["Skip to main content"] = "انتقل إلى المحتوى الرئيسي",
        ["Search for requests, tenders, bids, suppliers..."] = "ابحث عن الطلبات والعطاءات والعروض والموردين...",
        ["Search requests, tenders, bids and suppliers"] = "ابحث في الطلبات والعطاءات والعروض والموردين",
        ["Language"] = "اللغة",
        ["Interface and voice search language"] = "لغة الواجهة والبحث الصوتي",
        ["Contact support"] = "الدعم الفني",
        // Roles
        ["Admin"] = "مدير النظام",
        ["Requesting Department"] = "الإدارة الطالبة",
        ["Sourcing Officer"] = "مسؤول التوريد",
        ["Supplier"] = "المورد",
        ["Supplier Portal"] = "بوابة المورد",
        ["Evaluation Committee"] = "لجنة التقييم",
        ["Sourcing Manager"] = "مدير التوريد",
        ["Top Committee"] = "اللجنة العليا",
        ["Workspace"] = "مساحة العمل",
        // Navigation items (all roles)
        ["Dashboard"] = "لوحة التحكم",
        ["Request"] = "الطلبات",
        ["Tender"] = "العطاءات",
        ["Tenders"] = "العطاءات",
        ["Received Bids"] = "العروض المستلمة",
        ["Reports"] = "التقارير",
        ["Final Decisions"] = "القرارات النهائية",
        ["Clarifications"] = "الاستيضاحات",
        ["Notifications"] = "الإشعارات",
        ["New Request"] = "طلب جديد",
        ["My Requests"] = "طلباتي",
        ["Users"] = "المستخدمون",
        ["Roles"] = "الأدوار",
        ["Departments"] = "الإدارات",
        ["Suppliers"] = "الموردون",
        ["Settings"] = "الإعدادات",
        ["Committees"] = "اللجان",
        ["Audit Logs"] = "سجلات التدقيق",
        ["Analytics & Exports"] = "التحليلات والتصدير",
        ["Business & Maintenance"] = "الأعمال والصيانة",
        ["Invitations"] = "الدعوات",
        ["My Offers"] = "عروضي",
        ["Profile"] = "الملف الشخصي",
        ["Assigned Evaluations"] = "التقييمات المسندة",
        ["Criteria & Weights"] = "المعايير والأوزان",
        ["Technical Evaluation"] = "التقييم الفني",
        ["Weighted Evaluation"] = "التقييم الموزون",
        ["Evaluation Summary"] = "ملخص التقييم",
        ["Pending Reports"] = "التقارير المعلقة",
        ["Forwarding"] = "الإحالة",
        ["Voting"] = "التصويت",
        ["Decision History"] = "سجل القرارات",
        ["Management Statistics"] = "إحصاءات الإدارة",
        ["Manager Clarifications"] = "استيضاحات المدير",
        ["My Tasks"] = "مهامي"
    };

    public static bool IsArabic(Microsoft.AspNetCore.Http.HttpContext context) => UiLanguage.IsArabic(context);
    public static string Lang(Microsoft.AspNetCore.Http.HttpContext context) => UiLanguage.IsArabic(context) ? "ar" : "en";
    public static string Dir(Microsoft.AspNetCore.Http.HttpContext context) => UiLanguage.IsArabic(context) ? "rtl" : "ltr";

    /// <summary>The Arabic text when Arabic is selected and a translation exists; otherwise the English text.</summary>
    public static string T(Microsoft.AspNetCore.Http.HttpContext context, string english) =>
        UiLanguage.IsArabic(context) && Arabic.TryGetValue(System.Net.WebUtility.HtmlDecode(english), out var ar) ? ar : System.Net.WebUtility.HtmlDecode(english);
}
