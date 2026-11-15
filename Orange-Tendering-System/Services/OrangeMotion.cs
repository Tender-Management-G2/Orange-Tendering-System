using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace Orange_Tendering_System.Services;

// Orange Motion: server side of the shared action feedback (docs/ORANGE_MOTION.md, wwwroot/js/orange-motion.js).
// A controller records what actually happened AFTER the operation succeeded; the browser only plays it.

public enum OrangeVariant { Save, Submit, Send, Upload, Approve, Return, Error, Committee, Award }
public enum OrangePresentation { Popup, Toast, Inline }

public sealed record OrangeFeedback(string Title, string? Description, OrangeVariant Variant, OrangePresentation Mode)
{
    /// <summary>Committee variant only: approvals recorded for the current round, out of <see cref="Required"/>.</summary>
    public int? Approvals { get; init; }
    public int? Required { get; init; }
    /// <summary>True only when every required approval is recorded for the same committee round.</summary>
    public bool? Complete { get; init; }

    /// <summary>Important workflow transitions: submissions, official reports, approvals, final decisions.</summary>
    public static OrangeFeedback Popup(string title, string? description = null, OrangeVariant variant = OrangeVariant.Submit) =>
        new(title, description, variant, OrangePresentation.Popup);

    /// <summary>Frequent, lightweight operations: drafts, updates, routine messages, individual scores.</summary>
    public static OrangeFeedback Toast(string title, string? description = null, OrangeVariant variant = OrangeVariant.Save) =>
        new(title, description, variant, OrangePresentation.Toast);

    /// <summary>One committee member's recorded approval. Shows full approval only when all required approvals exist.</summary>
    public static OrangeFeedback CommitteeApproval(string committee, int approvals, int required = 4) => approvals >= required
        ? new($"{committee} approval complete!", $"All {required} members approved the same version.", OrangeVariant.Committee, OrangePresentation.Popup)
            { Approvals = approvals, Required = required, Complete = true }
        : new("Approval recorded", $"{approvals} of {required} {committee} approvals are recorded for this round.", OrangeVariant.Committee, OrangePresentation.Popup)
            { Approvals = approvals, Required = required, Complete = false };

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    public string ToJson() => JsonSerializer.Serialize(this, Json);
    /// <summary>For JSON responses (enum names as strings, matching the TempData signal).</summary>
    public JsonElement ToJsonElement() => JsonSerializer.SerializeToElement(this, Json);
    public static OrangeFeedback? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<OrangeFeedback>(json, Json); } catch (JsonException) { return null; }
    }
}

public static class OrangeMotionTempData
{
    public const string Key = "OrangeMotion";

    /// <summary>One-time signal for the next rendered page (survives the Post-Redirect-Get redirect, never replays on refresh).</summary>
    public static void Orange(this ITempDataDictionary tempData, OrangeFeedback feedback) => tempData[Key] = feedback.ToJson();

    /// <summary>Reads and consumes the signal.</summary>
    public static OrangeFeedback? TakeOrange(this ITempDataDictionary tempData) => OrangeFeedback.FromJson(tempData[Key] as string);
}

/// <summary>
/// When a form was sent by Orange Motion (header X-Orange-Motion: 1), a redirect result is answered as
/// { redirect, feedback } so the page that sent it can finish its animation and then navigate once. The feedback is
/// consumed here, so the next page does not replay it. Every other request is untouched.
/// </summary>
public sealed class OrangeMotionResultFilter(ITempDataDictionaryFactory tempDataFactory, IUrlHelperFactory urlHelperFactory) : IResultFilter
{
    public const string Header = "X-Orange-Motion";

    public void OnResultExecuting(ResultExecutingContext context)
    {
        if (context.HttpContext.Request.Headers[Header] != "1") return;
        var url = urlHelperFactory.GetUrlHelper(context);
        var target = context.Result switch
        {
            RedirectResult r => url.Content(r.Url),
            LocalRedirectResult r => url.Content(r.Url),
            RedirectToActionResult r => url.Action(r.ActionName, r.ControllerName, r.RouteValues, null, null, r.Fragment),
            RedirectToRouteResult r => url.RouteUrl(r.RouteName, r.RouteValues, null, null, r.Fragment),
            RedirectToPageResult r => url.Page(r.PageName, r.PageHandler, r.RouteValues, null, null, r.Fragment),
            _ => null
        };
        if (target is null) return;
        var feedback = tempDataFactory.GetTempData(context.HttpContext).TakeOrange();
        context.Result = new JsonResult(new { redirect = target, feedback = feedback?.ToJsonElement() });
    }

    public void OnResultExecuted(ResultExecutedContext context) { }
}
