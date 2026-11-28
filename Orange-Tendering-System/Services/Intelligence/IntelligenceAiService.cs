// ORANGE INTELLIGENCE - AI Studio (Google Gemini API). The whole AI backend is in this one class:
//   1. GetSupplierFactsAsync  authorized supplier data -> a few plain-text facts (used by all three features)
//   2. AskGeminiAsync         Supplier Summary and Text Chat: one REST request to Gemini, returns its text answer
//   3. CreateLiveTokenAsync   Live Voice: a single-use token; the browser then talks to Gemini Live directly
//   4. Errors                 Gemini failures become short messages that are safe to show
// Gemini never reads the database. We read the supplier statistics ourselves and put them in the prompt.
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Orange_Tendering_System.Services.Intelligence;

public class IntelligenceAiService
{
    private const string GeminiUrl = "https://generativelanguage.googleapis.com/v1beta";

    // The single system prompt used by the summary, the text chat and the voice call.
    public const string SystemPrompt = """
        You are Orange Intelligence, a procurement assistant.
        You help procurement employees understand the selected supplier using the supplied procurement facts.
        Answer only from the supplied facts.
        Do not invent numbers, dates, suppliers, awards, or explanations.
        If information is unavailable, say so clearly.
        Respond in the user's language (Arabic or English).
        Keep answers short and understandable.
        Write plain text without Markdown symbols.
        """;

    private readonly HttpClient http;
    private readonly IConfiguration configuration;
    private readonly IntelligenceService intelligence;

    public IntelligenceAiService(HttpClient http, IConfiguration configuration, IntelligenceService intelligence)
    {
        this.http = http;
        this.configuration = configuration;
        this.intelligence = intelligence;
    }

    // Model IDs come from appsettings.json ("Gemini" section), so they can change without code changes.
    public string TextModel
    {
        get { return configuration["Gemini:TextModel"] ?? "gemini-3.5-flash-lite"; }
    }

    public string LiveModel
    {
        get { return configuration["Gemini:LiveModel"] ?? "gemini-3.8-live"; }
    }

    // ======================================================================
    // 1. Supplier facts (authorization happens here)
    // ======================================================================

    // Returns the supplier facts as plain text, or null when the supplier is not in this user's authorized scope.
    public async Task<string?> GetSupplierFactsAsync(int supplierId, CancellationToken ct)
    {
        // Same loader as the other tabs: the user's scope and the sealing rule are already applied. Whole history.
        ProcurementSnapshot snapshot = await intelligence.LoadSnapshotAsync(null, null, false, ct);

        // Same statistics as the Suppliers tab, so Gemini never calculates numbers itself.
        SupplierRow? stats = SupplierCalculator.BuildStats(snapshot).FirstOrDefault(row => row.SupplierId == supplierId);
        if (stats == null)
        {
            return null;
        }

        // Only non-sensitive facts: no contact details, tax numbers, sealed offers or offer prices.
        StringBuilder facts = new StringBuilder();
        facts.AppendLine("Supplier name: " + stats.Name);
        facts.AppendLine("Business category: " + stats.Category);
        facts.AppendLine("Registration status: " + stats.Status);
        facts.AppendLine("Invitations to closed bidding cycles: " + stats.Invitations);
        facts.AppendLine("Closed bidding cycles with a submitted offer: " + stats.Participations);
        facts.AppendLine("Tenders with a submitted offer: " + stats.Tenders);
        facts.AppendLine("Official awards won: " + stats.Wins);
        facts.AppendLine("Response rate: " + Percent(stats.ParticipationRate));
        facts.AppendLine("Win rate: " + Percent(stats.WinRate));
        facts.AppendLine("First recorded activity: " + Date(stats.FirstActivity));
        facts.AppendLine("Last recorded activity: " + Date(stats.LastActivity));
        return facts.ToString();
    }

    // ======================================================================
    // 2. Supplier Summary and Text Chat (one REST request per question, no conversation memory)
    // ======================================================================

    public async Task<string> AskGeminiAsync(string supplierFacts, string question, CancellationToken ct)
    {
        string userMessage = "Supplier facts:\n" + supplierFacts + "\nQuestion: " + question;

        // POST .../models/{model}:generateContent  body: { systemInstruction, contents }
        var body = new
        {
            systemInstruction = new { parts = new[] { new { text = SystemPrompt } } },
            contents = new[] { new { role = "user", parts = new[] { new { text = userMessage } } } }
        };

        using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, GeminiUrl + "/models/" + TextModel + ":generateContent");
        request.Headers.Add("x-goog-api-key", ReadApiKey());
        request.Content = JsonContent.Create(body);

        using HttpResponseMessage response = await http.SendAsync(request, ct);
        await ThrowIfFailedAsync(response, ct);

        // The answer is in candidates[0].content.parts[*].text
        using JsonDocument json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        StringBuilder answer = new StringBuilder();
        JsonElement candidates = json.RootElement.GetProperty("candidates");
        if (candidates.GetArrayLength() > 0 && candidates[0].TryGetProperty("content", out JsonElement content))
        {
            foreach (JsonElement part in content.GetProperty("parts").EnumerateArray())
            {
                if (part.TryGetProperty("text", out JsonElement text))
                {
                    answer.Append(text.GetString());
                }
            }
        }
        if (answer.Length == 0)
        {
            throw new GeminiException(502, "Gemini returned an empty answer. Please try again.");
        }
        return answer.ToString();
    }

    // ======================================================================
    // 3. Live Voice: a short-lived, single-use token
    // ======================================================================

    // The permanent API key stays on the server. The model, the prompt and the supplier facts are locked inside the token,
    // so the browser can open exactly ONE Live session with exactly this setup.
    public async Task<string> CreateLiveTokenAsync(string supplierFacts, CancellationToken ct)
    {
        DateTime now = DateTime.UtcNow;

        var body = new
        {
            uses = 1,                                    // one session only
            expireTime = now.AddMinutes(10),             // the call must end within 10 minutes
            newSessionExpireTime = now.AddMinutes(1),    // the call must start within 1 minute
            // The session setup locked into the token. Without a "fieldMask", the browser cannot change any of it.
            bidiGenerateContentSetup = new
            {
                model = "models/" + LiveModel,
                generationConfig = new { responseModalities = new[] { "AUDIO" } },
                systemInstruction = new { parts = new[] { new { text = SystemPrompt + "\nSupplier facts:\n" + supplierFacts } } }
            }
        };

        using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, GeminiUrl + "/auth_tokens");
        request.Headers.Add("x-goog-api-key", ReadApiKey());
        request.Content = JsonContent.Create(body);

        using HttpResponseMessage response = await http.SendAsync(request, ct);
        await ThrowIfFailedAsync(response, ct);

        // The token value is the "name" field of the response.
        using JsonDocument json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        return json.RootElement.GetProperty("name").GetString() ?? "";
    }

    // ======================================================================
    // 4. API key and errors
    // ======================================================================

    // The key is stored in User Secrets as Gemini:Orange-Intelligence-API. It is never sent to the browser or logged.
    private string ReadApiKey()
    {
        string? key = configuration["Gemini:Orange-Intelligence-API"];
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new GeminiException(503, "The Gemini API key is not configured on the server.");
        }
        return key;
    }

    // Turns a Gemini error response into a short message the page can show.
    private static async Task ThrowIfFailedAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }
        string details = await response.Content.ReadAsStringAsync(ct);
        int status = (int)response.StatusCode;

        string message = "Gemini could not answer right now (error " + status + "). Please try again.";
        if (status == 429)
        {
            message = "The free Gemini quota is used up for now. Please wait a minute and try again.";
        }
        else if (details.Contains("API_KEY_INVALID") || status == 401 || status == 403)
        {
            message = "The Gemini API key was rejected. Check the key in User Secrets.";
        }
        else if (status == 404)
        {
            message = "The configured Gemini model is not available.";
        }
        else if (status == 503)
        {
            message = "Gemini is busy right now (high demand). Please try again in a moment.";
        }
        throw new GeminiException(status == 429 ? 429 : 502, message);
    }

    // 0.834 -> "83%"; null -> "not available" (for example no invitation yet, so no response rate).
    private static string Percent(double? value)
    {
        return value == null ? "not available" : Math.Round(value.Value * 100) + "%";
    }

    private static string Date(DateTime? value)
    {
        return value == null ? "not recorded" : value.Value.ToString("yyyy-MM-dd");
    }
}

// An error from Gemini (or missing configuration) with a message that is safe to show to the user.
public class GeminiException : Exception
{
    public int StatusCode { get; }

    public GeminiException(int statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }
}
