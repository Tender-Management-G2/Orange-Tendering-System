using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Orange_Tendering_System.Services;

namespace Orange_Tendering_System.Services.Assistant;

public interface IAiEmbeddingProvider
{
    string Model { get; }
    Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct);
}

// Ollama's embedding endpoint is the only vector provider dependency. Vectors are persisted in SQL Server and ranked in-process;
// this does not depend on a SQL Server vector extension or an external vector database.
public sealed class OllamaEmbeddingProvider(HttpClient http, IOptions<OllamaSettings> settings) : IAiEmbeddingProvider
{
    private sealed record EmbedResponse(float[][] Embeddings);
    public string Model => settings.Value.EmbeddingModel?.Trim() ?? "";

    public async Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct)
    {
        var options = settings.Value;
        if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseUri) || baseUri.Scheme is not ("http" or "https") || string.IsNullOrWhiteSpace(Model))
            throw new InvalidOperationException("Configure Ollama:BaseUrl and Ollama:EmbeddingModel before indexing knowledge.");
        if (texts.Count is < 1 or > 32 || texts.Any(string.IsNullOrWhiteSpace)) throw new ArgumentException("Embedding batches must contain 1–32 non-empty texts.", nameof(texts));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(options.TimeoutSeconds, 5, 300)));
        using var response = await http.PostAsJsonAsync(new Uri(baseUri, "/api/embed"), new
        {
            model = Model,
            input = texts,
            truncate = false,
            keep_alive = "30s",
            options = new { num_gpu = options.NumGpu }
        }, timeout.Token);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(timeout.Token);
            throw new HttpRequestException($"Ollama embeddings failed ({(int)response.StatusCode}): {body[..Math.Min(body.Length, 500)]}", null, response.StatusCode);
        }
        var result = await response.Content.ReadFromJsonAsync<EmbedResponse>(cancellationToken: timeout.Token)
            ?? throw new InvalidDataException("Ollama returned no embedding data.");
        if (result.Embeddings.Length != texts.Count || result.Embeddings.Any(v => v.Length == 0 || v.Any(x => !float.IsFinite(x))))
            throw new InvalidDataException("Ollama returned invalid embeddings.");
        var dimensions = result.Embeddings[0].Length;
        if (result.Embeddings.Any(v => v.Length != dimensions)) throw new InvalidDataException("Ollama returned inconsistent embedding dimensions.");
        return result.Embeddings;
    }
}
