// ORANGE TENDER MANAGEMENT SYSTEM
// Module: Sourcing Officer / Orange Intelligence
// One controller, two parts:
//   1. Overview, Suppliers and Tenders tabs: every action loads the user's authorized data through IntelligenceService
//      and returns JSON that the page scripts (wwwroot/js/intelligence-*.js) draw.
//   2. AI Studio (Gemini): SupplierSummary, AskSupplier and StartVoice, used by intelligence-ai.js and intelligence-audio.js,
//      served by IntelligenceAiService (Services/Intelligence/IntelligenceAiService.cs).
// Everything here is read-only with respect to procurement data.
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Services;
using Orange_Tendering_System.Services.Intelligence;

namespace Orange_Tendering_System.Controllers;

// Only Sourcing Officers may open this page. Every POST must carry the anti-forgery token (RequestVerificationToken header).
// AgentAntiforgeryFailure (Services/AgentAntiforgeryFailureAttribute.cs) turns a missing or wrong token into a clear JSON 400.
// NoStore: the browser never caches these answers, so a user always sees current, authorized data.
[Authorize(Roles = SystemRoles.SourcingOfficer)]
[AutoValidateAntiforgeryToken, AgentAntiforgeryFailure, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class IntelligenceController : Controller
{
    private readonly IntelligenceService intelligence;
    private readonly IntelligenceExportService exports;
    private readonly IntelligenceAiService ai;
    private readonly ILogger<IntelligenceController> logger;

    // ASP.NET Core creates the controller for each request and passes the registered services (Program.cs).
    public IntelligenceController(IntelligenceService intelligence, IntelligenceExportService exports, IntelligenceAiService ai,
        ILogger<IntelligenceController> logger)
    {
        this.intelligence = intelligence;
        this.exports = exports;
        this.ai = ai;
        this.logger = logger;
    }

    // Request bodies of the AI Studio actions.
    public sealed record SupplierRequest(int SupplierId);
    public sealed record AskRequest(int SupplierId, string? Question);

    // A supplier account never reaches this workspace, even if it also has the Sourcing Officer role.
    public override void OnActionExecuting(Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext context)
    {
        if (User.IsInRole(SystemRoles.Supplier)) context.Result = Forbid();
    }

    // GET /Intelligence -> Views/Intelligence/Index.cshtml (the page with the four tabs).
    [HttpGet]
    public IActionResult Index()
    {
        ViewData["Title"] = "Orange Intelligence";
        ViewData["BodyClass"] = "radar-page";
        return View();
    }

    // ---------- Tab 1: Overview ----------

    // GET /Intelligence/Overview?from=2026-01-01&to=2026-06-30&refresh=false
    [HttpGet]
    public async Task<IActionResult> Overview(string? from, string? to, bool refresh, CancellationToken ct)
    {
        OverviewData overview = await intelligence.GetOverviewAsync(from, to, refresh, ct);
        return Json(overview);
    }

    // ---------- Tab 2: Suppliers ----------

    // GET /Intelligence/Suppliers?from=&to=&department=&category=&activity=all&search=
    [HttpGet]
    public async Task<IActionResult> Suppliers(string? from, string? to, string? department, string? category, string? activity,
        string? search, bool refresh, CancellationToken ct)
    {
        SupplierFilters filters = new SupplierFilters();
        filters.Department = department ?? "";
        filters.Category = category ?? "";
        filters.Activity = activity ?? "all";
        filters.Search = search ?? "";
        SuppliersData suppliers = await intelligence.GetSuppliersAsync(from, to, filters, refresh, ct);
        return Json(suppliers);
    }

    // GET /Intelligence/SupplierDetails?supplierId=12&from=&to=
    [HttpGet]
    public async Task<IActionResult> SupplierDetails(int supplierId, string? from, string? to, CancellationToken ct)
    {
        SupplierDetails? details = await intelligence.GetSupplierDetailsAsync(supplierId, from, to, ct);
        if (details == null)
        {
            return NotFound(new { error = "This supplier is not in your authorized scope." });
        }
        return Json(details);
    }

    // GET /Intelligence/CompareSuppliers?supplierA=3&supplierB=7
    [HttpGet]
    public async Task<IActionResult> CompareSuppliers(int supplierA, int supplierB, CancellationToken ct)
    {
        if (supplierA <= 0 || supplierB <= 0 || supplierA == supplierB)
        {
            return BadRequest(new { error = "Choose two different suppliers." });
        }
        List<OfferComparison>? rows = await intelligence.CompareSuppliersAsync(supplierA, supplierB, ct);
        if (rows == null)
        {
            return NotFound(new { error = "A selected supplier is not in your authorized scope." });
        }
        return Json(rows);
    }

    // POST /Intelligence/Simulate   body: { "from": "", "to": "", "supplierIds": [3, 7] }
    [HttpPost]
    public async Task<IActionResult> Simulate([FromBody] SimulateRequest request, CancellationToken ct)
    {
        SimulationResult? result = await intelligence.SimulateAsync(request, ct);
        if (result == null)
        {
            return BadRequest(new { error = "Choose one or more existing suppliers to exclude." });
        }
        return Json(result);
    }

    // ---------- Tab 3: Tenders ----------

    // GET /Intelligence/Tenders?from=&to=
    [HttpGet]
    public async Task<IActionResult> Tenders(string? from, string? to, bool refresh, CancellationToken ct)
    {
        TendersData tenders = await intelligence.GetTendersAsync(from, to, refresh, ct);
        return Json(tenders);
    }

    // GET /Intelligence/TenderDetails?tenderId=5&from=&to=
    [HttpGet]
    public async Task<IActionResult> TenderDetails(int tenderId, string? from, string? to, CancellationToken ct)
    {
        TenderDetails? details = await intelligence.GetTenderDetailsAsync(tenderId, from, to, ct);
        if (details == null)
        {
            return NotFound(new { error = "This tender is not in your authorized scope and period." });
        }
        return Json(details);
    }

    // POST /Intelligence/Export   body: { "format": "xlsx", "reportType": "tender_offers", "tenderId": 5, "roundId": 9 }
    // Returns the file itself (Excel or PDF); the browser saves it.
    [HttpPost]
    public async Task<IActionResult> Export([FromBody] ExportRequest request, CancellationToken ct)
    {
        try
        {
            ExportFile file = await exports.BuildAsync(request, ct);
            Response.Headers.CacheControl = "no-store";
            return File(file.Bytes, file.ContentType, file.FileName);
        }
        catch (IntelligenceExportException e)
        {
            return BadRequest(new { error = e.Message });
        }
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
