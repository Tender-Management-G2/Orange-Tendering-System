// ORANGE TENDER MANAGEMENT SYSTEM
// Module: Orange Intelligence (shared AI intelligence workspace, formerly Sourcing Officer / Market Intelligence)
// Purpose: the intelligence page and its JSON / streaming endpoints for every internal role except Supplier.
// Data scope is enforced in RadarDataService (whole market for procurement, management, executives and Admin; otherwise the
// user's department and committee tenders). Read-only with respect to procurement data.
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Orange_Tendering_System.Areas.TopCommittee.Services;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Services;
using Orange_Tendering_System.Services.Radar;

namespace Orange_Tendering_System.Controllers;

[Authorize(Roles = InternalRoles)]
[AutoValidateAntiforgeryToken, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class IntelligenceController(RadarService radar, RadarDataService data, RadarDocumentDna dna, RadarAnalystService analyst,
    RadarBriefingService briefing, RadarReportService reports, RadarOllamaClient ollama, RadarSpeechRecognizer recognizer, RadarSpeechSynthesizer synthesizer,
    ReportExport export, RadarExportService files, Microsoft.Extensions.Caching.Memory.IMemoryCache memory, ILogger<IntelligenceController> logger) : Controller
{
    public const string InternalRoles = SystemRoles.Admin + "," + SystemRoles.SourcingOfficer + "," + SystemRoles.SourcingManager + "," + SystemRoles.RequestingDepartment + ","
        + SystemRoles.DepartmentRepresentative + "," + SystemRoles.Technical + "," + SystemRoles.Finance + "," + SystemRoles.EvaluationCommittee + "," + TopExecutiveRoles.Allowed;

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    public sealed record SimulateRequest(string? From, string? To, int[]? SupplierIds);
    public sealed record CompareRequest(string? From, string? To, int DocumentA, int DocumentB);
    public sealed record BriefingRequest(string? From, string? To, string? Language, bool AiNarration);
    public sealed record SpeakRequest(string? Text, string? Language, double Speed = 1.0);
    public sealed record PeriodRequest(string? From, string? To);

    public sealed record TableExportRequest(string? Title, string[]? Columns, string[][]? Rows, string? Source);
    public sealed record VisualExportRequest(string? From, string? To, RadarVisualSpec? Spec);
    private string Owner => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.Identity?.Name ?? "";

    // A supplier account never reaches this workspace, even if it also carries an internal role.
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
        var period = RadarAnalystService.ParsePeriod(from, to);
        if (refresh) { await data.ScopeAsync(ct); data.Invalidate(period); }
        return Json(await radar.WorkspaceAsync(period, ct));
    }

    [HttpPost]
    public async Task<IActionResult> Simulate([FromBody] SimulateRequest request, CancellationToken ct)
    {
        var s = await radar.SnapshotAsync(RadarAnalystService.ParsePeriod(request.From, request.To), ct);
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

    [HttpPost]
    public async Task<IActionResult> CompareDocuments([FromBody] CompareRequest request, CancellationToken ct)
    {
        var s = await radar.SnapshotAsync(RadarAnalystService.ParsePeriod(request.From, request.To), ct);
        try { return Json(await dna.CompareAsync(s, request.DocumentA, request.DocumentB, semantic: true, ct)); }
        catch (RadarNotFoundException e) { return NotFound(new { error = e.Message }); }
        catch (RadarAiUnavailableException)
        {
            // Exact (SHA-256) comparison and plain excerpts still work without the embedding model.
            var result = await dna.CompareAsync(s, request.DocumentA, request.DocumentB, semantic: false, ct);
            return Json(result with { Notice = "The embedding model is unavailable: only exact matching and shared phrases are shown." });
        }
    }

    [HttpGet]
    public async Task<IActionResult> Status(CancellationToken ct)
    {
        var (available, models) = await ollama.StatusAsync(ct);
        var m = ollama.Settings;
        return Json(new
        {
            ollama = available ? "ready" : "unavailable", chatModel = m.ChatModel, chatModelInstalled = models.Contains(m.ChatModel),
            embeddingModel = m.EmbeddingModel, embeddingModelInstalled = models.Contains(m.EmbeddingModel), deepModel = m.DeepModel,
            whisper = recognizer.State, piper = synthesizer.Components, contextLength = m.ContextLength
        });
    }

    // Called when a live conversation starts so the first answer does not pay the model's cold load.
    [HttpPost]
    public async Task<IActionResult> Warm(CancellationToken ct)
    {
        try
        {
            var speech = recognizer.WarmAsync(ct);
            var model = await ollama.WarmAsync(ollama.Settings.ChatModel, ct);
            return Json(new { ok = true, seconds = model, whisperMs = await speech });
        }
        catch (Exception e) when (e is HttpRequestException or RadarAiUnavailableException or TaskCanceledException) { return StatusCode(503, new { error = "The local model could not be loaded." }); }
    }

    // Streams newline-delimited JSON events: state, tool, command, delta, sentence, done, error.
    [HttpPost, RequestSizeLimit(160_000)]
    public async Task Ask([FromBody] RadarAskRequest request, CancellationToken ct)
    {
        Response.ContentType = "application/x-ndjson; charset=utf-8";
        Response.Headers.CacheControl = "no-store";
        Response.Headers["X-Accel-Buffering"] = "no";
        async Task Emit(object payload)
        {
            await Response.WriteAsync(JsonSerializer.Serialize(payload, Web) + "\n", ct);
            await Response.Body.FlushAsync(ct);
        }
        if (request.Attachment?.Text is { Length: > 24_000 }) request = request with { Attachment = request.Attachment with { Text = request.Attachment.Text[..24_000] } };
        try { await analyst.AskAsync(request, Owner, Emit, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { /* the officer interrupted this turn */ }
        catch (Exception e) when (e is RadarAiUnavailableException or HttpRequestException or OperationCanceledException)
        {
            logger.LogWarning(e, "Orange Radar analyst turn failed");
            await Emit(new { type = "error", message = "The local AI analyst did not answer in time. Recorded charts remain available." });
        }
    }

    [HttpPost]
    public async Task<IActionResult> Briefing([FromBody] BriefingRequest request, CancellationToken ct) =>
        Json(await briefing.BuildAsync(RadarAnalystService.ParsePeriod(request.From, request.To), request.Language == "ar" ? "ar" : "en", request.AiNarration, ct));

    [HttpPost, RequestSizeLimit(16_000_000)]
    public async Task<IActionResult> Report([FromBody] RadarReportRequest request, CancellationToken ct)
    {
        var officer = User.FindFirstValue(ClaimTypes.Name) ?? "Orange Intelligence user";
        var pdf = await reports.BuildAsync(request, officer, ct);
        return File(pdf, "application/pdf", $"Orange-Radar-Report-{DateTime.UtcNow:yyyyMMdd-HHmm}.pdf");
    }

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

    // A file the assistant generated during this user's turn (RadarAnalystService.OfferFileAsync). Single owner, short-lived.
    [HttpGet]
    public IActionResult Download(string? token)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 40 || !memory.TryGetValue($"oi-file:{token}", out RadarStoredFile? stored) || stored == null || stored.Owner != Owner)
            return NotFound(new { error = "This file has expired. Ask for it again." });
        Response.Headers.CacheControl = "no-store";
        return File(stored.File.Bytes, stored.File.ContentType, stored.File.FileName);
    }

    // Excel of a chart or table drawn in the conversation, rebuilt on the server from its spec with every row (the chat view
    // shows the top entries only). The page sends the spec, never rows or values.
    [HttpPost]
    public async Task<IActionResult> ExportVisual([FromBody] VisualExportRequest request, CancellationToken ct)
    {
        if (request.Spec is not { } spec || !RadarVisuals.Datasets.Contains(spec.Dataset) || !RadarVisuals.Metrics.Contains(spec.Metric ?? "") || (spec.SupplierIds?.Length ?? 0) > 8)
            return BadRequest(new { error = "This chart cannot be exported." });
        var period = RadarAnalystService.ParsePeriod(request.From, request.To);
        var snapshot = await radar.SnapshotAsync(period, ct);
        var visual = RadarVisuals.Build(snapshot, await radar.ForensicsAsync(period, ct), spec.Dataset, spec.Metric ?? "", spec.SupplierIds ?? [], Math.Max(0, spec.TenderId), "table",
            radar.Thresholds.MinimumBidsForCompetition, complete: true);
        var model = new ReportModel { Key = "orange-intelligence", Title = visual.Title, GeneratedAt = DateTime.UtcNow, SummaryLast = true,
            Description = $"Authorized scope: {snapshot.Scope.Label}. Period: {period.Label}. {visual.Subtitle}".Trim() };
        model.Kpis.Add(new ReportKpi("Source", visual.Source));
        model.Kpis.Add(new ReportKpi("Records", visual.Rows.Count.ToString()));
        if (visual.Rows.Count == 0) model.Kpis.Add(new ReportKpi("Result", "No authorized records match this chart. Nothing was omitted or invented."));
        model.Kpis.Add(new ReportKpi("Exported by", User.FindFirstValue(ClaimTypes.Name) ?? ""));
        model.Tables.Add(new ReportTable("table", visual.Title, visual.Columns.Select(c => c.Length == 0 ? "Label" : c).ToArray(), visual.Rows.Select(r => r.ToArray()).ToList()));
        return File(export.ToXlsx(model), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"Orange-Intelligence-{spec.Dataset.Replace('_', '-')}-{DateTime.UtcNow:yyyyMMdd-HHmm}.xlsx");
    }

    // AI Studio upload: PDF / XLSX / CSV / TXT read in memory, returned to the page, never stored.
    [HttpPost, RequestSizeLimit(RadarFileExtractor.MaxBytes + 100_000)]
    public IActionResult Extract(IFormFile? file)
    {
        if (file == null || file.Length == 0) return BadRequest(new { error = "No file received." });
        if (file.Length > RadarFileExtractor.MaxBytes) return BadRequest(new { error = "The file is larger than 8 MB." });
        if (!RadarFileExtractor.Extensions.Contains(Path.GetExtension(file.FileName).ToLowerInvariant())) return BadRequest(new { error = "Upload a PDF, Excel (.xlsx), CSV or text file." });
        try { using var stream = file.OpenReadStream(); return Json(RadarFileExtractor.Extract(file.FileName, stream)); }
        catch (RadarToolException e) { return BadRequest(new { error = e.Message }); }
    }

    // Excel export of a table already on the page (a generated chart/table or an extracted upload). Cell text only.
    [HttpPost, RequestSizeLimit(2_000_000)]
    public IActionResult ExportTable([FromBody] TableExportRequest request)
    {
        if (request.Columns is { Length: > 40 } || request.Rows is { Length: > 5000 } || (request.Rows ?? []).Any(r => r.Any(c => c?.Length > 32000)))
            return BadRequest(new { error = "This table exceeds the Excel export limit; no rows were omitted." });
        var columns = (request.Columns ?? []).Select(c => Clip(c, 120)).ToArray();
        if (columns.Length == 0) return BadRequest(new { error = "Nothing to export." });
        var model = new ReportModel { Key = "orange-intelligence", Title = Clip(request.Title, 120) is { Length: > 0 } t ? t : "Orange Intelligence table", GeneratedAt = DateTime.UtcNow,
            Description = Clip(request.Source, 200) ?? "", SummaryLast = true };
        model.Kpis.Add(new ReportKpi("Source", Clip(request.Source, 200) is { Length: > 0 } s ? s : "Orange Intelligence"));
        model.Kpis.Add(new ReportKpi("Exported by", User.FindFirstValue(ClaimTypes.Name) ?? ""));
        model.Tables.Add(new ReportTable("table", model.Title, columns, (request.Rows ?? []).Select(r => r.Take(columns.Length).Select(c => c ?? "").ToArray()).ToList()));
        return File(export.ToXlsx(model), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"Orange-Intelligence-{DateTime.UtcNow:yyyyMMdd-HHmm}.xlsx");
    }
    private static string Clip(string? s, int max) => s == null ? "" : s.Length <= max ? s : s[..max];

    // Audio is processed in memory only; nothing is stored.
    [HttpPost, RequestSizeLimit(2_500_000)]
    public async Task<IActionResult> Transcribe(IFormFile? audio, string? language, CancellationToken ct)
    {
        if (audio == null || audio.Length == 0) return BadRequest(new { error = "No audio received." });
        try
        {
            await using var stream = audio.OpenReadStream();
            return Json(await recognizer.TranscribeAsync(stream, language is "ar" or "en" ? language : "auto", ct));
        }
        catch (RadarSpeechException e) { return StatusCode(e.Status, new { error = e.Message }); }
    }

    [HttpPost, RequestSizeLimit(8_000)]
    public async Task<IActionResult> Speak([FromBody] SpeakRequest request, CancellationToken ct)
    {
        try
        {
            var language = request.Language == "ar" || (request.Language != "en" && RadarGrounding.IsArabic(request.Text)) ? "ar" : "en";
            var (wav, ms) = await synthesizer.SynthesizeAsync(request.Text ?? "", language, request.Speed, ct);
            Response.Headers["X-Synthesis-Ms"] = ms.ToString("0");
            return File(wav, "audio/wav");
        }
        catch (RadarSpeechException e) { return StatusCode(e.Status, new { error = e.Message }); }
    }
}
