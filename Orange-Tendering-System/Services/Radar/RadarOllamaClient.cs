using System.Diagnostics;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Orange_Tendering_System.Services.Radar;

public sealed record OllamaToolCall(string Name, JsonElement Arguments);
public sealed record OllamaStreamChunk(string? Content, IReadOnlyList<OllamaToolCall> ToolCalls, bool Done, JsonElement? Final);

// Orange Radar's own Ollama client: streaming /api/chat with tools and /api/embed. Separate from OllamaChatClient so the
// help assistant and bid analysis keep their configured model. Loopback endpoints only - procurement data stays on this machine.
public sealed class RadarOllamaClient(HttpClient http, IOptions<RadarOptions> options, ILogger<RadarOllamaClient> logger)
{
    private readonly HttpClient client = Configure(http);
    private static HttpClient Configure(HttpClient value) { value.Timeout = Timeout.InfiniteTimeSpan; return value; }
    private static DateTime availabilityCheckedAt;
    private static bool lastAvailability;
    private static HashSet<string> installedModels = [];

    public RadarModelOptions Settings => options.Value.Models;

    private Uri Endpoint(string path)
    {
        if (!Uri.TryCreate(Settings.BaseUrl, UriKind.Absolute, out var root) || !root.IsLoopback || root.Scheme != "http")
            throw new RadarAiUnavailableException("Orange Radar only talks to a local (loopback) Ollama endpoint. Check OrangeRadar:Models:BaseUrl.");
        return new Uri(root, path);
    }

    public async Task<(bool Available, IReadOnlySet<string> Models)> StatusAsync(CancellationToken ct)
    {
        if (DateTime.UtcNow - availabilityCheckedAt < TimeSpan.FromSeconds(15)) return (lastAvailability, installedModels);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(3));
            var tags = await client.GetFromJsonAsync<JsonElement>(Endpoint("/api/tags"), timeout.Token);
            installedModels = tags.GetProperty("models").EnumerateArray().Select(m => m.GetProperty("name").GetString() ?? "").ToHashSet();
            lastAvailability = true;
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException or JsonException or KeyNotFoundException or RadarAiUnavailableException)
        {
            lastAvailability = false; installedModels = [];
        }
        availabilityCheckedAt = DateTime.UtcNow;
        return (lastAvailability, installedModels);
    }

    public async Task<bool> HasModelAsync(string model, CancellationToken ct)
    {
        var (available, models) = await StatusAsync(ct);
        return available && (models.Contains(model) || models.Contains(model + ":latest"));
    }

    // Loads the model and keeps it resident for the configured time, so the first spoken answer does not pay the cold load.
    public async Task<double> WarmAsync(string model, CancellationToken ct)
    {
        var watch = Stopwatch.StartNew();
        using var response = await client.PostAsJsonAsync(Endpoint("/api/generate"), new { model, keep_alive = Settings.KeepAlive, prompt = "" }, ct);
        response.EnsureSuccessStatusCode();
        return watch.Elapsed.TotalSeconds;
    }

    public async IAsyncEnumerable<OllamaStreamChunk> StreamChatAsync(string model, IReadOnlyList<object> messages, object[]? tools, double temperature,
        [EnumeratorCancellation] CancellationToken ct, int? maxTokens = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["model"] = model, ["stream"] = true, ["think"] = false, ["messages"] = messages, ["keep_alive"] = Settings.KeepAlive,
            ["options"] = new { temperature, num_ctx = Settings.ContextLength, num_predict = maxTokens ?? Settings.MaxAnswerTokens }
        };
        if (tools is { Length: > 0 }) body["tools"] = tools;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(Settings.TimeoutSeconds, 10, 600)));
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint("/api/chat")) { Content = JsonContent.Create(body) };
        HttpResponseMessage response;
        try { response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token); }
        catch (HttpRequestException e) { throw new RadarAiUnavailableException("Local Ollama is not reachable. Market analytics remain available.", e); }
        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var text = await response.Content.ReadAsStringAsync(timeout.Token);
                logger.LogWarning("Orange Radar model call failed with HTTP {Status}", (int)response.StatusCode);
                throw new RadarAiUnavailableException($"The local model returned HTTP {(int)response.StatusCode}." + (text.Contains("not found") ? $" Install it with: ollama pull {model}" : ""));
            }
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var reader = new StreamReader(stream);
            while (await reader.ReadLineAsync(timeout.Token) is { } line)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                using var json = JsonDocument.Parse(line);
                var root = json.RootElement;
                if (root.TryGetProperty("error", out var error)) throw new RadarAiUnavailableException("Local model error: " + error.GetString());
                string? content = null; var calls = new List<OllamaToolCall>();
                if (root.TryGetProperty("message", out var message))
                {
                    if (message.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String) content = c.GetString();
                    if (message.TryGetProperty("tool_calls", out var tc) && tc.ValueKind == JsonValueKind.Array)
                        foreach (var call in tc.EnumerateArray())
                            if (call.TryGetProperty("function", out var f) && f.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String)
                                calls.Add(new OllamaToolCall(n.GetString()!, f.TryGetProperty("arguments", out var a) ? a.Clone() : default));
                }
                var done = root.TryGetProperty("done", out var d) && d.ValueKind == JsonValueKind.True;
                yield return new OllamaStreamChunk(content, calls, done, done ? root.Clone() : null);
                if (done) yield break;
            }
        }
    }

    public async Task<float[][]> EmbedAsync(IReadOnlyList<string> inputs, CancellationToken ct)
    {
        if (inputs.Count == 0) return [];
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(Settings.TimeoutSeconds, 10, 600)));
        HttpResponseMessage response;
        try { response = await client.PostAsJsonAsync(Endpoint("/api/embed"), new { model = Settings.EmbeddingModel, input = inputs, keep_alive = "5m" }, timeout.Token); }
        catch (HttpRequestException e) { throw new RadarAiUnavailableException("Local Ollama is not reachable for embeddings.", e); }
        using (response)
        {
            if (!response.IsSuccessStatusCode) throw new RadarAiUnavailableException($"Embedding model {Settings.EmbeddingModel} returned HTTP {(int)response.StatusCode}.");
            var json = await response.Content.ReadFromJsonAsync<JsonElement>(timeout.Token);
            return json.GetProperty("embeddings").EnumerateArray().Select(v => v.EnumerateArray().Select(x => x.GetSingle()).ToArray()).ToArray();
        }
    }
}

public sealed class RadarAiUnavailableException(string message, Exception? inner = null) : Exception(message, inner);
