// ORANGE TENDER MANAGEMENT SYSTEM
// Module: Sourcing Officer / Orange Intelligence
// The workspace, analytics endpoints and Gemini AI Studio are read-only with respect to procurement data.
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Services.Radar;

namespace Orange_Tendering_System.Controllers;

[Authorize(Roles = SystemRoles.SourcingOfficer)]
[AutoValidateAntiforgeryToken, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class IntelligenceController(RadarService radar, RadarDataService data, RadarExportService files, IntelligenceAiService ai,
    ILogger<IntelligenceController> logger) : Controller
{
    public sealed record SimulateRequest(string? From, string? To, int[]? SupplierIds);
    public sealed record SupplierRequest(int SupplierId);
    public sealed record AskRequest(int SupplierId, string? Question);

    // A supplier account never reaches this workspace, even if it also has the Sourcing Officer role.
    public override void OnActionExecuting(Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext context)
    {
        if (User.IsInRole(SystemRoles.Supplier)) context.Result = Forbid();
    }

    [HttpGet]
    public IActionResult Index()
    {
        ViewData["Title"] = "Orange Intelligence";
        ViewData["BodyClass"] = "radar-page";
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> Workspace(string? from, string? to, bool refresh, CancellationToken ct)
    {
        var period = RadarPeriod.Parse(from, to);
        if (refresh) { await data.ScopeAsync(ct); data.Invalidate(period); }
        return Json(await radar.WorkspaceAsync(period, ct));
    }

    [HttpPost]
    public async Task<IActionResult> Simulate([FromBody] SimulateRequest request, CancellationToken ct)
    {
        var s = await radar.SnapshotAsync(RadarPeriod.Parse(request.From, request.To), ct);
        var ids = (request.SupplierIds ?? []).Distinct().Take(10).ToArray();
        if (ids.Length == 0 || ids.Any(id => s.Supplier(id) == null)) return BadRequest(new { error = "Choose one or more existing suppliers to exclude." });
        return Json(RadarAnalytics.Simulate(s, ids, radar.Thresholds.MinimumBidsForCompetition));
    }

    [HttpGet]
    public async Task<IActionResult> OfferComparison(int supplierA, int supplierB, CancellationToken ct)
    {
        if (supplierA <= 0 || supplierB <= 0 || supplierA == supplierB) return BadRequest();
        var snapshot = await radar.SnapshotAsync(RadarPeriod.All, ct);
        if (snapshot.Supplier(supplierA) == null || snapshot.Supplier(supplierB) == null) return NotFound();
        return Json(RadarOfferComparison.Build(snapshot, supplierA, supplierB));
    }

    // Tender lifecycle / offers Excel and the tender PDF summary (Tenders tab).
    [HttpPost]
    public async Task<IActionResult> Export([FromBody] RadarExportRequest request, CancellationToken ct)
    {
        try
        {
            var file = await files.BuildAsync(request, ct);
            Response.Headers.CacheControl = "no-store";
            return File(file.Bytes, file.ContentType, file.FileName);
        }
        catch (RadarToolException e) { return BadRequest(new { error = e.Message }); }
    }

    // ---------- AI Studio (Gemini) ----------

    // A. AI Supplier Summary: facts -> Gemini -> { answer }
    [HttpPost]
    public async Task<IActionResult> SupplierSummary([FromBody] SupplierRequest request, CancellationToken ct)
    {
        string question = "Write a short summary (3 to 4 sentences) of this supplier's procurement activity.";
        return await AnswerAsync(request.SupplierId, question, ct);
    }

    // B. Text Chat: facts + the user's question -> Gemini -> { answer }
    [HttpPost]
    public async Task<IActionResult> AskSupplier([FromBody] AskRequest request, CancellationToken ct)
    {
        string question = (request.Question ?? "").Trim();
        if (question.Length == 0 || question.Length > 1000)
        {
            return BadRequest(new { error = "Write a question of up to 1000 characters." });
        }
        return await AnswerAsync(request.SupplierId, question, ct);
    }

    // C. Live Voice Call: facts -> single-use Gemini token -> { token, model }. The browser connects to Gemini with it.
    [HttpPost]
    public async Task<IActionResult> StartVoice([FromBody] SupplierRequest request, CancellationToken ct)
    {
        // Authorization: the facts are null when the supplier is outside this user's scope.
        string? facts = await ai.GetSupplierFactsAsync(request.SupplierId, ct);
        if (facts == null)
        {
            return NotFound(new { error = "This supplier is not in your authorized scope." });
        }
        try
        {
            string token = await ai.CreateLiveTokenAsync(facts, ct);
            return Json(new { token, model = ai.LiveModel });
        }
        catch (Exception e) when (e is GeminiException or HttpRequestException)
        {
            return GeminiError(e);
        }
    }

    // Shared by the summary and the chat: authorize, read the facts, ask Gemini once.
    private async Task<IActionResult> AnswerAsync(int supplierId, string question, CancellationToken ct)
    {
        // Authorization: the facts are null when the supplier is outside this user's scope, so nothing is sent to Gemini.
        string? facts = await ai.GetSupplierFactsAsync(supplierId, ct);
        if (facts == null)
        {
            return NotFound(new { error = "This supplier is not in your authorized scope." });
        }
        try
        {
            string answer = await ai.AskGeminiAsync(facts, question, ct);
            return Json(new { answer });
        }
        catch (Exception e) when (e is GeminiException or HttpRequestException)
        {
            return GeminiError(e);
        }
    }

    private ObjectResult GeminiError(Exception e)
    {
        logger.LogWarning("Gemini request failed: {Message}", e.Message);
        if (e is GeminiException gemini)
        {
            return StatusCode(gemini.StatusCode, new { error = gemini.Message });
        }
        return StatusCode(503, new { error = "Gemini could not be reached. Check the internet connection." });
    }
}
