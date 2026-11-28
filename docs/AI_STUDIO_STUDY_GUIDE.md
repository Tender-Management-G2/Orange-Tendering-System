# AI Studio — Study Guide (Gemini Supplier Summary · Text Chat · Live Voice)

> **Authoritative reference.** This guide and `docs/ORANGE_INTELLIGENCE_STUDY_GUIDE.md` are the two references for the final Orange Intelligence implementation. Every path, line number and code block below was generated from the **final source code**, so it matches Visual Studio. No API key, token or password appears in this document; where one would appear it is written as `<REDACTED>`.

**Contents**

1. Actual AI architecture
2. Complete important source code
3. Supplier Summary execution flow
4. Text Chat execution flow
5. Live Voice execution flow
6. Audio processing
7. AI concepts demonstrated by the project
8. AI security and privacy
9. Voice animation (architecture only)
10. AI expert questions
11. .NET expert questions
12. Debugging walkthroughs
13. Final AI revision sheet

---

## 1. Actual AI architecture

### 1.1 Facts

| Item | Actual value | Where |
|---|---|---|
| Text model (Summary, Chat) | `gemini-3.5-flash-lite` | `Orange-Tendering-System/appsettings.json` → `Gemini:TextModel`; read by `TextModel` `Orange-Tendering-System/Services/Intelligence/IntelligenceAiService.cs:41` |
| Live model (Voice) | `gemini-3.8-live` | `appsettings.json` → `Gemini:LiveModel`; read by `LiveModel` `Orange-Tendering-System/Services/Intelligence/IntelligenceAiService.cs:46` |
| API base URL | `https://generativelanguage.googleapis.com/v1beta` | `Orange-Tendering-System/Services/Intelligence/IntelligenceAiService.cs:15` |
| Text endpoint | `POST {base}/models/{TextModel}:generateContent` (REST, JSON) | `AskGeminiAsync` |
| Token endpoint | `POST {base}/auth_tokens` (REST, JSON) — ephemeral token for Live | `CreateLiveTokenAsync` |
| Live connection | **WebSocket** (Gemini Live API, bidirectional streaming) opened by the browser | `wwwroot/js/intelligence-audio.js` |
| SDK | **Server: none** (plain `HttpClient`, no NuGet package for Gemini). **Browser: `@google/genai` 2.32.0** ES module from jsDelivr, used only for the Live WebSocket session | `Orange-Tendering-System/wwwroot/js/intelligence-audio.js:5` |
| API key | User Secrets `Gemini:Orange-Intelligence-API` = `<REDACTED>`; server-side only | `Orange-Tendering-System/Services/Intelligence/IntelligenceAiService.cs:168` |
| Browser APIs | `fetch`, `navigator.mediaDevices.getUserMedia`, `AudioContext`, `ScriptProcessorNode`, `AnalyserNode`, `AudioBufferSourceNode`, `atob`/`btoa`, typed arrays | `intelligence-audio.js` |

### 1.2 Files

| File | Lines | Responsibility |
|---|---|---|
| `Orange-Tendering-System/Services/Intelligence/IntelligenceAiService.cs` | 229 | **All AI backend logic**: supplier facts, system prompt, Gemini REST call + parsing, ephemeral token, error messages |
| `Orange-Tendering-System/Controllers/IntelligenceController.cs` (AI part) | — | `SupplierSummary`, `AskSupplier`, `StartVoice`, `AnswerAsync`, `GeminiError` |
| `Orange-Tendering-System/wwwroot/js/intelligence-ai.js` | 136 | Supplier selection state, Summary, Text Chat, `chooseSupplier` (Ask AI) |
| `Orange-Tendering-System/wwwroot/js/intelligence-audio.js` | 219 | Live Voice: token request, Live connection, microphone, PCM/base64, playback, interruptions, cleanup |
| Shared: `Services/Intelligence/IntelligenceService.cs` → `LoadSnapshotAsync` | — | authorized data loading (scope + sealing) — the same loader as the other tabs |
| Shared: `Services/Intelligence/SupplierCalculator.cs` → `BuildStats` | — | supplier statistics — the same formulas as the Suppliers tab |
| Visual only: `intelligence-voice-visual.js`, `intelligence-studio-picker.js` | — | the orb animation and the supplier list (no requests, no AI logic) |

**What changed in the final simplification:** `IntelligenceAiService` moved from `Services/Radar/` to `Services/Intelligence/`; it now calls `IntelligenceService.LoadSnapshotAsync` (the removed `RadarService.SnapshotAsync` was a forwarder) and `SupplierCalculator.BuildStats` (the removed `RadarAnalytics.Suppliers` was a second copy of the same formulas). Prompts, models, endpoints, token rules and messages are unchanged. Small visible effect: the supplier name and category in the facts now go through the same clean-up as the Suppliers tab (the fictional "RADAR DEMO" prefix is removed; an empty category reads "Uncategorized").

**Why two JavaScript files remain:** `intelligence-audio.js` must be an ES **module** because it imports the Gemini SDK (`import … from 'https://cdn.jsdelivr.net/npm/@google/genai@2.32.0/+esm'`), and it holds the browser-audio code that is genuinely complex (two sample rates, PCM, scheduling). Keeping it separate also means a CDN failure can never break Summary and Chat.

### 1.3 Architecture diagram

```text
                    ┌──────────── SERVER (ASP.NET Core) ────────────┐
Summary / Chat:     │                                               │
 intelligence-ai.js │ IntelligenceController.SupplierSummary/AskSupplier
   postJson ───────▶│   → AnswerAsync                               │
                    │     → IntelligenceAiService.GetSupplierFactsAsync
                    │         → IntelligenceService.LoadSnapshotAsync (scope + sealing, EF Core)
                    │         → SupplierCalculator.BuildStats        │
                    │     → IntelligenceAiService.AskGeminiAsync ───┼──▶ Gemini REST generateContent
   { answer } ◀─────│   ← Json(new { answer })                      │◀── candidates[0].content.parts[].text
                    │                                               │
Live Voice:         │ IntelligenceController.StartVoice             │
 intelligence-audio │   → GetSupplierFactsAsync (same as above)     │
   fetch ──────────▶│   → CreateLiveTokenAsync ─────────────────────┼──▶ Gemini REST auth_tokens
   { token, model } │   ← Json(new { token, model })                │◀── { name: <REDACTED token> }
                    └───────────────────────────────────────────────┘
   GoogleGenAI({ apiKey: token }).live.connect(...)  ═══ WebSocket ═══▶ Gemini Live (gemini-3.8-live)
   microphone → 16 kHz PCM → base64 → sendRealtimeInput ═════════════▶
   speakers ◀── 24 kHz PCM ◀── base64 inlineData ◀═════════════════════  (audio never passes through our server)
```

### 1.4 Study order

1. `IntelligenceController.cs` AI actions (section 2.2).
2. `IntelligenceAiService.cs` top to bottom (2.1).
3. `SupplierCalculator.BuildStats` and `IntelligenceService.LoadSnapshotAsync` (2.4) — where the facts come from.
4. `intelligence-ai.js` (2.5).
5. `intelligence-audio.js` (2.6) with section 6 of this guide.

---

## 2. Complete important source code

### 2.1 AI backend service

**`Orange-Tendering-System/Services/Intelligence/IntelligenceAiService.cs` — lines 1–229**

```csharp
  1  // ORANGE INTELLIGENCE - AI Studio (Google Gemini API). The whole AI backend is in this one class:
  2  //   1. GetSupplierFactsAsync  authorized supplier data -> a few plain-text facts (used by all three features)
  3  //   2. AskGeminiAsync         Supplier Summary and Text Chat: one REST request to Gemini, returns its text answer
  4  //   3. CreateLiveTokenAsync   Live Voice: a single-use token; the browser then talks to Gemini Live directly
  5  //   4. Errors                 Gemini failures become short messages that are safe to show
  6  // Gemini never reads the database. We read the supplier statistics ourselves and put them in the prompt.
  7  using System.Net.Http.Json;
  8  using System.Text;
  9  using System.Text.Json;
 10  
 11  namespace Orange_Tendering_System.Services.Intelligence;
 12  
 13  public class IntelligenceAiService
 14  {
 15      private const string GeminiUrl = "https://generativelanguage.googleapis.com/v1beta";
 16  
 17      // The single system prompt used by the summary, the text chat and the voice call.
 18      public const string SystemPrompt = """
 19          You are Orange Intelligence, a procurement assistant.
 20          You help procurement employees understand the selected supplier using the supplied procurement facts.
 21          Answer only from the supplied facts.
 22          Do not invent numbers, dates, suppliers, awards, or explanations.
 23          If information is unavailable, say so clearly.
 24          Respond in the user's language (Arabic or English).
 25          Keep answers short and understandable.
 26          Write plain text without Markdown symbols.
 27          """;
 28  
 29      private readonly HttpClient http;
 30      private readonly IConfiguration configuration;
 31      private readonly IntelligenceService intelligence;
 32  
 33      public IntelligenceAiService(HttpClient http, IConfiguration configuration, IntelligenceService intelligence)
 34      {
 35          this.http = http;
 36          this.configuration = configuration;
 37          this.intelligence = intelligence;
 38      }
 39  
 40      // Model IDs come from appsettings.json ("Gemini" section), so they can change without code changes.
 41      public string TextModel
 42      {
 43          get { return configuration["Gemini:TextModel"] ?? "gemini-3.5-flash-lite"; }
 44      }
 45  
 46      public string LiveModel
 47      {
 48          get { return configuration["Gemini:LiveModel"] ?? "gemini-3.8-live"; }
 49      }
 50  
 51      // ======================================================================
 52      // 1. Supplier facts (authorization happens here)
 53      // ======================================================================
 54  
 55      // Returns the supplier facts as plain text, or null when the supplier is not in this user's authorized scope.
 56      public async Task<string?> GetSupplierFactsAsync(int supplierId, CancellationToken ct)
 57      {
 58          // Same loader as the other tabs: the user's scope and the sealing rule are already applied. Whole history.
 59          ProcurementSnapshot snapshot = await intelligence.LoadSnapshotAsync(null, null, false, ct);
 60  
 61          // Same statistics as the Suppliers tab, so Gemini never calculates numbers itself.
 62          SupplierRow? stats = SupplierCalculator.BuildStats(snapshot).FirstOrDefault(row => row.SupplierId == supplierId);
 63          if (stats == null)
 64          {
 65              return null;
 66          }
 67  
 68          // Only non-sensitive facts: no contact details, tax numbers, sealed offers or offer prices.
 69          StringBuilder facts = new StringBuilder();
 70          facts.AppendLine("Supplier name: " + stats.Name);
 71          facts.AppendLine("Business category: " + stats.Category);
 72          facts.AppendLine("Registration status: " + stats.Status);
 73          facts.AppendLine("Invitations to closed bidding cycles: " + stats.Invitations);
 74          facts.AppendLine("Closed bidding cycles with a submitted offer: " + stats.Participations);
 75          facts.AppendLine("Tenders with a submitted offer: " + stats.Tenders);
 76          facts.AppendLine("Official awards won: " + stats.Wins);
 77          facts.AppendLine("Response rate: " + Percent(stats.ParticipationRate));
 78          facts.AppendLine("Win rate: " + Percent(stats.WinRate));
 79          facts.AppendLine("First recorded activity: " + Date(stats.FirstActivity));
 80          facts.AppendLine("Last recorded activity: " + Date(stats.LastActivity));
 81          return facts.ToString();
 82      }
 83  
 84      // ======================================================================
 85      // 2. Supplier Summary and Text Chat (one REST request per question, no conversation memory)
 86      // ======================================================================
 87  
 88      public async Task<string> AskGeminiAsync(string supplierFacts, string question, CancellationToken ct)
 89      {
 90          string userMessage = "Supplier facts:\n" + supplierFacts + "\nQuestion: " + question;
 91  
 92          // POST .../models/{model}:generateContent  body: { systemInstruction, contents }
 93          var body = new
 94          {
 95              systemInstruction = new { parts = new[] { new { text = SystemPrompt } } },
 96              contents = new[] { new { role = "user", parts = new[] { new { text = userMessage } } } }
 97          };
 98  
 99          using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, GeminiUrl + "/models/" + TextModel + ":generateContent");
100          request.Headers.Add("x-goog-api-key", ReadApiKey());
101          request.Content = JsonContent.Create(body);
102  
103          using HttpResponseMessage response = await http.SendAsync(request, ct);
104          await ThrowIfFailedAsync(response, ct);
105  
106          // The answer is in candidates[0].content.parts[*].text
107          using JsonDocument json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
108          StringBuilder answer = new StringBuilder();
109          JsonElement candidates = json.RootElement.GetProperty("candidates");
110          if (candidates.GetArrayLength() > 0 && candidates[0].TryGetProperty("content", out JsonElement content))
111          {
112              foreach (JsonElement part in content.GetProperty("parts").EnumerateArray())
113              {
114                  if (part.TryGetProperty("text", out JsonElement text))
115                  {
116                      answer.Append(text.GetString());
117                  }
118              }
119          }
120          if (answer.Length == 0)
121          {
122              throw new GeminiException(502, "Gemini returned an empty answer. Please try again.");
123          }
124          return answer.ToString();
125      }
126  
127      // ======================================================================
128      // 3. Live Voice: a short-lived, single-use token
129      // ======================================================================
130  
131      // The permanent API key stays on the server. The model, the prompt and the supplier facts are locked inside the token,
132      // so the browser can open exactly ONE Live session with exactly this setup.
133      public async Task<string> CreateLiveTokenAsync(string supplierFacts, CancellationToken ct)
134      {
135          DateTime now = DateTime.UtcNow;
136  
137          var body = new
138          {
139              uses = 1,                                    // one session only
140              expireTime = now.AddMinutes(10),             // the call must end within 10 minutes
141              newSessionExpireTime = now.AddMinutes(1),    // the call must start within 1 minute
142              // The session setup locked into the token. Without a "fieldMask", the browser cannot change any of it.
143              bidiGenerateContentSetup = new
144              {
145                  model = "models/" + LiveModel,
146                  generationConfig = new { responseModalities = new[] { "AUDIO" } },
147                  systemInstruction = new { parts = new[] { new { text = SystemPrompt + "\nSupplier facts:\n" + supplierFacts } } }
148              }
149          };
150  
151          using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, GeminiUrl + "/auth_tokens");
152          request.Headers.Add("x-goog-api-key", ReadApiKey());
153          request.Content = JsonContent.Create(body);
154  
155          using HttpResponseMessage response = await http.SendAsync(request, ct);
156          await ThrowIfFailedAsync(response, ct);
157  
158          // The token value is the "name" field of the response.
159          using JsonDocument json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
160          return json.RootElement.GetProperty("name").GetString() ?? "";
161      }
162  
163      // ======================================================================
164      // 4. API key and errors
165      // ======================================================================
166  
167      // The key is stored in User Secrets as Gemini:Orange-Intelligence-API. It is never sent to the browser or logged.
168      private string ReadApiKey()
169      {
170          string? key = configuration["Gemini:Orange-Intelligence-API"];
171          if (string.IsNullOrWhiteSpace(key))
172          {
173              throw new GeminiException(503, "The Gemini API key is not configured on the server.");
174          }
175          return key;
176      }
177  
178      // Turns a Gemini error response into a short message the page can show.
179      private static async Task ThrowIfFailedAsync(HttpResponseMessage response, CancellationToken ct)
180      {
181          if (response.IsSuccessStatusCode)
182          {
183              return;
184          }
185          string details = await response.Content.ReadAsStringAsync(ct);
186          int status = (int)response.StatusCode;
187  
188          string message = "Gemini could not answer right now (error " + status + "). Please try again.";
189          if (status == 429)
190          {
191              message = "The free Gemini quota is used up for now. Please wait a minute and try again.";
192          }
193          else if (details.Contains("API_KEY_INVALID") || status == 401 || status == 403)
194          {
195              message = "The Gemini API key was rejected. Check the key in User Secrets.";
196          }
197          else if (status == 404)
198          {
199              message = "The configured Gemini model is not available.";
200          }
201          else if (status == 503)
202          {
203              message = "Gemini is busy right now (high demand). Please try again in a moment.";
204          }
205          throw new GeminiException(status == 429 ? 429 : 502, message);
206      }
207  
208      // 0.834 -> "83%"; null -> "not available" (for example no invitation yet, so no response rate).
209      private static string Percent(double? value)
210      {
211          return value == null ? "not available" : Math.Round(value.Value * 100) + "%";
212      }
213  
214      private static string Date(DateTime? value)
215      {
216          return value == null ? "not recorded" : value.Value.ToString("yyyy-MM-dd");
217      }
218  }
219  
220  // An error from Gemini (or missing configuration) with a message that is safe to show to the user.
221  public class GeminiException : Exception
222  {
223      public int StatusCode { get; }
224  
225      public GeminiException(int statusCode, string message) : base(message)
226      {
227          StatusCode = statusCode;
228      }
229  }
```

### 2.2 AI controller actions (and the class attributes that protect them)

**`Orange-Tendering-System/Controllers/IntelligenceController.cs` — lines 20–22**

```csharp
20  [Authorize(Roles = SystemRoles.SourcingOfficer)]
21  [AutoValidateAntiforgeryToken, AgentAntiforgeryFailure, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
22  public sealed class IntelligenceController : Controller
```

**`Orange-Tendering-System/Controllers/IntelligenceController.cs` — lines 39–46**

```csharp
39      // Request bodies of the AI Studio actions.
40      public sealed record SupplierRequest(int SupplierId);
41      public sealed record AskRequest(int SupplierId, string? Question);
42  
43      // A supplier account never reaches this workspace, even if it also has the Sourcing Officer role.
44      public override void OnActionExecuting(Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext context)
45      {
46          if (User.IsInRole(SystemRoles.Supplier)) context.Result = Forbid();
```

**`Orange-Tendering-System/Controllers/IntelligenceController.cs` — lines 163–233**

```csharp
163      // ---------- AI Studio (Gemini) ----------
164  
165      // A. AI Supplier Summary: facts -> Gemini -> { answer }
166      [HttpPost]
167      public async Task<IActionResult> SupplierSummary([FromBody] SupplierRequest request, CancellationToken ct)
168      {
169          string question = "Write a short summary (3 to 4 sentences) of this supplier's procurement activity.";
170          return await AnswerAsync(request.SupplierId, question, ct);
171      }
172  
173      // B. Text Chat: facts + the user's question -> Gemini -> { answer }
174      [HttpPost]
175      public async Task<IActionResult> AskSupplier([FromBody] AskRequest request, CancellationToken ct)
176      {
177          string question = (request.Question ?? "").Trim();
178          if (question.Length == 0 || question.Length > 1000)
179          {
180              return BadRequest(new { error = "Write a question of up to 1000 characters." });
181          }
182          return await AnswerAsync(request.SupplierId, question, ct);
183      }
184  
185      // C. Live Voice Call: facts -> single-use Gemini token -> { token, model }. The browser connects to Gemini with it.
186      [HttpPost]
187      public async Task<IActionResult> StartVoice([FromBody] SupplierRequest request, CancellationToken ct)
188      {
189          // Authorization: the facts are null when the supplier is outside this user's scope.
190          string? facts = await ai.GetSupplierFactsAsync(request.SupplierId, ct);
191          if (facts == null)
192          {
193              return NotFound(new { error = "This supplier is not in your authorized scope." });
194          }
195          try
196          {
197              string token = await ai.CreateLiveTokenAsync(facts, ct);
198              return Json(new { token, model = ai.LiveModel });
199          }
200          catch (Exception e) when (e is GeminiException or HttpRequestException)
201          {
202              return GeminiError(e);
203          }
204      }
205  
206      // Shared by the summary and the chat: authorize, read the facts, ask Gemini once.
207      private async Task<IActionResult> AnswerAsync(int supplierId, string question, CancellationToken ct)
208      {
209          // Authorization: the facts are null when the supplier is outside this user's scope, so nothing is sent to Gemini.
210          string? facts = await ai.GetSupplierFactsAsync(supplierId, ct);
211          if (facts == null)
212          {
213              return NotFound(new { error = "This supplier is not in your authorized scope." });
214          }
215          try
216          {
217              string answer = await ai.AskGeminiAsync(facts, question, ct);
218              return Json(new { answer });
219          }
220          catch (Exception e) when (e is GeminiException or HttpRequestException)
221          {
222              return GeminiError(e);
223          }
224      }
225  
226      private ObjectResult GeminiError(Exception e)
227      {
228          logger.LogWarning("Gemini request failed: {Message}", e.Message);
229          if (e is GeminiException gemini)
230          {
231              return StatusCode(gemini.StatusCode, new { error = gemini.Message });
232          }
233          return StatusCode(503, new { error = "Gemini could not be reached. Check the internet connection." });
```

### 2.3 Registration and configuration

**`Orange-Tendering-System/Program.cs` — lines 144–150**

```csharp
144  // ORANGE INTELLIGENCE (docs/ORANGE_INTELLIGENCE_STUDY_GUIDE.md, docs/AI_STUDIO_STUDY_GUIDE.md).
145  // IntelligenceService loads the authorized data (EF Core + memory cache) for the Overview, Suppliers and Tenders tabs;
146  // IntelligenceExportService builds their Excel/PDF files; IntelligenceAiService is the Gemini AI Studio tab.
147  // AddMemoryCache and AddHttpContextAccessor are registered above.
148  builder.Services.AddScoped<Orange_Tendering_System.Services.Intelligence.IntelligenceService>();
149  builder.Services.AddScoped<Orange_Tendering_System.Services.Intelligence.IntelligenceExportService>();
150  builder.Services.AddHttpClient<Orange_Tendering_System.Services.Intelligence.IntelligenceAiService>();
```

`AddHttpClient<IntelligenceAiService>()` registers the service as a *typed client*: ASP.NET Core creates it per request and gives it a managed `HttpClient` (connection pooling is handled for us).

**`Orange-Tendering-System/appsettings.json` — lines 53–56**

```json
53    "Gemini": {
54      "TextModel": "gemini-3.5-flash-lite",
55      "LiveModel": "gemini-3.8-live"
56    },
```

The key is **not** in `appsettings.json`; it lives in User Secrets (`dotnet user-secrets set "Gemini:Orange-Intelligence-API" "<REDACTED>"`).

### 2.4 Shared code AI Studio depends on

**Authorized data (same loader as all tabs):**

**`Orange-Tendering-System/Services/Intelligence/IntelligenceService.cs` — lines 128–146**

```csharp
128      public async Task<ProcurementSnapshot> LoadSnapshotAsync(string? from, string? to, bool refresh, CancellationToken ct)
129      {
130          DateRange period = DateRange.Parse(from, to);
131          DataScope userScope = await GetScopeAsync(ct);
132  
133          // The cache key contains the user's scope and the period, so one user's data is never shown to another scope.
134          string cacheKey = "orange-intelligence:" + userScope.Key + ":" + period.From?.ToString("O") + ":" + period.To?.ToString("O");
135          if (refresh)
136          {
137              cache.Remove(cacheKey);
138          }
139          else if (cache.TryGetValue(cacheKey, out ProcurementSnapshot? cached) && cached != null)
140          {
141              return cached;
142          }
143  
144          ProcurementSnapshot snapshot = await LoadFromDatabaseAsync(period, userScope, ct);
145          cache.Set(cacheKey, snapshot, TimeSpan.FromSeconds(CacheSeconds));
146          return snapshot;
```

**Sealing rule (offers of open cycles are never loaded with prices):**

**`Orange-Tendering-System/Services/Intelligence/IntelligenceService.cs` — lines 360–366**

```csharp
360      public static bool IsUnsealed(TenderRoundStatus status, DateTime? deadline, DateTime now)
361      {
362          if (status == TenderRoundStatus.Closed || status == TenderRoundStatus.Failed || status == TenderRoundStatus.SentToEvaluation)
363          {
364              return true;
365          }
366          return status == TenderRoundStatus.Open && deadline != null && deadline.Value <= now;
```

**Supplier statistics (the only implementation — the facts use these numbers):**

**`Orange-Tendering-System/Services/Intelligence/SupplierCalculator.cs` — lines 18–159**

```csharp
 18      public static List<SupplierRow> BuildStats(ProcurementSnapshot snapshot)
 19      {
 20          // Closed (unsealed) bidding cycles: only these count as invitations and offers.
 21          HashSet<int> closedRounds = new HashSet<int>();
 22          foreach (RoundRecord round in snapshot.Rounds)
 23          {
 24              if (round.Unsealed)
 25              {
 26                  closedRounds.Add(round.Id);
 27              }
 28          }
 29  
 30          List<AwardRecord> awards = IntelligenceService.FindAwards(snapshot);
 31          HashSet<int> awardedTenders = new HashSet<int>();
 32          foreach (AwardRecord award in awards)
 33          {
 34              awardedTenders.Add(award.TenderId);
 35          }
 36  
 37          List<PricePosition> positions = BuildPricePositions(snapshot);
 38          Dictionary<string, int> categoryColors = BuildCategoryColors(snapshot);
 39  
 40          List<SupplierRow> rows = new List<SupplierRow>();
 41          foreach (SupplierRecord supplier in snapshot.Suppliers)
 42          {
 43              SupplierRow row = new SupplierRow();
 44              row.SupplierId = supplier.Id;
 45              row.Name = IntelligenceService.CleanName(supplier.Name);
 46              row.Category = CategoryOf(supplier.Category);
 47              row.CategoryColor = CategoryColor(categoryColors, row.Category);
 48              row.Status = supplier.Status.ToString();
 49              row.IsApproved = IsApproved(supplier.Status);
 50  
 51              // Invitations to closed bidding cycles.
 52              HashSet<int> invitationIds = new HashSet<int>();
 53              foreach (InvitationRecord invitation in snapshot.Invitations)
 54              {
 55                  if (invitation.SupplierId == supplier.Id && closedRounds.Contains(invitation.RoundId))
 56                  {
 57                      invitationIds.Add(invitation.Id);
 58                  }
 59              }
 60              row.Invitations = invitationIds.Count;
 61  
 62              // Offers (snapshot.Bids already holds only the latest submitted revision of closed cycles).
 63              HashSet<int> roundsWithOffer = new HashSet<int>();
 64              HashSet<int> tendersWithOffer = new HashSet<int>();
 65              int offersAnsweringInvitations = 0;
 66              DateTime? first = null;
 67              DateTime? last = null;
 68              foreach (BidRecord bid in snapshot.Bids)
 69              {
 70                  if (bid.SupplierId != supplier.Id)
 71                  {
 72                      continue;
 73                  }
 74                  roundsWithOffer.Add(bid.RoundId);
 75                  tendersWithOffer.Add(bid.TenderId);
 76                  if (invitationIds.Contains(bid.InvitationId))
 77                  {
 78                      offersAnsweringInvitations++;
 79                  }
 80                  if (bid.Compliance == ComplianceStatus.Compliant || bid.Compliance == ComplianceStatus.NonCompliant)
 81                  {
 82                      row.ComplianceChecked++;
 83                      if (bid.Compliance == ComplianceStatus.Compliant)
 84                      {
 85                          row.Compliant++;
 86                      }
 87                  }
 88                  if (first == null || bid.AvailableAt < first)
 89                  {
 90                      first = bid.AvailableAt;
 91                  }
 92                  if (last == null || bid.AvailableAt > last)
 93                  {
 94                      last = bid.AvailableAt;
 95                  }
 96              }
 97              row.Participations = roundsWithOffer.Count;
 98              row.Tenders = tendersWithOffer.Count;
 99              row.FirstActivity = first;
100              row.LastActivity = last;
101  
102              // Response rate = offers that answer an invitation / invitations (closed cycles only).
103              if (row.Invitations > 0)
104              {
105                  row.ParticipationRate = Math.Round((double)offersAnsweringInvitations / row.Invitations, 3);
106              }
107  
108              // Win rate = official awards won / awarded tenders the supplier bid on.
109              foreach (int tenderId in tendersWithOffer)
110              {
111                  if (awardedTenders.Contains(tenderId))
112                  {
113                      row.AwardedTendersBid++;
114                  }
115              }
116              foreach (AwardRecord award in awards)
117              {
118                  if (award.SupplierId == supplier.Id)
119                  {
120                      row.Wins++;
121                      AddMoney(row.AwardedValue, award.Currency, award.Value);
122                  }
123              }
124              if (row.AwardedTendersBid > 0)
125              {
126                  row.WinRate = Math.Round((double)row.Wins / row.AwardedTendersBid, 3);
127              }
128  
129              // Relative price = average of (offer / cycle median - 1) over the supplier's comparable offers.
130              double relativeSum = 0;
131              foreach (PricePosition position in positions)
132              {
133                  if (position.SupplierId == supplier.Id)
134                  {
135                      relativeSum += position.Relative;
136                      row.PricedRounds++;
137                  }
138              }
139              if (row.PricedRounds > 0)
140              {
141                  row.RelativePrice = Math.Round(relativeSum / row.PricedRounds, 4);
142              }
143  
144              // Supplier documents (metadata only).
145              foreach (SupplierDocumentRecord document in snapshot.SupplierDocuments)
146              {
147                  if (document.SupplierId == supplier.Id)
148                  {
149                      row.Documents++;
150                      if (document.Verification == DocumentVerificationStatus.Verified)
151                      {
152                          row.VerifiedDocuments++;
153                      }
154                  }
155              }
156              rows.Add(row);
157          }
158  
159          AssignSupplierColors(rows);
```

### 2.5 `intelligence-ai.js` — selection, Summary, Chat, Ask AI

**`Orange-Tendering-System/wwwroot/js/intelligence-ai.js` — lines 1–136**

```javascript
  1  // ORANGE INTELLIGENCE - AI Studio: supplier selection, Summary and Text Chat (Views/Intelligence/Index.cshtml).
  2  // Each button sends one ordinary JSON request to IntelligenceController and shows the "answer" it returns.
  3  // The live voice call is in intelligence-audio.js.
  4  (() => {
  5    'use strict';
  6    const studio = document.querySelector('[data-studio]');
  7    if (!studio) return;
  8  
  9    const csrfToken = document.querySelector('[data-radar]').dataset.csrf;
 10    const supplierSelect = studio.querySelector('[data-studio-supplier]');
 11    const summaryButton = studio.querySelector('[data-studio-summary-button]');
 12    const summaryBox = studio.querySelector('[data-studio-summary]');
 13    const chatList = studio.querySelector('[data-studio-chat]');
 14    const askForm = studio.querySelector('[data-studio-ask-form]');
 15    const questionInput = studio.querySelector('[data-studio-question]');
 16    const errorBox = studio.querySelector('[data-studio-error]');
 17  
 18    // Sends JSON to the server and returns the JSON reply. Throws an Error with the server's message on failure.
 19    async function postJson(url, body) {
 20      const response = await fetch(url, {
 21        method: 'POST',
 22        headers: { 'Content-Type': 'application/json', RequestVerificationToken: csrfToken },
 23        body: JSON.stringify(body)
 24      });
 25      const data = await response.json().catch(() => ({}));
 26      if (!response.ok) {
 27        throw new Error(data.error || `Request failed (${response.status}).`);
 28      }
 29      return data;
 30    }
 31  
 32    function showError(message) {
 33      errorBox.textContent = message || '';
 34      errorBox.hidden = !message;
 35    }
 36  
 37    // Returns the chosen supplier ID, or shows an error and returns null.
 38    function selectedSupplierId() {
 39      const id = Number(supplierSelect.value);
 40      if (!id) {
 41        showError('Please choose a supplier first.');
 42        return null;
 43      }
 44      showError('');
 45      return id;
 46    }
 47  
 48    // The supplier list comes from the page's analytics data (already limited to the user's authorized scope).
 49    function fillSuppliers(suppliers) {
 50      const current = supplierSelect.value;
 51      supplierSelect.length = 1; // keep the "Choose a supplier…" option
 52      const sorted = suppliers.slice().sort((a, b) => a.name.localeCompare(b.name));
 53      for (const supplier of sorted) {
 54        const name = supplier.name.replace(/^RADAR DEMO /, ''); // the demo dataset's label is not part of the name
 55        supplierSelect.add(new Option(name, supplier.supplierId));
 56      }
 57      supplierSelect.value = current;
 58    }
 59  
 60    // Shows one of the three modes: summary, chat or voice.
 61    function setMode(mode) {
 62      for (const button of studio.querySelectorAll('[data-studio-mode]')) {
 63        button.setAttribute('aria-selected', String(button.dataset.studioMode === mode));
 64      }
 65      for (const panel of studio.querySelectorAll('[data-studio-panel]')) {
 66        panel.hidden = panel.dataset.studioPanel !== mode;
 67      }
 68      showError('');
 69    }
 70  
 71    // A. Supplier summary
 72    async function generateSummary() {
 73      const supplierId = selectedSupplierId();
 74      if (!supplierId) return;
 75      summaryButton.disabled = true;
 76      summaryBox.innerHTML = '<p class="ais-loading">Gemini is writing the summary…</p>';
 77      try {
 78        const data = await postJson(studio.dataset.urlSummary, { supplierId });
 79        summaryBox.textContent = data.answer;
 80      } catch (error) {
 81        summaryBox.textContent = '';
 82        showError(error.message);
 83      }
 84      summaryButton.disabled = false;
 85    }
 86  
 87    // B. Text chat: each question is sent on its own (no conversation memory).
 88    function addMessage(text, who) {
 89      const item = document.createElement('li');
 90      item.className = `ais-msg is-${who}`;
 91      item.dir = 'auto';
 92      item.textContent = text;
 93      chatList.append(item);
 94      item.scrollIntoView({ block: 'nearest' });
 95      return item;
 96    }
 97  
 98    async function askQuestion(event) {
 99      event.preventDefault();
100      const supplierId = selectedSupplierId();
101      const question = questionInput.value.trim();
102      if (!supplierId || !question) return;
103      addMessage(question, 'user');
104      questionInput.value = '';
105      const reply = addMessage('Thinking…', 'ai');
106      reply.classList.add('ais-loading');
107      try {
108        const data = await postJson(studio.dataset.urlAsk, { supplierId, question });
109        reply.textContent = data.answer;
110      } catch (error) {
111        reply.textContent = error.message;
112        reply.classList.add('is-error');
113      }
114      reply.classList.remove('ais-loading');
115    }
116  
117    studio.querySelector('.ais-modes').addEventListener('click', event => {
118      const button = event.target.closest('[data-studio-mode]');
119      if (button) setMode(button.dataset.studioMode);
120    });
121    summaryButton.addEventListener('click', generateSummary);
122    askForm.addEventListener('submit', askQuestion);
123    supplierSelect.addEventListener('change', () => showError(''));
124    window.addEventListener('radar:data', event => fillSuppliers(event.detail.suppliers));
125    if (window.OrangeRadar?.data) fillSuppliers(window.OrangeRadar.data.suppliers);
126  
127    // Used by the "Ask AI" buttons in the Supplier Universe: open AI Studio with that supplier already chosen.
128    window.OrangeIntelligenceAI = {
129      chooseSupplier(supplierId) {
130        supplierSelect.value = String(supplierId);
131        supplierSelect.dispatchEvent(new Event('change')); // the supplier list (intelligence-studio-picker.js) shows the choice
132        window.OrangeRadar?.setTab('studio');
133        setMode('summary');
134      }
135    };
136  })();
```

### 2.6 `intelligence-audio.js` — Live Voice

**`Orange-Tendering-System/wwwroot/js/intelligence-audio.js` — lines 1–219**

```javascript
  1  // ORANGE INTELLIGENCE - AI Studio: Live Voice Call with the Gemini Live API (Views/Intelligence/Index.cshtml).
  2  // 1. Ask our server for a single-use token (IntelligenceController.StartVoice). The real API key never reaches the browser.
  3  // 2. Connect the browser directly to Gemini Live with the official Google SDK.
  4  // 3. Send microphone audio (16-bit PCM, 16 kHz) and play Gemini's spoken reply (16-bit PCM, 24 kHz).
  5  import { GoogleGenAI, Modality } from 'https://cdn.jsdelivr.net/npm/@google/genai@2.32.0/+esm';
  6  
  7  const studio = document.querySelector('[data-studio]');
  8  const csrfToken = document.querySelector('[data-radar]')?.dataset.csrf;
  9  const supplierSelect = studio?.querySelector('[data-studio-supplier]');
 10  const startButton = studio?.querySelector('[data-voice-start]');
 11  const endButton = studio?.querySelector('[data-voice-end]');
 12  const voicePanel = studio?.querySelector('[data-voice-panel]');
 13  const statusText = studio?.querySelector('[data-voice-status]');
 14  
 15  let callActive = false;      // true from Start Call until the call has ended
 16  let session = null;          // the open Gemini Live connection
 17  let microphone = null;       // the microphone MediaStream
 18  let micContext = null;       // AudioContext that records at 16 kHz
 19  let speakerContext = null;   // AudioContext that plays at 24 kHz
 20  let micAnalyser = null;      // measures how loud the microphone is (for the animation only)
 21  let speakerAnalyser = null;  // measures how loud Gemini's voice is (for the animation only)
 22  let nextPlayTime = 0;        // when the next audio chunk should start, so chunks play back to back
 23  let playingSources = [];     // chunks currently scheduled, so an interruption can stop them
 24  let lastSpeechTime = 0;      // when the user was last heard; 0 = not speaking since the last answer
 25  let thinkingSince = 0;       // when the "thinking" state started
 26  
 27  // The panel's data-state picks the CSS animation: idle, connecting, listening, thinking, speaking.
 28  function setState(state, text) {
 29    voicePanel.dataset.state = state;
 30    voicePanel.dataset.outcome = ''; // visual only: endCall sets "error" after a failure (intelligence-voice-visual.js)
 31    statusText.textContent = text;
 32    startButton.hidden = state !== 'idle';
 33    endButton.hidden = state === 'idle';
 34    if (state === 'listening') lastSpeechTime = 0;
 35    if (state === 'thinking') thinkingSince = performance.now();
 36  }
 37  
 38  // ---------- animation only: audio level and the "thinking" state ----------
 39  const SPEECH_VOLUME = 0.02;   // microphone volume that counts as the user speaking
 40  let shownLevel = 0;
 41  
 42  // Returns the volume (root mean square, 0..1) of the last few milliseconds the analyser heard.
 43  function readVolume(analyser) {
 44    if (!analyser) return 0;
 45    const samples = new Float32Array(analyser.fftSize);
 46    analyser.getFloatTimeDomainData(samples);
 47    let sum = 0;
 48    for (const sample of samples) sum += sample * sample;
 49    return Math.sqrt(sum / samples.length);
 50  }
 51  
 52  // Runs about 60 times per second during a call: sets --level for the CSS and decides when Gemini is "thinking".
 53  function animateVoice() {
 54    if (!callActive) {
 55      voicePanel.style.setProperty('--level', 0);
 56      return;
 57    }
 58    const state = voicePanel.dataset.state;
 59    const now = performance.now();
 60    let volume = 0;
 61    if (state === 'speaking') volume = readVolume(speakerAnalyser);
 62    if (state === 'listening' || state === 'thinking') volume = readVolume(micAnalyser);
 63  
 64    // The user spoke and then stayed quiet for 0.7 s: Gemini is preparing its answer.
 65    if (state === 'listening' && volume > SPEECH_VOLUME) lastSpeechTime = now;
 66    if (state === 'listening' && lastSpeechTime > 0 && now - lastSpeechTime > 700) setState('thinking', 'Thinking…');
 67    // No answer after 8 s, or the user keeps talking: back to listening.
 68    if (state === 'thinking' && (now - thinkingSince > 8000 || volume > SPEECH_VOLUME * 2)) setState('listening', 'Listening… speak now');
 69  
 70    // Smooth the level so the orb moves softly instead of flickering.
 71    const target = state === 'thinking' ? 0 : Math.min(1, volume * 6);
 72    shownLevel = shownLevel * 0.75 + target * 0.25;
 73    voicePanel.style.setProperty('--level', shownLevel.toFixed(3));
 74    requestAnimationFrame(animateVoice);
 75  }
 76  
 77  async function startCall() {
 78    const supplierId = Number(supplierSelect.value);
 79    if (!supplierId) {
 80      setState('idle', 'Please choose a supplier first.');
 81      return;
 82    }
 83    callActive = true;
 84    setState('connecting', 'Connecting to Gemini…');
 85    try {
 86      // Step 1: the server checks the supplier is in our scope and returns a short-lived token.
 87      const response = await fetch(studio.dataset.urlVoice, {
 88        method: 'POST',
 89        headers: { 'Content-Type': 'application/json', RequestVerificationToken: csrfToken },
 90        body: JSON.stringify({ supplierId })
 91      });
 92      const data = await response.json().catch(() => ({}));
 93      if (!response.ok) throw new Error(data.error || `Request failed (${response.status}).`);
 94  
 95      // Step 2: open the Live session. The prompt and supplier facts are already locked inside the token.
 96      speakerContext = new AudioContext({ sampleRate: 24000 });
 97      speakerAnalyser = speakerContext.createAnalyser();
 98      speakerAnalyser.connect(speakerContext.destination);
 99      const ai = new GoogleGenAI({ apiKey: data.token, httpOptions: { apiVersion: 'v1beta' } });
100      session = await ai.live.connect({
101        model: data.model,
102        config: { responseModalities: [Modality.AUDIO] },
103        callbacks: {
104          onmessage: handleGeminiMessage,
105          onerror: () => endCall('The voice connection failed.', true),
106          onclose: event => endCall(event.reason ? `Call ended: ${event.reason}` : 'Call ended.')
107        }
108      });
109  
110      if (!callActive) return; // the connection already closed while opening
111  
112      // Step 3: start sending the microphone.
113      await startMicrophone();
114      window.OrangeVoiceVisual?.connect(micAnalyser, speakerAnalyser); // the animated orb reads the same two analysers
115      setState('listening', 'Listening… speak now');
116      requestAnimationFrame(animateVoice);
117    } catch (error) {
118      endCall(error.name === 'NotAllowedError' ? 'Microphone permission was denied.' : error.message, true);
119    }
120  }
121  
122  async function startMicrophone() {
123    microphone = await navigator.mediaDevices.getUserMedia({ audio: { channelCount: 1, echoCancellation: true, noiseSuppression: true } });
124    // A 16 kHz AudioContext lets the browser resample the microphone to the rate Gemini expects.
125    micContext = new AudioContext({ sampleRate: 16000 });
126    const source = micContext.createMediaStreamSource(microphone);
127    const recorder = micContext.createScriptProcessor(4096, 1, 1); // about 0.25 s of audio per chunk
128  
129    recorder.onaudioprocess = event => {
130      if (!session) return;
131      const samples = event.inputBuffer.getChannelData(0); // numbers between -1 and 1
132      session.sendRealtimeInput({ audio: { data: floatToPcmBase64(samples), mimeType: 'audio/pcm;rate=16000' } });
133    };
134    source.connect(recorder);
135    recorder.connect(micContext.destination); // required for onaudioprocess to run; the output is silent
136    micAnalyser = micContext.createAnalyser(); // only for the orb animation
137    source.connect(micAnalyser);
138  }
139  
140  // Gemini sends audio as base64 chunks inside serverContent.modelTurn.parts[].inlineData.
141  function handleGeminiMessage(message) {
142    const content = message.serverContent;
143    if (!content) return;
144  
145    // The user started talking while Gemini was speaking: stop the queued audio at once.
146    if (content.interrupted) {
147      stopPlayback();
148      setState('listening', 'Listening… speak now');
149      return;
150    }
151    for (const part of content.modelTurn?.parts || []) {
152      if (part.inlineData?.data) playPcmBase64(part.inlineData.data);
153    }
154  }
155  
156  function playPcmBase64(base64) {
157    const bytes = Uint8Array.from(atob(base64), c => c.charCodeAt(0));
158    const pcm = new Int16Array(bytes.buffer);
159    const buffer = speakerContext.createBuffer(1, pcm.length, 24000);
160    const channel = buffer.getChannelData(0);
161    for (let i = 0; i < pcm.length; i++) channel[i] = pcm[i] / 32768;
162  
163    const source = speakerContext.createBufferSource();
164    source.buffer = buffer;
165    source.connect(speakerAnalyser); // the analyser passes the sound on to the speakers
166    nextPlayTime = Math.max(nextPlayTime, speakerContext.currentTime);
167    source.start(nextPlayTime);
168    nextPlayTime += buffer.duration;
169  
170    playingSources.push(source);
171    setState('speaking', 'Gemini is speaking…');
172    source.onended = () => {
173      playingSources = playingSources.filter(s => s !== source);
174      if (playingSources.length === 0 && session) setState('listening', 'Listening… speak now');
175    };
176  }
177  
178  function stopPlayback() {
179    for (const source of playingSources) source.stop();
180    playingSources = [];
181    nextPlayTime = 0;
182  }
183  
184  // Converts browser audio samples (-1..1) to 16-bit little-endian PCM, then to base64 text.
185  function floatToPcmBase64(samples) {
186    const pcm = new Int16Array(samples.length);
187    for (let i = 0; i < samples.length; i++) {
188      const value = Math.max(-1, Math.min(1, samples[i]));
189      pcm[i] = value * 32767;
190    }
191    let binary = '';
192    const bytes = new Uint8Array(pcm.buffer);
193    for (let i = 0; i < bytes.length; i++) binary += String.fromCharCode(bytes[i]);
194    return btoa(binary);
195  }
196  
197  // Closes the Gemini session and releases the microphone and both audio contexts.
198  function endCall(message = 'Call ended.', failed = false) {
199    if (!callActive) return; // already ended (closing the session fires onclose again)
200    callActive = false;
201    window.OrangeVoiceVisual?.connect(null, null);
202    const openSession = session;
203    session = null;
204    openSession?.close();
205    microphone?.getTracks().forEach(track => track.stop());
206    micContext?.close();
207    speakerContext?.close();
208    microphone = micContext = speakerContext = micAnalyser = speakerAnalyser = null;
209    playingSources = [];
210    nextPlayTime = 0;
211    setState('idle', message);
212    if (failed) voicePanel.dataset.outcome = 'error';
213  }
214  
215  if (studio) {
216    startButton.addEventListener('click', startCall);
217    endButton.addEventListener('click', () => endCall());
218    window.addEventListener('pagehide', () => endCall());
219  }
```

### 2.7 Razor markup of AI Studio

**`Orange-Tendering-System/Views/Intelligence/Index.cshtml` — lines 327–355**

```cshtml
327          <div class="ais" data-studio
328               data-url-summary="@U("SupplierSummary")" data-url-ask="@U("AskSupplier")" data-url-voice="@U("StartVoice")">
329            <div class="ais-layout">
330              @* Supplier list (intelligence-studio-picker.js). The hidden <select> is still the real choice:
331                 intelligence-ai.js fills and reads it, intelligence-audio.js reads it. *@
332              <aside class="ais-picker" data-studio-picker aria-labelledby="ais-picker-title">
333                  <header class="ais-picker-head">
334                      <span class="ais-picker-icon" aria-hidden="true"><svg viewBox="0 0 24 24"><path d="M4 21V5l8-3 8 3v16M4 21h16M9 9h1M14 9h1M9 13h1M14 13h1M10 21v-4h4v4" /></svg></span>
335                      <div><h3 id="ais-picker-title">@L("Choose a supplier", "اختر مورداً")</h3><p data-picker-count aria-live="polite"></p></div>
336                  </header>
337                  <label class="ais-picker-search"><svg viewBox="0 0 24 24" aria-hidden="true"><circle cx="10" cy="10" r="6" /><path d="m15 15 5 5" /></svg>
338                      <input type="search" data-picker-search placeholder="@L("Search name or category", "ابحث بالاسم أو الفئة")" aria-label="@L("Search suppliers", "ابحث في الموردين")" aria-controls="ais-picker-list" autocomplete="off" /></label>
339                  <ul class="ais-picker-list" id="ais-picker-list" role="listbox" tabindex="0" aria-labelledby="ais-picker-title" data-picker-list></ul>
340                  <select data-studio-supplier hidden aria-hidden="true" tabindex="-1"><option value="">@L("Choose a supplier…", "اختر مورداً…")</option></select>
341              </aside>
342  
343              <div class="ais-main">
344              <div class="ais-bar">
345                  <div class="ais-current" data-picker-selected aria-live="polite"></div>
346                  <div class="ais-modes" role="tablist" aria-label="@L("AI Studio mode", "وضع الاستوديو")">
347                      <button type="button" role="tab" aria-selected="true" data-studio-mode="summary"><svg viewBox="0 0 24 24" aria-hidden="true"><path d="M7 3h7l4 4v14H7zM10 12h5M10 16h5" /></svg>@L("Summary", "ملخص")</button>
348                      <button type="button" role="tab" aria-selected="false" data-studio-mode="chat"><svg viewBox="0 0 24 24" aria-hidden="true"><path d="M4 4h16v12H9l-5 4z" /></svg>@L("Text Chat", "محادثة نصية")</button>
349                      <button type="button" role="tab" aria-selected="false" data-studio-mode="voice"><svg viewBox="0 0 24 24" aria-hidden="true"><rect x="9" y="3" width="6" height="11" rx="3" /><path d="M5 11a7 7 0 0 0 14 0M12 18v3" /></svg>@L("Live Voice", "مكالمة صوتية")</button>
350                  </div>
351              </div>
352  
353              <p class="ais-error" data-studio-error role="alert" hidden></p>
354  
355              <!-- A. Supplier summary -->
```

**`Orange-Tendering-System/Views/Intelligence/Index.cshtml` — lines 363–386**

```cshtml
363              <!-- B. Text chat -->
364              <div class="ais-panel" data-studio-panel="chat" hidden>
365                  <ol class="ais-chat" data-studio-chat aria-live="polite">
366                      <li class="ais-msg is-ai">@L("Ask anything about the selected supplier, in Arabic or English.", "اسأل أي شيء عن المورد المختار بالعربية أو الإنجليزية.")</li>
367                  </ol>
368                  <form class="ais-ask" data-studio-ask-form>
369                      <input type="text" maxlength="1000" dir="auto" data-studio-question placeholder="@L("e.g. How often does this supplier respond to invitations?", "مثال: كم مرة يستجيب هذا المورد للدعوات؟")" aria-label="@L("Your question", "سؤالك")" />
370                      <button type="submit" class="rd-btn rd-btn-primary">@L("Send", "إرسال")</button>
371                  </form>
372              </div>
373  
374              <!-- C. Live voice call -->
375              <div class="ais-panel ais-voice" data-studio-panel="voice" data-voice-panel data-state="idle" hidden>
376                  <!-- Voice orb (intelligence-voice-visual.js draws the canvas). It follows the panel's data-state
377                       (idle | connecting | listening | thinking | speaking, set by intelligence-audio.js) and the real audio analysers. -->
378                  <div class="ais-voice-stage" aria-hidden="true">
379                      <canvas class="ais-voice-canvas" data-voice-canvas></canvas>
380                      <span class="ais-voice-mic"><svg viewBox="0 0 24 24"><rect x="9" y="3" width="6" height="11" rx="3" /><path d="M5 11a7 7 0 0 0 14 0M12 18v3" /></svg></span>
381                  </div>
382                  <p class="ais-voice-status" role="status"><i aria-hidden="true"></i><span data-voice-status>@L("Ready", "جاهز")</span></p>
383                  <p class="ais-voice-activity" data-voice-activity aria-hidden="true"></p>
384                  <div class="ais-voice-actions">
385                      <button type="button" class="rd-btn rd-btn-primary" data-voice-start>@L("Start Call", "بدء المكالمة")</button>
386                      <button type="button" class="rd-btn ais-end" data-voice-end hidden>@L("End Call", "إنهاء المكالمة")</button>
```

The anti-forgery token used by both AI scripts is the page's `data-csrf` attribute (`Orange-Tendering-System/Views/Intelligence/Index.cshtml:29`).

---

## 3. Supplier Summary execution flow

| # | Step | Where | What happens | Runs on |
|---|---|---|---|---|
| 1 | Click **Generate Summary** | `[data-studio-summary-button]`; listener `Orange-Tendering-System/wwwroot/js/intelligence-ai.js:121` | calls `generateSummary` | browser |
| 2 | Read the chosen supplier | `selectedSupplierId()` `Orange-Tendering-System/wwwroot/js/intelligence-ai.js:38` | `Number(supplierSelect.value)`; none → "Please choose a supplier first." and stop | browser |
| 3 | HTTP request | `postJson(studio.dataset.urlSummary, { supplierId })` `Orange-Tendering-System/wwwroot/js/intelligence-ai.js:19` | `POST /Intelligence/SupplierSummary`, JSON body, header `RequestVerificationToken` | browser → server |
| 4 | Authorization | class attributes | role Sourcing Officer; supplier accounts blocked; anti-forgery validated | server |
| 5 | Action | `SupplierSummary` `Orange-Tendering-System/Controllers/IntelligenceController.cs:167` | fixed question: "Write a short summary (3 to 4 sentences)…"; calls `AnswerAsync` | server |
| 6 | Facts | `AnswerAsync` → `ai.GetSupplierFactsAsync(supplierId, ct)` `Orange-Tendering-System/Services/Intelligence/IntelligenceAiService.cs:56` | `LoadSnapshotAsync(null, null, false)` (whole history, user scope) → `SupplierCalculator.BuildStats` → find the row; not found → `null` → **404**, nothing is sent to Gemini | server (DB/cache + memory) |
| 7 | Prompt | `AskGeminiAsync` `Orange-Tendering-System/Services/Intelligence/IntelligenceAiService.cs:88` | `systemInstruction` = `SystemPrompt`; user content = `"Supplier facts:\n" + facts + "\nQuestion: " + question` | server |
| 8 | Gemini call | `http.SendAsync` | `POST …/models/gemini-3.5-flash-lite:generateContent`, header `x-goog-api-key: <REDACTED>` | server → Gemini |
| 9 | Errors | `ThrowIfFailedAsync` `Orange-Tendering-System/Services/Intelligence/IntelligenceAiService.cs:179` | 429 → quota message; 401/403/API_KEY_INVALID → key message; 404 → model message; 503 → busy; others → generic; thrown as `GeminiException` | server |
| 10 | Parse | `JsonDocument.ParseAsync` | concatenates `candidates[0].content.parts[*].text`; empty → `GeminiException(502)` | server |
| 11 | Response | `Json(new { answer })` | `{ "answer": "…" }`; on failure `GeminiError` → `{ "error": "…" }` with 429/502/503 | server → browser |
| 12 | Render | `summaryBox.textContent = data.answer` | `textContent` (not HTML) → no script injection from the model's text | browser |

---

## 4. Text Chat execution flow

1. The user types in `[data-studio-question]` (max 1000 characters in HTML) and submits the form → `askQuestion` `Orange-Tendering-System/wwwroot/js/intelligence-ai.js:98` (`event.preventDefault()` stops the page reload).
2. Supplier = `selectedSupplierId()`; question = trimmed text; both required.
3. `addMessage(question, 'user')` and a "Thinking…" bubble are added to the list (DOM only).
4. `postJson(urlAsk, { supplierId, question })` → `POST /Intelligence/AskSupplier`.
5. `AskSupplier` `Orange-Tendering-System/Controllers/IntelligenceController.cs:175` re-validates: empty or > 1000 characters → **400** (the browser limit is not trusted).
6. `AnswerAsync` → `GetSupplierFactsAsync` (authorization: out-of-scope → 404) → `AskGeminiAsync(facts, question)` → Gemini → `{ answer }`.
7. The "Thinking…" bubble text becomes the answer (`textContent`), or the error message with class `is-error`.

**Memory:** there is **no conversation memory**. Each request sends only the system prompt, the facts and the current question (`contents` has a single user message at `Orange-Tendering-System/Services/Intelligence/IntelligenceAiService.cs:96`). Previous chat bubbles exist only in the page. The code comment says so too: "each question is sent on its own (no conversation memory)" (`Orange-Tendering-System/wwwroot/js/intelligence-ai.js:87`).

---

## 5. Live Voice execution flow

| # | Step | File · function | Code / API | Runs on |
|---|---|---|---|---|
| 1 | Click **Start Call** | `intelligence-audio.js` · listener `Orange-Tendering-System/wwwroot/js/intelligence-audio.js:216` | → `startCall()` | browser |
| 2 | Check supplier, state "connecting" | `startCall` `Orange-Tendering-System/wwwroot/js/intelligence-audio.js:77` | `setState('connecting', …)` | browser |
| 3 | Request token | same | `fetch(studio.dataset.urlVoice, { method: 'POST', headers: { RequestVerificationToken }, body: { supplierId } })` | browser → server |
| 4 | Authorization + facts | `StartVoice` `Orange-Tendering-System/Controllers/IntelligenceController.cs:187` → `GetSupplierFactsAsync` | out of scope → 404 | server |
| 5 | Ephemeral token | `CreateLiveTokenAsync` `Orange-Tendering-System/Services/Intelligence/IntelligenceAiService.cs:133` | `POST …/auth_tokens` with `uses = 1`, `expireTime = now + 10 min`, `newSessionExpireTime = now + 1 min`, `bidiGenerateContentSetup` = { model, `responseModalities: ["AUDIO"]`, systemInstruction = prompt + facts } | server → Gemini |
| 6 | Answer | `Json(new { token, model = ai.LiveModel })` | token value = response `name` | server → browser |
| 7 | Speaker context | `startCall` | `new AudioContext({ sampleRate: 24000 })` + `AnalyserNode` → destination | browser |
| 8 | Connect | `new GoogleGenAI({ apiKey: data.token, httpOptions: { apiVersion: 'v1beta' } })` → `ai.live.connect({ model, config: { responseModalities: [Modality.AUDIO] }, callbacks })` | opens the **WebSocket**; the setup is already locked in the token | browser ↔ Gemini |
| 9 | Microphone permission | `startMicrophone` `Orange-Tendering-System/wwwroot/js/intelligence-audio.js:122` | `getUserMedia({ audio: { channelCount: 1, echoCancellation: true, noiseSuppression: true } })`; denied → "Microphone permission was denied." | browser |
| 10 | Capture + convert + send | `recorder.onaudioprocess` | 16 kHz context, 4096-sample chunks → `floatToPcmBase64` → `session.sendRealtimeInput({ audio: { data, mimeType: 'audio/pcm;rate=16000' } })` | browser → Gemini |
| 11 | Visual hook | `window.OrangeVoiceVisual?.connect(micAnalyser, speakerAnalyser)` | the orb reads the same analysers | browser |
| 12 | Gemini answers | `handleGeminiMessage` `Orange-Tendering-System/wwwroot/js/intelligence-audio.js:141` | `serverContent.modelTurn.parts[].inlineData.data` (base64 PCM 24 kHz) → `playPcmBase64` | Gemini → browser |
| 13 | Decode + play | `playPcmBase64` `Orange-Tendering-System/wwwroot/js/intelligence-audio.js:156` | base64 → bytes → `Int16Array` → `/ 32768` → `AudioBuffer` → `BufferSource.start(nextPlayTime)` | browser |
| 14 | Interruption | `content.interrupted` → `stopPlayback()` `Orange-Tendering-System/wwwroot/js/intelligence-audio.js:178` | stops every scheduled chunk; state back to listening | browser |
| 15 | End Call | `endCall()` `Orange-Tendering-System/wwwroot/js/intelligence-audio.js:198` | closes session, stops microphone tracks, closes both `AudioContext`s, clears queues; also on `pagehide`, `onerror`, `onclose` | browser |

**Server vs browser vs Gemini:** the server only authorizes, builds the facts and asks Google for the token. The microphone audio and Gemini's voice travel **directly** between the browser and Gemini; they never pass through our server and are not stored.

---

## 6. Audio processing

| Topic | Implementation | Why |
|---|---|---|
| Two `AudioContext`s | mic at **16 000 Hz**, speaker at **24 000 Hz** | Live API input is 16 kHz PCM; its audio output is 24 kHz PCM. Creating the mic context at 16 kHz lets the browser resample the microphone for us |
| Channels | `channelCount: 1` | speech needs mono; half the data of stereo |
| Chunk size | `createScriptProcessor(4096, 1, 1)` → 4096 ÷ 16000 ≈ 0.256 s per chunk | a small steady stream; the processor must be connected to `destination` for `onaudioprocess` to fire (output is silent) |
| PCM (float → int16) | `floatToPcmBase64` `Orange-Tendering-System/wwwroot/js/intelligence-audio.js:185`: clamp to −1…1, × 32767 | browsers give 32-bit floats; the protocol expects 16-bit little-endian integers. Example: 0.5 → 16383; −1 → −32767 |
| Base64 | `btoa` (send) / `atob` (receive) | the JSON messages of the Live protocol carry binary audio as text |
| Decoding | `Int16Array(bytes.buffer)`, each value ÷ 32768 → float | back to the browser's float format. Example: 16384 → 0.5 |
| Scheduling | `nextPlayTime = max(nextPlayTime, currentTime)`; `source.start(nextPlayTime)`; `nextPlayTime += duration` | chunks play back-to-back without gaps or overlap |
| Interruptions | `serverContent.interrupted` → `stopPlayback()` stops all queued sources | the user can talk over Gemini (barge-in) |
| "Thinking" state | `animateVoice`: user was louder than 0.02 RMS and then silent for 0.7 s → "Thinking…" (back to listening after 8 s or new speech) | a visual estimate only — the protocol does not send a "thinking" event |
| Cleanup | `endCall`: `session.close()`, `track.stop()` for every mic track, `micContext.close()`, `speakerContext.close()` | releases the microphone (browser indicator turns off) and audio hardware |

Required by the protocol: 16 kHz 16-bit PCM input with the `audio/pcm;rate=16000` MIME type, base64 inside messages, 24 kHz PCM output, the ephemeral token as the client key. Implementation choices: `ScriptProcessorNode` (simple, deprecated but supported; an `AudioWorklet` would be the modern alternative), 4096-sample chunks, the 0.7 s "thinking" heuristic.

---

## 7. AI concepts demonstrated by the project

| Concept | In this project |
|---|---|
| What Gemini does | generates text (Summary/Chat) or speech (Live) from our prompt and facts; speech recognition and voice synthesis happen inside Gemini Live |
| What C# does | authorization, data loading, statistics, fact text, prompt assembly, REST calls, token creation, error translation |
| What JavaScript does | UI, requests to our server, the Live WebSocket session, microphone capture, PCM/base64 conversion, playback |
| What Gemini receives | the system prompt + **11 fact lines** (name, category, status, invitations, cycles with an offer, tenders with an offer, awards, response rate, win rate, first/last activity) + the question (or the voice). No prices, no contact data, no sealed offers, no other suppliers |
| Grounding | **ordinary prompt grounding**: facts are pasted into the prompt and the system prompt says "Answer only from the supplied facts" |
| System vs user prompt | system = `SystemPrompt` (role, rules, language, plain text); user = facts + question |
| Context | everything in one request; there is no history |
| Tokens (LLM sense) | the prompt is a few hundred tokens; no `maxOutputTokens` is set — the system prompt asks for short answers |
| Inference | Google runs the model; we send one request and wait for the full answer (no streaming for text) |
| Generation configuration | Summary/Chat: none set (model defaults). Live: `responseModalities: ["AUDIO"]` |
| Hallucination | reduced, not eliminated: facts-only instruction, numbers pre-computed in C#, "say so if unavailable" |
| Prompt injection | a user could write "ignore your rules"; damage is limited because Gemini only has the facts we sent for a supplier the user is already allowed to see, and it has no tools or database access |
| Memory | none (stateless REST requests). A Live session keeps context only during that one call |
| REST vs WebSocket | REST for one question → one answer; WebSocket for continuous two-way audio |
| Streaming | Live audio is streamed both ways; text answers are not streamed |
| Ephemeral token | short-lived, single-use credential created by our server with the real key; the browser never sees the real key |
| **Not used** | RAG (no retrieval/search step), embeddings, vector database, fine-tuning, function calling / tools, agents |

---

## 8. AI security and privacy

| Question | Answer from the code |
|---|---|
| Who can use AI Studio? | Sourcing Officers only (`[Authorize(Roles = SystemRoles.SourcingOfficer)]`); supplier accounts are blocked (`OnActionExecuting`) |
| Where is the API key? | User Secrets `Gemini:Orange-Intelligence-API` = `<REDACTED>`, read only by `ReadApiKey`; never logged, never sent to the browser |
| Why must it stay on the server? | anyone who saw it could use our Google quota and billing |
| Ephemeral token restrictions | `uses = 1`; must start within **1 minute**; expires after **10 minutes**; model, audio-only output and system instruction (with the facts) are **locked** (no `fieldMask`), so the browser cannot change the prompt or model |
| How are facts authorized? | `GetSupplierFactsAsync` uses the user's scoped snapshot; a supplier outside it → `null` → 404 **before** any Gemini call (verified: `SupplierSummary` / `StartVoice` for an unknown id return 404) |
| Sealed bids | the snapshot never contains offers of open cycles; the facts contain no prices at all |
| Raw records or prepared facts? | prepared facts only (11 text lines) |
| Can Gemini reach SQL Server? | no — it has no tool, connection or credential |
| Does audio pass through our backend? | no — browser ↔ Gemini directly over the WebSocket |
| Is anything stored? | no audio, transcript or chat history is stored by our application |
| Anti-forgery | all three AI actions are POST and require `RequestVerificationToken` (missing token → JSON 400) |
| Input validation | chat question 1–1000 characters (server-side check) |
| Errors and logging | `GeminiError` logs `"Gemini request failed: {Message}"` (no key, no facts) and returns a safe message with 429/502/503 |

---

## 9. Voice animation (architecture only)

- **State source:** `setState(state, text)` in `intelligence-audio.js` (`Orange-Tendering-System/wwwroot/js/intelligence-audio.js:28`) writes `data-state` (idle / connecting / listening / thinking / speaking) on the voice panel; `endCall(…, true)` adds `data-outcome="error"`.
- **Amplitude source:** `window.OrangeVoiceVisual.connect(micAnalyser, speakerAnalyser)` after the microphone starts, `connect(null, null)` in `endCall`. The visual file reads the microphone analyser while listening/thinking and the speaker analyser while Gemini speaks.
- **Rendering:** `wwwroot/js/intelligence-voice-visual.js` (canvas particle sphere, ribbons, spectrum). It sends no request, touches no audio graph and only **reads** analysers.
- **Isolation:** if the visual file failed to load, `window.OrangeVoiceVisual?.connect` is simply skipped (`?.`); the call works the same.

---

## 10. AI expert questions

| # | Question | Short answer | Open |
|---|---|---|---|
| 1 | Why Gemini? | One provider for text and real-time voice (Live API with built-in speech in/out) through simple REST + an ephemeral-token flow. | `IntelligenceAiService.cs` |
| 2 | Which models? | Text `gemini-3.5-flash-lite`; Live `gemini-3.8-live` (both from `appsettings.json`). | `Orange-Tendering-System/Services/Intelligence/IntelligenceAiService.cs:41` |
| 3 | Text vs Live model? | Text: one request → one text answer (REST). Live: continuous audio conversation over a WebSocket. | §1.1 |
| 4 | How does Gemini know the supplier? | We compute statistics in C# and paste 11 fact lines into the prompt. | `GetSupplierFactsAsync` |
| 5 | Is this RAG? | No. There is no retrieval step, index or embeddings; it is prompt grounding with pre-computed facts. | §7 |
| 6 | Does Gemini access the database? | No. | §8 |
| 7 | How is the prompt built? | `systemInstruction` = fixed `SystemPrompt`; user text = "Supplier facts: … Question: …". | `AskGeminiAsync` |
| 8 | How do you prevent unauthorized exposure? | Scoped snapshot + 404 before Gemini; no prices or contact data in facts; sealed offers never loaded. | `GetSupplierFactsAsync` |
| 9 | Does the chat remember? | No; each question is independent. | §4 |
| 10 | What if Gemini invents information? | The prompt forbids it and asks to say "not available"; numbers come from C#; answers should be checked against the Suppliers tab. It cannot be fully prevented. | `SystemPrompt` |
| 11 | Prompt injection? | Possible in principle; impact limited: no tools, no extra data beyond the user's own authorized facts, output shown as plain text. | §7 |
| 12 | What is an ephemeral token? | A temporary, single-use credential created with our real key; it unlocks one Live session with a locked setup. | `CreateLiveTokenAsync` |
| 13 | Why is it necessary? | The browser must connect to Gemini directly for low-latency audio, but must never hold the real key. | §8 |
| 14 | Why WebSockets for voice? | Audio flows in both directions continuously; REST would need a new request per chunk. | §5 |
| 15 | Why PCM? | The Live API expects raw 16-bit PCM (16 kHz in); raw samples avoid codec latency. | `floatToPcmBase64` |
| 16 | Why base64? | Binary audio inside JSON text messages. | §6 |
| 17 | User interrupts Gemini? | Gemini sends `interrupted`; we stop all scheduled audio and listen again. | `handleGeminiMessage` |
| 18 | How does Gemini's audio reach the speaker? | base64 → Int16 → float → `AudioBuffer` → `BufferSource` → analyser → speakers, scheduled back-to-back. | `playPcmBase64` |
| 19 | Failures? | Server: `GeminiException` with safe messages and status codes; browser: `endCall(message, true)` resets everything. | `ThrowIfFailedAsync`, `endCall` |
| 20 | Limitations? | No memory, no streaming text, depends on Google availability/quota, `ScriptProcessorNode` is deprecated, the "thinking" state is a heuristic, the Live session lasts at most 10 minutes. | — |

## 11. .NET expert questions

| # | Question | Short answer | Open |
|---|---|---|---|
| 1 | Controller vs service responsibilities? | Controller: HTTP, validation, status codes. Service: data, prompt, Gemini, token. | §2.1–2.2 |
| 2 | Why `async`/`await`? | Waiting for SQL Server and Gemini without blocking a thread. | `AskGeminiAsync` |
| 3 | `CancellationToken`? | Passed from the action down to EF Core and `HttpClient`: a closed browser tab cancels the Gemini call. | all AI methods |
| 4 | How is the JSON body built? | Anonymous objects + `JsonContent.Create(body)` (System.Text.Json, camelCase). | `AskGeminiAsync` |
| 5 | How is the answer parsed? | `JsonDocument.ParseAsync` + `GetProperty("candidates")`, `TryGetProperty("text")` — no DTO for Google's large schema. | same |
| 6 | `HttpClient` lifetime? | Typed client via `AddHttpClient<IntelligenceAiService>()`; the factory manages pooling. | `Program.cs` |
| 7 | Configuration? | Model IDs in `appsettings.json`, key in User Secrets, read through `IConfiguration`. | `TextModel`, `ReadApiKey` |
| 8 | Authorization? | Class-level `[Authorize(Roles=…)]` + supplier block + data scope inside `LoadSnapshotAsync`. | controller attributes |
| 9 | DTOs / request models? | `SupplierRequest(int SupplierId)` and `AskRequest(int SupplierId, string? Question)` records bound with `[FromBody]`. | controller |
| 10 | Validation? | Question length on the server; supplier existence via the scoped snapshot. | `AskSupplier` |
| 11 | Error handling? | `ThrowIfFailedAsync` → `GeminiException(statusCode, message)`; controller catches it and `HttpRequestException` → `GeminiError`. | `GeminiError` |
| 12 | Logging? | `ILogger<IntelligenceController>.LogWarning` with the safe message only. | `GeminiError` |
| 13 | Shared statistics? | `SupplierCalculator.BuildStats` is the single formula for the Suppliers tab and the AI facts; the tests cross-check it. | `BuildStats` |
| 14 | Data-loading architecture? | `IntelligenceService.LoadSnapshotAsync` (scope + cache + EF Core) is shared by every tab and AI Studio. | §2.4 |
| 15 | What wrappers were removed? | `RadarService` (forwarder) and `RadarAnalytics` (duplicate statistics); the AI service moved into the Intelligence folder. | §1.2 |
| 16 | Why is `intelligence-audio.js` separate? | It is an ES module importing the Gemini SDK and contains the audio pipeline; Summary/Chat do not depend on the CDN. | §1.2 |

---

## 12. Debugging walkthroughs

| Scenario | Breakpoint | Inspect | Then |
|---|---|---|---|
| Supplier Summary | `Orange-Tendering-System/Controllers/IntelligenceController.cs:170` | `request.SupplierId` | F11 into `AnswerAsync` |
| Supplier facts | `Orange-Tendering-System/Services/Intelligence/IntelligenceAiService.cs:62` | `snapshot.Suppliers.Count`, `stats` | F10 and read `facts` before `return` |
| Out-of-scope supplier | same line | `stats == null` → 404 | send `supplierId: 999999` from DevTools → 404, no Gemini request |
| Gemini request | `Orange-Tendering-System/Services/Intelligence/IntelligenceAiService.cs:103` | `request.RequestUri`, `userMessage` (do **not** copy the `x-goog-api-key` header) | F10 → `response.StatusCode` |
| Response parsing | `Orange-Tendering-System/Services/Intelligence/IntelligenceAiService.cs:109` | `json.RootElement`, `answer` | — |
| Errors | `Orange-Tendering-System/Services/Intelligence/IntelligenceAiService.cs:186` | `status`, `details` | message chosen |
| Text Chat (browser) | DevTools → Sources → `intelligence-ai.js` → `askQuestion` | `supplierId`, `question`, Network → `AskSupplier` request/response | — |
| Token creation | `Orange-Tendering-System/Services/Intelligence/IntelligenceAiService.cs:151` | `body` (expire times, `uses`) | never paste the returned token anywhere |
| Live connect | `intelligence-audio.js` → `startCall` at `session = await ai.live.connect(` | `data.model` | step over; watch the WebSocket in DevTools → Network → WS |
| Audio out | `recorder.onaudioprocess` | `samples.length` (4096) | WS frames in DevTools |
| Audio in / playback | `playPcmBase64` | `pcm.length`, `nextPlayTime`, `speakerContext.currentTime` | — |
| Interruption | `if (content.interrupted)` in `handleGeminiMessage` | `playingSources.length` | — |
| End Call | `endCall` | `callActive`, `microphone` | the browser's microphone indicator turns off |

---

## 13. Final AI revision sheet

**Files:** `IntelligenceAiService.cs`, the three AI actions in `IntelligenceController.cs`, `intelligence-ai.js`, `intelligence-audio.js` (+ shared `LoadSnapshotAsync`, `BuildStats`).

**Functions:** `GetSupplierFactsAsync`, `AskGeminiAsync`, `CreateLiveTokenAsync`, `ThrowIfFailedAsync`, `AnswerAsync`; JS `generateSummary`, `askQuestion`, `chooseSupplier`, `startCall`, `startMicrophone`, `handleGeminiMessage`, `playPcmBase64`, `stopPlayback`, `endCall`.

**Shortest chains:**
- Summary/Chat: `postJson → SupplierSummary/AskSupplier → AnswerAsync → GetSupplierFactsAsync → AskGeminiAsync → { answer }`.
- Voice: `startCall → StartVoice → GetSupplierFactsAsync → CreateLiveTokenAsync → { token } → live.connect (WebSocket) → mic PCM in / PCM out → endCall`.

**AI concepts:** prompt grounding (not RAG), system vs user prompt, stateless requests, ephemeral tokens, WebSocket streaming for voice, hallucination and prompt-injection limits.

**Audio:** 16 kHz mono in, 24 kHz out, float ↔ int16 PCM, base64, back-to-back scheduling, interruption = stop queued sources, cleanup = close session, stop tracks, close contexts.

**Security:** key only on the server; token single-use, 1-minute start, 10-minute life, locked setup; facts only for authorized suppliers; no prices/contacts/sealed data; no audio through our server; nothing stored.

**Avoid saying:** "it is RAG", "the chatbot remembers", "Gemini reads our database", "the browser has the API key", "the audio goes through our server", "Gemini cannot hallucinate".

**Live demo (4 minutes):** 1) select a supplier (or Ask AI from Suppliers); 2) Generate Summary, show the Network request and `{ answer }`; 3) ask a chat question; show the 404 for an out-of-scope id; 4) Start Call (needs microphone and a valid key), speak, interrupt, End Call; 5) open `CreateLiveTokenAsync` and explain `uses`, `expireTime`, `newSessionExpireTime`.
