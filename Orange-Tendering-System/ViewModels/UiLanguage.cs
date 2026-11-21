namespace Orange_Tendering_System.ViewModels;

// Interface language chosen with the top-bar selector (cookie "orange-lang": "en" or "ar", written by wwwroot/js/ui-language.js).
// The Sourcing Officer workspace renders right-to-left with Arabic navigation, top bar and page headers when "ar" is chosen.
public static class UiLanguage
{
    public const string Cookie = "orange-lang";

    public static bool IsArabic(Microsoft.AspNetCore.Http.HttpContext context) =>
        string.Equals(context.Request.Cookies[Cookie], "ar", StringComparison.Ordinal);
}
