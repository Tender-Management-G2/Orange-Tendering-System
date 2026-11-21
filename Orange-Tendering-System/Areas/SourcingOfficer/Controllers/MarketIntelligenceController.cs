// ORANGE TENDER MANAGEMENT SYSTEM
// Module: Sourcing Officer / Market Intelligence (ORANGE RADAR)
// Purpose: the Orange Radar workspace page and its JSON / streaming endpoints. Read-only with respect to procurement data.
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Services.Radar;

namespace Orange_Tendering_System.Areas.SourcingOfficer.Controllers;

[Area("SourcingOfficer")]
[Authorize(Roles = SystemRoles.SourcingOfficer)]
[AutoValidateAntiforgeryToken, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class MarketIntelligenceController(RadarService radar, RadarDataService data, RadarDocumentDna dna, RadarAnalystService analyst,
    RadarBriefingService briefing, RadarReportService reports, RadarOllamaClient ollama, RadarSpeechRecognizer recognizer, RadarSpeechSynthesizer synthesizer,
    ILogger<MarketIntelligenceController> logger) : Controller
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    public sealed record SimulateRequest(string? From, string? To, int[]? SupplierIds);
    public sealed record CompareRequest(string? From, string? To, int DocumentA, int DocumentB);
    public sealed record BriefingRequest(string? From, string? To, string? Language, bool AiNarration);
    public sealed record SpeakRequest(string? Text, string? Language, double Speed = 1.0);
    public sealed record PeriodRequest(string? From, string? To);

    [HttpGet]
    public IActionResult Index()
    {
        ViewData["Title"] = "Orange Radar · Market Intelligence";
        ViewData["BodyClass"] = "radar-page";
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> Workspace(string? from, string? to, bool refresh, CancellationToken ct)
    {
        var period = RadarAnalystService.ParsePeriod(from, to);
        if (refresh) data.Invalidate(period);
        return Json(await radar.WorkspaceAsync(period, ct));
    }

    [HttpPost]
    public async Task<IActionResult> DocumentDna([FromBody] PeriodRequest request, CancellationToken ct)
    {
        var period = RadarAnalystService.ParsePeriod(request.From, request.To);
        var state = await radar.RunDocumentDnaAsync(period, ct);
        return Json(new { state, workspace = await radar.WorkspaceAsync(period, ct) });
    }

    [HttpPost]
    public async Task<IActionResult> Simulate([FromBody] SimulateRequest request, CancellationToken ct)
    {
        var s = await radar.SnapshotAsync(RadarAnalystService.ParsePeriod(request.From, request.To), ct);
        var ids = (request.SupplierIds ?? []).Distinct().Take(10).ToArray();
        if (ids.Length == 0 || ids.Any(id => s.Supplier(id) == null)) return BadRequest(new { error = "Choose one or more existing suppliers to exclude." });
        return Json(RadarAnalytics.Simulate(s, ids, radar.Thresholds.MinimumBidsForCompetition));
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
    [HttpPost, RequestSizeLimit(64_000)]
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
        try { await analyst.AskAsync(request, Emit, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { /* the officer interrupted this turn */ }
        catch (Exception e) when (e is RadarAiUnavailableException or HttpRequestException or OperationCanceledException)
        {
            logger.LogWarning(e, "Orange Radar analyst turn failed");
            await Emit(new { type = "error", message = "The local AI analyst did not answer in time. Charts and evidence remain available." });
        }
    }

    [HttpPost]
    public async Task<IActionResult> Briefing([FromBody] BriefingRequest request, CancellationToken ct) =>
        Json(await briefing.BuildAsync(RadarAnalystService.ParsePeriod(request.From, request.To), request.Language == "ar" ? "ar" : "en", request.AiNarration, ct));

    [HttpPost, RequestSizeLimit(16_000_000)]
    public async Task<IActionResult> Report([FromBody] RadarReportRequest request, CancellationToken ct)
    {
        var officer = User.FindFirstValue(ClaimTypes.Name) ?? "Sourcing Officer";
        var pdf = await reports.BuildAsync(request, officer, ct);
        return File(pdf, "application/pdf", $"Orange-Radar-Report-{DateTime.UtcNow:yyyyMMdd-HHmm}.pdf");
    }

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
