using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orange_Tendering_System.Services;

namespace Orange_Tendering_System.Controllers;

[Authorize, AutoValidateAntiforgeryToken, AgentAntiforgeryFailure, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ExecutiveAgentController(ExecutiveAgentService agent, TenderWorkflowService workflow, ExecutiveSpeechService speech, ExecutiveNavigationService navigation) : Controller
{
    public sealed record CommandRequest(Guid RequestId, string? Command);
    public sealed record ConfirmRequest(Guid ActionId, bool Confirmed, string? Password, string? CredentialJson);
    public sealed record ChallengeRequest(Guid ActionId);
    public sealed record NavigateRequest(string? Command, string? Area, string? PageId = null);
    [HttpGet] public async Task<IActionResult> Pages() => await Result(async () => await navigation.Catalog(User));
    [HttpPost, RequestSizeLimit(16_000)]
    public async Task<IActionResult> Navigate([FromBody] NavigateRequest request) =>
        !ModelState.IsValid || request == null ? BadRequest(new { error = "Invalid navigation command." }) : await Result(async () => await navigation.Resolve(request.Command, request.Area, User, request.PageId));
    [HttpGet] public IActionResult SpeechStatus() => Json(new { available = speech.Available, components = speech.Components, languages = new[] { "ar", "en" }, localOnly = true });
    [HttpPost, RequestSizeLimit(5_100_000)]
    public async Task<IActionResult> Transcribe(IFormFile audio, string language, CancellationToken ct)
    {
        if (!ModelState.IsValid || audio == null) return BadRequest(new { error = "Audio is required." });
        return await Result(async () => new { text = await speech.Transcribe(audio, language, ct), localOnly = true });
    }
    [HttpGet] public async Task<IActionResult> Progress(Guid id) => await Result(() => agent.Progress(id, User));
    [HttpPost, RequestSizeLimit(16_000)]
    public async Task<IActionResult> Run([FromBody] CommandRequest request, CancellationToken ct) =>
        !ModelState.IsValid ? BadRequest(new { error = "Invalid command." }) : await Result(() => agent.Run(request.RequestId, request.Command, User, ct));
    [HttpPost, RequestSizeLimit(100_000)]
    public async Task<IActionResult> Confirm([FromBody] ConfirmRequest request) =>
        !ModelState.IsValid ? BadRequest(new { error = "Invalid confirmation." }) : await Result(() => agent.Confirm(request.ActionId, request.Confirmed, request.Password, request.CredentialJson, User));
    [HttpPost, RequestSizeLimit(1000)]
    public async Task<IActionResult> Challenge([FromBody] ChallengeRequest request) =>
        !ModelState.IsValid || request == null ? BadRequest(new { error = "Invalid action." }) : await Result(() => agent.Challenge(request.ActionId, User));
    [HttpGet]
    public async Task<IActionResult> Pdf(int id)
    {
        try { var pdf = await workflow.ReportPdf(id, User); Response.Headers.ContentDisposition = "inline"; return File(pdf.Bytes, "application/pdf"); }
        catch (WorkflowException e) { return StatusCode(403, new { error = e.Message }); }
    }
    private async Task<IActionResult> Result(Func<Task<object>> operation)
    {
        try { return Json(await operation()); }
        catch (AgentException e) { return StatusCode(e.Status, new { error = e.Message }); }
        catch (WorkflowException e) { return StatusCode(403, new { error = e.Message }); }
    }
}
