using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Orange_Tendering_System.ViewModels;

namespace Orange_Tendering_System.Services;

// Zoom Server-to-Server OAuth app credentials. Keep them in user secrets or deployment configuration,
// never in source control. Nothing is assumed when a value is missing.
public class ZoomOptions
{
    public string? AccountId { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    // Zoom user (ID or email) that hosts evaluation meetings.
    public string? HostUserId { get; set; }
    public string TokenUrl { get; set; } = "https://zoom.us/oauth/token";
    public string ApiBaseUrl { get; set; } = "https://api.zoom.us/v2/";
    public int TimeoutSeconds { get; set; } = 30;
    public bool Configured => !string.IsNullOrWhiteSpace(AccountId) && !string.IsNullOrWhiteSpace(ClientId) &&
        !string.IsNullOrWhiteSpace(ClientSecret) && !string.IsNullOrWhiteSpace(HostUserId);
}
public record ZoomMeeting(string Id, string JoinUrl);

// Creates and removes scheduled Zoom meetings. The supplied meetingZoom ZIP was a Jitsi (meet.jit.si)
// browser embed with no Zoom API code, so nothing from it could be reused here.
public class ZoomMeetingService(HttpClient http, IOptions<ZoomOptions> options, ILogger<ZoomMeetingService> logger)
{
    private ZoomOptions Settings => options.Value;
    public bool Configured => Settings.Configured;

    private async Task<string> Token(CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{Settings.TokenUrl}?grant_type=account_credentials&account_id={Uri.EscapeDataString(Settings.AccountId!)}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Settings.ClientId}:{Settings.ClientSecret}")));
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) throw new WorkflowException($"Zoom rejected the configured credentials (HTTP {(int)response.StatusCode}). No meeting was created and nothing was saved.");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        return body.GetProperty("access_token").GetString() ?? throw new InvalidDataException("Zoom returned no access token.");
    }

    public async Task<ZoomMeeting> Create(string topic, DateTime startsAtUtc, DateTime endsAtUtc, CancellationToken ct)
    {
        TenderWorkflowService.Require(Configured, "Zoom is not configured (Zoom:AccountId, ClientId, ClientSecret and HostUserId). Paste an existing Zoom link instead, or ask an administrator to configure Zoom.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(Settings.TimeoutSeconds, 5, 120)));
        try
        {
            var token = await Token(timeout.Token);
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(Settings.ApiBaseUrl), $"users/{Uri.EscapeDataString(Settings.HostUserId!)}/meetings"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Content = JsonContent.Create(new
            {
                topic = topic.Length > 200 ? topic[..200] : topic,
                type = 2, // scheduled meeting
                start_time = startsAtUtc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
                timezone = "UTC",
                duration = Math.Max(1, (int)Math.Ceiling((endsAtUtc - startsAtUtc).TotalMinutes)),
                settings = new { join_before_host = false, waiting_room = true }
            });
            using var response = await http.SendAsync(request, timeout.Token);
            if (!response.IsSuccessStatusCode) throw new WorkflowException($"Zoom could not create the meeting (HTTP {(int)response.StatusCode}). Nothing was saved.");
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(timeout.Token);
            var id = body.GetProperty("id").GetRawText().Trim('"');
            var joinUrl = body.GetProperty("join_url").GetString() ?? throw new InvalidDataException("Zoom returned no join link.");
            return new ZoomMeeting(id, joinUrl);
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException or JsonException or InvalidDataException or KeyNotFoundException or UriFormatException)
        {
            logger.LogWarning(e, "Zoom meeting creation failed");
            throw new WorkflowException("Zoom could not be reached or returned an unexpected response. No meeting was recorded and nothing was saved.");
        }
    }

    // Best effort: used when the session could not be saved after the meeting was created.
    public async Task<bool> TryDelete(string meetingId, CancellationToken ct)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(Settings.TimeoutSeconds, 5, 120)));
            var token = await Token(timeout.Token);
            using var request = new HttpRequestMessage(HttpMethod.Delete, new Uri(new Uri(Settings.ApiBaseUrl), $"meetings/{Uri.EscapeDataString(meetingId)}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await http.SendAsync(request, timeout.Token);
            return response.IsSuccessStatusCode;
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException or JsonException or InvalidDataException or KeyNotFoundException or WorkflowException or UriFormatException)
        {
            logger.LogWarning(e, "Zoom meeting {MeetingId} could not be removed after a failed schedule", meetingId);
            return false;
        }
    }
}

// Officer scheduling entry point: optionally creates the Zoom meeting, then saves the session, attaches the
// reviewed AI analysis and eligible supplier documents, and invites the four members in one transaction.
public class EvaluationSchedulingService(TenderWorkflowService workflow, ZoomMeetingService zoom)
{
    public async Task Schedule(int tenderId, SessionInput input, ClaimsPrincipal principal, CancellationToken ct)
    {
        if (!input.CreateZoomMeeting) { await workflow.Setup(tenderId, input, principal); return; }
        // Check every other precondition first so a meeting is not created for a request that cannot be saved.
        await workflow.ValidateSetup(tenderId, input, principal);
        var meeting = await zoom.Create($"Orange tender {tenderId} evaluation", input.StartsAt.UtcDateTime, input.EndsAt.UtcDateTime, ct);
        input.MeetingUrl = meeting.JoinUrl;
        try { await workflow.Setup(tenderId, input, principal, meeting.Id); }
        catch { await zoom.TryDelete(meeting.Id, CancellationToken.None); throw; }
    }
}
