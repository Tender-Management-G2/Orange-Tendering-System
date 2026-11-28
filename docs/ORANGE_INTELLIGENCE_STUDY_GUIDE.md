# Orange Intelligence — Study Guide (Overview · Suppliers · Tenders)

> **Authoritative reference.** This guide and `docs/AI_STUDIO_STUDY_GUIDE.md` are the two references for the final Orange Intelligence implementation. They replace the older study notes (`INTELLIGENCE_BEGINNER_GUIDE.md`, `INTELLIGENCE_DEMO_WALKTHROUGH.md`, `INTELLIGENCE_FUNCTION_MAP.md`, `INTELLIGENCE_REFACTOR_REPORT.md`), which describe earlier stages.
>
> Every file path, line number and code block below was generated from the **final source code** by a script, so the line numbers match what you see in Visual Studio. Paths are relative to the repository root (`Orange-Tendering-System-finalfinalVersion`).

**Contents**

1. Final architecture
2. Complete important source code
3. Function reference
4. Chronological execution flows (22 features)
5. Database and EF Core
6. Calculations and business rules
7. Security
8. Practical debugging
9. Committee questions
10. Final revision sheet

---

## 1. Final architecture

### 1.1 The picture

```text
Browser (Views/Intelligence/Index.cshtml + wwwroot/js/intelligence-*.js)
   │  fetch()  GET /Intelligence/Overview | Suppliers | SupplierDetails | CompareSuppliers | Tenders | TenderDetails
   │           POST /Intelligence/Simulate | Export          (POST carries the anti-forgery header)
   ▼
IntelligenceController            (Controllers/IntelligenceController.cs)   — HTTP in, JSON/file out
   │
   ▼
IntelligenceService               (Services/Intelligence/IntelligenceService.cs)
   │   1. GetXxxAsync entry point per request
   │   2. LoadSnapshotAsync  → GetScopeAsync (who is the user?) → memory cache (60 s)
   │   3. LoadFromDatabaseAsync  → EF Core → SQL Server   (sealing rule applied here)
   │                                         returns ProcurementSnapshot (IntelligenceModels.cs, Part A)
   ▼
OverviewCalculator / SupplierCalculator / TenderCalculator   (pure C#, no database)
   │                                         returns DTOs (IntelligenceModels.cs, Part B)
   ▼
IntelligenceController → Json(dto)  → browser draws it (ECharts, tables, 3D universe)

Excel/PDF: IntelligenceController.Export → IntelligenceExportService.BuildAsync → (LoadSnapshotAsync, TenderCalculator) → ReportExport
```

### 1.2 Final files

| # | File (repository-relative) | Lines | Responsibility |
|---|---|---|---|
| 1 | `Orange-Tendering-System/Controllers/IntelligenceController.cs` | 235 | HTTP endpoints, input checks, 404/400 answers, role + anti-forgery attributes |
| 2 | `Orange-Tendering-System/Services/Intelligence/IntelligenceService.cs` | 561 | Entry point per request; user scope; cache; **every EF Core query**; sealing rule; shared rules (median, awards, shared cycles) |
| 3 | `Orange-Tendering-System/Services/Intelligence/IntelligenceModels.cs` | 669 | Part A: loaded data (`ProcurementSnapshot`, `DateRange`, `DataScope`, records). Part B: JSON DTOs |
| 4 | `Orange-Tendering-System/Services/Intelligence/OverviewCalculator.cs` | 139 | Overview KPIs, lifecycle funnel, attention items |
| 5 | `Orange-Tendering-System/Services/Intelligence/SupplierCalculator.cs` | 1286 | Supplier statistics (**the only implementation**, also used by AI Studio), filters, charts, relationships, details, comparison, what-if |
| 6 | `Orange-Tendering-System/Services/Intelligence/TenderCalculator.cs` | 531 | Tender list, lifecycle, bidding cycles, offer groups, competition, award distributions |
| 7 | `Orange-Tendering-System/Services/Intelligence/IntelligenceExportService.cs` | 297 | Excel / PDF / Word tender reports |
| 8 | `Orange-Tendering-System/Services/Intelligence/IntelligenceAiService.cs` | 229 | AI Studio (see the AI guide) |

Frontend files that talk to these endpoints: `wwwroot/js/intelligence-page.js` (shared: tabs, period, `getJson`/`postJson`), `intelligence-overview.js`, `intelligence-suppliers.js`, `intelligence-tenders.js`. Purely visual modules (no requests, no calculations): `orange-intelligence-universe.js` (3D), `intelligence-voice-visual.js`, `intelligence-studio-picker.js`.

### 1.3 What was removed or merged in the final simplification

| Before | After | Why |
|---|---|---|
| `Services/Radar/RadarService.cs` | **deleted** | `SnapshotAsync` only forwarded to `RadarDataService.LoadAsync`; `Thresholds` only forwarded configuration |
| `Services/Radar/RadarSnapshot.cs` (`RadarDataService`, `RadarSnapshot`, `RadarPeriod`, `RadarScope`, `RSupplier`…`RDocument`) | merged into `IntelligenceService.cs` (loading) and `IntelligenceModels.cs` Part A (classes, renamed) | one place for the queries; descriptive names |
| `Services/Radar/RadarOptions.cs` (`RadarOptions`, `RadarThresholds` with 9 thresholds, `RadarDemoOptions`) | **deleted**; `MinimumOffers = 3`, `MinimumSharedRounds = 2`, `CacheSeconds = 60` constants in `IntelligenceService` | only 2 thresholds were still used and none was ever overridden in configuration |
| `Services/Radar/RadarAnalytics.cs` (second supplier-statistics formula, used only by AI Studio) | **deleted**; AI Studio uses `SupplierCalculator.BuildStats` | one formula instead of two; the test project keeps the old LINQ version only as an independent cross-check |
| `Services/Intelligence/IntelligenceHelpers.cs` | shared helpers → `IntelligenceService` section 5; single-use helpers (`CategoryOf`, `AddMoney`, `IsApproved`) → `SupplierCalculator` | fewer files; helpers live next to their users |
| `Services/Radar/IntelligenceAiService.cs` | moved to `Services/Intelligence/IntelligenceAiService.cs` | the whole module now lives in one folder |
| `Program.cs`: `Configure<RadarOptions>`, `AddScoped<RadarDataService>`, `AddScoped<RadarService>`, a duplicate `AddMemoryCache` | removed | nothing uses them any more |
| Supplier e-mail, phone, address, tax and registration numbers, tender requirements/description, document file names and storage paths loaded into memory | **no longer loaded** | no calculation used them (privacy by default) |

Remaining dependencies and why they stay:

| Dependency | Why it is needed |
|---|---|
| `ApplicationDbContext` (`Orange-Tendering-System/Data/ApplicationDbContext.cs`) | the EF Core database context of the whole application |
| `IMemoryCache` | 60-second reuse of a loaded snapshot (details, comparisons and tab switches would otherwise re-run ~8 queries each) |
| `IHttpContextAccessor` | to know the signed-in user inside the service (scope) |
| `TopExecutiveRoles` (`Areas/TopCommittee/Services`) | the list of executive roles that also see the whole market |
| `ReportExport` (`Services/Reports`) | the application's shared Excel/Word/PDF writer |
| `Services/Radar/RadarDemoSeeder.cs` | development-only seeder of the fictional "RADAR DEMO" database; not part of the runtime module |

### 1.4 Study order

1. `Controllers/IntelligenceController.cs` — the doors (URLs).
2. `Services/Intelligence/IntelligenceService.cs` — sections 1–4 (constants, entry points, scope + cache, EF Core queries).
3. `Services/Intelligence/IntelligenceModels.cs` — Part A (loaded data), then skim Part B (DTOs).
4. `IntelligenceService.cs` section 5 — the shared rules (median, awards, shared cycles).
5. `OverviewCalculator.cs` (shortest calculator — learn the pattern here).
6. `SupplierCalculator.cs` — `BuildStats` first, then `Build`, `BuildDetails`, `CompareOffers`, `Simulate`.
7. `TenderCalculator.cs` — `BuildTender`, `BuildRound`, `BuildCompetition`, `BuildAwardDistributions`.
8. `IntelligenceExportService.cs`.
9. JavaScript: `intelligence-page.js` → `intelligence-suppliers.js` → `intelligence-tenders.js` → `intelligence-overview.js`.
10. `Tests/RadarChecks/Program.cs` — the checks prove each rule.

---

## 2. Complete important source code

### 2.1 Controller

**`Orange-Tendering-System/Controllers/IntelligenceController.cs` — lines 1–235**

```csharp
  1  // ORANGE TENDER MANAGEMENT SYSTEM
  2  // Module: Sourcing Officer / Orange Intelligence
  3  // One controller, two parts:
  4  //   1. Overview, Suppliers and Tenders tabs: every action loads the user's authorized data through IntelligenceService
  5  //      and returns JSON that the page scripts (wwwroot/js/intelligence-*.js) draw.
  6  //   2. AI Studio (Gemini): SupplierSummary, AskSupplier and StartVoice, used by intelligence-ai.js and intelligence-audio.js,
  7  //      served by IntelligenceAiService (Services/Intelligence/IntelligenceAiService.cs).
  8  // Everything here is read-only with respect to procurement data.
  9  using Microsoft.AspNetCore.Authorization;
 10  using Microsoft.AspNetCore.Mvc;
 11  using Orange_Tendering_System.Models;
 12  using Orange_Tendering_System.Services;
 13  using Orange_Tendering_System.Services.Intelligence;
 14  
 15  namespace Orange_Tendering_System.Controllers;
 16  
 17  // Only Sourcing Officers may open this page. Every POST must carry the anti-forgery token (RequestVerificationToken header).
 18  // AgentAntiforgeryFailure (Services/AgentAntiforgeryFailureAttribute.cs) turns a missing or wrong token into a clear JSON 400.
 19  // NoStore: the browser never caches these answers, so a user always sees current, authorized data.
 20  [Authorize(Roles = SystemRoles.SourcingOfficer)]
 21  [AutoValidateAntiforgeryToken, AgentAntiforgeryFailure, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
 22  public sealed class IntelligenceController : Controller
 23  {
 24      private readonly IntelligenceService intelligence;
 25      private readonly IntelligenceExportService exports;
 26      private readonly IntelligenceAiService ai;
 27      private readonly ILogger<IntelligenceController> logger;
 28  
 29      // ASP.NET Core creates the controller for each request and passes the registered services (Program.cs).
 30      public IntelligenceController(IntelligenceService intelligence, IntelligenceExportService exports, IntelligenceAiService ai,
 31          ILogger<IntelligenceController> logger)
 32      {
 33          this.intelligence = intelligence;
 34          this.exports = exports;
 35          this.ai = ai;
 36          this.logger = logger;
 37      }
 38  
 39      // Request bodies of the AI Studio actions.
 40      public sealed record SupplierRequest(int SupplierId);
 41      public sealed record AskRequest(int SupplierId, string? Question);
 42  
 43      // A supplier account never reaches this workspace, even if it also has the Sourcing Officer role.
 44      public override void OnActionExecuting(Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext context)
 45      {
 46          if (User.IsInRole(SystemRoles.Supplier)) context.Result = Forbid();
 47      }
 48  
 49      // GET /Intelligence -> Views/Intelligence/Index.cshtml (the page with the four tabs).
 50      [HttpGet]
 51      public IActionResult Index()
 52      {
 53          ViewData["Title"] = "Orange Intelligence";
 54          ViewData["BodyClass"] = "radar-page";
 55          return View();
 56      }
 57  
 58      // ---------- Tab 1: Overview ----------
 59  
 60      // GET /Intelligence/Overview?from=2026-01-01&to=2026-06-30&refresh=false
 61      [HttpGet]
 62      public async Task<IActionResult> Overview(string? from, string? to, bool refresh, CancellationToken ct)
 63      {
 64          OverviewData overview = await intelligence.GetOverviewAsync(from, to, refresh, ct);
 65          return Json(overview);
 66      }
 67  
 68      // ---------- Tab 2: Suppliers ----------
 69  
 70      // GET /Intelligence/Suppliers?from=&to=&department=&category=&activity=all&search=
 71      [HttpGet]
 72      public async Task<IActionResult> Suppliers(string? from, string? to, string? department, string? category, string? activity,
 73          string? search, bool refresh, CancellationToken ct)
 74      {
 75          SupplierFilters filters = new SupplierFilters();
 76          filters.Department = department ?? "";
 77          filters.Category = category ?? "";
 78          filters.Activity = activity ?? "all";
 79          filters.Search = search ?? "";
 80          SuppliersData suppliers = await intelligence.GetSuppliersAsync(from, to, filters, refresh, ct);
 81          return Json(suppliers);
 82      }
 83  
 84      // GET /Intelligence/SupplierDetails?supplierId=12&from=&to=
 85      [HttpGet]
 86      public async Task<IActionResult> SupplierDetails(int supplierId, string? from, string? to, CancellationToken ct)
 87      {
 88          SupplierDetails? details = await intelligence.GetSupplierDetailsAsync(supplierId, from, to, ct);
 89          if (details == null)
 90          {
 91              return NotFound(new { error = "This supplier is not in your authorized scope." });
 92          }
 93          return Json(details);
 94      }
 95  
 96      // GET /Intelligence/CompareSuppliers?supplierA=3&supplierB=7
 97      [HttpGet]
 98      public async Task<IActionResult> CompareSuppliers(int supplierA, int supplierB, CancellationToken ct)
 99      {
100          if (supplierA <= 0 || supplierB <= 0 || supplierA == supplierB)
101          {
102              return BadRequest(new { error = "Choose two different suppliers." });
103          }
104          List<OfferComparison>? rows = await intelligence.CompareSuppliersAsync(supplierA, supplierB, ct);
105          if (rows == null)
106          {
107              return NotFound(new { error = "A selected supplier is not in your authorized scope." });
108          }
109          return Json(rows);
110      }
111  
112      // POST /Intelligence/Simulate   body: { "from": "", "to": "", "supplierIds": [3, 7] }
113      [HttpPost]
114      public async Task<IActionResult> Simulate([FromBody] SimulateRequest request, CancellationToken ct)
115      {
116          SimulationResult? result = await intelligence.SimulateAsync(request, ct);
117          if (result == null)
118          {
119              return BadRequest(new { error = "Choose one or more existing suppliers to exclude." });
120          }
121          return Json(result);
122      }
123  
124      // ---------- Tab 3: Tenders ----------
125  
126      // GET /Intelligence/Tenders?from=&to=
127      [HttpGet]
128      public async Task<IActionResult> Tenders(string? from, string? to, bool refresh, CancellationToken ct)
129      {
130          TendersData tenders = await intelligence.GetTendersAsync(from, to, refresh, ct);
131          return Json(tenders);
132      }
133  
134      // GET /Intelligence/TenderDetails?tenderId=5&from=&to=
135      [HttpGet]
136      public async Task<IActionResult> TenderDetails(int tenderId, string? from, string? to, CancellationToken ct)
137      {
138          TenderDetails? details = await intelligence.GetTenderDetailsAsync(tenderId, from, to, ct);
139          if (details == null)
140          {
141              return NotFound(new { error = "This tender is not in your authorized scope and period." });
142          }
143          return Json(details);
144      }
145  
146      // POST /Intelligence/Export   body: { "format": "xlsx", "reportType": "tender_offers", "tenderId": 5, "roundId": 9 }
147      // Returns the file itself (Excel or PDF); the browser saves it.
148      [HttpPost]
149      public async Task<IActionResult> Export([FromBody] ExportRequest request, CancellationToken ct)
150      {
151          try
152          {
153              ExportFile file = await exports.BuildAsync(request, ct);
154              Response.Headers.CacheControl = "no-store";
155              return File(file.Bytes, file.ContentType, file.FileName);
156          }
157          catch (IntelligenceExportException e)
158          {
159              return BadRequest(new { error = e.Message });
160          }
161      }
162  
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
234      }
235  }
```

Key points: the class attributes (lines 20–21) apply to **every** action; `OnActionExecuting` (`Orange-Tendering-System/Controllers/IntelligenceController.cs:44`) blocks supplier accounts; every action is one call into a service and one `Json(...)`/`File(...)`/error answer.

### 2.2 Main service: entry points, scope, cache, EF Core queries, shared rules

**`Orange-Tendering-System/Services/Intelligence/IntelligenceService.cs` — lines 1–561**

```csharp
  1  // ORANGE INTELLIGENCE - the one service behind the Overview, Suppliers and Tenders tabs (and the data of AI Studio).
  2  // Read it top to bottom:
  3  //   1. Fixed business rules (constants)
  4  //   2. One public method per tab request: load the data, then call that tab's calculator
  5  //   3. LoadSnapshotAsync: the user's data scope, the 60-second cache and the refresh button
  6  //   4. LoadFromDatabaseAsync: every EF Core query of the module, with the sealing rule
  7  //   5. Small rules shared by the three calculators (median, awards, shared cycles, names)
  8  // The calculators (OverviewCalculator, SupplierCalculator, TenderCalculator) never touch the database:
  9  // they receive the ProcurementSnapshot built here (classes in IntelligenceModels.cs).
 10  using System.Globalization;
 11  using System.Security.Claims;
 12  using Microsoft.EntityFrameworkCore;
 13  using Microsoft.Extensions.Caching.Memory;
 14  using Orange_Tendering_System.Areas.TopCommittee.Services;
 15  using Orange_Tendering_System.Data;
 16  using Orange_Tendering_System.Models;
 17  using Orange_Tendering_System.Models.Entities;
 18  
 19  namespace Orange_Tendering_System.Services.Intelligence;
 20  
 21  public class IntelligenceService
 22  {
 23      // ======================================================================
 24      // 1. Fixed business rules
 25      // ======================================================================
 26  
 27      // A closed bidding cycle with fewer offers than this has "low competition".
 28      public const int MinimumOffers = 3;
 29      // Two suppliers are linked in a competition group when they met in at least this many closed bidding cycles.
 30      public const int MinimumSharedRounds = 2;
 31      // How long a loaded snapshot is reused for the same user and period (the refresh button skips it).
 32      private const int CacheSeconds = 60;
 33  
 34      private readonly ApplicationDbContext db;
 35      private readonly IMemoryCache cache;
 36      private readonly IHttpContextAccessor httpContextAccessor;
 37      private DataScope? scope; // the signed-in user's scope, found once per request
 38  
 39      public IntelligenceService(ApplicationDbContext db, IMemoryCache cache, IHttpContextAccessor httpContextAccessor)
 40      {
 41          this.db = db;
 42          this.cache = cache;
 43          this.httpContextAccessor = httpContextAccessor;
 44      }
 45  
 46      // ======================================================================
 47      // 2. One method per tab request (called by IntelligenceController)
 48      // ======================================================================
 49  
 50      public async Task<OverviewData> GetOverviewAsync(string? from, string? to, bool refresh, CancellationToken ct)
 51      {
 52          ProcurementSnapshot snapshot = await LoadSnapshotAsync(from, to, refresh, ct);
 53          return OverviewCalculator.Build(snapshot, MinimumOffers);
 54      }
 55  
 56      public async Task<SuppliersData> GetSuppliersAsync(string? from, string? to, SupplierFilters filters, bool refresh, CancellationToken ct)
 57      {
 58          ProcurementSnapshot snapshot = await LoadSnapshotAsync(from, to, refresh, ct);
 59          return SupplierCalculator.Build(snapshot, filters, MinimumSharedRounds, MinimumOffers);
 60      }
 61  
 62      // Returns null when the supplier is not in the user's authorized scope.
 63      public async Task<SupplierDetails?> GetSupplierDetailsAsync(int supplierId, string? from, string? to, CancellationToken ct)
 64      {
 65          ProcurementSnapshot snapshot = await LoadSnapshotAsync(from, to, false, ct);
 66          return SupplierCalculator.BuildDetails(snapshot, supplierId);
 67      }
 68  
 69      // Uses all recorded history (not the date filter), like the AI Studio supplier facts.
 70      // Returns null when either supplier is not in the user's authorized scope.
 71      public async Task<List<OfferComparison>?> CompareSuppliersAsync(int supplierA, int supplierB, CancellationToken ct)
 72      {
 73          ProcurementSnapshot snapshot = await LoadSnapshotAsync(null, null, false, ct);
 74          if (snapshot.Supplier(supplierA) == null || snapshot.Supplier(supplierB) == null)
 75          {
 76              return null;
 77          }
 78          return SupplierCalculator.CompareOffers(snapshot, supplierA, supplierB);
 79      }
 80  
 81      // Returns null when no supplier was chosen or a chosen supplier is not in the user's authorized scope.
 82      public async Task<SimulationResult?> SimulateAsync(SimulateRequest request, CancellationToken ct)
 83      {
 84          ProcurementSnapshot snapshot = await LoadSnapshotAsync(request.From, request.To, false, ct);
 85  
 86          // At most ten distinct suppliers, each of them in scope.
 87          List<int> supplierIds = new List<int>();
 88          foreach (int id in request.SupplierIds ?? Array.Empty<int>())
 89          {
 90              if (!supplierIds.Contains(id) && supplierIds.Count < 10)
 91              {
 92                  supplierIds.Add(id);
 93              }
 94          }
 95          if (supplierIds.Count == 0)
 96          {
 97              return null;
 98          }
 99          foreach (int id in supplierIds)
100          {
101              if (snapshot.Supplier(id) == null)
102              {
103                  return null;
104              }
105          }
106          return SupplierCalculator.Simulate(snapshot, supplierIds, MinimumOffers);
107      }
108  
109      public async Task<TendersData> GetTendersAsync(string? from, string? to, bool refresh, CancellationToken ct)
110      {
111          ProcurementSnapshot snapshot = await LoadSnapshotAsync(from, to, refresh, ct);
112          return TenderCalculator.Build(snapshot, MinimumOffers);
113      }
114  
115      // Returns null when the tender is not in the user's scope or has no bidding cycle in the period.
116      public async Task<TenderDetails?> GetTenderDetailsAsync(int tenderId, string? from, string? to, CancellationToken ct)
117      {
118          ProcurementSnapshot snapshot = await LoadSnapshotAsync(from, to, false, ct);
119          return TenderCalculator.BuildTender(snapshot, tenderId);
120      }
121  
122      // ======================================================================
123      // 3. Loading the authorized data (scope + cache)
124      // ======================================================================
125  
126      // The procurement records of the period that this user may analyse. Also used by IntelligenceExportService
127      // (refresh = true, so a file is never built from old data) and IntelligenceAiService (whole history).
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
147      }
148  
149      // Procurement roles, Admin and the top executives see the whole market. Anyone else sees only the tenders of their
150      // department and the tenders whose committee they sit on (and only the suppliers invited to them).
151      // The Intelligence page itself is for Sourcing Officers (market view); the rule also protects any other caller.
152      private async Task<DataScope> GetScopeAsync(CancellationToken ct)
153      {
154          if (scope != null)
155          {
156              return scope;
157          }
158          ClaimsPrincipal? user = httpContextAccessor.HttpContext?.User;
159          // No signed-in user (tests, background work) = the whole market.
160          if (user == null || user.Identity?.IsAuthenticated != true || HasMarketView(user))
161          {
162              scope = DataScope.Market;
163              return scope;
164          }
165  
166          string userId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
167          if (user.IsInRole(SystemRoles.Supplier))
168          {
169              scope = new DataScope("none:" + userId, "No access", new HashSet<int>());
170              return scope;
171          }
172  
173          int? departmentId = await db.Users.AsNoTracking()
174              .Where(u => u.Id == userId)
175              .Select(u => u.DepartmentId)
176              .FirstOrDefaultAsync(ct);
177          List<int> tenderIds = await db.Tenders.AsNoTracking()
178              .Where(t => (departmentId != null && t.PurchaseRequest.DepartmentId == departmentId) || t.CommitteeMembers.Any(m => m.UserId == userId))
179              .Select(t => t.Id)
180              .ToListAsync(ct);
181          string label = departmentId != null ? "Your department and committee tenders" : "Your committee tenders";
182          scope = new DataScope("user:" + userId, label, tenderIds.ToHashSet());
183          return scope;
184      }
185  
186      private static bool HasMarketView(ClaimsPrincipal user)
187      {
188          if (user.IsInRole(SystemRoles.Admin) || user.IsInRole(SystemRoles.SourcingOfficer) || user.IsInRole(SystemRoles.SourcingManager))
189          {
190              return true;
191          }
192          foreach (string role in TopExecutiveRoles.Allowed.Split(','))
193          {
194              if (user.IsInRole(role))
195              {
196                  return true;
197              }
198          }
199          return false;
200      }
201  
202      // ======================================================================
203      // 4. EF Core queries (read-only: AsNoTracking, projections with Select)
204      // ======================================================================
205  
206      // Sealing rule: the offers of a bidding cycle may be analysed only after bidding has closed. Offers of a cycle that is
207      // still open are counted as "sealed" and never loaded with their prices, so no chart, file or AI prompt can show them.
208      private async Task<ProcurementSnapshot> LoadFromDatabaseAsync(DateRange period, DataScope userScope, CancellationToken ct)
209      {
210          DateTime now = DateTime.UtcNow;
211          HashSet<int>? allowedTenders = userScope.TenderIds;
212  
213          // ---- Suppliers (table Suppliers). Contact details and tax numbers are not loaded: nothing here needs them. ----
214          List<SupplierRecord> suppliers = await db.Suppliers.AsNoTracking()
215              .Select(s => new SupplierRecord(s.Id, s.CompanyName, s.Category, s.Status))
216              .ToListAsync(ct);
217          if (allowedTenders != null)
218          {
219              // A scoped user sees only the suppliers invited to one of their tenders, never the whole supplier register.
220              var invitedRows = await db.TenderInvitations.AsNoTracking()
221                  .Select(i => new { i.SupplierId, i.TenderRound.TenderId })
222                  .ToListAsync(ct);
223              HashSet<int> invitedSuppliers = invitedRows.Where(i => allowedTenders.Contains(i.TenderId)).Select(i => i.SupplierId).ToHashSet();
224              suppliers = suppliers.Where(s => invitedSuppliers.Contains(s.Id)).ToList();
225          }
226          HashSet<int> supplierIds = suppliers.Select(s => s.Id).ToHashSet();
227  
228          // ---- Tenders (Tenders + PurchaseRequests + Departments) ----
229          List<TenderRecord> allTenders = await db.Tenders.AsNoTracking()
230              .Select(t => new TenderRecord(t.Id, t.Title, t.Status, t.Currency, t.EstimatedValue, t.PurchaseRequest.DepartmentId,
231                  t.PurchaseRequest.Department.Name, t.CreatedAt, t.AwardedBidId, t.AwardedAt))
232              .ToListAsync(ct);
233  
234          // ---- Bidding cycles (TenderRounds): only those in the period and in the user's scope ----
235          var roundRows = await db.TenderRounds.AsNoTracking()
236              .Select(r => new { r.Id, r.TenderId, r.RoundNumber, r.Status, r.Deadline, r.ClosedAt, r.CreatedAt })
237              .ToListAsync(ct);
238          List<RoundRecord> rounds = new List<RoundRecord>();
239          foreach (var row in roundRows)
240          {
241              RoundRecord round = new RoundRecord(row.Id, row.TenderId, row.RoundNumber, row.Status, row.Deadline, row.ClosedAt, row.CreatedAt,
242                  IsUnsealed(row.Status, row.Deadline, now));
243              bool inPeriod = period.Contains(round.ReferenceDate);
244              bool inScope = allowedTenders == null || allowedTenders.Contains(round.TenderId);
245              if (inPeriod && inScope)
246              {
247                  rounds.Add(round);
248              }
249          }
250          HashSet<int> roundIds = rounds.Select(r => r.Id).ToHashSet();
251          HashSet<int> unsealedRoundIds = rounds.Where(r => r.Unsealed).Select(r => r.Id).ToHashSet();
252          HashSet<int> tenderIds = rounds.Select(r => r.TenderId).ToHashSet();
253  
254          // ---- Invitations (TenderInvitations) of those cycles ----
255          var invitationRows = await db.TenderInvitations.AsNoTracking()
256              .Select(i => new { i.Id, i.TenderRoundId, i.TenderRound.TenderId, i.SupplierId, i.Status, i.InvitedAt })
257              .ToListAsync(ct);
258          List<InvitationRecord> invitations = invitationRows
259              .Where(i => roundIds.Contains(i.TenderRoundId))
260              .Select(i => new InvitationRecord(i.Id, i.TenderRoundId, i.TenderId, i.SupplierId, i.Status, i.InvitedAt))
261              .ToList();
262  
263          // ---- Offers (Bids): the latest submitted revision per invitation. Superseded and withdrawn versions are not offers. ----
264          var submittedBids = await db.Bids.AsNoTracking()
265              .Where(b => b.Status == BidStatus.Submitted)
266              .Select(b => new
267              {
268                  b.Id, b.TenderInvitationId, RoundId = b.TenderInvitation.TenderRoundId, b.TenderInvitation.TenderRound.TenderId,
269                  b.TenderInvitation.SupplierId, b.RevisionNumber, b.CurrencyCode, b.SubmittedAt, b.ComplianceStatus, b.DeliveryDays, b.Deviations,
270                  Extras = b.ShippingCost + b.InstallationCost + b.MaintenanceCost + b.TaxAmount + b.OtherCost
271              })
272              .ToListAsync(ct);
273          var latestBids = submittedBids
274              .Where(b => roundIds.Contains(b.RoundId))
275              .GroupBy(b => b.TenderInvitationId)
276              .Select(g => g.OrderByDescending(b => b.RevisionNumber).First())
277              .ToList();
278          int sealedBids = latestBids.Count(b => !unsealedRoundIds.Contains(b.RoundId));
279          var visibleBids = latestBids.Where(b => unsealedRoundIds.Contains(b.RoundId)).ToList();
280          List<int> visibleBidIds = visibleBids.Select(b => b.Id).ToList();
281  
282          // ---- Offer lines (BidItems + TenderItems): only for unsealed offers ----
283          var lineRows = await db.BidItems.AsNoTracking()
284              .Where(i => visibleBidIds.Contains(i.BidId))
285              .Select(i => new { i.BidId, i.TenderItemId, i.TenderItem.Name, i.TenderItem.Unit, TenderQuantity = i.TenderItem.Quantity, i.Quantity, i.UnitPrice, i.OfferedSpecifications })
286              .ToListAsync(ct);
287          Dictionary<int, List<BidLineRecord>> linesByBid = new Dictionary<int, List<BidLineRecord>>();
288          foreach (var line in lineRows)
289          {
290              if (!linesByBid.ContainsKey(line.BidId))
291              {
292                  linesByBid[line.BidId] = new List<BidLineRecord>();
293              }
294              linesByBid[line.BidId].Add(new BidLineRecord(line.TenderItemId, line.Name, line.Unit, line.TenderQuantity, line.Quantity, line.UnitPrice, line.OfferedSpecifications));
295          }
296  
297          Dictionary<int, RoundRecord> roundById = rounds.ToDictionary(r => r.Id);
298          List<BidRecord> bids = new List<BidRecord>();
299          foreach (var bid in visibleBids)
300          {
301              List<BidLineRecord> lines = linesByBid.ContainsKey(bid.Id) ? linesByBid[bid.Id] : new List<BidLineRecord>();
302              // Offer total = sum of (quantity x unit price) + shipping, installation, maintenance, tax and other costs.
303              decimal total = lines.Sum(l => l.Quantity * l.UnitPrice) + bid.Extras;
304              // An offer is "visible" from the moment its cycle closed (or later, if it was submitted later).
305              DateTime availableAt = roundById[bid.RoundId].AvailableAt;
306              if (bid.SubmittedAt > availableAt)
307              {
308                  availableAt = bid.SubmittedAt.Value;
309              }
310              decimal? recordedTotal = lines.Count > 0 ? total : null;
311              bids.Add(new BidRecord(bid.Id, bid.TenderInvitationId, bid.RoundId, bid.TenderId, bid.SupplierId, bid.RevisionNumber, bid.CurrencyCode,
312                  decimal.Round(total, 2), bid.SubmittedAt, bid.ComplianceStatus, bid.DeliveryDays, availableAt, lines, recordedTotal, bid.Deviations));
313          }
314  
315          // ---- Supplier documents (Documents): metadata only, for the "verified documents" statistic ----
316          var documentRows = await db.Documents.AsNoTracking()
317              .Where(d => d.BidId == null && d.SupplierId != null)
318              .Select(d => new { SupplierId = d.SupplierId!.Value, d.TenderId, d.VerificationStatus })
319              .ToListAsync(ct);
320          List<SupplierDocumentRecord> supplierDocuments = documentRows
321              .Where(d => d.TenderId == null ? supplierIds.Contains(d.SupplierId) : tenderIds.Contains(d.TenderId.Value))
322              .Select(d => new SupplierDocumentRecord(d.SupplierId, d.VerificationStatus))
323              .ToList();
324  
325          // ---- Winner notifications (FinalDecisionNotices): when the award decision reached the suppliers ----
326          var notices = await db.FinalDecisionNotices.AsNoTracking()
327              .Where(n => n.Outcome == FinalDecisionOutcome.Award && n.SuppliersNotifiedAt != null)
328              .Select(n => new { n.TenderId, NotifiedAt = n.SuppliersNotifiedAt!.Value })
329              .ToListAsync(ct);
330          Dictionary<int, DateTime> winnerNotifiedAt = new Dictionary<int, DateTime>();
331          foreach (var notice in notices)
332          {
333              if (!tenderIds.Contains(notice.TenderId))
334              {
335                  continue;
336              }
337              if (!winnerNotifiedAt.ContainsKey(notice.TenderId) || notice.NotifiedAt > winnerNotifiedAt[notice.TenderId])
338              {
339                  winnerNotifiedAt[notice.TenderId] = notice.NotifiedAt;
340              }
341          }
342  
343          ProcurementSnapshot snapshot = new ProcurementSnapshot();
344          snapshot.GeneratedAt = now;
345          snapshot.Period = period;
346          snapshot.Scope = userScope;
347          snapshot.Suppliers = suppliers;
348          snapshot.Tenders = allTenders.Where(t => tenderIds.Contains(t.Id)).ToList();
349          snapshot.Rounds = rounds;
350          snapshot.Invitations = invitations;
351          snapshot.Bids = bids;
352          snapshot.SupplierDocuments = supplierDocuments;
353          snapshot.SealedBids = sealedBids;
354          snapshot.WinnerNotifiedAt = winnerNotifiedAt;
355          return snapshot;
356      }
357  
358      // A cycle is "unsealed" (its offers may be analysed) when it is Closed, Failed or SentToEvaluation,
359      // or when it is Open but its deadline has passed. Draft cycles and open cycles before the deadline stay sealed.
360      public static bool IsUnsealed(TenderRoundStatus status, DateTime? deadline, DateTime now)
361      {
362          if (status == TenderRoundStatus.Closed || status == TenderRoundStatus.Failed || status == TenderRoundStatus.SentToEvaluation)
363          {
364              return true;
365          }
366          return status == TenderRoundStatus.Open && deadline != null && deadline.Value <= now;
367      }
368  
369      // ======================================================================
370      // 5. Small rules shared by the three calculators
371      // ======================================================================
372  
373      // The fictional demo dataset starts every name with "RADAR DEMO "; the page never shows that prefix.
374      public static string CleanName(string? name)
375      {
376          string text = name ?? "";
377          if (text.StartsWith("RADAR DEMO - "))
378          {
379              return text.Substring("RADAR DEMO - ".Length);
380          }
381          if (text.StartsWith("RADAR DEMO "))
382          {
383              return text.Substring("RADAR DEMO ".Length);
384          }
385          return text;
386      }
387  
388      // "Initial Bidding" for cycle 1, "Re-tendering — Cycle 2" for later cycles.
389      public static string CycleLabel(int roundNumber)
390      {
391          if (roundNumber == 1)
392          {
393              return "Initial Bidding";
394          }
395          return "Re-tendering — Cycle " + roundNumber;
396      }
397  
398      // Median = the middle value after sorting (or the average of the two middle values). 0 when the list is empty.
399      public static decimal Median(List<decimal> values)
400      {
401          if (values.Count == 0)
402          {
403              return 0;
404          }
405          List<decimal> sorted = new List<decimal>(values);
406          sorted.Sort();
407          int middle = sorted.Count / 2;
408          if (sorted.Count % 2 == 1)
409          {
410              return sorted[middle];
411          }
412          return (sorted[middle - 1] + sorted[middle]) / 2;
413      }
414  
415      // Same rule for durations in days; null (not 0) when there is nothing to measure.
416      public static double? Median(List<double> values)
417      {
418          if (values.Count == 0)
419          {
420              return null;
421          }
422          List<double> sorted = new List<double>(values);
423          sorted.Sort();
424          int middle = sorted.Count / 2;
425          if (sorted.Count % 2 == 1)
426          {
427              return sorted[middle];
428          }
429          return (sorted[middle - 1] + sorted[middle]) / 2;
430      }
431  
432      // Two offers are "structurally comparable" when they price the same tender items, units and quantities.
433      // Example key: "11:UNIT:10:10|12:UNIT:10:10" (item id : unit : tender quantity : offered quantity).
434      public static string StructureKey(BidRecord bid)
435      {
436          List<BidLineRecord> lines = bid.Lines.OrderBy(line => line.TenderItemId).ToList();
437          List<string> parts = new List<string>();
438          foreach (BidLineRecord line in lines)
439          {
440              string unit = line.Unit.Trim().ToUpperInvariant();
441              string tenderQuantity = line.TenderQuantity.ToString(CultureInfo.InvariantCulture);
442              string quantity = line.Quantity.ToString(CultureInfo.InvariantCulture);
443              parts.Add(line.TenderItemId + ":" + unit + ":" + tenderQuantity + ":" + quantity);
444          }
445          return string.Join("|", parts);
446      }
447  
448      // The context bar of every tab: scope label, period, demo marker and how many cycles were analysed.
449      public static PageContext BuildContext(ProcurementSnapshot snapshot)
450      {
451          PageContext context = new PageContext();
452          context.Scope = snapshot.Scope.Label;
453          context.IsWholeMarket = snapshot.Scope.IsMarket;
454          context.PeriodLabel = snapshot.Period.Label;
455          context.TenderCount = snapshot.Tenders.Count;
456          foreach (RoundRecord round in snapshot.Rounds)
457          {
458              if (round.Unsealed)
459              {
460                  context.AnalyzedRounds++;
461              }
462          }
463          // The "Demo dataset" marker: at least half of the suppliers belong to the fictional demo dataset.
464          int demoSuppliers = 0;
465          foreach (SupplierRecord supplier in snapshot.Suppliers)
466          {
467              if (supplier.Name.StartsWith("RADAR DEMO "))
468              {
469                  demoSuppliers++;
470              }
471          }
472          context.IsDemo = snapshot.Suppliers.Count > 0 && demoSuppliers >= snapshot.Suppliers.Count / 2.0;
473          return context;
474      }
475  
476      // Official awards: a tender whose AwardedBidId points to a visible (closed-cycle) offer. Oldest award first.
477      public static List<AwardRecord> FindAwards(ProcurementSnapshot snapshot)
478      {
479          Dictionary<int, BidRecord> bidsById = new Dictionary<int, BidRecord>();
480          foreach (BidRecord bid in snapshot.Bids)
481          {
482              bidsById[bid.Id] = bid;
483          }
484  
485          List<AwardRecord> awards = new List<AwardRecord>();
486          foreach (TenderRecord tender in snapshot.Tenders)
487          {
488              if (tender.AwardedBidId == null || !bidsById.ContainsKey(tender.AwardedBidId.Value))
489              {
490                  continue;
491              }
492              BidRecord bid = bidsById[tender.AwardedBidId.Value];
493              AwardRecord award = new AwardRecord();
494              award.TenderId = tender.Id;
495              award.TenderTitle = tender.Title;
496              award.RoundId = bid.RoundId;
497              award.BidId = bid.Id;
498              award.SupplierId = bid.SupplierId;
499              award.SupplierName = snapshot.SupplierName(bid.SupplierId);
500              award.Department = tender.Department;
501              award.Currency = bid.Currency;
502              award.Value = bid.Total;
503              award.AwardedAt = tender.AwardedAt;
504              awards.Add(award);
505          }
506          return awards.OrderBy(award => award.AwardedAt).ToList();
507      }
508  
509      // Supplier pairs that submitted offers in the same closed bidding cycle.
510      // Key "3-7" = suppliers 3 and 7 (smaller id first); value = the ids of the shared cycles.
511      public static Dictionary<string, List<int>> FindSharedRounds(ProcurementSnapshot snapshot)
512      {
513          // 1. Which suppliers submitted an offer in each closed cycle?
514          Dictionary<int, List<int>> suppliersByRound = new Dictionary<int, List<int>>();
515          List<int> roundOrder = new List<int>();
516          foreach (BidRecord bid in snapshot.Bids)
517          {
518              if (!suppliersByRound.ContainsKey(bid.RoundId))
519              {
520                  suppliersByRound[bid.RoundId] = new List<int>();
521                  roundOrder.Add(bid.RoundId);
522              }
523              if (!suppliersByRound[bid.RoundId].Contains(bid.SupplierId))
524              {
525                  suppliersByRound[bid.RoundId].Add(bid.SupplierId);
526              }
527          }
528  
529          // 2. Every pair of suppliers inside one cycle shares that cycle.
530          Dictionary<string, List<int>> pairs = new Dictionary<string, List<int>>();
531          foreach (int roundId in roundOrder)
532          {
533              List<int> suppliers = suppliersByRound[roundId];
534              suppliers.Sort();
535              for (int i = 0; i < suppliers.Count; i++)
536              {
537                  for (int j = i + 1; j < suppliers.Count; j++)
538                  {
539                      string key = suppliers[i] + "-" + suppliers[j];
540                      if (!pairs.ContainsKey(key))
541                      {
542                          pairs[key] = new List<int>();
543                      }
544                      pairs[key].Add(roundId);
545                  }
546              }
547          }
548          return pairs;
549      }
550  
551      // "3-7" -> 3 and 7
552      public static int FirstOfPair(string key)
553      {
554          return int.Parse(key.Split('-')[0]);
555      }
556  
557      public static int SecondOfPair(string key)
558      {
559          return int.Parse(key.Split('-')[1]);
560      }
561  }
```

### 2.3 Models (loaded data + DTOs)

**`Orange-Tendering-System/Services/Intelligence/IntelligenceModels.cs` — lines 1–669**

```csharp
  1  // ORANGE INTELLIGENCE - every data class of the module, in two parts:
  2  //   Part A. The procurement data loaded from the database (IntelligenceService.LoadFromDatabaseAsync).
  3  //           It stays on the server: the calculators read it, the browser never receives it.
  4  //   Part B. The answers sent to the browser (DTOs). The controller returns them with Json(...), and ASP.NET Core
  5  //           turns every property into a camelCase JSON field: "TenderCount" in C# becomes "tenderCount" in JavaScript.
  6  using Orange_Tendering_System.Models.Entities;
  7  
  8  namespace Orange_Tendering_System.Services.Intelligence;
  9  
 10  // ######################################################################
 11  // Part A. Loaded procurement data (server only)
 12  // ######################################################################
 13  
 14  // The date filter of the page. Empty or invalid dates mean "no limit"; a reversed range is swapped.
 15  public class DateRange
 16  {
 17      public DateTime? From { get; set; }
 18      public DateTime? To { get; set; }
 19  
 20      public static DateRange All
 21      {
 22          get { return new DateRange(); }
 23      }
 24  
 25      // Reads the page's "yyyy-MM-dd" date inputs.
 26      public static DateRange Parse(string? from, string? to)
 27      {
 28          DateRange range = new DateRange();
 29          range.From = ParseDate(from);
 30          range.To = ParseDate(to);
 31          if (range.From != null && range.To != null && range.From > range.To)
 32          {
 33              DateTime? earlier = range.To;
 34              range.To = range.From;
 35              range.From = earlier;
 36          }
 37          return range;
 38      }
 39  
 40      private static DateTime? ParseDate(string? value)
 41      {
 42          System.Globalization.DateTimeStyles styles = System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal;
 43          if (DateTime.TryParseExact(value, "yyyy-MM-dd", null, styles, out DateTime date))
 44          {
 45              return date.Date;
 46          }
 47          return null;
 48      }
 49  
 50      // The "To" day is included completely (until midnight).
 51      public bool Contains(DateTime value)
 52      {
 53          bool afterStart = From == null || value >= From;
 54          bool beforeEnd = To == null || value < To.Value.Date.AddDays(1);
 55          return afterStart && beforeEnd;
 56      }
 57  
 58      public string Label
 59      {
 60          get
 61          {
 62              if (From == null && To == null)
 63              {
 64                  return "All recorded history";
 65              }
 66              return From?.ToString("yyyy-MM-dd") + " to " + To?.ToString("yyyy-MM-dd");
 67          }
 68      }
 69  }
 70  
 71  // What the signed-in user may analyse. TenderIds == null means the whole market.
 72  public record DataScope(string Key, string Label, HashSet<int>? TenderIds)
 73  {
 74      public static DataScope Market { get; } = new DataScope("market", "Whole market", null);
 75      public bool IsMarket => TenderIds == null;
 76  }
 77  
 78  // One row per table, holding only the columns the calculations need (a "projection" of the entity).
 79  public record SupplierRecord(int Id, string Name, string Category, SupplierStatus Status);
 80  public record TenderRecord(int Id, string Title, TenderStatus Status, string Currency, decimal EstimatedValue, int DepartmentId, string Department,
 81      DateTime CreatedAt, int? AwardedBidId, DateTime? AwardedAt);
 82  public record RoundRecord(int Id, int TenderId, int RoundNumber, TenderRoundStatus Status, DateTime? Deadline, DateTime? ClosedAt, DateTime CreatedAt, bool Unsealed)
 83  {
 84      // When the cycle's offers became visible to the organisation.
 85      public DateTime AvailableAt => ClosedAt ?? Deadline ?? CreatedAt;
 86      // The date used by the period filter.
 87      public DateTime ReferenceDate => Deadline ?? ClosedAt ?? CreatedAt;
 88  }
 89  public record InvitationRecord(int Id, int RoundId, int TenderId, int SupplierId, InvitationStatus Status, DateTime InvitedAt);
 90  public record BidLineRecord(int TenderItemId, string Item, string Unit, decimal TenderQuantity, decimal Quantity, decimal UnitPrice,
 91      string? OfferedSpecifications = null);
 92  public record BidRecord(int Id, int InvitationId, int RoundId, int TenderId, int SupplierId, int Revision, string Currency, decimal Total,
 93      DateTime? SubmittedAt, ComplianceStatus Compliance, int? DeliveryDays, DateTime AvailableAt, List<BidLineRecord> Lines,
 94      decimal? RecordedTotal = null, string? Deviations = null);
 95  public record SupplierDocumentRecord(int SupplierId, DocumentVerificationStatus? Verification);
 96  
 97  // Everything one user may analyse for one period. Built by IntelligenceService.LoadFromDatabaseAsync.
 98  public class ProcurementSnapshot
 99  {
100      public DateTime GeneratedAt { get; set; }
101      public DateRange Period { get; set; } = DateRange.All;
102      public DataScope Scope { get; set; } = DataScope.Market;
103      public List<SupplierRecord> Suppliers { get; set; } = new List<SupplierRecord>();
104      public List<TenderRecord> Tenders { get; set; } = new List<TenderRecord>();
105      public List<RoundRecord> Rounds { get; set; } = new List<RoundRecord>();
106      public List<InvitationRecord> Invitations { get; set; } = new List<InvitationRecord>();
107      // Effective offers only: the latest submitted revision per invitation, in unsealed cycles of the period.
108      public List<BidRecord> Bids { get; set; } = new List<BidRecord>();
109      public List<SupplierDocumentRecord> SupplierDocuments { get; set; } = new List<SupplierDocumentRecord>();
110      // Offers of cycles still open for bidding: counted, never loaded with their prices.
111      public int SealedBids { get; set; }
112      // When the award decision reached the suppliers, per tender.
113      public Dictionary<int, DateTime> WinnerNotifiedAt { get; set; } = new Dictionary<int, DateTime>();
114  
115      public SupplierRecord? Supplier(int id)
116      {
117          return Suppliers.FirstOrDefault(s => s.Id == id);
118      }
119  
120      public TenderRecord? Tender(int id)
121      {
122          return Tenders.FirstOrDefault(t => t.Id == id);
123      }
124  
125      public RoundRecord? Round(int id)
126      {
127          return Rounds.FirstOrDefault(r => r.Id == id);
128      }
129  
130      public string SupplierName(int id)
131      {
132          SupplierRecord? supplier = Supplier(id);
133          return supplier != null ? supplier.Name : "Supplier #" + id;
134      }
135  
136      // The cycles whose offers may be analysed.
137      public IEnumerable<RoundRecord> AnalyzedRounds
138      {
139          get { return Rounds.Where(r => r.Unsealed); }
140      }
141  }
142  
143  // ######################################################################
144  // Part B. Answers sent to the browser (DTOs)
145  // ######################################################################
146  
147  // ======================================================================
148  // Shared by the three tabs
149  // ======================================================================
150  
151  // Facts about the analysed data that every tab shows in the context bar (scope label, demo marker, status line).
152  public class PageContext
153  {
154      public string Scope { get; set; } = "";
155      public bool IsWholeMarket { get; set; }
156      public string PeriodLabel { get; set; } = "";
157      public bool IsDemo { get; set; }
158      public int AnalyzedRounds { get; set; }
159      public int TenderCount { get; set; }
160  }
161  
162  // An amount of money in one currency. Different currencies are never added together.
163  public class MoneyValue
164  {
165      public string Currency { get; set; } = "";
166      public decimal Value { get; set; }
167      public int Count { get; set; }
168  }
169  
170  // One official award: the awarded bid of a tender.
171  public class AwardRecord
172  {
173      public int TenderId { get; set; }
174      public string TenderTitle { get; set; } = "";
175      public int RoundId { get; set; }
176      public int BidId { get; set; }
177      public int SupplierId { get; set; }
178      public string SupplierName { get; set; } = "";
179      public string Department { get; set; } = "";
180      public string Currency { get; set; } = "";
181      public decimal Value { get; set; }
182      public DateTime? AwardedAt { get; set; }
183  }
184  
185  // Two suppliers that submitted offers in the same closed bidding cycles.
186  public class SupplierPair
187  {
188      public int SupplierA { get; set; }
189      public int SupplierB { get; set; }
190      public string NameA { get; set; } = "";
191      public string NameB { get; set; } = "";
192      public int SharedRounds { get; set; }
193  }
194  
195  // ======================================================================
196  // Tab 1: Overview
197  // ======================================================================
198  
199  public class OverviewData
200  {
201      public PageContext Context { get; set; } = new PageContext();
202      public int TenderCount { get; set; }
203      public int CompletedTenders { get; set; }
204      public int InProgressTenders { get; set; }
205      public int SupplierCount { get; set; }
206      public int ParticipatingSuppliers { get; set; }
207      public int SubmittedOffers { get; set; }
208      public int SealedOffers { get; set; }
209      public double? MedianLifecycleDays { get; set; }
210      public List<LifecycleStep> Lifecycle { get; set; } = new List<LifecycleStep>();
211      public List<AttentionItem> Attention { get; set; } = new List<AttentionItem>();
212  }
213  
214  // One step of the lifecycle funnel: how many tenders reached it, and the median days from tender creation.
215  public class LifecycleStep
216  {
217      public string Key { get; set; } = "";
218      public int Reached { get; set; }
219      public double Share { get; set; }
220      public double? MedianDays { get; set; }
221  }
222  
223  // One line of "Worth your attention". Kind is "low-competition", "in-progress" or "silent-suppliers".
224  public class AttentionItem
225  {
226      public string Kind { get; set; } = "";
227      public int Count { get; set; }
228      public int MinimumOffers { get; set; }
229      public int? TenderId { get; set; }
230      public string TenderTitle { get; set; } = "";
231      public string Status { get; set; } = "";
232      public double Days { get; set; }
233      public List<string> Examples { get; set; } = new List<string>();
234  }
235  
236  // ======================================================================
237  // Tab 2: Suppliers
238  // ======================================================================
239  
240  // The filters the user chose on the Suppliers tab (sent in the query string).
241  public class SupplierFilters
242  {
243      public string Department { get; set; } = "";
244      public string Category { get; set; } = "";
245      public string Activity { get; set; } = "all";
246      public string Search { get; set; } = "";
247  }
248  
249  // Statistics of one supplier (SupplierCalculator.BuildStats). The AI Studio supplier facts use the same row.
250  public class SupplierRow
251  {
252      public int SupplierId { get; set; }
253      public string Name { get; set; } = "";
254      public string Category { get; set; } = "";
255      public int CategoryColor { get; set; } = -1;
256      public int SupplierColor { get; set; } = -1;
257      public string Status { get; set; } = "";
258      public bool IsApproved { get; set; }
259      public int Invitations { get; set; }
260      public int Participations { get; set; }
261      public int Tenders { get; set; }
262      public int AwardedTendersBid { get; set; }
263      public int Wins { get; set; }
264      public double? WinRate { get; set; }
265      public double? ParticipationRate { get; set; }
266      public List<MoneyValue> AwardedValue { get; set; } = new List<MoneyValue>();
267      public double? RelativePrice { get; set; }
268      public int PricedRounds { get; set; }
269      public int ComplianceChecked { get; set; }
270      public int Compliant { get; set; }
271      public int Documents { get; set; }
272      public int VerifiedDocuments { get; set; }
273      public DateTime? FirstActivity { get; set; }
274      public DateTime? LastActivity { get; set; }
275  }
276  
277  // One offer compared with the median offer of its bidding cycle and currency.
278  public class PricePosition
279  {
280      public int SupplierId { get; set; }
281      public string SupplierName { get; set; } = "";
282      public int RoundId { get; set; }
283      public int TenderId { get; set; }
284      public string TenderTitle { get; set; } = "";
285      public DateTime Date { get; set; }
286      public string Currency { get; set; } = "";
287      public decimal Total { get; set; }
288      public decimal Median { get; set; }
289      public double Relative { get; set; }
290      public int Comparable { get; set; }
291  }
292  
293  public class CategoryCount
294  {
295      public string Name { get; set; } = "";
296      public int Count { get; set; }
297      public int Color { get; set; } = -1;
298      public bool IsOthers { get; set; }
299  }
300  
301  public class MonthCount
302  {
303      public string Month { get; set; } = "";
304      public int Suppliers { get; set; }
305  }
306  
307  // One supplier of the drop-down lists, the 3D Supplier Universe and the AI Studio supplier list.
308  public class SupplierOption
309  {
310      public int SupplierId { get; set; }
311      public string Name { get; set; } = "";
312      public string Category { get; set; } = "";
313      public int Participations { get; set; }
314      public int CategoryColor { get; set; } = -1;
315  }
316  
317  public class LandscapeSeries
318  {
319      public string Category { get; set; } = "";
320      public int Color { get; set; } = -1;
321      public List<SupplierRow> Points { get; set; } = new List<SupplierRow>();
322  }
323  
324  public class PositionSeries
325  {
326      public int SupplierId { get; set; }
327      public string Name { get; set; } = "";
328      public int SupplierColor { get; set; } = -1;
329      public List<PricePosition> Points { get; set; } = new List<PricePosition>();
330  }
331  
332  // Suppliers connected through shared closed bidding cycles (directly or through each other).
333  public class CompetitionGroup
334  {
335      public List<string> Names { get; set; } = new List<string>();
336  }
337  
338  public class SuppliersData
339  {
340      public PageContext Context { get; set; } = new PageContext();
341      public SupplierFilters Filters { get; set; } = new SupplierFilters();
342      public List<string> Departments { get; set; } = new List<string>();
343      public List<CategoryCount> Categories { get; set; } = new List<CategoryCount>();
344  
345      // KPI tiles (for the suppliers that pass the filters)
346      public int RegisteredSuppliers { get; set; }
347      public int ApprovedSuppliers { get; set; }
348      public int ParticipatingSuppliers { get; set; }
349      public int CategoryCount { get; set; }
350      public string LargestCategory { get; set; } = "";
351      public int LargestCategoryCount { get; set; }
352      public int Invitations { get; set; }
353      public int Offers { get; set; }
354      public double? ResponseRate { get; set; }
355  
356      // Market snapshot
357      public List<CategoryCount> CategoryChart { get; set; } = new List<CategoryCount>();
358      public List<MonthCount> ParticipationTrend { get; set; } = new List<MonthCount>();
359  
360      // Supplier table
361      public List<SupplierRow> Suppliers { get; set; } = new List<SupplierRow>();
362  
363      // Chart 1: participation vs win (or response) rate
364      public bool UseWinRate { get; set; }
365      public int NotPlotted { get; set; }
366      public decimal MaxAwardedValue { get; set; }
367      public List<LandscapeSeries> Landscape { get; set; } = new List<LandscapeSeries>();
368      public string LeaderName { get; set; } = "";
369  
370      // Chart 2: price position over time
371      public List<PositionSeries> Positions { get; set; } = new List<PositionSeries>();
372      public List<PricePosition> PositionRows { get; set; } = new List<PricePosition>();
373      public double? AverageRelative { get; set; }
374      public double? RelativeSpread { get; set; }
375      public string LowestAverageName { get; set; } = "";
376      public double? LowestAverage { get; set; }
377      public int ShownOffers { get; set; }
378  
379      // Market relationships
380      public int CompetitionMinimum { get; set; }
381      public int PairCount { get; set; }
382      public List<SupplierPair> Pairs { get; set; } = new List<SupplierPair>();
383      public List<CompetitionGroup> Groups { get; set; } = new List<CompetitionGroup>();
384      public int UnconnectedSuppliers { get; set; }
385  
386      // Drop-down lists (all suppliers in scope, not only the filtered ones)
387      public List<SupplierOption> AllSuppliers { get; set; } = new List<SupplierOption>();
388      public List<SupplierOption> ScenarioSuppliers { get; set; } = new List<SupplierOption>();
389      public int MinimumOffers { get; set; }
390  }
391  
392  // Everything the supplier details panel shows for one supplier.
393  public class SupplierDetails
394  {
395      public SupplierRow Supplier { get; set; } = new SupplierRow();
396      public List<SupplierRound> Rounds { get; set; } = new List<SupplierRound>();
397      public int MaxOffers { get; set; }
398      public List<PricePosition> Prices { get; set; } = new List<PricePosition>();
399      public List<SupplierPair> Competitors { get; set; } = new List<SupplierPair>();
400  }
401  
402  // A closed bidding cycle in which the supplier submitted an offer.
403  public class SupplierRound
404  {
405      public int RoundId { get; set; }
406      public int TenderId { get; set; }
407      public string Tender { get; set; } = "";
408      public int RoundNumber { get; set; }
409      public DateTime Date { get; set; }
410      public string Department { get; set; } = "";
411      public int Offers { get; set; }
412      public bool Awarded { get; set; }
413  }
414  
415  // ---------- supplier offer comparison ----------
416  
417  public class ComparedOffer
418  {
419      public int BidId { get; set; }
420      public string Supplier { get; set; } = "";
421      public string? Currency { get; set; }
422      public decimal? Total { get; set; }
423      public DateTimeOffset? SubmittedAt { get; set; }
424      public decimal? SecondsBeforeDeadline { get; set; }
425      public string? RecordedTime { get; set; }
426  }
427  
428  public class OfferComparison
429  {
430      public int TenderId { get; set; }
431      public string Title { get; set; } = "";
432      public int RoundId { get; set; }
433      public int RoundNumber { get; set; }
434      public ComparedOffer A { get; set; } = new ComparedOffer();
435      public ComparedOffer B { get; set; } = new ComparedOffer();
436      public DateTimeOffset? Deadline { get; set; }
437      public string? PriceIssue { get; set; }
438      public decimal? Difference { get; set; }
439      public decimal? RelativeDifference { get; set; }
440      public string? LowerOffer { get; set; }
441      public decimal? SecondsApart { get; set; }
442      public string? FirstSubmission { get; set; }
443  }
444  
445  // ---------- what-if scenario ----------
446  
447  public class SimulateRequest
448  {
449      public string? From { get; set; }
450      public string? To { get; set; }
451      public int[]? SupplierIds { get; set; }
452  }
453  
454  public class SimulatedRound
455  {
456      public int RoundId { get; set; }
457      public string Tender { get; set; } = "";
458      public int RoundNumber { get; set; }
459      public int Before { get; set; }
460      public int After { get; set; }
461      public bool DropsBelowMinimum { get; set; }
462      public bool LosesAllBidders { get; set; }
463      public bool LosesAward { get; set; }
464  }
465  
466  public class SupplierAlternative
467  {
468      public int SupplierId { get; set; }
469      public string Name { get; set; } = "";
470      public string Category { get; set; } = "";
471      public int Participations { get; set; }
472      public double? ParticipationRate { get; set; }
473  }
474  
475  public class SimulationResult
476  {
477      public List<int> Excluded { get; set; } = new List<int>();
478      public List<string> ExcludedNames { get; set; } = new List<string>();
479      public int RoundsAffected { get; set; }
480      public int RoundsBelowMinimumBefore { get; set; }
481      public int RoundsBelowMinimumAfter { get; set; }
482      public int RoundsWithoutBidders { get; set; }
483      public double BidShareRemoved { get; set; }
484      public double? HhiBefore { get; set; }
485      public double? HhiAfter { get; set; }
486      public List<MoneyValue> AwardExposure { get; set; } = new List<MoneyValue>();
487      public List<SimulatedRound> Rounds { get; set; } = new List<SimulatedRound>();
488      public List<SupplierAlternative> Alternatives { get; set; } = new List<SupplierAlternative>();
489      public int MinimumOffers { get; set; }
490      public string Notice { get; set; } = "";
491  }
492  
493  // ======================================================================
494  // Tab 3: Tenders
495  // ======================================================================
496  
497  public class TendersData
498  {
499      public PageContext Context { get; set; } = new PageContext();
500      public List<TenderListItem> Tenders { get; set; } = new List<TenderListItem>();
501      public int? DefaultTenderId { get; set; }
502      public int MinimumOffers { get; set; }
503      public List<RoundCompetition> Competition { get; set; } = new List<RoundCompetition>();
504      public List<DepartmentCompetition> Departments { get; set; } = new List<DepartmentCompetition>();
505      public List<SupplierPair> RecurringPairs { get; set; } = new List<SupplierPair>();
506      public List<AwardDistribution> Awards { get; set; } = new List<AwardDistribution>();
507  }
508  
509  public class TenderListItem
510  {
511      public int TenderId { get; set; }
512      public string Title { get; set; } = "";
513      public string Department { get; set; } = "";
514      public DateTime CreatedAt { get; set; }
515      public string State { get; set; } = "";
516      public string Status { get; set; } = "";
517      public double? DaysToNotification { get; set; }
518  }
519  
520  // One closed bidding cycle: how many suppliers were invited and how many submitted an offer.
521  public class RoundCompetition
522  {
523      public int RoundId { get; set; }
524      public int TenderId { get; set; }
525      public string Tender { get; set; } = "";
526      public int RoundNumber { get; set; }
527      public DateTime Date { get; set; }
528      public string Department { get; set; } = "";
529      public int Invited { get; set; }
530      public int Submitted { get; set; }
531      public int Compliant { get; set; }
532      public bool LowCompetition { get; set; }
533      public List<string> Bidders { get; set; } = new List<string>();
534  }
535  
536  public class DepartmentCompetition
537  {
538      public string Department { get; set; } = "";
539      public int Rounds { get; set; }
540      public int Bids { get; set; }
541      public double AverageBids { get; set; }
542  }
543  
544  public class AwardShare
545  {
546      public string Name { get; set; } = "";
547      public int? SupplierId { get; set; }
548      public int SupplierColor { get; set; } = -1;
549      public decimal Value { get; set; }
550      public double Share { get; set; }
551      public int Count { get; set; }
552  }
553  
554  // Official award value in ONE currency, split by supplier and by department.
555  public class AwardDistribution
556  {
557      public string Currency { get; set; } = "";
558      public decimal Total { get; set; }
559      public int Count { get; set; }
560      public double? Hhi { get; set; }
561      public List<AwardShare> BySupplier { get; set; } = new List<AwardShare>();
562      public List<AwardShare> ByDepartment { get; set; } = new List<AwardShare>();
563  }
564  
565  // Everything the Tenders tab shows for one selected tender.
566  public class TenderDetails
567  {
568      public int TenderId { get; set; }
569      public string Title { get; set; } = "";
570      public string Department { get; set; } = "";
571      public string Status { get; set; } = "";
572      public string State { get; set; } = "";
573      public string Currency { get; set; } = "";
574      public decimal EstimatedValue { get; set; }
575      public DateTime CreatedAt { get; set; }
576      public DateTime? AwardedAt { get; set; }
577      public DateTime? WinnerNotifiedAt { get; set; }
578      public double? DaysToNotification { get; set; }
579      public double? DaysToAward { get; set; }
580      public double DaysSoFar { get; set; }
581      public int? AwardedSupplierId { get; set; }
582      public string? AwardedSupplier { get; set; }
583      public decimal? AwardedValue { get; set; }
584      public string? AwardCurrency { get; set; }
585      public int TotalInvitations { get; set; }
586      public List<TenderStage> Stages { get; set; } = new List<TenderStage>();
587      public List<TenderRound> Rounds { get; set; } = new List<TenderRound>();
588  }
589  
590  // One lifecycle milestone. At is null when the milestone is not recorded (it is never estimated).
591  public class TenderStage
592  {
593      public string Key { get; set; } = "";
594      public string Label { get; set; } = "";
595      public DateTime? At { get; set; }
596      public string Source { get; set; } = "";
597      public double? DaysFromCreation { get; set; }
598  }
599  
600  // One bidding cycle of the tender. Bids stay empty while the cycle is sealed (bidding still open).
601  public class TenderRound
602  {
603      public int RoundId { get; set; }
604      public int RoundNumber { get; set; }
605      public string Status { get; set; } = "";
606      public DateTime? Deadline { get; set; }
607      public DateTime? ClosedAt { get; set; }
608      public bool Analysed { get; set; }
609      public int Invited { get; set; }
610      public int Offers { get; set; }
611      public List<TenderOffer> Bids { get; set; } = new List<TenderOffer>();
612      public List<OfferGroup> Groups { get; set; } = new List<OfferGroup>();
613  }
614  
615  public class TenderOffer
616  {
617      public int BidId { get; set; }
618      public int SupplierId { get; set; }
619      public string Supplier { get; set; } = "";
620      public string Currency { get; set; } = "";
621      public decimal Total { get; set; }
622      public int? ComparisonGroup { get; set; }
623      public double? RelativeToMedian { get; set; }
624      public bool Awarded { get; set; }
625      public string Compliance { get; set; } = "";
626      public int? DeliveryDays { get; set; }
627      public string ComparisonNote { get; set; } = "";
628  }
629  
630  // Offers of one bidding cycle that can be compared: same currency and the same recorded items, quantities and units.
631  public class OfferGroup
632  {
633      public int GroupId { get; set; }
634      public string Currency { get; set; } = "";
635      public List<TenderOffer> Offers { get; set; } = new List<TenderOffer>();
636      public decimal Median { get; set; }
637      public TenderOffer Lowest { get; set; } = new TenderOffer();
638      public TenderOffer Highest { get; set; } = new TenderOffer();
639      public int MedianOwners { get; set; }
640      public string MedianOwnerName { get; set; } = "";
641  }
642  
643  // ---------- Excel / PDF export ----------
644  
645  public class ExportRequest
646  {
647      public string Format { get; set; } = "";
648      public string ReportType { get; set; } = "";
649      public int? TenderId { get; set; }
650      public int? RoundId { get; set; }
651      public string? From { get; set; }
652      public string? To { get; set; }
653  }
654  
655  public class ExportFile
656  {
657      public byte[] Bytes { get; set; } = Array.Empty<byte>();
658      public string ContentType { get; set; } = "";
659      public string FileName { get; set; } = "";
660      public List<string> Sheets { get; set; } = new List<string>();
661  }
662  
663  // A request the export cannot fulfil (unknown report, tender outside the user's scope). Its message is shown to the user.
664  public class IntelligenceExportException : Exception
665  {
666      public IntelligenceExportException(string message) : base(message)
667      {
668      }
669  }
```

### 2.4 Overview calculations

**`Orange-Tendering-System/Services/Intelligence/OverviewCalculator.cs` — lines 1–139**

```csharp
  1  // ORANGE INTELLIGENCE - Tab 1 "Overview": the KPI tiles, the tender lifecycle funnel and "Worth your attention".
  2  // It reuses the tender lifecycles (TenderCalculator) and supplier statistics (SupplierCalculator), so the Overview shows
  3  // exactly the same numbers as the Tenders and Suppliers tabs.
  4  
  5  namespace Orange_Tendering_System.Services.Intelligence;
  6  
  7  public static class OverviewCalculator
  8  {
  9      // The five funnel steps, in order. "round" (cycle created) is shown on the Tenders tab only.
 10      private static readonly string[] FunnelSteps = { "created", "invited", "closed", "awarded", "notified" };
 11  
 12      public static OverviewData Build(ProcurementSnapshot snapshot, int minimumOffers)
 13      {
 14          OverviewData data = new OverviewData();
 15          data.Context = IntelligenceService.BuildContext(snapshot);
 16  
 17          List<TenderDetails> tenders = TenderCalculator.BuildAllTenders(snapshot);
 18          List<SupplierRow> suppliers = SupplierCalculator.BuildStats(snapshot);
 19  
 20          // ---- KPI 1: tenders in scope ----
 21          List<double> lifecycleDays = new List<double>();
 22          foreach (TenderDetails tender in tenders)
 23          {
 24              data.TenderCount++;
 25              if (tender.DaysToNotification != null)
 26              {
 27                  data.CompletedTenders++;
 28                  lifecycleDays.Add(tender.DaysToNotification.Value);
 29              }
 30              if (tender.State == "In progress" || tender.State == "Awarded")
 31              {
 32                  data.InProgressTenders++;
 33              }
 34          }
 35  
 36          // ---- KPI 2: suppliers, and how many submitted an offer in a closed cycle ----
 37          data.SupplierCount = snapshot.Suppliers.Count;
 38          HashSet<int> bidders = new HashSet<int>();
 39          foreach (BidRecord bid in snapshot.Bids)
 40          {
 41              bidders.Add(bid.SupplierId);
 42          }
 43          data.ParticipatingSuppliers = bidders.Count;
 44  
 45          // ---- KPI 3: submitted offers (closed cycles only; sealed offers are counted, never shown) ----
 46          data.SubmittedOffers = snapshot.Bids.Count;
 47          data.SealedOffers = snapshot.SealedBids;
 48  
 49          // ---- KPI 4: median lifecycle = median of (winner notification sent - tender created) ----
 50          // Example: completed tenders took 10, 14 and 30 days -> median 14 days.
 51          data.MedianLifecycleDays = IntelligenceService.Median(lifecycleDays);
 52  
 53          // ---- Lifecycle funnel ----
 54          int total = Math.Max(1, tenders.Count);
 55          foreach (string key in FunnelSteps)
 56          {
 57              LifecycleStep step = new LifecycleStep();
 58              step.Key = key;
 59              List<double> days = new List<double>();
 60              foreach (TenderDetails tender in tenders)
 61              {
 62                  foreach (TenderStage stage in tender.Stages)
 63                  {
 64                      if (stage.Key == key && stage.At != null)
 65                      {
 66                          step.Reached++;
 67                          days.Add(stage.DaysFromCreation ?? 0);
 68                      }
 69                  }
 70              }
 71              step.Share = (double)step.Reached / total;
 72              step.MedianDays = IntelligenceService.Median(days);
 73              data.Lifecycle.Add(step);
 74          }
 75  
 76          // ---- Worth your attention ----
 77          // (a) Closed cycles with fewer offers than the minimum.
 78          List<RoundCompetition> lowRounds = new List<RoundCompetition>();
 79          foreach (RoundCompetition round in TenderCalculator.BuildCompetition(snapshot, minimumOffers))
 80          {
 81              if (round.LowCompetition)
 82              {
 83                  lowRounds.Add(round);
 84              }
 85          }
 86          if (lowRounds.Count > 0)
 87          {
 88              AttentionItem item = new AttentionItem();
 89              item.Kind = "low-competition";
 90              item.Count = lowRounds.Count;
 91              item.MinimumOffers = minimumOffers;
 92              item.TenderId = lowRounds[0].TenderId;
 93              for (int i = 0; i < lowRounds.Count && i < 2; i++)
 94              {
 95                  item.Examples.Add(lowRounds[i].Tender + " · " + IntelligenceService.CycleLabel(lowRounds[i].RoundNumber));
 96              }
 97              data.Attention.Add(item);
 98          }
 99  
100          // (b) The two oldest tenders still in progress.
101          List<TenderDetails> inProgress = new List<TenderDetails>();
102          foreach (TenderDetails tender in tenders)
103          {
104              if (tender.State == "In progress")
105              {
106                  inProgress.Add(tender);
107              }
108          }
109          inProgress = inProgress.OrderBy(tender => tender.CreatedAt).ToList();
110          for (int i = 0; i < inProgress.Count && i < 2; i++)
111          {
112              AttentionItem item = new AttentionItem();
113              item.Kind = "in-progress";
114              item.TenderId = inProgress[i].TenderId;
115              item.TenderTitle = inProgress[i].Title;
116              item.Status = inProgress[i].Status;
117              item.Days = Math.Round(inProgress[i].DaysSoFar);
118              data.Attention.Add(item);
119          }
120  
121          // (c) Suppliers invited to closed cycles that never submitted an offer.
122          int silentSuppliers = 0;
123          foreach (SupplierRow supplier in suppliers)
124          {
125              if (supplier.Invitations > 0 && supplier.Participations == 0)
126              {
127                  silentSuppliers++;
128              }
129          }
130          if (silentSuppliers > 0)
131          {
132              AttentionItem item = new AttentionItem();
133              item.Kind = "silent-suppliers";
134              item.Count = silentSuppliers;
135              data.Attention.Add(item);
136          }
137          return data;
138      }
139  }
```

### 2.5 Supplier calculations

**`Orange-Tendering-System/Services/Intelligence/SupplierCalculator.cs` — lines 1–1286**

```csharp
   1  // ORANGE INTELLIGENCE - Tab 2 "Suppliers": every number of the Suppliers tab is calculated here.
   2  // Input: a ProcurementSnapshot (the authorized, closed-round data loaded by IntelligenceService). Output: plain classes from
   3  // IntelligenceModels.cs. Nothing here reads the database, so the same code runs in the tests (Tests/RadarChecks).
   4  // BuildStats is the ONE place where supplier statistics are calculated: the Suppliers tab and the AI Studio facts
   5  // (IntelligenceAiService.GetSupplierFactsAsync) both use it.
   6  using System.Globalization;
   7  using Orange_Tendering_System.Models.Entities;
   8  
   9  namespace Orange_Tendering_System.Services.Intelligence;
  10  
  11  public static class SupplierCalculator
  12  {
  13      // ==================================================================
  14      // 1. Statistics of every supplier
  15      // ==================================================================
  16  
  17      // One SupplierRow per supplier in scope. Used by the Suppliers tab and by AI Studio.
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
 160          return rows;
 161      }
 162  
 163      // Each offer compared with the median offer of the same bidding cycle and the same currency.
 164      // Example: offers 900, 1000, 1100 JOD -> median 1000 -> positions -10%, 0%, +10%.
 165      public static List<PricePosition> BuildPricePositions(ProcurementSnapshot snapshot)
 166      {
 167          // Group the offers by "cycle id | currency", keeping the first-seen order.
 168          Dictionary<string, List<BidRecord>> groups = new Dictionary<string, List<BidRecord>>();
 169          List<string> groupOrder = new List<string>();
 170          foreach (BidRecord bid in snapshot.Bids)
 171          {
 172              string key = bid.RoundId + "|" + bid.Currency;
 173              if (!groups.ContainsKey(key))
 174              {
 175                  groups[key] = new List<BidRecord>();
 176                  groupOrder.Add(key);
 177              }
 178              groups[key].Add(bid);
 179          }
 180  
 181          List<PricePosition> result = new List<PricePosition>();
 182          foreach (string key in groupOrder)
 183          {
 184              List<BidRecord> pricedBids = new List<BidRecord>();
 185              List<decimal> totals = new List<decimal>();
 186              foreach (BidRecord bid in groups[key])
 187              {
 188                  if (bid.Total > 0)
 189                  {
 190                      pricedBids.Add(bid);
 191                      totals.Add(bid.Total);
 192                  }
 193              }
 194              // A comparison needs at least two offers.
 195              if (pricedBids.Count < 2)
 196              {
 197                  continue;
 198              }
 199              decimal median = IntelligenceService.Median(totals);
 200              if (median <= 0)
 201              {
 202                  continue;
 203              }
 204              RoundRecord round = snapshot.Round(pricedBids[0].RoundId)!;
 205              foreach (BidRecord bid in pricedBids)
 206              {
 207                  PricePosition position = new PricePosition();
 208                  position.SupplierId = bid.SupplierId;
 209                  position.SupplierName = IntelligenceService.CleanName(snapshot.SupplierName(bid.SupplierId));
 210                  position.RoundId = bid.RoundId;
 211                  position.TenderId = bid.TenderId;
 212                  position.TenderTitle = IntelligenceService.CleanName(snapshot.Tender(bid.TenderId)?.Title ?? "Tender #" + bid.TenderId);
 213                  position.Date = round.ReferenceDate;
 214                  position.Currency = bid.Currency;
 215                  position.Total = bid.Total;
 216                  position.Median = median;
 217                  position.Relative = Math.Round((double)(bid.Total / median) - 1, 4);
 218                  position.Comparable = pricedBids.Count;
 219                  result.Add(position);
 220              }
 221          }
 222          return result;
 223      }
 224  
 225      // ==================================================================
 226      // 2. The whole Suppliers tab (filters, KPIs, charts, table, relationships)
 227      // ==================================================================
 228  
 229      public static SuppliersData Build(ProcurementSnapshot snapshot, SupplierFilters filters, int competitionMinimum, int minimumOffers)
 230      {
 231          SuppliersData data = new SuppliersData();
 232          data.Context = IntelligenceService.BuildContext(snapshot);
 233          data.MinimumOffers = minimumOffers;
 234          data.CompetitionMinimum = Math.Max(1, competitionMinimum);
 235  
 236          List<SupplierRow> allRows = BuildStats(snapshot);
 237  
 238          // ---- Filter choices ----
 239          data.Departments = FindDepartments(snapshot);
 240          Dictionary<string, int> categoryColors = BuildCategoryColors(snapshot);
 241          List<string> categoryNames = new List<string>();
 242          foreach (SupplierRow row in allRows)
 243          {
 244              if (!categoryNames.Contains(row.Category))
 245              {
 246                  categoryNames.Add(row.Category);
 247              }
 248          }
 249          categoryNames.Sort(string.Compare);
 250          data.Filters = CleanFilters(filters, data.Departments, categoryNames);
 251  
 252          // ---- Suppliers that pass the filters ----
 253          List<SupplierRow> rows = ApplyFilters(snapshot, allRows, data.Filters);
 254          HashSet<int> shownIds = new HashSet<int>();
 255          foreach (SupplierRow row in rows)
 256          {
 257              shownIds.Add(row.SupplierId);
 258          }
 259  
 260          foreach (string name in categoryNames)
 261          {
 262              CategoryCount category = new CategoryCount();
 263              category.Name = name;
 264              category.Color = CategoryColor(categoryColors, name);
 265              foreach (SupplierRow row in rows)
 266              {
 267                  if (row.Category == name)
 268                  {
 269                      category.Count++;
 270                  }
 271              }
 272              data.Categories.Add(category);
 273          }
 274  
 275          // ---- KPI tiles ----
 276          Dictionary<string, int> perCategory = new Dictionary<string, int>();
 277          List<string> categoryOrder = new List<string>();
 278          foreach (SupplierRow row in rows)
 279          {
 280              data.RegisteredSuppliers++;
 281              if (row.IsApproved)
 282              {
 283                  data.ApprovedSuppliers++;
 284              }
 285              if (row.Participations > 0)
 286              {
 287                  data.ParticipatingSuppliers++;
 288              }
 289              data.Invitations += row.Invitations;
 290              data.Offers += row.Participations;
 291              if (!perCategory.ContainsKey(row.Category))
 292              {
 293                  perCategory[row.Category] = 0;
 294                  categoryOrder.Add(row.Category);
 295              }
 296              perCategory[row.Category]++;
 297          }
 298          data.CategoryCount = perCategory.Count;
 299          // Response rate = offers in closed cycles / invitations to closed cycles.
 300          if (data.Invitations > 0)
 301          {
 302              data.ResponseRate = (double)data.Offers / data.Invitations;
 303          }
 304  
 305          // ---- Market snapshot: biggest four categories + "Others" ----
 306          List<string> byCount = categoryOrder.OrderByDescending(name => perCategory[name]).ToList();
 307          if (byCount.Count > 0)
 308          {
 309              data.LargestCategory = byCount[0];
 310              data.LargestCategoryCount = perCategory[byCount[0]];
 311          }
 312          int othersCount = 0;
 313          for (int i = 0; i < byCount.Count; i++)
 314          {
 315              if (i < 4)
 316              {
 317                  CategoryCount slice = new CategoryCount();
 318                  slice.Name = byCount[i];
 319                  slice.Count = perCategory[byCount[i]];
 320                  slice.Color = CategoryColor(categoryColors, byCount[i]);
 321                  data.CategoryChart.Add(slice);
 322              }
 323              else
 324              {
 325                  othersCount += perCategory[byCount[i]];
 326              }
 327          }
 328          if (othersCount > 0)
 329          {
 330              CategoryCount others = new CategoryCount();
 331              others.Name = "Others (" + (byCount.Count - 4) + ")";
 332              others.Count = othersCount;
 333              others.IsOthers = true;
 334              data.CategoryChart.Add(others);
 335          }
 336          data.ParticipationTrend = BuildParticipationTrend(snapshot, shownIds);
 337  
 338          // ---- Supplier table: most active first ----
 339          data.Suppliers = rows.OrderByDescending(row => row.Participations).ThenBy(row => row.Name, StringComparer.CurrentCulture).ToList();
 340  
 341          BuildLandscape(data, snapshot, rows);
 342          BuildPositionsChart(data, snapshot, shownIds);
 343          BuildRelationships(data, snapshot, rows, shownIds);
 344  
 345          // ---- Drop-down lists ----
 346          foreach (SupplierRow row in allRows.OrderBy(row => row.Name, StringComparer.CurrentCulture))
 347          {
 348              data.AllSuppliers.Add(ToOption(row));
 349          }
 350          foreach (SupplierRow row in allRows.OrderByDescending(row => row.Participations))
 351          {
 352              if (row.Participations > 0)
 353              {
 354                  data.ScenarioSuppliers.Add(ToOption(row));
 355              }
 356          }
 357          return data;
 358      }
 359  
 360      // Unknown filter values (for example a department that is no longer in the period) are ignored.
 361      private static SupplierFilters CleanFilters(SupplierFilters filters, List<string> departments, List<string> categories)
 362      {
 363          SupplierFilters clean = new SupplierFilters();
 364          clean.Department = departments.Contains(filters.Department) ? filters.Department : "";
 365          clean.Category = categories.Contains(filters.Category) ? filters.Category : "";
 366          if (filters.Activity == "active" || filters.Activity == "silent" || filters.Activity == "approved")
 367          {
 368              clean.Activity = filters.Activity;
 369          }
 370          clean.Search = (filters.Search ?? "").Trim();
 371          return clean;
 372      }
 373  
 374      private static List<SupplierRow> ApplyFilters(ProcurementSnapshot snapshot, List<SupplierRow> allRows, SupplierFilters filters)
 375      {
 376          // Department filter = suppliers that submitted an offer to a tender of that department.
 377          HashSet<int> departmentBidders = new HashSet<int>();
 378          if (filters.Department != "")
 379          {
 380              foreach (BidRecord bid in snapshot.Bids)
 381              {
 382                  TenderRecord? tender = snapshot.Tender(bid.TenderId);
 383                  if (tender != null && tender.Department == filters.Department)
 384                  {
 385                      departmentBidders.Add(bid.SupplierId);
 386                  }
 387              }
 388          }
 389  
 390          List<SupplierRow> result = new List<SupplierRow>();
 391          foreach (SupplierRow row in allRows)
 392          {
 393              if (filters.Category != "" && row.Category != filters.Category)
 394              {
 395                  continue;
 396              }
 397              if (filters.Activity == "active" && row.Participations == 0)
 398              {
 399                  continue;
 400              }
 401              if (filters.Activity == "silent" && row.Participations > 0)
 402              {
 403                  continue;
 404              }
 405              if (filters.Activity == "approved" && !row.IsApproved)
 406              {
 407                  continue;
 408              }
 409              if (filters.Department != "" && !departmentBidders.Contains(row.SupplierId))
 410              {
 411                  continue;
 412              }
 413              if (filters.Search != "")
 414              {
 415                  bool nameMatches = row.Name.Contains(filters.Search, StringComparison.OrdinalIgnoreCase);
 416                  bool categoryMatches = row.Category.Contains(filters.Search, StringComparison.OrdinalIgnoreCase);
 417                  if (!nameMatches && !categoryMatches)
 418                  {
 419                      continue;
 420                  }
 421              }
 422              result.Add(row);
 423          }
 424          return result;
 425      }
 426  
 427      // Departments of the tenders that have a closed bidding cycle in the period.
 428      private static List<string> FindDepartments(ProcurementSnapshot snapshot)
 429      {
 430          List<string> departments = new List<string>();
 431          foreach (RoundRecord round in snapshot.AnalyzedRounds)
 432          {
 433              string department = snapshot.Tender(round.TenderId)?.Department ?? "-";
 434              if (!departments.Contains(department))
 435              {
 436                  departments.Add(department);
 437              }
 438          }
 439          departments.Sort(string.CompareOrdinal);
 440          return departments;
 441      }
 442  
 443      // Distinct suppliers that submitted an offer, per month in which the cycle closed (empty months included).
 444      private static List<MonthCount> BuildParticipationTrend(ProcurementSnapshot snapshot, HashSet<int> shownIds)
 445      {
 446          Dictionary<string, HashSet<int>> suppliersByMonth = new Dictionary<string, HashSet<int>>();
 447          foreach (RoundRecord round in snapshot.AnalyzedRounds)
 448          {
 449              string month = round.AvailableAt.ToString("yyyy-MM", CultureInfo.InvariantCulture);
 450              if (!suppliersByMonth.ContainsKey(month))
 451              {
 452                  suppliersByMonth[month] = new HashSet<int>();
 453              }
 454              foreach (BidRecord bid in snapshot.Bids)
 455              {
 456                  if (bid.RoundId == round.Id && shownIds.Contains(bid.SupplierId))
 457                  {
 458                      suppliersByMonth[month].Add(bid.SupplierId);
 459                  }
 460              }
 461          }
 462  
 463          List<MonthCount> trend = new List<MonthCount>();
 464          if (suppliersByMonth.Count == 0)
 465          {
 466              return trend;
 467          }
 468          List<string> months = suppliersByMonth.Keys.ToList();
 469          months.Sort(string.CompareOrdinal);
 470          DateTime current = DateTime.ParseExact(months[0] + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);
 471          DateTime lastMonth = DateTime.ParseExact(months[months.Count - 1] + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);
 472          while (current <= lastMonth)
 473          {
 474              string key = current.ToString("yyyy-MM", CultureInfo.InvariantCulture);
 475              MonthCount point = new MonthCount();
 476              point.Month = key;
 477              if (suppliersByMonth.ContainsKey(key))
 478              {
 479                  point.Suppliers = suppliersByMonth[key].Count;
 480              }
 481              trend.Add(point);
 482              current = current.AddMonths(1);
 483          }
 484          return trend;
 485      }
 486  
 487      // Chart "Participation vs win rate": one dot per supplier with an offer, one colour (series) per category.
 488      // Win rate needs official awards; without awards the chart uses the response rate instead.
 489      private static void BuildLandscape(SuppliersData data, ProcurementSnapshot snapshot, List<SupplierRow> rows)
 490      {
 491          List<SupplierRow> active = new List<SupplierRow>();
 492          foreach (SupplierRow row in rows)
 493          {
 494              if (row.Participations > 0)
 495              {
 496                  active.Add(row);
 497              }
 498          }
 499          data.NotPlotted = rows.Count - active.Count;
 500  
 501          bool anyWinRate = false;
 502          foreach (SupplierRow row in active)
 503          {
 504              if (row.WinRate != null)
 505              {
 506                  anyWinRate = true;
 507              }
 508          }
 509          data.UseWinRate = IntelligenceService.FindAwards(snapshot).Count > 0 && anyWinRate;
 510  
 511          data.MaxAwardedValue = 1;
 512          foreach (SupplierRow row in active)
 513          {
 514              decimal awarded = row.AwardedValue.Count > 0 ? row.AwardedValue[0].Value : 0;
 515              if (awarded > data.MaxAwardedValue)
 516              {
 517                  data.MaxAwardedValue = awarded;
 518              }
 519  
 520              double? y = data.UseWinRate ? row.WinRate : row.ParticipationRate;
 521              if (y == null)
 522              {
 523                  continue;
 524              }
 525              LandscapeSeries? series = null;
 526              foreach (LandscapeSeries existing in data.Landscape)
 527              {
 528                  if (existing.Category == row.Category)
 529                  {
 530                      series = existing;
 531                  }
 532              }
 533              if (series == null)
 534              {
 535                  series = new LandscapeSeries();
 536                  series.Category = row.Category;
 537                  series.Color = row.CategoryColor;
 538                  data.Landscape.Add(series);
 539              }
 540              series.Points.Add(row);
 541          }
 542  
 543          // "Most rounds with an offer" takeaway.
 544          SupplierRow? leader = null;
 545          foreach (SupplierRow row in active)
 546          {
 547              if (leader == null || row.Participations > leader.Participations)
 548              {
 549                  leader = row;
 550              }
 551          }
 552          data.LeaderName = leader == null ? "" : leader.Name;
 553      }
 554  
 555      // Chart "Price position over time": the four suppliers with the most comparable offers.
 556      private static void BuildPositionsChart(SuppliersData data, ProcurementSnapshot snapshot, HashSet<int> shownIds)
 557      {
 558          List<PricePosition> positions = new List<PricePosition>();
 559          foreach (PricePosition position in BuildPricePositions(snapshot))
 560          {
 561              if (shownIds.Contains(position.SupplierId))
 562              {
 563                  positions.Add(position);
 564              }
 565          }
 566          data.PositionRows = positions;
 567  
 568          Dictionary<int, int> countBySupplier = new Dictionary<int, int>();
 569          List<int> supplierOrder = new List<int>();
 570          foreach (PricePosition position in positions)
 571          {
 572              if (!countBySupplier.ContainsKey(position.SupplierId))
 573              {
 574                  countBySupplier[position.SupplierId] = 0;
 575                  supplierOrder.Add(position.SupplierId);
 576              }
 577              countBySupplier[position.SupplierId]++;
 578          }
 579          List<int> shownSuppliers = supplierOrder.OrderByDescending(id => countBySupplier[id]).Take(4).ToList();
 580  
 581          Dictionary<int, SupplierRow> rowsById = new Dictionary<int, SupplierRow>();
 582          foreach (SupplierRow row in data.Suppliers)
 583          {
 584              rowsById[row.SupplierId] = row;
 585          }
 586  
 587          double relativeSum = 0;
 588          double? lowest = null;
 589          double? highest = null;
 590          foreach (int supplierId in shownSuppliers)
 591          {
 592              PositionSeries series = new PositionSeries();
 593              series.SupplierId = supplierId;
 594              series.Name = IntelligenceService.CleanName(snapshot.SupplierName(supplierId));
 595              series.SupplierColor = rowsById.ContainsKey(supplierId) ? rowsById[supplierId].SupplierColor : -1;
 596              double supplierSum = 0;
 597              foreach (PricePosition position in positions)
 598              {
 599                  if (position.SupplierId != supplierId)
 600                  {
 601                      continue;
 602                  }
 603                  series.Points.Add(position);
 604                  supplierSum += position.Relative;
 605                  relativeSum += position.Relative;
 606                  data.ShownOffers++;
 607                  if (lowest == null || position.Relative < lowest)
 608                  {
 609                      lowest = position.Relative;
 610                  }
 611                  if (highest == null || position.Relative > highest)
 612                  {
 613                      highest = position.Relative;
 614                  }
 615              }
 616              series.Points = series.Points.OrderBy(point => point.Date).ToList();
 617              data.Positions.Add(series);
 618  
 619              // The supplier with the lowest average position ("cheapest on average").
 620              double supplierAverage = supplierSum / series.Points.Count;
 621              if (data.LowestAverage == null || supplierAverage < data.LowestAverage)
 622              {
 623                  data.LowestAverage = supplierAverage;
 624                  data.LowestAverageName = series.Name;
 625              }
 626          }
 627          if (data.ShownOffers > 0)
 628          {
 629              data.AverageRelative = relativeSum / data.ShownOffers;
 630              data.RelativeSpread = highest - lowest;
 631          }
 632      }
 633  
 634      // Market relationships: supplier pairs that met in closed bidding cycles, and the groups they form.
 635      private static void BuildRelationships(SuppliersData data, ProcurementSnapshot snapshot, List<SupplierRow> rows, HashSet<int> shownIds)
 636      {
 637          Dictionary<string, List<int>> sharedRounds = IntelligenceService.FindSharedRounds(snapshot);
 638          List<SupplierPair> pairs = new List<SupplierPair>();
 639          foreach (string key in sharedRounds.Keys)
 640          {
 641              int a = IntelligenceService.FirstOfPair(key);
 642              int b = IntelligenceService.SecondOfPair(key);
 643              if (shownIds.Contains(a) && shownIds.Contains(b))
 644              {
 645                  pairs.Add(MakePair(snapshot, a, b, sharedRounds[key].Count));
 646              }
 647          }
 648          pairs = pairs.OrderByDescending(pair => pair.SharedRounds).ThenBy(pair => pair.NameA, StringComparer.CurrentCulture).ToList();
 649          data.PairCount = pairs.Count;
 650          data.Pairs = pairs.Take(25).ToList();
 651  
 652          // Groups: suppliers linked by pairs with at least CompetitionMinimum shared cycles (breadth-first search).
 653          Dictionary<int, List<int>> neighbours = new Dictionary<int, List<int>>();
 654          foreach (SupplierRow row in rows)
 655          {
 656              neighbours[row.SupplierId] = new List<int>();
 657          }
 658          foreach (SupplierPair pair in pairs)
 659          {
 660              if (pair.SharedRounds >= data.CompetitionMinimum)
 661              {
 662                  neighbours[pair.SupplierA].Add(pair.SupplierB);
 663                  neighbours[pair.SupplierB].Add(pair.SupplierA);
 664              }
 665          }
 666          HashSet<int> visited = new HashSet<int>();
 667          foreach (SupplierRow row in rows)
 668          {
 669              if (visited.Contains(row.SupplierId))
 670              {
 671                  continue;
 672              }
 673              List<int> members = new List<int>();
 674              Queue<int> queue = new Queue<int>();
 675              queue.Enqueue(row.SupplierId);
 676              visited.Add(row.SupplierId);
 677              while (queue.Count > 0)
 678              {
 679                  int current = queue.Dequeue();
 680                  members.Add(current);
 681                  foreach (int next in neighbours[current])
 682                  {
 683                      if (!visited.Contains(next))
 684                      {
 685                          visited.Add(next);
 686                          queue.Enqueue(next);
 687                      }
 688                  }
 689              }
 690              if (members.Count == 1)
 691              {
 692                  data.UnconnectedSuppliers++;
 693                  continue;
 694              }
 695              CompetitionGroup group = new CompetitionGroup();
 696              foreach (int member in members)
 697              {
 698                  group.Names.Add(IntelligenceService.CleanName(snapshot.SupplierName(member)));
 699              }
 700              group.Names.Sort(string.Compare);
 701              data.Groups.Add(group);
 702          }
 703          data.Groups = data.Groups.OrderByDescending(group => group.Names.Count).ToList();
 704      }
 705  
 706      // ==================================================================
 707      // 3. Supplier details panel
 708      // ==================================================================
 709  
 710      public static SupplierDetails? BuildDetails(ProcurementSnapshot snapshot, int supplierId)
 711      {
 712          if (snapshot.Supplier(supplierId) == null)
 713          {
 714              return null;
 715          }
 716          SupplierDetails details = new SupplierDetails();
 717          foreach (SupplierRow row in BuildStats(snapshot))
 718          {
 719              if (row.SupplierId == supplierId)
 720              {
 721                  details.Supplier = row;
 722              }
 723          }
 724  
 725          // Who won each closed cycle (official awards only).
 726          Dictionary<int, int> winnerByRound = new Dictionary<int, int>();
 727          foreach (AwardRecord award in IntelligenceService.FindAwards(snapshot))
 728          {
 729              if (!winnerByRound.ContainsKey(award.RoundId))
 730              {
 731                  winnerByRound[award.RoundId] = award.SupplierId;
 732              }
 733          }
 734  
 735          // Participation: closed cycles with an offer from this supplier, newest first.
 736          List<RoundRecord> rounds = snapshot.AnalyzedRounds.OrderBy(round => round.AvailableAt).ThenBy(round => round.Id).ToList();
 737          foreach (RoundRecord round in rounds)
 738          {
 739              HashSet<int> bidders = new HashSet<int>();
 740              foreach (BidRecord bid in snapshot.Bids)
 741              {
 742                  if (bid.RoundId == round.Id)
 743                  {
 744                      bidders.Add(bid.SupplierId);
 745                  }
 746              }
 747              if (!bidders.Contains(supplierId))
 748              {
 749                  continue;
 750              }
 751              TenderRecord? tender = snapshot.Tender(round.TenderId);
 752              SupplierRound item = new SupplierRound();
 753              item.RoundId = round.Id;
 754              item.TenderId = round.TenderId;
 755              item.Tender = IntelligenceService.CleanName(tender?.Title ?? "Tender #" + round.TenderId);
 756              item.RoundNumber = round.RoundNumber;
 757              item.Date = round.AvailableAt;
 758              item.Department = IntelligenceService.CleanName(tender?.Department ?? "-");
 759              item.Offers = bidders.Count;
 760              item.Awarded = winnerByRound.ContainsKey(round.Id) && winnerByRound[round.Id] == supplierId;
 761              if (item.Offers > details.MaxOffers)
 762              {
 763                  details.MaxOffers = item.Offers;
 764              }
 765              details.Rounds.Insert(0, item);
 766          }
 767  
 768          // Pricing: every comparable offer of this supplier, oldest first.
 769          foreach (PricePosition position in BuildPricePositions(snapshot))
 770          {
 771              if (position.SupplierId == supplierId)
 772              {
 773                  details.Prices.Add(position);
 774              }
 775          }
 776          details.Prices = details.Prices.OrderBy(position => position.Date).ToList();
 777  
 778          // Relationships: suppliers that met this supplier in closed cycles, most shared cycles first.
 779          Dictionary<string, List<int>> sharedRounds = IntelligenceService.FindSharedRounds(snapshot);
 780          foreach (string key in sharedRounds.Keys)
 781          {
 782              int a = IntelligenceService.FirstOfPair(key);
 783              int b = IntelligenceService.SecondOfPair(key);
 784              if (a == supplierId)
 785              {
 786                  details.Competitors.Add(MakePair(snapshot, a, b, sharedRounds[key].Count));
 787              }
 788              else if (b == supplierId)
 789              {
 790                  details.Competitors.Add(MakePair(snapshot, b, a, sharedRounds[key].Count));
 791              }
 792          }
 793          details.Competitors = details.Competitors.OrderByDescending(pair => pair.SharedRounds).ToList();
 794          return details;
 795      }
 796  
 797      // ==================================================================
 798      // 4. Supplier offer comparison (two suppliers, their shared tenders)
 799      // ==================================================================
 800  
 801      public const string BusinessZone = "Asia/Amman";
 802      private static readonly TimeZoneInfo Amman = TimeZoneInfo.FindSystemTimeZoneById(BusinessZone);
 803  
 804      // One row per tender in which BOTH suppliers submitted an offer in the same closed cycle.
 805      // When a tender has several such cycles, the latest comparable one is used.
 806      public static List<OfferComparison> CompareOffers(ProcurementSnapshot snapshot, int supplierA, int supplierB)
 807      {
 808          List<OfferComparison> result = new List<OfferComparison>();
 809          if (supplierA == supplierB || snapshot.Supplier(supplierA) == null || snapshot.Supplier(supplierB) == null)
 810          {
 811              return result;
 812          }
 813  
 814          // Every shared cycle, grouped by tender.
 815          Dictionary<int, List<OfferComparison>> byTender = new Dictionary<int, List<OfferComparison>>();
 816          foreach (RoundRecord round in snapshot.AnalyzedRounds)
 817          {
 818              TenderRecord? tender = snapshot.Tender(round.TenderId);
 819              if (tender == null)
 820              {
 821                  continue;
 822              }
 823              foreach (BidRecord bidA in snapshot.Bids)
 824              {
 825                  if (bidA.SupplierId != supplierA || bidA.RoundId != round.Id || bidA.TenderId != round.TenderId)
 826                  {
 827                      continue;
 828                  }
 829                  foreach (BidRecord bidB in snapshot.Bids)
 830                  {
 831                      if (bidB.SupplierId != supplierB || bidB.RoundId != round.Id || bidB.TenderId != round.TenderId)
 832                      {
 833                          continue;
 834                      }
 835                      if (!byTender.ContainsKey(tender.Id))
 836                      {
 837                          byTender[tender.Id] = new List<OfferComparison>();
 838                      }
 839                      byTender[tender.Id].Add(CompareTwoOffers(snapshot, round, tender, bidA, bidB));
 840                  }
 841              }
 842          }
 843  
 844          // Per tender: prefer a cycle whose prices are comparable, then the latest cycle.
 845          foreach (int tenderId in byTender.Keys)
 846          {
 847              OfferComparison best = byTender[tenderId]
 848                  .OrderBy(row => row.PriceIssue != null)
 849                  .ThenByDescending(row => row.RoundNumber)
 850                  .First();
 851              result.Add(best);
 852          }
 853          return result.OrderBy(row => row.Title, StringComparer.CurrentCultureIgnoreCase).ThenBy(row => row.TenderId).ToList();
 854      }
 855  
 856      private static OfferComparison CompareTwoOffers(ProcurementSnapshot snapshot, RoundRecord round, TenderRecord tender, BidRecord a, BidRecord b)
 857      {
 858          OfferComparison row = new OfferComparison();
 859          row.TenderId = tender.Id;
 860          row.Title = IntelligenceService.CleanName(tender.Title);
 861          row.RoundId = round.Id;
 862          row.RoundNumber = round.RoundNumber;
 863          row.A = DescribeOffer(snapshot, round, a);
 864          row.B = DescribeOffer(snapshot, round, b);
 865          row.Deadline = ToAmmanTime(round.Deadline);
 866  
 867          // Price: compared only when both offers are directly comparable.
 868          row.PriceIssue = FindPriceIssue(a, b);
 869          if (row.PriceIssue == null)
 870          {
 871              decimal totalA = a.RecordedTotal!.Value;
 872              decimal totalB = b.RecordedTotal!.Value;
 873              decimal difference = Math.Abs(totalA - totalB);
 874              row.Difference = difference;
 875              // Percentage = difference / the HIGHER offer. Example: 900 vs 1000 -> 100 / 1000 = 10%.
 876              row.RelativeDifference = difference / Math.Max(totalA, totalB);
 877              if (difference != 0)
 878              {
 879                  row.LowerOffer = totalA < totalB ? "a" : "b";
 880              }
 881          }
 882  
 883          // Timing: seconds from A's submission to B's submission (positive = A submitted first).
 884          decimal? apart = SecondsBetween(a.SubmittedAt, b.SubmittedAt);
 885          if (apart != null)
 886          {
 887              row.SecondsApart = Math.Abs(apart.Value);
 888              if (apart > 0)
 889              {
 890                  row.FirstSubmission = "a";
 891              }
 892              else if (apart < 0)
 893              {
 894                  row.FirstSubmission = "b";
 895              }
 896          }
 897          return row;
 898      }
 899  
 900      private static ComparedOffer DescribeOffer(ProcurementSnapshot snapshot, RoundRecord round, BidRecord bid)
 901      {
 902          ComparedOffer offer = new ComparedOffer();
 903          offer.BidId = bid.Id;
 904          offer.Supplier = IntelligenceService.CleanName(snapshot.SupplierName(bid.SupplierId));
 905          offer.Currency = NormalizeCurrency(bid.Currency);
 906          offer.Total = bid.RecordedTotal;
 907          offer.SubmittedAt = ToAmmanTime(bid.SubmittedAt);
 908          offer.SecondsBeforeDeadline = SecondsBetween(bid.SubmittedAt, round.Deadline);
 909          if (offer.SubmittedAt != null)
 910          {
 911              offer.RecordedTime = offer.SubmittedAt.Value.ToString("HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture);
 912          }
 913          return offer;
 914      }
 915  
 916      // Returns null when the two offers can be compared, otherwise the reason they cannot.
 917      private static string? FindPriceIssue(BidRecord a, BidRecord b)
 918      {
 919          string? currencyA = NormalizeCurrency(a.Currency);
 920          string? currencyB = NormalizeCurrency(b.Currency);
 921          if (currencyA == null || currencyB == null)
 922          {
 923              return "missing_currency";
 924          }
 925          if (currencyA != currencyB)
 926          {
 927              return "different_currency";
 928          }
 929          if (a.Lines.Count == 0 || b.Lines.Count == 0 || a.RecordedTotal == null || a.RecordedTotal <= 0 || b.RecordedTotal == null || b.RecordedTotal <= 0)
 930          {
 931              return "missing_price";
 932          }
 933          if (HasInvalidLine(a) || HasInvalidLine(b) || IntelligenceService.StructureKey(a) != IntelligenceService.StructureKey(b))
 934          {
 935              return "different_structure";
 936          }
 937          if (Specifications(a) != Specifications(b) || NormalizeText(a.Deviations) != NormalizeText(b.Deviations))
 938          {
 939              return "different_specifications";
 940          }
 941          return null;
 942      }
 943  
 944      private static bool HasInvalidLine(BidRecord bid)
 945      {
 946          foreach (BidLineRecord line in bid.Lines)
 947          {
 948              if (line.Quantity <= 0 || line.UnitPrice < 0 || string.IsNullOrWhiteSpace(line.Unit))
 949              {
 950                  return true;
 951              }
 952          }
 953          return false;
 954      }
 955  
 956      // All offered specifications of the bid, item by item, as one normalized text.
 957      private static string Specifications(BidRecord bid)
 958      {
 959          List<string> parts = new List<string>();
 960          foreach (BidLineRecord line in bid.Lines.OrderBy(line => line.TenderItemId))
 961          {
 962              parts.Add(NormalizeText(line.OfferedSpecifications));
 963          }
 964          return string.Join("\n", parts);
 965      }
 966  
 967      // "  red   steel " -> "RED STEEL"
 968      private static string NormalizeText(string? value)
 969      {
 970          string[] words = (value ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
 971          return string.Join(' ', words).ToUpperInvariant();
 972      }
 973  
 974      private static string? NormalizeCurrency(string? currency)
 975      {
 976          if (string.IsNullOrWhiteSpace(currency))
 977          {
 978              return null;
 979          }
 980          return currency.Trim().ToUpperInvariant();
 981      }
 982  
 983      // Exact seconds (with fractions) from one recorded time to another; null when either is missing.
 984      private static decimal? SecondsBetween(DateTime? from, DateTime? to)
 985      {
 986          if (from == null || to == null)
 987          {
 988              return null;
 989          }
 990          long ticks = (to.Value - from.Value).Ticks;
 991          return ticks / (decimal)TimeSpan.TicksPerSecond;
 992      }
 993  
 994      // Database times are UTC; the comparison shows them in Amman time.
 995      private static DateTimeOffset? ToAmmanTime(DateTime? utc)
 996      {
 997          if (utc == null)
 998          {
 999              return null;
1000          }
1001          DateTimeOffset value = new DateTimeOffset(DateTime.SpecifyKind(utc.Value, DateTimeKind.Utc));
1002          return TimeZoneInfo.ConvertTime(value, Amman);
1003      }
1004  
1005      // ==================================================================
1006      // 5. What-if scenario: replay recorded history without some suppliers
1007      // ==================================================================
1008  
1009      public static SimulationResult Simulate(ProcurementSnapshot snapshot, List<int> excludedIds, int minimumOffers)
1010      {
1011          HashSet<int> excluded = new HashSet<int>(excludedIds);
1012          List<AwardRecord> awards = IntelligenceService.FindAwards(snapshot);
1013          SimulationResult result = new SimulationResult();
1014          result.MinimumOffers = minimumOffers;
1015  
1016          // Count the bidders of every closed cycle before and after removing the chosen suppliers.
1017          List<SimulatedRound> rounds = new List<SimulatedRound>();
1018          foreach (RoundRecord round in snapshot.AnalyzedRounds)
1019          {
1020              HashSet<int> bidders = new HashSet<int>();
1021              foreach (BidRecord bid in snapshot.Bids)
1022              {
1023                  if (bid.RoundId == round.Id)
1024                  {
1025                      bidders.Add(bid.SupplierId);
1026                  }
1027              }
1028              if (bidders.Count == 0)
1029              {
1030                  continue;
1031              }
1032              int after = 0;
1033              foreach (int supplierId in bidders)
1034              {
1035                  if (!excluded.Contains(supplierId))
1036                  {
1037                      after++;
1038                  }
1039              }
1040              SimulatedRound item = new SimulatedRound();
1041              item.RoundId = round.Id;
1042              item.Tender = IntelligenceService.CleanName(snapshot.Tender(round.TenderId)?.Title ?? "-");
1043              item.RoundNumber = round.RoundNumber;
1044              item.Before = bidders.Count;
1045              item.After = after;
1046              item.DropsBelowMinimum = item.Before >= minimumOffers && after < minimumOffers;
1047              item.LosesAllBidders = after == 0;
1048              foreach (AwardRecord award in awards)
1049              {
1050                  if (award.RoundId == round.Id && excluded.Contains(award.SupplierId))
1051                  {
1052                      item.LosesAward = true;
1053                  }
1054              }
1055              rounds.Add(item);
1056          }
1057  
1058          foreach (SimulatedRound round in rounds)
1059          {
1060              if (round.After < round.Before)
1061              {
1062                  result.RoundsAffected++;
1063                  result.Rounds.Add(round);
1064              }
1065              if (round.Before < minimumOffers)
1066              {
1067                  result.RoundsBelowMinimumBefore++;
1068              }
1069              if (round.After < minimumOffers)
1070              {
1071                  result.RoundsBelowMinimumAfter++;
1072              }
1073              if (round.LosesAllBidders)
1074              {
1075                  result.RoundsWithoutBidders++;
1076              }
1077          }
1078  
1079          // Share of all offers that came from the excluded suppliers.
1080          List<BidRecord> remainingBids = new List<BidRecord>();
1081          int removedBids = 0;
1082          foreach (BidRecord bid in snapshot.Bids)
1083          {
1084              if (excluded.Contains(bid.SupplierId))
1085              {
1086                  removedBids++;
1087              }
1088              else
1089              {
1090                  remainingBids.Add(bid);
1091              }
1092          }
1093          if (snapshot.Bids.Count > 0)
1094          {
1095              result.BidShareRemoved = Math.Round((double)removedBids / snapshot.Bids.Count, 4);
1096          }
1097          result.HhiBefore = ConcentrationIndex(snapshot.Bids.ToList());
1098          result.HhiAfter = ConcentrationIndex(remainingBids);
1099  
1100          // Award value previously won by the excluded suppliers, per currency.
1101          foreach (AwardRecord award in awards)
1102          {
1103              if (excluded.Contains(award.SupplierId))
1104              {
1105                  AddMoney(result.AwardExposure, award.Currency, award.Value);
1106              }
1107          }
1108  
1109          // Approved suppliers of the same categories that could replace them.
1110          HashSet<string> categories = new HashSet<string>();
1111          foreach (SupplierRecord supplier in snapshot.Suppliers)
1112          {
1113              if (excluded.Contains(supplier.Id))
1114              {
1115                  categories.Add(supplier.Category);
1116              }
1117          }
1118          Dictionary<int, SupplierRow> stats = new Dictionary<int, SupplierRow>();
1119          foreach (SupplierRow row in BuildStats(snapshot))
1120          {
1121              stats[row.SupplierId] = row;
1122          }
1123          List<SupplierAlternative> alternatives = new List<SupplierAlternative>();
1124          foreach (SupplierRecord supplier in snapshot.Suppliers)
1125          {
1126              if (excluded.Contains(supplier.Id) || supplier.Status != SupplierStatus.Approved || !categories.Contains(supplier.Category))
1127              {
1128                  continue;
1129              }
1130              SupplierAlternative alternative = new SupplierAlternative();
1131              alternative.SupplierId = supplier.Id;
1132              alternative.Name = IntelligenceService.CleanName(supplier.Name);
1133              alternative.Category = supplier.Category;
1134              alternative.Participations = stats[supplier.Id].Participations;
1135              alternative.ParticipationRate = stats[supplier.Id].ParticipationRate;
1136              alternatives.Add(alternative);
1137          }
1138          result.Alternatives = alternatives.OrderByDescending(item => item.Participations).ThenBy(item => item.Name).Take(8).ToList();
1139  
1140          result.Excluded = excluded.OrderBy(id => id).ToList();
1141          foreach (int id in result.Excluded)
1142          {
1143              result.ExcludedNames.Add(IntelligenceService.CleanName(snapshot.SupplierName(id)));
1144          }
1145          result.Notice = "Hypothetical replay of recorded history only. It is not a forecast and changes no supplier status or procurement decision.";
1146          return result;
1147      }
1148  
1149      // Herfindahl-Hirschman Index of the offers: sum of (each supplier's share of offers)².
1150      // Example: 4 offers, supplier A has 2 and B and C have 1 -> 0.5² + 0.25² + 0.25² = 0.375. 1.0 = one supplier only.
1151      private static double? ConcentrationIndex(List<BidRecord> bids)
1152      {
1153          if (bids.Count == 0)
1154          {
1155              return null;
1156          }
1157          Dictionary<int, int> offersBySupplier = new Dictionary<int, int>();
1158          foreach (BidRecord bid in bids)
1159          {
1160              if (!offersBySupplier.ContainsKey(bid.SupplierId))
1161              {
1162                  offersBySupplier[bid.SupplierId] = 0;
1163              }
1164              offersBySupplier[bid.SupplierId]++;
1165          }
1166          double sum = 0;
1167          foreach (int count in offersBySupplier.Values)
1168          {
1169              double share = (double)count / bids.Count;
1170              sum += share * share;
1171          }
1172          return Math.Round(sum, 4);
1173      }
1174  
1175      // ==================================================================
1176      // Colours: the browser turns these numbers into CSS colour variables.
1177      // ==================================================================
1178  
1179      // Category colour = position of the category in the alphabetical list (0..7, repeating). -1 = grey.
1180      // Example: categories "Furniture", "IT", "Medical" -> colours 0, 1, 2.
1181      private static Dictionary<string, int> BuildCategoryColors(ProcurementSnapshot snapshot)
1182      {
1183          List<string> categories = new List<string>();
1184          foreach (SupplierRecord supplier in snapshot.Suppliers)
1185          {
1186              string category = CategoryOf(supplier.Category);
1187              if (category != Uncategorized && !categories.Contains(category))
1188              {
1189                  categories.Add(category);
1190              }
1191          }
1192          categories.Sort(string.Compare);
1193          Dictionary<string, int> colors = new Dictionary<string, int>();
1194          for (int i = 0; i < categories.Count; i++)
1195          {
1196              colors[categories[i]] = i % 8;
1197          }
1198          return colors;
1199      }
1200  
1201      private static int CategoryColor(Dictionary<string, int> colors, string category)
1202      {
1203          if (colors.ContainsKey(category))
1204          {
1205              return colors[category];
1206          }
1207          return -1;
1208      }
1209  
1210      // Supplier colour: the eight most active suppliers get their own colour (0..7); everyone else is grey (-1).
1211      private static void AssignSupplierColors(List<SupplierRow> rows)
1212      {
1213          List<SupplierRow> ranked = rows
1214              .Where(row => row.Participations > 0)
1215              .OrderByDescending(row => row.Participations)
1216              .ThenBy(row => row.SupplierId)
1217              .Take(8)
1218              .ToList();
1219          for (int i = 0; i < ranked.Count; i++)
1220          {
1221              ranked[i].SupplierColor = i;
1222          }
1223      }
1224  
1225      public static SupplierPair MakePair(ProcurementSnapshot snapshot, int a, int b, int sharedRounds)
1226      {
1227          SupplierPair pair = new SupplierPair();
1228          pair.SupplierA = a;
1229          pair.SupplierB = b;
1230          pair.NameA = IntelligenceService.CleanName(snapshot.SupplierName(a));
1231          pair.NameB = IntelligenceService.CleanName(snapshot.SupplierName(b));
1232          pair.SharedRounds = sharedRounds;
1233          return pair;
1234      }
1235  
1236      private static SupplierOption ToOption(SupplierRow row)
1237      {
1238          SupplierOption option = new SupplierOption();
1239          option.SupplierId = row.SupplierId;
1240          option.Name = row.Name;
1241          option.Category = row.Category;
1242          option.Participations = row.Participations;
1243          option.CategoryColor = row.CategoryColor;
1244          return option;
1245      }
1246  
1247      // ==================================================================
1248      // Small rules used only by this calculator
1249      // ==================================================================
1250  
1251      public const string Uncategorized = "Uncategorized";
1252  
1253      // A supplier without a registered category is shown in the "Uncategorized" group.
1254      public static string CategoryOf(string? category)
1255      {
1256          if (string.IsNullOrWhiteSpace(category))
1257          {
1258              return Uncategorized;
1259          }
1260          return category.Trim();
1261      }
1262  
1263      private static bool IsApproved(SupplierStatus status)
1264      {
1265          return status == SupplierStatus.Approved;
1266      }
1267  
1268      // Adds a value to the running total of its currency (one MoneyValue per currency): currencies are never added together.
1269      private static void AddMoney(List<MoneyValue> totals, string currency, decimal value)
1270      {
1271          foreach (MoneyValue total in totals)
1272          {
1273              if (total.Currency == currency)
1274              {
1275                  total.Value += value;
1276                  total.Count++;
1277                  return;
1278              }
1279          }
1280          MoneyValue newTotal = new MoneyValue();
1281          newTotal.Currency = currency;
1282          newTotal.Value = value;
1283          newTotal.Count = 1;
1284          totals.Add(newTotal);
1285      }
1286  }
```

### 2.6 Tender calculations

**`Orange-Tendering-System/Services/Intelligence/TenderCalculator.cs` — lines 1–531**

```csharp
  1  // ORANGE INTELLIGENCE - Tab 3 "Tenders": tender lifecycle, bidding cycles, submitted offers and official awards.
  2  // Durations are calculated only between recorded timestamps: a missing milestone stays "not recorded", never estimated.
  3  // Sealed bidding cycles (bidding still open) never expose an offer: snapshot.Bids already excludes them.
  4  using Orange_Tendering_System.Models.Entities;
  5  
  6  namespace Orange_Tendering_System.Services.Intelligence;
  7  
  8  public static class TenderCalculator
  9  {
 10      // ==================================================================
 11      // 1. The Tenders tab: list of tenders + market charts
 12      // ==================================================================
 13  
 14      public static TendersData Build(ProcurementSnapshot snapshot, int minimumOffers)
 15      {
 16          TendersData data = new TendersData();
 17          data.Context = IntelligenceService.BuildContext(snapshot);
 18          data.MinimumOffers = minimumOffers;
 19  
 20          // Tender list (newest first) and the tender selected when the tab opens:
 21          // the first tender that has a closed cycle with offers, otherwise the newest tender.
 22          List<TenderDetails> tenders = BuildAllTenders(snapshot);
 23          foreach (TenderDetails tender in tenders)
 24          {
 25              TenderListItem item = new TenderListItem();
 26              item.TenderId = tender.TenderId;
 27              item.Title = tender.Title;
 28              item.Department = tender.Department;
 29              item.CreatedAt = tender.CreatedAt;
 30              item.State = tender.State;
 31              item.Status = tender.Status;
 32              item.DaysToNotification = tender.DaysToNotification;
 33              data.Tenders.Add(item);
 34  
 35              if (data.DefaultTenderId == null && HasOpenedOffers(tender))
 36              {
 37                  data.DefaultTenderId = tender.TenderId;
 38              }
 39          }
 40          if (data.DefaultTenderId == null && tenders.Count > 0)
 41          {
 42              data.DefaultTenderId = tenders[0].TenderId;
 43          }
 44  
 45          data.Competition = BuildCompetition(snapshot, minimumOffers);
 46          data.Departments = BuildDepartments(data.Competition);
 47          data.RecurringPairs = BuildRecurringPairs(snapshot);
 48          data.Awards = BuildAwardDistributions(snapshot);
 49          return data;
 50      }
 51  
 52      private static bool HasOpenedOffers(TenderDetails tender)
 53      {
 54          foreach (TenderRound round in tender.Rounds)
 55          {
 56              if (round.Analysed && round.Offers > 0)
 57              {
 58                  return true;
 59              }
 60          }
 61          return false;
 62      }
 63  
 64      // Supplier participation per closed bidding cycle (oldest first): invited vs submitted vs compliant.
 65      public static List<RoundCompetition> BuildCompetition(ProcurementSnapshot snapshot, int minimumOffers)
 66      {
 67          List<RoundCompetition> result = new List<RoundCompetition>();
 68          List<RoundRecord> rounds = snapshot.AnalyzedRounds.OrderBy(round => round.ReferenceDate).ToList();
 69          foreach (RoundRecord round in rounds)
 70          {
 71              TenderRecord? tender = snapshot.Tender(round.TenderId);
 72              RoundCompetition item = new RoundCompetition();
 73              item.RoundId = round.Id;
 74              item.TenderId = round.TenderId;
 75              item.Tender = IntelligenceService.CleanName(tender?.Title ?? "Tender #" + round.TenderId);
 76              item.RoundNumber = round.RoundNumber;
 77              item.Date = round.ReferenceDate;
 78              item.Department = IntelligenceService.CleanName(tender?.Department ?? "-");
 79  
 80              foreach (InvitationRecord invitation in snapshot.Invitations)
 81              {
 82                  if (invitation.RoundId == round.Id)
 83                  {
 84                      item.Invited++;
 85                  }
 86              }
 87              List<int> bidderIds = new List<int>();
 88              foreach (BidRecord bid in snapshot.Bids)
 89              {
 90                  if (bid.RoundId != round.Id)
 91                  {
 92                      continue;
 93                  }
 94                  item.Submitted++;
 95                  if (bid.Compliance == ComplianceStatus.Compliant)
 96                  {
 97                      item.Compliant++;
 98                  }
 99                  if (!bidderIds.Contains(bid.SupplierId))
100                  {
101                      bidderIds.Add(bid.SupplierId);
102                  }
103              }
104              bidderIds.Sort();
105              foreach (int supplierId in bidderIds)
106              {
107                  item.Bidders.Add(IntelligenceService.CleanName(snapshot.SupplierName(supplierId)));
108              }
109              // Low competition = fewer offers than IntelligenceService.MinimumOffers (3).
110              item.LowCompetition = item.Submitted < minimumOffers;
111              result.Add(item);
112          }
113          return result;
114      }
115  
116      // Average offers per closed cycle, per department (lowest average first).
117      private static List<DepartmentCompetition> BuildDepartments(List<RoundCompetition> rounds)
118      {
119          List<DepartmentCompetition> result = new List<DepartmentCompetition>();
120          foreach (RoundCompetition round in rounds)
121          {
122              DepartmentCompetition? department = null;
123              foreach (DepartmentCompetition existing in result)
124              {
125                  if (existing.Department == round.Department)
126                  {
127                      department = existing;
128                  }
129              }
130              if (department == null)
131              {
132                  department = new DepartmentCompetition();
133                  department.Department = round.Department;
134                  result.Add(department);
135              }
136              department.Rounds++;
137              department.Bids += round.Submitted;
138          }
139          foreach (DepartmentCompetition department in result)
140          {
141              department.AverageBids = Math.Round((double)department.Bids / department.Rounds, 2);
142          }
143          return result.OrderBy(department => (double)department.Bids / department.Rounds).ToList();
144      }
145  
146      // The five supplier pairs that met most often in closed cycles.
147      private static List<SupplierPair> BuildRecurringPairs(ProcurementSnapshot snapshot)
148      {
149          Dictionary<string, List<int>> sharedRounds = IntelligenceService.FindSharedRounds(snapshot);
150          List<SupplierPair> pairs = new List<SupplierPair>();
151          foreach (string key in sharedRounds.Keys)
152          {
153              int a = IntelligenceService.FirstOfPair(key);
154              int b = IntelligenceService.SecondOfPair(key);
155              pairs.Add(SupplierCalculator.MakePair(snapshot, a, b, sharedRounds[key].Count));
156          }
157          return pairs.OrderByDescending(pair => pair.SharedRounds).Take(5).ToList();
158      }
159  
160      // Official award value per currency, by supplier and by department.
161      // HHI (concentration) = sum of squared shares. Example: shares 60% and 40% -> 0.36 + 0.16 = 0.52.
162      private static List<AwardDistribution> BuildAwardDistributions(ProcurementSnapshot snapshot)
163      {
164          List<AwardRecord> awards = IntelligenceService.FindAwards(snapshot);
165          Dictionary<int, int> supplierColors = new Dictionary<int, int>();
166          foreach (SupplierRow row in SupplierCalculator.BuildStats(snapshot))
167          {
168              supplierColors[row.SupplierId] = row.SupplierColor;
169          }
170  
171          List<AwardDistribution> result = new List<AwardDistribution>();
172          foreach (AwardRecord award in awards)
173          {
174              AwardDistribution? distribution = null;
175              foreach (AwardDistribution existing in result)
176              {
177                  if (existing.Currency == award.Currency)
178                  {
179                      distribution = existing;
180                  }
181              }
182              if (distribution == null)
183              {
184                  distribution = new AwardDistribution();
185                  distribution.Currency = award.Currency;
186                  result.Add(distribution);
187              }
188              distribution.Total += award.Value;
189              distribution.Count++;
190              AddShare(distribution.BySupplier, award.SupplierName, award.SupplierId, award.Value);
191              AddShare(distribution.ByDepartment, award.Department, null, award.Value);
192          }
193  
194          foreach (AwardDistribution distribution in result)
195          {
196              double hhi = 0;
197              foreach (AwardShare share in distribution.BySupplier)
198              {
199                  share.Share = distribution.Total == 0 ? 0 : (double)(share.Value / distribution.Total);
200                  share.SupplierColor = supplierColors.ContainsKey(share.SupplierId!.Value) ? supplierColors[share.SupplierId.Value] : -1;
201                  hhi += share.Share * share.Share;
202              }
203              foreach (AwardShare share in distribution.ByDepartment)
204              {
205                  share.Share = distribution.Total == 0 ? 0 : (double)(share.Value / distribution.Total);
206              }
207              if (distribution.Total != 0)
208              {
209                  distribution.Hhi = Math.Round(hhi, 4);
210              }
211              distribution.BySupplier = distribution.BySupplier.OrderByDescending(share => share.Value).ToList();
212              distribution.ByDepartment = distribution.ByDepartment.OrderByDescending(share => share.Value).ToList();
213          }
214          return result;
215      }
216  
217      private static void AddShare(List<AwardShare> shares, string name, int? supplierId, decimal value)
218      {
219          foreach (AwardShare share in shares)
220          {
221              bool sameSupplier = supplierId != null && share.SupplierId == supplierId;
222              bool sameDepartment = supplierId == null && share.Name == IntelligenceService.CleanName(name);
223              if (sameSupplier || sameDepartment)
224              {
225                  share.Value += value;
226                  share.Count++;
227                  return;
228              }
229          }
230          AwardShare newShare = new AwardShare();
231          newShare.Name = IntelligenceService.CleanName(name);
232          newShare.SupplierId = supplierId;
233          newShare.Value = value;
234          newShare.Count = 1;
235          shares.Add(newShare);
236      }
237  
238      // ==================================================================
239      // 2. One tender: lifecycle, bidding cycles and offers
240      // ==================================================================
241  
242      public static List<TenderDetails> BuildAllTenders(ProcurementSnapshot snapshot)
243      {
244          Dictionary<int, AwardRecord> awardByTender = LatestAwardPerTender(snapshot);
245          List<TenderDetails> result = new List<TenderDetails>();
246          foreach (TenderRecord tender in snapshot.Tenders.OrderByDescending(tender => tender.CreatedAt))
247          {
248              result.Add(BuildTender(snapshot, tender, awardByTender));
249          }
250          return result;
251      }
252  
253      public static TenderDetails? BuildTender(ProcurementSnapshot snapshot, int tenderId)
254      {
255          TenderRecord? tender = snapshot.Tender(tenderId);
256          if (tender == null)
257          {
258              return null;
259          }
260          return BuildTender(snapshot, tender, LatestAwardPerTender(snapshot));
261      }
262  
263      private static Dictionary<int, AwardRecord> LatestAwardPerTender(ProcurementSnapshot snapshot)
264      {
265          Dictionary<int, AwardRecord> result = new Dictionary<int, AwardRecord>();
266          foreach (AwardRecord award in IntelligenceService.FindAwards(snapshot))
267          {
268              if (!result.ContainsKey(award.TenderId) || award.AwardedAt > result[award.TenderId].AwardedAt)
269              {
270                  result[award.TenderId] = award;
271              }
272          }
273          return result;
274      }
275  
276      private static TenderDetails BuildTender(ProcurementSnapshot snapshot, TenderRecord tender, Dictionary<int, AwardRecord> awardByTender)
277      {
278          TenderDetails details = new TenderDetails();
279          details.TenderId = tender.Id;
280          details.Title = IntelligenceService.CleanName(tender.Title);
281          details.Department = IntelligenceService.CleanName(tender.Department);
282          details.Status = tender.Status.ToString();
283          details.Currency = tender.Currency;
284          details.EstimatedValue = tender.EstimatedValue;
285          details.CreatedAt = tender.CreatedAt;
286          details.AwardedAt = tender.AwardedAt;
287          details.DaysSoFar = (snapshot.GeneratedAt - tender.CreatedAt).TotalDays;
288  
289          AwardRecord? award = awardByTender.ContainsKey(tender.Id) ? awardByTender[tender.Id] : null;
290          if (award != null)
291          {
292              details.AwardedSupplierId = award.SupplierId;
293              details.AwardedSupplier = IntelligenceService.CleanName(snapshot.SupplierName(award.SupplierId));
294              details.AwardedValue = award.Value;
295              details.AwardCurrency = award.Currency;
296          }
297          if (snapshot.WinnerNotifiedAt.ContainsKey(tender.Id))
298          {
299              details.WinnerNotifiedAt = snapshot.WinnerNotifiedAt[tender.Id];
300          }
301          details.DaysToNotification = DaysFromCreation(tender, details.WinnerNotifiedAt);
302          details.DaysToAward = DaysFromCreation(tender, tender.AwardedAt);
303  
304          // State: Completed (winner notified) > Stopped (cancelled / rejected) > Awarded > In progress.
305          if (details.WinnerNotifiedAt != null)
306          {
307              details.State = "Completed";
308          }
309          else if (tender.Status == TenderStatus.Cancelled || tender.Status == TenderStatus.Rejected)
310          {
311              details.State = "Stopped";
312          }
313          else if (tender.AwardedAt != null)
314          {
315              details.State = "Awarded";
316          }
317          else
318          {
319              details.State = "In progress";
320          }
321  
322          List<RoundRecord> rounds = new List<RoundRecord>();
323          foreach (RoundRecord round in snapshot.Rounds)
324          {
325              if (round.TenderId == tender.Id)
326              {
327                  rounds.Add(round);
328              }
329          }
330          rounds = rounds.OrderBy(round => round.RoundNumber).ToList();
331  
332          // ---- Lifecycle milestones (each from its own database column) ----
333          DateTime? firstRoundCreated = null;
334          DateTime? lastClosed = null;
335          foreach (RoundRecord round in rounds)
336          {
337              if (round.RoundNumber == 1 && (firstRoundCreated == null || round.CreatedAt < firstRoundCreated))
338              {
339                  firstRoundCreated = round.CreatedAt;
340              }
341              if (round.ClosedAt != null && (lastClosed == null || round.ClosedAt > lastClosed))
342              {
343                  lastClosed = round.ClosedAt;
344              }
345          }
346          DateTime? firstInvitation = null;
347          foreach (InvitationRecord invitation in snapshot.Invitations)
348          {
349              if (invitation.TenderId == tender.Id)
350              {
351                  details.TotalInvitations++;
352                  if (firstInvitation == null || invitation.InvitedAt < firstInvitation)
353                  {
354                      firstInvitation = invitation.InvitedAt;
355                  }
356              }
357          }
358          AddStage(details, tender, "created", "Tender created", tender.CreatedAt, "Tenders.CreatedAt");
359          AddStage(details, tender, "round", "Initial Bidding cycle created", firstRoundCreated, "TenderRounds.CreatedAt");
360          AddStage(details, tender, "invited", "First invitation", firstInvitation, "TenderInvitations.InvitedAt");
361          AddStage(details, tender, "closed", "Bidding closed", lastClosed, "TenderRounds.ClosedAt");
362          AddStage(details, tender, "awarded", "Official award", tender.AwardedAt, "Tenders.AwardedAt");
363          AddStage(details, tender, "notified", "Winner notification sent", details.WinnerNotifiedAt, "FinalDecisionNotices.SuppliersNotifiedAt");
364  
365          // ---- Bidding cycles ----
366          foreach (RoundRecord round in rounds)
367          {
368              details.Rounds.Add(BuildRound(snapshot, round, award));
369          }
370          return details;
371      }
372  
373      private static void AddStage(TenderDetails details, TenderRecord tender, string key, string label, DateTime? at, string source)
374      {
375          TenderStage stage = new TenderStage();
376          stage.Key = key;
377          stage.Label = label;
378          stage.At = at;
379          stage.Source = source;
380          if (at != null)
381          {
382              stage.DaysFromCreation = (at.Value - tender.CreatedAt).TotalDays;
383          }
384          details.Stages.Add(stage);
385      }
386  
387      private static double? DaysFromCreation(TenderRecord tender, DateTime? end)
388      {
389          if (end == null)
390          {
391              return null;
392          }
393          return Math.Round((end.Value - tender.CreatedAt).TotalDays, 1);
394      }
395  
396      private static TenderRound BuildRound(ProcurementSnapshot snapshot, RoundRecord round, AwardRecord? award)
397      {
398          TenderRound view = new TenderRound();
399          view.RoundId = round.Id;
400          view.RoundNumber = round.RoundNumber;
401          view.Status = round.Status.ToString();
402          view.Deadline = round.Deadline;
403          view.ClosedAt = round.ClosedAt;
404          view.Analysed = round.Unsealed;
405          foreach (InvitationRecord invitation in snapshot.Invitations)
406          {
407              if (invitation.RoundId == round.Id)
408              {
409                  view.Invited++;
410              }
411          }
412  
413          // A sealed cycle reports only its invitations: even an offer count would leak bidding activity.
414          if (!round.Unsealed)
415          {
416              return view;
417          }
418  
419          List<BidRecord> bids = new List<BidRecord>();
420          foreach (BidRecord bid in snapshot.Bids)
421          {
422              if (bid.RoundId == round.Id)
423              {
424                  bids.Add(bid);
425              }
426          }
427  
428          // Comparable groups: offers with recorded items and a positive total, grouped by currency + item structure.
429          Dictionary<string, List<BidRecord>> groupBids = new Dictionary<string, List<BidRecord>>();
430          List<string> groupOrder = new List<string>();
431          foreach (BidRecord bid in bids)
432          {
433              if (bid.Total <= 0 || bid.Lines.Count == 0)
434              {
435                  continue;
436              }
437              string key = bid.Currency + "#" + IntelligenceService.StructureKey(bid);
438              if (!groupBids.ContainsKey(key))
439              {
440                  groupBids[key] = new List<BidRecord>();
441                  groupOrder.Add(key);
442              }
443              groupBids[key].Add(bid);
444          }
445          Dictionary<int, int> groupIdByBid = new Dictionary<int, int>();
446          Dictionary<int, decimal> medianByGroup = new Dictionary<int, decimal>();
447          Dictionary<int, int> sizeByGroup = new Dictionary<int, int>();
448          for (int i = 0; i < groupOrder.Count; i++)
449          {
450              int groupId = i + 1;
451              List<decimal> totals = new List<decimal>();
452              foreach (BidRecord bid in groupBids[groupOrder[i]])
453              {
454                  groupIdByBid[bid.Id] = groupId;
455                  totals.Add(bid.Total);
456              }
457              medianByGroup[groupId] = IntelligenceService.Median(totals);
458              sizeByGroup[groupId] = totals.Count;
459          }
460  
461          // Offers, cheapest first.
462          foreach (BidRecord bid in bids.OrderBy(bid => bid.Total))
463          {
464              TenderOffer offer = new TenderOffer();
465              offer.BidId = bid.Id;
466              offer.SupplierId = bid.SupplierId;
467              offer.Supplier = IntelligenceService.CleanName(snapshot.SupplierName(bid.SupplierId));
468              offer.Currency = bid.Currency;
469              offer.Total = bid.Total;
470              offer.Awarded = award != null && award.RoundId == round.Id && award.SupplierId == bid.SupplierId;
471              offer.Compliance = bid.Compliance.ToString();
472              offer.DeliveryDays = bid.DeliveryDays;
473              if (!groupIdByBid.ContainsKey(bid.Id))
474              {
475                  offer.ComparisonNote = "Missing item structure or non-positive total";
476              }
477              else
478              {
479                  int groupId = groupIdByBid[bid.Id];
480                  offer.ComparisonGroup = groupId;
481                  if (sizeByGroup[groupId] < 2)
482                  {
483                      offer.ComparisonNote = "No second comparable offer";
484                  }
485                  else
486                  {
487                      offer.ComparisonNote = "Comparable within this bidding cycle and currency";
488                      decimal median = medianByGroup[groupId];
489                      if (median > 0)
490                      {
491                          // Relative to median: (offer - median) / median. Example: 1100 vs median 1000 -> +10%.
492                          offer.RelativeToMedian = (double)((bid.Total - median) / median);
493                      }
494                  }
495              }
496              view.Bids.Add(offer);
497          }
498          view.Offers = view.Bids.Count;
499  
500          // The chart shows one comparable group at a time: largest group first.
501          for (int i = 0; i < groupOrder.Count; i++)
502          {
503              int groupId = i + 1;
504              OfferGroup group = new OfferGroup();
505              group.GroupId = groupId;
506              group.Median = medianByGroup[groupId];
507              foreach (TenderOffer offer in view.Bids)
508              {
509                  if (offer.ComparisonGroup == groupId)
510                  {
511                      group.Offers.Add(offer);
512                  }
513              }
514              group.Offers = group.Offers.OrderByDescending(offer => offer.Total).ToList();
515              group.Currency = group.Offers[0].Currency;
516              group.Highest = group.Offers[0];
517              group.Lowest = group.Offers[group.Offers.Count - 1];
518              foreach (TenderOffer offer in group.Offers)
519              {
520                  if (offer.Total == group.Median)
521                  {
522                      group.MedianOwners++;
523                      group.MedianOwnerName = offer.Supplier;
524                  }
525              }
526              view.Groups.Add(group);
527          }
528          view.Groups = view.Groups.OrderByDescending(group => group.Offers.Count).ThenBy(group => group.GroupId).ToList();
529          return view;
530      }
531  }
```

### 2.7 Export

**`Orange-Tendering-System/Services/Intelligence/IntelligenceExportService.cs` — lines 1–297**

```csharp
  1  // ORANGE INTELLIGENCE - the Tenders tab downloads:
  2  //   "tender_lifecycle"  Excel  every recorded workflow event of the tender
  3  //   "tender_offers"     Excel  the effective submitted offers of one closed bidding cycle
  4  //   "tender_summary"    PDF    lifecycle + offers in one report
  5  // Each file is rebuilt on the server from the user's authorized data. The browser only sends identifiers
  6  // (tender id, cycle id, period), never rows or totals, so a user cannot export data outside their scope.
  7  using System.Globalization;
  8  using System.Text.RegularExpressions;
  9  using Microsoft.EntityFrameworkCore;
 10  using Orange_Tendering_System.Data;
 11  using Orange_Tendering_System.Models.Entities;
 12  
 13  namespace Orange_Tendering_System.Services.Intelligence;
 14  
 15  public class IntelligenceExportService
 16  {
 17      private readonly IntelligenceService intelligence;
 18      private readonly ApplicationDbContext db;
 19      private readonly ReportExport writer;
 20  
 21      public IntelligenceExportService(IntelligenceService intelligence, ApplicationDbContext db, ReportExport writer)
 22      {
 23          this.intelligence = intelligence;
 24          this.db = db;
 25          this.writer = writer;
 26      }
 27  
 28      public async Task<ExportFile> BuildAsync(ExportRequest request, CancellationToken ct)
 29      {
 30          // ---- 1. Check the request ----
 31          string format = (request.Format ?? "").ToLowerInvariant();
 32          string reportType = request.ReportType ?? "";
 33          if (format != "xlsx" && format != "pdf" && format != "docx")
 34          {
 35              throw new IntelligenceExportException("Choose a PDF, Excel or Word file.");
 36          }
 37          if (reportType != "tender_lifecycle" && reportType != "tender_offers" && reportType != "tender_summary")
 38          {
 39              throw new IntelligenceExportException("Choose a supported tender report.");
 40          }
 41  
 42          // ---- 2. Load fresh authorized data (refresh = true: an export never uses a cached copy) ----
 43          ProcurementSnapshot snapshot = await intelligence.LoadSnapshotAsync(request.From, request.To, true, ct);
 44          TenderRecord? tender = request.TenderId == null ? null : snapshot.Tender(request.TenderId.Value);
 45          if (tender == null)
 46          {
 47              throw new IntelligenceExportException("The selected tender is unavailable in your reporting scope and period.");
 48          }
 49          if (request.RoundId != null)
 50          {
 51              RoundRecord? round = snapshot.Round(request.RoundId.Value);
 52              if (round == null || round.TenderId != tender.Id)
 53              {
 54                  throw new IntelligenceExportException("The selected round is unavailable for this tender and period.");
 55              }
 56          }
 57          TenderDetails details = TenderCalculator.BuildTender(snapshot, tender.Id)!;
 58  
 59          // ---- 3. Describe the report ----
 60          string title = "Tender report · #" + tender.Id + " " + tender.Title;
 61          if (reportType == "tender_lifecycle")
 62          {
 63              title = "Tender Lifecycle · #" + tender.Id + " " + tender.Title;
 64          }
 65          else if (reportType == "tender_offers")
 66          {
 67              title = "Submitted offers · #" + tender.Id + " " + tender.Title;
 68          }
 69          string cycleText = request.RoundId == null ? "all eligible closed bidding cycles" : "#" + request.RoundId;
 70          ReportModel model = new ReportModel
 71          {
 72              Key = "orange-intelligence",
 73              Title = title,
 74              GeneratedAt = DateTime.UtcNow,
 75              SummaryLast = true,
 76              Description = "Authorized scope: " + snapshot.Scope.Label + ". Period: " + snapshot.Period.Label
 77                  + ". Tenders: #" + tender.Id + " " + tender.Title + ". Bidding cycle: " + cycleText + "."
 78          };
 79          model.Kpis.Add(new ReportKpi("Date basis", "Bidding cycles are included by deadline, closure or creation date in that order. Offers in cycles still open for bidding remain sealed and are never shown."));
 80          model.Kpis.Add(new ReportKpi("Tender ID", tender.Id.ToString(CultureInfo.InvariantCulture)));
 81          model.Kpis.Add(new ReportKpi("Tender status", tender.Status.ToString()));
 82  
 83          // ---- 4. Add the tables ----
 84          if (reportType == "tender_lifecycle" || reportType == "tender_summary")
 85          {
 86              await AddLifecycleTableAsync(model, details, snapshot, ct);
 87          }
 88          if (reportType == "tender_offers" || reportType == "tender_summary")
 89          {
 90              AddOffersTable(model, details, request.RoundId);
 91          }
 92  
 93          List<string> sheets = new List<string>();
 94          bool anyRows = false;
 95          foreach (ReportTable table in model.Tables)
 96          {
 97              sheets.Add(table.Title + ": " + table.Rows.Count);
 98              if (table.Rows.Count > 0)
 99              {
100                  anyRows = true;
101              }
102          }
103          model.Kpis.Add(new ReportKpi("Records", string.Join("; ", sheets)));
104          if (!anyRows)
105          {
106              model.Kpis.Add(new ReportKpi("Result", "No authorized records match these filters. Nothing was omitted or invented."));
107          }
108  
109          // ---- 5. Write the file (ReportExport is the shared Excel / Word / PDF writer of the system) ----
110          ExportFile file = new ExportFile();
111          file.Sheets = sheets;
112          if (format == "xlsx")
113          {
114              file.Bytes = writer.ToXlsx(model);
115              file.ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
116          }
117          else if (format == "docx")
118          {
119              file.Bytes = writer.ToDocx(model);
120              file.ContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
121          }
122          else
123          {
124              file.Bytes = writer.ToPdf(model);
125              file.ContentType = "application/pdf";
126          }
127          string stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
128          file.FileName = "Orange-Intelligence-" + reportType.Replace('_', '-') + "-" + tender.Id + "-" + stamp + "-" + Guid.NewGuid().ToString("N") + "." + format;
129          return file;
130      }
131  
132      // One row per recorded workflow event: cycles, offers, committee cycles, reports, final decision, award.
133      private async Task AddLifecycleTableAsync(ReportModel model, TenderDetails tender, ProcurementSnapshot snapshot, CancellationToken ct)
134      {
135          List<int> roundIds = new List<int>();
136          foreach (TenderRound round in tender.Rounds)
137          {
138              roundIds.Add(round.RoundId);
139          }
140  
141          // EF Core: three read-only queries for the evaluation workflow records of these cycles.
142          var committees = await db.CommitteeRounds.AsNoTracking()
143              .Where(x => roundIds.Contains(x.TenderRoundId))
144              .Select(x => new { x.TenderRoundId, x.CommitteeType, x.RoundNumber, x.Status, x.StartedAt, x.CompletedAt })
145              .ToListAsync(ct);
146          var reports = await db.TenderReports.AsNoTracking()
147              .Where(x => roundIds.Contains(x.TenderRoundId))
148              .Select(x => new { x.TenderRoundId, x.ReportType, x.VersionNumber, x.Status, x.SubmittedAt, x.ReviewedAt })
149              .ToListAsync(ct);
150          var notices = await db.FinalDecisionNotices.AsNoTracking()
151              .Where(x => x.TenderId == tender.TenderId)
152              .Select(x => new { x.CreatedAt, x.Status, x.Outcome, x.RejectedAt, x.ManagerAcknowledgedAt, x.SuppliersNotifiedAt })
153              .ToListAsync(ct);
154  
155          List<string[]> rows = new List<string[]>();
156          const string bidding = "Bidding & Supplier Submissions";
157          const string evaluation = "Technical & Financial Evaluation";
158          const string topCommittee = "Top Committee Final Decision";
159          const string managerReview = "Sourcing Manager Review";
160          const string notification = "Award & Supplier Notification";
161  
162          AddEvent(rows, tender, bidding, "Tender created", "Recorded", tender.CreatedAt, "Tenders.CreatedAt", null);
163          foreach (TenderRound round in tender.Rounds)
164          {
165              RoundRecord source = snapshot.Round(round.RoundId)!;
166              string cycle = IntelligenceService.CycleLabel(round.RoundNumber);
167              AddEvent(rows, tender, bidding, cycle + " created", "Recorded", source.CreatedAt, "TenderRounds.CreatedAt", round.RoundId);
168              AddEvent(rows, tender, bidding, cycle + " deadline", "Recorded", round.Deadline, "TenderRounds.Deadline", round.RoundId);
169              AddEvent(rows, tender, bidding, cycle + " closed", "Recorded", round.ClosedAt, "TenderRounds.ClosedAt", round.RoundId);
170              string counts = round.Analysed
171                  ? round.Invited + " invited; " + round.Offers + " effective submitted offers"
172                  : round.Invited + " invited; submitted offers sealed";
173              AddEvent(rows, tender, bidding, cycle + " invitations / submitted offers", counts, null, "TenderInvitations / effective Bids", round.RoundId);
174              if (round.Analysed)
175              {
176                  foreach (BidRecord bid in snapshot.Bids.Where(bid => bid.RoundId == round.RoundId).OrderBy(bid => bid.SubmittedAt))
177                  {
178                      AddEvent(rows, tender, bidding, "Bid #" + bid.Id + " · " + snapshot.SupplierName(bid.SupplierId), "Submitted", bid.SubmittedAt, "Bids.SubmittedAt", round.RoundId);
179                  }
180              }
181          }
182          foreach (var committee in committees.OrderBy(x => x.StartedAt))
183          {
184              string stage = committee.CommitteeType == CommitteeType.Evaluation ? evaluation : topCommittee;
185              string status = "Current cycle status: " + committee.Status;
186              AddEvent(rows, tender, stage, "Committee cycle " + committee.RoundNumber + " started", status, committee.StartedAt, "CommitteeRounds.StartedAt", committee.TenderRoundId);
187              AddEvent(rows, tender, stage, "Committee cycle " + committee.RoundNumber + " completed", status, committee.CompletedAt, "CommitteeRounds.CompletedAt", committee.TenderRoundId);
188          }
189          foreach (var report in reports.OrderBy(x => x.SubmittedAt))
190          {
191              string stage = evaluation;
192              if (report.ReportType == ReportType.ExecutiveSummary)
193              {
194                  stage = topCommittee;
195              }
196              else if (report.ReportType == ReportType.FullEvaluation)
197              {
198                  stage = managerReview;
199              }
200              string name = report.ReportType + " report v" + report.VersionNumber;
201              AddEvent(rows, tender, stage, name + " submitted", report.Status.ToString(), report.SubmittedAt, "TenderReports.SubmittedAt", report.TenderRoundId);
202              AddEvent(rows, tender, stage, name + " reviewed", report.Status.ToString(), report.ReviewedAt, "TenderReports.ReviewedAt", report.TenderRoundId);
203          }
204          foreach (var notice in notices.OrderBy(x => x.CreatedAt))
205          {
206              AddEvent(rows, tender, topCommittee, "Decision notice created", notice.Outcome.ToString(), notice.CreatedAt, "FinalDecisionNotices.CreatedAt", null);
207              if (notice.RejectedAt != null)
208              {
209                  AddEvent(rows, tender, topCommittee, "Decision rejected", notice.Outcome.ToString(), notice.RejectedAt, "FinalDecisionNotices.RejectedAt", null);
210              }
211              AddEvent(rows, tender, managerReview, "Manager acknowledged final decision", notice.Status.ToString(), notice.ManagerAcknowledgedAt, "FinalDecisionNotices.ManagerAcknowledgedAt", null);
212              AddEvent(rows, tender, notification, "Supplier notification sent", notice.Status.ToString(), notice.SuppliersNotifiedAt, "FinalDecisionNotices.SuppliersNotifiedAt", null);
213          }
214          AddEvent(rows, tender, notification, "Official award", tender.Status, tender.AwardedAt, "Tenders.AwardedAt", null);
215  
216          string[] columns = { "Workflow stage", "Bidding cycle ID", "Event", "Recorded status / count", "Recorded at (UTC)", "Days from tender creation", "Recorded in" };
217          model.Tables.Add(new ReportTable("lifecycle", "Recorded tender lifecycle", columns, rows));
218          string duration = "Not available: supplier notification is not recorded";
219          if (tender.DaysToNotification != null)
220          {
221              duration = tender.DaysToNotification.Value.ToString("0.###", CultureInfo.InvariantCulture) + " days, tender creation to supplier notification";
222          }
223          model.Kpis.Add(new ReportKpi("Lifecycle duration", duration));
224      }
225  
226      private static void AddEvent(List<string[]> rows, TenderDetails tender, string stage, string eventName, string status, DateTime? at, string source, int? roundId)
227      {
228          string recordedAt = "Not recorded";
229          string days = "";
230          if (at != null)
231          {
232              recordedAt = at.Value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
233              days = (at.Value - tender.CreatedAt).TotalDays.ToString("0.###", CultureInfo.InvariantCulture);
234          }
235          string cycle = roundId == null ? "" : roundId.Value.ToString(CultureInfo.InvariantCulture);
236          rows.Add(new string[] { stage, cycle, eventName, status, recordedAt, days, ReadableSource(source) });
237      }
238  
239      // "TenderRounds.ClosedAt" -> "Tender rounds · closed at": readable, still traceable to the database column.
240      private static string ReadableSource(string field)
241      {
242          string[] parts = field.Split('.', 2);
243          List<string> readable = new List<string>();
244          foreach (string part in parts)
245          {
246              readable.Add(Regex.Replace(part, "(?<=[a-z])(?=[A-Z])", " ").ToLowerInvariant());
247          }
248          string text = string.Join(" · ", readable);
249          if (text.Length == 0)
250          {
251              return field;
252          }
253          return char.ToUpperInvariant(text[0]) + text.Substring(1);
254      }
255  
256      // The offers of one closed cycle (roundId), or of every closed cycle of the tender (roundId = null).
257      private static void AddOffersTable(ReportModel model, TenderDetails tender, int? roundId)
258      {
259          List<string[]> rows = new List<string[]>();
260          int cycles = 0;
261          foreach (TenderRound round in tender.Rounds)
262          {
263              if (!round.Analysed || (roundId != null && round.RoundId != roundId))
264              {
265                  continue;
266              }
267              cycles++;
268              foreach (TenderOffer offer in round.Bids)
269              {
270                  rows.Add(new string[]
271                  {
272                      tender.TenderId.ToString(CultureInfo.InvariantCulture),
273                      round.RoundId.ToString(CultureInfo.InvariantCulture),
274                      round.RoundNumber.ToString(CultureInfo.InvariantCulture),
275                      offer.BidId.ToString(CultureInfo.InvariantCulture),
276                      offer.SupplierId.ToString(CultureInfo.InvariantCulture),
277                      offer.Supplier,
278                      offer.Currency,
279                      offer.Total.ToString("0.00", CultureInfo.InvariantCulture),
280                      offer.ComparisonGroup == null ? "" : offer.ComparisonGroup.Value.ToString(CultureInfo.InvariantCulture),
281                      offer.RelativeToMedian == null ? "" : offer.RelativeToMedian.Value.ToString("0.######", CultureInfo.InvariantCulture),
282                      offer.ComparisonNote,
283                      offer.Compliance,
284                      offer.DeliveryDays == null ? "" : offer.DeliveryDays.Value.ToString(CultureInfo.InvariantCulture),
285                      offer.Awarded ? "Yes" : "No"
286                  });
287              }
288          }
289          if (roundId != null && cycles == 0)
290          {
291              throw new IntelligenceExportException("This round has no accessible closed offers.");
292          }
293          string[] columns = { "Tender ID", "Bidding cycle ID", "Bidding cycle number", "Bid ID", "Supplier ID", "Supplier", "Currency", "Total offer", "Comparison group", "Relative to group median", "Comparison note", "Compliance", "Delivery days", "Official award" };
294          model.Tables.Add(new ReportTable("offers", "Effective submitted offers by closed bidding cycle", columns, rows));
295          model.Kpis.Add(new ReportKpi("Offer scope", rows.Count + " effective submitted offers across " + cycles + " closed bidding cycles; currencies and item structures remain separate."));
296      }
297  }
```

### 2.8 Registration in `Program.cs`

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

`AddScoped` = one `IntelligenceService` object per HTTP request. That is why the private field `scope` can remember the user's scope for the rest of the request.

### 2.9 Razor: where the JavaScript finds the URLs and the anti-forgery token

**`Orange-Tendering-System/Views/Intelligence/Index.cshtml` — lines 28–36**

```cshtml
28  <div class="radar oi" id="radar" data-radar data-tab="overview"
29       data-csrf="@Antiforgery.GetAndStoreTokens(Context).RequestToken"
30       data-ui-language="@(arabic ? "ar" : "en")"
31       data-url-overview="@U("Overview")"
32       data-url-suppliers="@U("Suppliers")" data-url-supplier-details="@U("SupplierDetails")"
33       data-url-compare-suppliers="@U("CompareSuppliers")" data-url-simulate="@U("Simulate")"
34       data-url-tenders="@U("Tenders")" data-url-tender-details="@U("TenderDetails")"
35       data-url-export="@U("Export")"
36       data-url-offer-record="@Url.Action("Details", "Bids", new { area = "SourcingOfficer" })">
```

### 2.10 JavaScript that talks to the server

**Requests (every call to the controller goes through these):**

**`Orange-Tendering-System/wwwroot/js/intelligence-page.js` — lines 101–155**

```javascript
101  // buildUrl('/Intelligence/Suppliers', { from: '2026-01-01', category: 'IT' }) -> '/Intelligence/Suppliers?from=2026-01-01&category=IT'
102  function buildUrl(url, params) {
103    var query = new URLSearchParams();
104    for (var key in params) {
105      if (params[key] !== null && params[key] !== undefined) {
106        query.append(key, params[key]);
107      }
108    }
109    return url + '?' + query.toString();
110  }
111  
112  // The error message sent by the controller ({ error: "..." }), or a general message.
113  async function readErrorMessage(response) {
114    var message = tr('Request failed', 'تعذر تنفيذ الطلب') + ' (' + response.status + ')';
115    try {
116      var body = await response.json();
117      if (body && body.error) {
118        message = body.error;
119      }
120    } catch (error) {
121      // The answer was not JSON (for example an HTML error page): keep the general message.
122    }
123    return message;
124  }
125  
126  // GET request that returns JSON. Throws an Error with a readable message when the server answers with an error.
127  async function getJson(url, params) {
128    var response = await fetch(buildUrl(url, params), { credentials: 'same-origin' });
129    if (!response.ok || response.redirected) {
130      throw new Error(await readErrorMessage(response));
131    }
132    return await response.json();
133  }
134  
135  // POST request with a JSON body. The RequestVerificationToken header is the anti-forgery token
136  // required by [AutoValidateAntiforgeryToken] on IntelligenceController.
137  async function postRequest(url, body) {
138    var response = await fetch(url, {
139      method: 'POST',
140      credentials: 'same-origin',
141      headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': page.dataset.csrf },
142      body: JSON.stringify(body)
143    });
144    if (!response.ok) {
145      throw new Error(await readErrorMessage(response));
146    }
147    return response;
148  }
149  
150  async function postJson(url, body) {
151    var response = await postRequest(url, body);
152    return await response.json();
153  }
154  
155  // Asks the server for an Excel/PDF file and saves it (used by the Tenders tab).
```

**Export download, period and status helpers:**

**`Orange-Tendering-System/wwwroot/js/intelligence-page.js` — lines 155–213**

```javascript
155  // Asks the server for an Excel/PDF file and saves it (used by the Tenders tab).
156  async function downloadFile(request) {
157    try {
158      var response = await postRequest(page.dataset.urlExport, request);
159      var blob = await response.blob();
160      var fileName = 'Orange-Intelligence.' + request.format;
161      var disposition = response.headers.get('content-disposition') || '';
162      var match = disposition.match(/filename="?([^";]+)/);
163      if (match) {
164        fileName = match[1];
165      }
166      var link = document.createElement('a');
167      link.href = URL.createObjectURL(blob);
168      link.download = fileName;
169      document.body.appendChild(link);
170      link.click();
171      link.remove();
172      setTimeout(function () { URL.revokeObjectURL(link.href); }, 4000);
173      showToast(request.format.toUpperCase() + ' ' + tr('file downloaded.', 'تم تنزيل الملف.'));
174    } catch (error) {
175      showStatus(tr('Export failed: ', 'فشل التصدير: ') + error.message, true);
176    }
177  }
178  
179  // ======================================================================
180  // 3. Context bar (scope, period, status) and toast
181  // ======================================================================
182  
183  // The date inputs of the context bar. Empty = no limit.
184  function periodParams() {
185    return {
186      from: document.getElementById('rd-from').value,
187      to: document.getElementById('rd-to').value
188    };
189  }
190  
191  function showStatus(text, isError) {
192    var status = document.getElementById('ctx-status');
193    status.textContent = text || '';
194    status.classList.toggle('is-error', isError === true);
195  }
196  
197  // Every tab answer contains a "context" object (PageContext in IntelligenceModels.cs).
198  function showContext(context) {
199    document.getElementById('ctx-scope').textContent = context.scope || tr('Authorized scope', 'النطاق المصرح');
200    document.getElementById('ctx-demo').hidden = !context.isDemo;
201    if (context.tenderCount === 0 && !context.isWholeMarket) {
202      showStatus(tr('No authorized tenders in this scope. Data appears when a tender of your department or committee falls inside the period.',
203        'لا توجد عطاءات مصرح بها في هذا النطاق. تظهر البيانات عندما يقع عطاء لجهتك أو لجنتك ضمن الفترة.'));
204    } else if (context.analyzedRounds === 0) {
205      showStatus(tr('No closed bidding cycle falls inside this period - every supplier in scope is still shown. Widen the period for patterns.',
206        'لا توجد دورة عطاء مغلقة ضمن هذه الفترة - يبقى كل الموردين ظاهرين. وسّع الفترة لرؤية الأنماط.'));
207    } else {
208      showStatus('');
209    }
210  }
211  
212  // The chip "2026-01-01 → 2026-06-30 ×" under the period inputs.
213  function renderPeriodChip() {
```

**Tabs and page start:**

**`Orange-Tendering-System/wwwroot/js/intelligence-page.js` — lines 266–284**

```javascript
266  // Shows one tab panel and hides the three others, then lets the tab file load or redraw its data.
267  function showTab(tab) {
268    if (PAGE_TABS.indexOf(tab) < 0) {
269      tab = 'overview';
270    }
271    currentTab = tab;
272    page.dataset.tab = tab;
273  
274    var buttons = document.querySelectorAll('[data-tab-link]');
275    for (var i = 0; i < buttons.length; i++) {
276      var selected = buttons[i].dataset.tabLink === tab;
277      buttons[i].setAttribute('aria-selected', selected ? 'true' : 'false');
278      buttons[i].tabIndex = selected ? 0 : -1;
279    }
280    var panels = document.querySelectorAll('[data-tab-panel]');
281    for (var j = 0; j < panels.length; j++) {
282      panels[j].hidden = panels[j].dataset.tabPanel !== tab;
283    }
284    history.replaceState(null, '', '#' + tab);
```

**`Orange-Tendering-System/wwwroot/js/intelligence-page.js` — lines 486–555**

```javascript
486  // intelligence-ai.js (AI Studio, unchanged) expects:
487  //   window.OrangeRadar.data.suppliers  - the authorized suppliers for its drop-down list
488  //   window.OrangeRadar.setTab('studio') - to open its tab
489  //   a "radar:data" event whenever the supplier list is (re)loaded
490  window.OrangeRadar = {
491    data: null,
492    setTab: function (tab) { showTab(tab); }
493  };
494  
495  function shareSuppliersWithAiStudio(suppliers) {
496    window.OrangeRadar.data = { suppliers: suppliers };
497    window.dispatchEvent(new CustomEvent('radar:data', { detail: { suppliers: suppliers } }));
498  }
499  
500  // When the page opens on another tab, AI Studio still needs its supplier list.
501  async function loadSupplierListForAiStudio() {
502    try {
503      var data = await getJson(page.dataset.urlSuppliers, periodParams());
504      shareSuppliersWithAiStudio(data.allSuppliers);
505    } catch (error) {
506      showStatus(tr('The supplier list could not load: ', 'تعذر تحميل قائمة الموردين: ') + error.message, true);
507    }
508  }
509  
510  // Redraws the visible tab with the data it already has (used after a light/dark theme change).
511  function redrawCurrentTab() {
512    if (currentTab === 'overview') {
513      openOverviewTab();
514    } else if (currentTab === 'suppliers') {
515      openSuppliersTab();
516    } else if (currentTab === 'tenders') {
517      openTendersTab();
518    }
519  }
520  
521  function startIntelligencePage() {
522    // Tab buttons, and links such as "Open Tenders" that jump to a tab.
523    page.addEventListener('click', function (event) {
524      var tabButton = event.target.closest('[data-tab-link], [data-tab-jump]');
525      if (tabButton) {
526        showTab(tabButton.dataset.tabLink || tabButton.dataset.tabJump);
527      }
528    });
529    document.getElementById('page-tabs').addEventListener('keydown', onTabKeyDown);
530    // The address bar hash changed without a reload (for example a link to /Intelligence#tenders): show that tab.
531    window.addEventListener('hashchange', function () {
532      if (readTabFromHash() !== currentTab) {
533        showTab(readTabFromHash());
534      }
535    });
536  
537    // Period and refresh button.
538    document.getElementById('rd-from').addEventListener('change', function () { onPeriodChanged(false); });
539    document.getElementById('rd-to').addEventListener('change', function () { onPeriodChanged(false); });
540    document.getElementById('ctx-refresh').addEventListener('click', function () { onPeriodChanged(true); });
541    window.addEventListener('orange:themechange', function () { requestAnimationFrame(redrawCurrentTab); });
542  
543    // Each tab file attaches the events of its own buttons and selects.
544    setupOverviewEvents();
545    setupSuppliersEvents();
546    setupTendersEvents();
547  
548    showTab(readTabFromHash());
549    if (currentTab !== 'suppliers') {
550      loadSupplierListForAiStudio();
551    }
552  }
553  
554  // DOMContentLoaded fires after all the page scripts have loaded, so every function above already exists.
555  document.addEventListener('DOMContentLoaded', startIntelligencePage);
```

**Overview loading:**

**`Orange-Tendering-System/wwwroot/js/intelligence-overview.js` — lines 22–37**

```javascript
22  async function loadOverview(refresh) {
23    showStatus(tr('Running the analysis on recorded procurement data…', 'جارٍ تحليل بيانات المشتريات المسجلة…'));
24    var params = periodParams();
25    params.refresh = refresh ? 'true' : 'false';
26    try {
27      overviewData = await getJson(page.dataset.urlOverview, params);
28      showContext(overviewData.context);
29      if (currentTab === 'overview') {
30        renderOverview(overviewData);
31      }
32    } catch (error) {
33      showStatus(tr('The analysis could not load: ', 'تعذر تحميل التحليل: ') + error.message, true);
34    }
35  }
36  
37  function renderOverview(data) {
```

**Suppliers: loading, filters, details:**

**`Orange-Tendering-System/wwwroot/js/intelligence-suppliers.js` — lines 46–91**

```javascript
46  async function loadSuppliers(refresh) {
47    showStatus(tr('Running the analysis on recorded procurement data…', 'جارٍ تحليل بيانات المشتريات المسجلة…'));
48    var params = periodParams();
49    params.department = supplierFilters.department;
50    params.category = supplierFilters.category;
51    params.activity = supplierFilters.activity;
52    params.search = supplierFilters.search;
53    params.refresh = refresh ? 'true' : 'false';
54  
55    // When the user types fast, several requests can be running: only the newest answer is drawn.
56    suppliersRequestNumber++;
57    var myRequest = suppliersRequestNumber;
58    try {
59      var data = await getJson(page.dataset.urlSuppliers, params);
60      if (myRequest !== suppliersRequestNumber) {
61        return;
62      }
63      suppliersData = data;
64      supplierFilters = data.filters;
65      showContext(data.context);
66      shareSuppliersWithAiStudio(data.allSuppliers);
67      if (currentTab === 'suppliers') {
68        renderSuppliers(data);
69      }
70      if (selectedSupplierId !== null && supplierDetails === null) {
71        openSupplierDetails(selectedSupplierId);
72      }
73    } catch (error) {
74      showStatus(tr('The analysis could not load: ', 'تعذر تحميل التحليل: ') + error.message, true);
75    }
76  }
77  
78  // Draws every part of the tab from one SuppliersData answer.
79  function renderSuppliers(data) {
80    renderSupplierFilters(data);
81    renderSupplierKpis(data);
82    renderSupplierUniverse(data);
83    renderCategoryDonut(data);
84    renderParticipationTrend(data);
85    renderSupplierTable(data);
86    renderSupplierDetails();
87    renderSupplierInsights(data);
88    renderSupplierRelationships(data);
89    renderComparisonControls();
90    renderComparison();
91    renderScenarioPick(data);
```

**`Orange-Tendering-System/wwwroot/js/intelligence-suppliers.js` — lines 118–121**

```javascript
118  // A filter changed: ask the server again (the server applies the filters and recalculates every number).
119  function setSupplierFilter(name, value) {
120    supplierFilters[name] = value;
121    loadSuppliers(false);
```

**`Orange-Tendering-System/wwwroot/js/intelligence-suppliers.js` — lines 164–211**

```javascript
164  var universeStarted = false;
165  
166  // Gives the 3D engine the latest /Intelligence/Suppliers answer. The engine only draws: every number came from the server.
167  function renderSupplierUniverse(data) {
168    if (!window.SupplierUniverse) {
169      return;
170    }
171    if (!universeStarted) {
172      window.SupplierUniverse.init(document.getElementById('sup-universe'), {
173        onSupplierClick: onUniverseSupplierClick,
174        onCategoryClick: toggleCategoryFilter,
175        colorOf: categoryColor
176      });
177      universeStarted = true;
178    }
179    var shownIds = [];
180    for (var i = 0; i < data.suppliers.length; i++) {
181      shownIds.push(data.suppliers[i].supplierId);
182    }
183    window.SupplierUniverse.update({
184      suppliers: data.allSuppliers,           // every supplier in the authorized scope = the dots
185      shownIds: shownIds,                     // the suppliers that pass the filters = the bright dots
186      rows: data.suppliers,                   // their statistics, for the hover card
187      category: data.filters.category,        // a chosen category gathers into one cluster
188      pairs: data.pairs,                      // competing pairs = the arcs of the Competition view
189      competitionMinimum: data.competitionMinimum
190    });
191  }
192  
193  // A click on a dot opens its details; clicking the selected dot again (or Escape) closes them.
194  function onUniverseSupplierClick(supplierId) {
195    if (supplierId === null) {
196      closeSupplierDetails();
197    } else {
198      openSupplierDetails(supplierId);
199    }
200  }
201  
202  // Tells the 3D engine which supplier the details panel shows, and its competitors once they are loaded.
203  function showSelectedSupplierInUniverse() {
204    if (!window.SupplierUniverse) {
205      return;
206    }
207    var competitors = [];
208    if (supplierDetails !== null && supplierDetails.supplier.supplierId === selectedSupplierId) {
209      competitors = supplierDetails.competitors;
210    }
211    window.SupplierUniverse.selectSupplier(selectedSupplierId, competitors);
```

**`Orange-Tendering-System/wwwroot/js/intelligence-suppliers.js` — lines 350–378**

```javascript
350  async function openSupplierDetails(supplierId) {
351    selectedSupplierId = supplierId;
352    markSelectedRow();
353    showSelectedSupplierInUniverse();
354    document.getElementById('sup-details-empty').hidden = false;
355    document.getElementById('sup-details-content').hidden = true;
356    document.getElementById('sup-details-empty').querySelector('b').textContent = tr('Loading supplier…', 'جارٍ تحميل المورد…');
357    try {
358      var params = periodParams();
359      params.supplierId = supplierId;
360      supplierDetails = await getJson(page.dataset.urlSupplierDetails, params);
361      renderSupplierDetails();
362      showSelectedSupplierInUniverse();
363      document.getElementById('sup-details-title').focus();
364    } catch (error) {
365      supplierDetails = null;
366      document.getElementById('sup-details-empty').querySelector('b').textContent = error.message;
367    }
368  }
369  
370  function closeSupplierDetails() {
371    selectedSupplierId = null;
372    supplierDetails = null;
373    markSelectedRow();
374    renderSupplierDetails();
375    showSelectedSupplierInUniverse();
376  }
377  
378  function markSelectedRow() {
```

**Suppliers: comparison and what-if:**

**`Orange-Tendering-System/wwwroot/js/intelligence-suppliers.js` — lines 814–854**

```javascript
814  async function loadComparison() {
815    comparison.rows = [];
816    var wantedTender = comparison.tenderId;
817    comparison.tenderId = null;
818    renderComparisonControls();
819    if (comparison.a === null || comparison.b === null) {
820      renderComparison();
821      return;
822    }
823    renderComparison(tr('Finding shared tenders…', 'جارٍ البحث عن العطاءات المشتركة…'));
824    try {
825      var rows = await getJson(page.dataset.urlCompareSuppliers, { supplierA: comparison.a, supplierB: comparison.b });
826      comparison.rows = rows;
827      // Keep the tender chosen before if it is still there; with only one shared tender, choose it.
828      for (var i = 0; i < rows.length; i++) {
829        if (rows[i].tenderId === wantedTender) {
830          comparison.tenderId = wantedTender;
831        }
832      }
833      if (comparison.tenderId === null && rows.length === 1) {
834        comparison.tenderId = rows[0].tenderId;
835      }
836      renderComparisonControls();
837      if (rows.length === 0) {
838        renderComparison(tr('These suppliers have no eligible shared tenders. Both must have submitted in the same closed bidding cycle.',
839          'لا توجد عطاءات مشتركة مؤهلة لهذين الموردين. يجب أن يقدم كلاهما عرضاً في دورة العطاء المغلقة نفسها.'));
840      } else {
841        renderComparison();
842      }
843    } catch (error) {
844      renderComparison(tr('The comparison could not be loaded. Select a supplier again to retry.', 'تعذر تحميل المقارنة. أعد اختيار أحد الموردين للمحاولة مجدداً.'));
845    }
846  }
847  
848  // Used by "Compare offers" in the details panel and in the relationships table.
849  function compareSuppliers(supplierA, supplierB) {
850    comparison.a = supplierA;
851    comparison.b = supplierB;
852    comparison.tenderId = null;
853    loadComparison();
854    document.getElementById('cmp-section').scrollIntoView({ behavior: 'smooth', block: 'start' });
```

**`Orange-Tendering-System/wwwroot/js/intelligence-suppliers.js` — lines 1040–1061**

```javascript
1040  async function runSimulation() {
1041    var result = document.getElementById('sim-result');
1042    var ids = [];
1043    var inputs = document.querySelectorAll('#sim-pick input:checked');
1044    for (var i = 0; i < inputs.length; i++) {
1045      ids.push(Number(inputs[i].value));
1046    }
1047    if (ids.length === 0) {
1048      result.innerHTML = '<p class="rd-muted">' + tr('Choose at least one supplier to exclude.', 'اختر مورداً واحداً على الأقل لاستبعاده.') + '</p>';
1049      return;
1050    }
1051    result.innerHTML = '<p class="rd-muted">' + tr('Replaying history without the chosen suppliers…', 'جارٍ إعادة التاريخ دون الموردين المختارين…') + '</p>';
1052    try {
1053      var period = periodParams();
1054      var simulation = await postJson(page.dataset.urlSimulate, { from: period.from, to: period.to, supplierIds: ids });
1055      result.innerHTML = simulationHtml(simulation);
1056    } catch (error) {
1057      result.innerHTML = '<p class="rd-note">' + escapeHtml(error.message) + '</p>';
1058    }
1059  }
1060  
1061  function beforeAfterHtml(label, before, after) {
```

**Tenders: loading, selection, details, export:**

**`Orange-Tendering-System/wwwroot/js/intelligence-tenders.js` — lines 47–137**

```javascript
 47  async function loadTenders(refresh) {
 48    showStatus(tr('Running the analysis on recorded procurement data…', 'جارٍ تحليل بيانات المشتريات المسجلة…'));
 49    var params = periodParams();
 50    params.refresh = refresh ? 'true' : 'false';
 51    try {
 52      tendersData = await getJson(page.dataset.urlTenders, params);
 53      showContext(tendersData.context);
 54      if (pendingTender !== null) {
 55        applyPendingTender();
 56        return;
 57      }
 58      if (!findTenderInList(selectedTenderId)) {
 59        selectedTenderId = tendersData.defaultTenderId;
 60      }
 61      renderTendersTab();
 62      loadTenderDetails(selectedTenderId);
 63    } catch (error) {
 64      showStatus(tr('The analysis could not load: ', 'تعذر تحميل التحليل: ') + error.message, true);
 65    }
 66  }
 67  
 68  // Opens the tender requested by another tab. When it is outside the chosen period, the period is cleared first.
 69  function applyPendingTender() {
 70    var wanted = pendingTender;
 71    if (findTenderInList(wanted.tenderId)) {
 72      pendingTender = null;
 73      selectedTenderId = wanted.tenderId;
 74      offerRoundId = wanted.roundId;
 75      offerGroupId = null;
 76      offerBidId = null;
 77      renderTendersTab();
 78      loadTenderDetails(wanted.tenderId);
 79      document.getElementById('ten-main').scrollIntoView({ behavior: 'smooth', block: 'start' });
 80      return;
 81    }
 82    var period = periodParams();
 83    if (period.from || period.to) {
 84      document.getElementById('rd-from').value = '';
 85      document.getElementById('rd-to').value = '';
 86      showToast(tr('The period was cleared to show this tender.', 'تم مسح الفترة لعرض هذا العطاء.'));
 87      onPeriodChanged(false);
 88      return;
 89    }
 90    pendingTender = null;
 91    showToast(tr('The tender details could not be loaded.', 'تعذر تحميل تفاصيل العطاء.'));
 92    renderTendersTab();
 93  }
 94  
 95  function findTenderInList(tenderId) {
 96    if (tendersData === null || tenderId === null) {
 97      return null;
 98    }
 99    for (var i = 0; i < tendersData.tenders.length; i++) {
100      if (tendersData.tenders[i].tenderId === tenderId) {
101        return tendersData.tenders[i];
102      }
103    }
104    return null;
105  }
106  
107  function selectTender(tenderId) {
108    selectedTenderId = tenderId;
109    offerRoundId = null;
110    offerGroupId = null;
111    offerBidId = null;
112    renderTenderList();
113    loadTenderDetails(tenderId);
114  }
115  
116  async function loadTenderDetails(tenderId) {
117    if (tenderId === null) {
118      tenderDetails = null;
119      renderTenderDetails();
120      return;
121    }
122    try {
123      var params = periodParams();
124      params.tenderId = tenderId;
125      var details = await getJson(page.dataset.urlTenderDetails, params);
126      if (details.tenderId !== selectedTenderId) {
127        return;
128      }
129      tenderDetails = details;
130    } catch (error) {
131      tenderDetails = null;
132      showStatus(error.message, true);
133    }
134    renderTenderDetails();
135  }
136  
137  function renderTendersTab() {
```

**`Orange-Tendering-System/wwwroot/js/intelligence-tenders.js` — lines 789–810**

```javascript
789  function exportTender(kind) {
790    if (tenderDetails === null) {
791      return;
792    }
793    var period = periodParams();
794    var request = { tenderId: tenderDetails.tenderId, from: period.from, to: period.to };
795    if (kind === 'lifecycle') {
796      request.format = 'xlsx';
797      request.reportType = 'tender_lifecycle';
798    } else if (kind === 'pdf') {
799      request.format = 'pdf';
800      request.reportType = 'tender_summary';
801    } else {
802      var round = chosenOfferRound();
803      if (round === null) {
804        return;
805      }
806      request.format = 'xlsx';
807      request.reportType = 'tender_offers';
808      request.roundId = round.roundId;
809    }
810    downloadFile(request);
```

### 2.11 Database entities used (where to look)

| Entity | File | DbSet |
|---|---|---|
| `Supplier` | `Orange-Tendering-System/Models/Entities/Supplier.cs` | `Suppliers` |
| `Tender` | `Orange-Tendering-System/Models/Entities/Tender.cs` | `Tenders` |
| `PurchaseRequest`, `Department` | `Models/Entities/PurchaseRequest.cs`, `Models/Entities/Department.cs` | (navigation from `Tender`) |
| `TenderRound` | `Models/Entities/TenderRound.cs` | `TenderRounds` |
| `TenderInvitation` | `Models/Entities/TenderInvitation.cs` | `TenderInvitations` |
| `Bid` | `Models/Entities/Bid.cs` | `Bids` |
| `BidItem`, `TenderItem` | `Models/Entities/BidItem.cs`, `Models/Entities/TenderItem.cs` | `BidItems` |
| `Document` | `Models/Entities/Document.cs` | `Documents` |
| `FinalDecisionNotice` | `Models/Entities/FinalDecision.cs` | `FinalDecisionNotices` |
| `ApplicationUser` | `Models/Entities/ApplicationUser.cs` | `Users` |

The DbSets are declared in `Orange-Tendering-System/Data/ApplicationDbContext.cs` (lines 17–56).

---

## 3. Function reference

Legend for the "kind" column in the narrative below: **DB** = reads SQL Server, **memory** = processes loaded data, **browser** = answers an HTTP request, **built-in** = .NET / ASP.NET Core / EF Core / browser API.

### 3.1 The custom methods you must know (with what happens next)

| Method | Defined at | Called by | Input → Output | Internally | Next | Kind |
|---|---|---|---|---|---|---|
| `IntelligenceController.Overview` | `Orange-Tendering-System/Controllers/IntelligenceController.cs:62` | ASP.NET Core routing for `GET /Intelligence/Overview` | `from`, `to`, `refresh` (query string) → `Json(OverviewData)` | one service call | `IntelligenceService.GetOverviewAsync` | browser |
| `IntelligenceService.GetOverviewAsync` | `Orange-Tendering-System/Services/Intelligence/IntelligenceService.cs:50` | `IntelligenceController.Overview` | dates, refresh → `OverviewData` | load, then calculate | `LoadSnapshotAsync`, `OverviewCalculator.Build` | DB + memory |
| `IntelligenceService.LoadSnapshotAsync` | `Orange-Tendering-System/Services/Intelligence/IntelligenceService.cs:128` | every `GetXxxAsync`, `IntelligenceExportService.BuildAsync`, `IntelligenceAiService.GetSupplierFactsAsync` | dates, refresh → `ProcurementSnapshot` | `DateRange.Parse`; `GetScopeAsync`; cache key = scope + period; `cache.Remove` on refresh or `cache.TryGetValue` | `LoadFromDatabaseAsync` (only when not cached) | DB (or cache) |
| `IntelligenceService.GetScopeAsync` | `Orange-Tendering-System/Services/Intelligence/IntelligenceService.cs:152` | `LoadSnapshotAsync` | — → `DataScope` | reads the user from `HttpContext`; market roles → `DataScope.Market`; others → their department/committee tender ids | `HasMarketView`; EF: `db.Users`, `db.Tenders` | DB |
| `IntelligenceService.LoadFromDatabaseAsync` | `Orange-Tendering-System/Services/Intelligence/IntelligenceService.cs:208` | `LoadSnapshotAsync` | period, scope → `ProcurementSnapshot` | 9 EF Core queries (section 5 of this guide); sealing rule; offer totals | `IsUnsealed` | DB |
| `IntelligenceService.IsUnsealed` | `Orange-Tendering-System/Services/Intelligence/IntelligenceService.cs:360` | `LoadFromDatabaseAsync`, tests | status, deadline, now → `bool` | the sealing rule | — | memory |
| `OverviewCalculator.Build` | `Orange-Tendering-System/Services/Intelligence/OverviewCalculator.cs:12` | `GetOverviewAsync` | snapshot, minimumOffers → `OverviewData` | KPIs, funnel, attention | `BuildContext`, `TenderCalculator.BuildAllTenders`, `SupplierCalculator.BuildStats`, `TenderCalculator.BuildCompetition`, `IntelligenceService.Median` | memory |
| `SupplierCalculator.BuildStats` | `Orange-Tendering-System/Services/Intelligence/SupplierCalculator.cs:18` | `SupplierCalculator.Build`, `Simulate`, `OverviewCalculator.Build`, `TenderCalculator.BuildAwardDistributions`, **`IntelligenceAiService.GetSupplierFactsAsync`** | snapshot → one `SupplierRow` per supplier | invitations, participations, response/win rate, awarded value, price position, documents | `FindAwards`, `BuildPricePositions` | memory |
| `SupplierCalculator.Build` | `Orange-Tendering-System/Services/Intelligence/SupplierCalculator.cs:229` | `GetSuppliersAsync` | snapshot, filters, 2, 3 → `SuppliersData` | filters, KPIs, charts, table, relationships, drop-downs | `BuildStats`, `CleanFilters`, `ApplyFilters`, `BuildLandscape`, `BuildPositionsChart`, `BuildRelationships` | memory |
| `SupplierCalculator.BuildDetails` | `Orange-Tendering-System/Services/Intelligence/SupplierCalculator.cs:710` | `GetSupplierDetailsAsync` | snapshot, supplierId → `SupplierDetails` or `null` | rounds, prices, competitors | `BuildStats`, `FindSharedRounds` | memory |
| `SupplierCalculator.CompareOffers` | `Orange-Tendering-System/Services/Intelligence/SupplierCalculator.cs:806` | `CompareSuppliersAsync` | snapshot, a, b → list | one row per shared tender | `CompareTwoOffers` → `FindPriceIssue`, `DescribeOffer` | memory |
| `SupplierCalculator.Simulate` | `Orange-Tendering-System/Services/Intelligence/SupplierCalculator.cs:1009` | `SimulateAsync` | snapshot, excluded ids, 3 → `SimulationResult` | bidders before/after, HHI, award exposure, alternatives | `ConcentrationIndex`, `BuildStats` | memory |
| `TenderCalculator.Build` | `Orange-Tendering-System/Services/Intelligence/TenderCalculator.cs:14` | `GetTendersAsync` | snapshot, 3 → `TendersData` | list, competition, departments, pairs, awards | `BuildAllTenders`, `BuildCompetition`, `BuildDepartments`, `BuildRecurringPairs`, `BuildAwardDistributions` | memory |
| `TenderCalculator.BuildTender` (public) | `Orange-Tendering-System/Services/Intelligence/TenderCalculator.cs:253` | `GetTenderDetailsAsync`, `IntelligenceExportService.BuildAsync` | snapshot, tenderId → `TenderDetails` or `null` | finds the tender | private `BuildTender` → `AddStage`, `BuildRound` | memory |
| `IntelligenceExportService.BuildAsync` | `Orange-Tendering-System/Services/Intelligence/IntelligenceExportService.cs:28` | `IntelligenceController.Export` | `ExportRequest` → `ExportFile` | validate; fresh load; scope checks; tables; write file | `LoadSnapshotAsync(…, true, …)`, `TenderCalculator.BuildTender`, `AddLifecycleTableAsync`, `AddOffersTable`, `ReportExport.ToXlsx/ToPdf/ToDocx` | DB + memory |

### 3.2 Built-in methods you will meet (not written by us)

| Method | From | Meaning here |
|---|---|---|
| `Json(obj)`, `NotFound(...)`, `BadRequest(...)`, `File(...)`, `View()`, `StatusCode(...)` | ASP.NET Core MVC `Controller` | build the HTTP answer |
| `[Authorize(Roles=…)]`, `[AutoValidateAntiforgeryToken]`, `[HttpGet]`, `[HttpPost]`, `[FromBody]` | ASP.NET Core | role check, anti-forgery check on POST, HTTP verb, read JSON body |
| `AsNoTracking()`, `Where`, `Select`, `ToListAsync(ct)`, `FirstOrDefaultAsync(ct)` | EF Core | read-only query, SQL `WHERE`, column projection, run the SQL |
| `GroupBy`, `OrderBy(Descending)`, `ToHashSet`, `ToDictionary`, `Sum`, `Count`, `FirstOrDefault` | LINQ (C#) — in memory after `ToListAsync` | grouping / sorting / lookups in C# |
| `IMemoryCache.TryGetValue / Set / Remove` | ASP.NET Core caching | the 60-second snapshot cache |
| `fetch`, `URLSearchParams`, `response.json()`, `Blob`, `URL.createObjectURL` | browser | HTTP calls and file download |
| `echarts.init`, `chart.setOption` | ECharts (third party) | drawing charts |

### 3.3 Every C# method of the module (generated)

| # | Method (signature) | Defined at | What it does (from the source comment / summary) | Called from |
|---|---|---|---|---|
| 1 | `public sealed record SupplierRequest(int SupplierId);` | `Orange-Tendering-System/Controllers/IntelligenceController.cs:40` | Request bodies of the AI Studio actions. | — (event / entry point) |
| 2 | `public sealed record AskRequest(int SupplierId, string? Question);` | `Orange-Tendering-System/Controllers/IntelligenceController.cs:41` | — | — (event / entry point) |
| 3 | `public override void OnActionExecuting(Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext context)` | `Orange-Tendering-System/Controllers/IntelligenceController.cs:44` | A supplier account never reaches this workspace, even if it also has the Sourcing Officer role. | — (event / entry point) |
| 4 | `public IActionResult Index()` | `Orange-Tendering-System/Controllers/IntelligenceController.cs:51` | GET /Intelligence -> Views/Intelligence/Index.cshtml (the page with the four tabs). | ASP.NET Core routing (HTTP request) |
| 5 | `public async Task<IActionResult> Overview(string? from, string? to, bool refresh, CancellationToken ct)` | `Orange-Tendering-System/Controllers/IntelligenceController.cs:62` | GET /Intelligence/Overview?from=2026-01-01&to=2026-06-30&refresh=false | ASP.NET Core routing (HTTP request) |
| 6 | `public async Task<IActionResult> Suppliers(string? from, string? to, string? department, string? category, string? activity, …` | `Orange-Tendering-System/Controllers/IntelligenceController.cs:72` | GET /Intelligence/Suppliers?from=&to=&department=&category=&activity=all&search= | ASP.NET Core routing (HTTP request) |
| 7 | `public async Task<IActionResult> SupplierDetails(int supplierId, string? from, string? to, CancellationToken ct)` | `Orange-Tendering-System/Controllers/IntelligenceController.cs:86` | GET /Intelligence/SupplierDetails?supplierId=12&from=&to= | ASP.NET Core routing (HTTP request) |
| 8 | `public async Task<IActionResult> CompareSuppliers(int supplierA, int supplierB, CancellationToken ct)` | `Orange-Tendering-System/Controllers/IntelligenceController.cs:98` | GET /Intelligence/CompareSuppliers?supplierA=3&supplierB=7 | ASP.NET Core routing (HTTP request) |
| 9 | `public async Task<IActionResult> Simulate([FromBody] SimulateRequest request, CancellationToken ct)` | `Orange-Tendering-System/Controllers/IntelligenceController.cs:114` | POST /Intelligence/Simulate   body: { "from": "", "to": "", "supplierIds": [3, 7] } | ASP.NET Core routing (HTTP request) |
| 10 | `public async Task<IActionResult> Tenders(string? from, string? to, bool refresh, CancellationToken ct)` | `Orange-Tendering-System/Controllers/IntelligenceController.cs:128` | GET /Intelligence/Tenders?from=&to= | ASP.NET Core routing (HTTP request) |
| 11 | `public async Task<IActionResult> TenderDetails(int tenderId, string? from, string? to, CancellationToken ct)` | `Orange-Tendering-System/Controllers/IntelligenceController.cs:136` | GET /Intelligence/TenderDetails?tenderId=5&from=&to= | ASP.NET Core routing (HTTP request) |
| 12 | `public async Task<IActionResult> Export([FromBody] ExportRequest request, CancellationToken ct)` | `Orange-Tendering-System/Controllers/IntelligenceController.cs:149` | POST /Intelligence/Export   body: { "format": "xlsx", "reportType": "tender_offers", "tenderId": 5, "roundId": 9 } Returns the file itself (Excel or PDF); the browser saves it. | ASP.NET Core routing (HTTP request) |
| 13 | `public async Task<IActionResult> SupplierSummary([FromBody] SupplierRequest request, CancellationToken ct)` | `Orange-Tendering-System/Controllers/IntelligenceController.cs:167` | A. AI Supplier Summary: facts -> Gemini -> { answer } | ASP.NET Core routing (HTTP request) |
| 14 | `public async Task<IActionResult> AskSupplier([FromBody] AskRequest request, CancellationToken ct)` | `Orange-Tendering-System/Controllers/IntelligenceController.cs:175` | B. Text Chat: facts + the user's question -> Gemini -> { answer } | ASP.NET Core routing (HTTP request) |
| 15 | `public async Task<IActionResult> StartVoice([FromBody] SupplierRequest request, CancellationToken ct)` | `Orange-Tendering-System/Controllers/IntelligenceController.cs:187` | C. Live Voice Call: facts -> single-use Gemini token -> { token, model }. The browser connects to Gemini with it. | ASP.NET Core routing (HTTP request) |
| 16 | `private async Task<IActionResult> AnswerAsync(int supplierId, string question, CancellationToken ct)` | `Orange-Tendering-System/Controllers/IntelligenceController.cs:207` | Shared by the summary and the chat: authorize, read the facts, ask Gemini once. | `IntelligenceController.cs:170`, `IntelligenceController.cs:182` |
| 17 | `private ObjectResult GeminiError(Exception e)` | `Orange-Tendering-System/Controllers/IntelligenceController.cs:226` | — | `IntelligenceController.cs:202`, `IntelligenceController.cs:222` |
| 18 | `public async Task<OverviewData> GetOverviewAsync(string? from, string? to, bool refresh, CancellationToken ct)` | `Orange-Tendering-System/Services/Intelligence/IntelligenceService.cs:50` | — | `IntelligenceController.cs:64` |
| 19 | `public async Task<SuppliersData> GetSuppliersAsync(string? from, string? to, SupplierFilters filters, bool refresh, CancellationToken ct)` | `Orange-Tendering-System/Services/Intelligence/IntelligenceService.cs:56` | — | `IntelligenceController.cs:80` |
| 20 | `public async Task<SupplierDetails?> GetSupplierDetailsAsync(int supplierId, string? from, string? to, CancellationToken ct)` | `Orange-Tendering-System/Services/Intelligence/IntelligenceService.cs:63` | Returns null when the supplier is not in the user's authorized scope. | `IntelligenceController.cs:88` |
| 21 | `public async Task<List<OfferComparison>?> CompareSuppliersAsync(int supplierA, int supplierB, CancellationToken ct)` | `Orange-Tendering-System/Services/Intelligence/IntelligenceService.cs:71` | Uses all recorded history (not the date filter), like the AI Studio supplier facts. Returns null when either supplier is not in the user's authorized scope. | `IntelligenceController.cs:104` |
| 22 | `public async Task<SimulationResult?> SimulateAsync(SimulateRequest request, CancellationToken ct)` | `Orange-Tendering-System/Services/Intelligence/IntelligenceService.cs:82` | Returns null when no supplier was chosen or a chosen supplier is not in the user's authorized scope. | `IntelligenceController.cs:116` |
| 23 | `public async Task<TendersData> GetTendersAsync(string? from, string? to, bool refresh, CancellationToken ct)` | `Orange-Tendering-System/Services/Intelligence/IntelligenceService.cs:109` | — | `IntelligenceController.cs:130` |
| 24 | `public async Task<TenderDetails?> GetTenderDetailsAsync(int tenderId, string? from, string? to, CancellationToken ct)` | `Orange-Tendering-System/Services/Intelligence/IntelligenceService.cs:116` | Returns null when the tender is not in the user's scope or has no bidding cycle in the period. | `IntelligenceController.cs:138` |
| 25 | `public async Task<ProcurementSnapshot> LoadSnapshotAsync(string? from, string? to, bool refresh, CancellationToken ct)` | `Orange-Tendering-System/Services/Intelligence/IntelligenceService.cs:128` | The procurement records of the period that this user may analyse. Also used by IntelligenceExportService (refresh = true, so a file is never built from old data) and IntelligenceAiService (whole history). | `IntelligenceService.cs:52`, `IntelligenceService.cs:58`, `IntelligenceService.cs:65`, `IntelligenceService.cs:73`, `IntelligenceService.cs:84`, `IntelligenceService.cs:111` …(+4) |
| 26 | `private async Task<DataScope> GetScopeAsync(CancellationToken ct)` | `Orange-Tendering-System/Services/Intelligence/IntelligenceService.cs:152` | Procurement roles, Admin and the top executives see the whole market. Anyone else sees only the tenders of their department and the tenders whose committee they sit on (and only the suppliers invited to them). The Intelligence page itself is for Sourcing Officers (market view); the rule also protects any other caller. | `IntelligenceService.cs:131` |
| 27 | `private static bool HasMarketView(ClaimsPrincipal user)` | `Orange-Tendering-System/Services/Intelligence/IntelligenceService.cs:186` | — | `IntelligenceService.cs:160` |
| 28 | `private async Task<ProcurementSnapshot> LoadFromDatabaseAsync(DateRange period, DataScope userScope, CancellationToken ct)` | `Orange-Tendering-System/Services/Intelligence/IntelligenceService.cs:208` | Sealing rule: the offers of a bidding cycle may be analysed only after bidding has closed. Offers of a cycle that is still open are counted as "sealed" and never loaded with their prices, so no chart, file or AI prompt can show them. | `IntelligenceService.cs:144` |
| 29 | `public static bool IsUnsealed(TenderRoundStatus status, DateTime? deadline, DateTime now)` | `Orange-Tendering-System/Services/Intelligence/IntelligenceService.cs:360` | A cycle is "unsealed" (its offers may be analysed) when it is Closed, Failed or SentToEvaluation, or when it is Open but its deadline has passed. Draft cycles and open cycles before the deadline stay sealed. | `IntelligenceService.cs:242`, `RadarChecks/Program.cs:208`, `RadarChecks/Program.cs:209`, `RadarChecks/Program.cs:210` |
| 30 | `public static string CleanName(string? name)` | `Orange-Tendering-System/Services/Intelligence/IntelligenceService.cs:374` | The fictional demo dataset starts every name with "RADAR DEMO "; the page never shows that prefix. | `SupplierCalculator.cs:45`, `SupplierCalculator.cs:209`, `SupplierCalculator.cs:212`, `SupplierCalculator.cs:594`, `SupplierCalculator.cs:698`, `SupplierCalculator.cs:755` …(+18) |
| 31 | `public static string CycleLabel(int roundNumber)` | `Orange-Tendering-System/Services/Intelligence/IntelligenceService.cs:389` | "Initial Bidding" for cycle 1, "Re-tendering — Cycle 2" for later cycles. | `OverviewCalculator.cs:95`, `IntelligenceExportService.cs:166` |
| 32 | `public static decimal Median(List<decimal> values)` | `Orange-Tendering-System/Services/Intelligence/IntelligenceService.cs:399` | Median = the middle value after sorting (or the average of the two middle values). 0 when the list is empty. | `IntelligenceService.cs:416`, `OverviewCalculator.cs:51`, `OverviewCalculator.cs:72`, `SupplierCalculator.cs:199`, `TenderCalculator.cs:457` |
| 33 | `public static double? Median(List<double> values)` | `Orange-Tendering-System/Services/Intelligence/IntelligenceService.cs:416` | Same rule for durations in days; null (not 0) when there is nothing to measure. | `IntelligenceService.cs:399`, `OverviewCalculator.cs:51`, `OverviewCalculator.cs:72`, `SupplierCalculator.cs:199`, `TenderCalculator.cs:457` |
| 34 | `public static string StructureKey(BidRecord bid)` | `Orange-Tendering-System/Services/Intelligence/IntelligenceService.cs:434` | Two offers are "structurally comparable" when they price the same tender items, units and quantities. Example key: "11:UNIT:10:10\|12:UNIT:10:10" (item id : unit : tender quantity : offered quantity). | `SupplierCalculator.cs:933`, `TenderCalculator.cs:437` |
| 35 | `public static PageContext BuildContext(ProcurementSnapshot snapshot)` | `Orange-Tendering-System/Services/Intelligence/IntelligenceService.cs:449` | The context bar of every tab: scope label, period, demo marker and how many cycles were analysed. | `OverviewCalculator.cs:15`, `SupplierCalculator.cs:232`, `TenderCalculator.cs:17` |
| 36 | `public static List<AwardRecord> FindAwards(ProcurementSnapshot snapshot)` | `Orange-Tendering-System/Services/Intelligence/IntelligenceService.cs:477` | Official awards: a tender whose AwardedBidId points to a visible (closed-cycle) offer. Oldest award first. | `SupplierCalculator.cs:30`, `SupplierCalculator.cs:509`, `SupplierCalculator.cs:727`, `SupplierCalculator.cs:1012`, `TenderCalculator.cs:164`, `TenderCalculator.cs:266` |
| 37 | `public static Dictionary<string, List<int>> FindSharedRounds(ProcurementSnapshot snapshot)` | `Orange-Tendering-System/Services/Intelligence/IntelligenceService.cs:511` | Supplier pairs that submitted offers in the same closed bidding cycle. Key "3-7" = suppliers 3 and 7 (smaller id first); value = the ids of the shared cycles. | `SupplierCalculator.cs:637`, `SupplierCalculator.cs:779`, `TenderCalculator.cs:149`, `RadarChecks/Program.cs:164` |
| 38 | `public static int FirstOfPair(string key)` | `Orange-Tendering-System/Services/Intelligence/IntelligenceService.cs:552` | "3-7" -> 3 and 7 | `SupplierCalculator.cs:641`, `SupplierCalculator.cs:782`, `TenderCalculator.cs:153` |
| 39 | `public static int SecondOfPair(string key)` | `Orange-Tendering-System/Services/Intelligence/IntelligenceService.cs:557` | — | `SupplierCalculator.cs:642`, `SupplierCalculator.cs:783`, `TenderCalculator.cs:154` |
| 40 | `public static DateRange Parse(string? from, string? to)` | `Orange-Tendering-System/Services/Intelligence/IntelligenceModels.cs:26` | Reads the page's "yyyy-MM-dd" date inputs. | `IntelligenceService.cs:130`, `RadarChecks/Program.cs:213`, `RadarChecks/Program.cs:215` |
| 41 | `private static DateTime? ParseDate(string? value)` | `Orange-Tendering-System/Services/Intelligence/IntelligenceModels.cs:40` | — | `IntelligenceModels.cs:29`, `IntelligenceModels.cs:30` |
| 42 | `public bool Contains(DateTime value)` | `Orange-Tendering-System/Services/Intelligence/IntelligenceModels.cs:51` | The "To" day is included completely (until midnight). | `IntelligenceService.cs:243` |
| 43 | `public SupplierRecord? Supplier(int id)` | `Orange-Tendering-System/Services/Intelligence/IntelligenceModels.cs:115` | — | `IntelligenceService.cs:74`, `IntelligenceService.cs:101`, `SupplierCalculator.cs:712`, `SupplierCalculator.cs:809` |
| 44 | `public TenderRecord? Tender(int id)` | `Orange-Tendering-System/Services/Intelligence/IntelligenceModels.cs:120` | — | `SupplierCalculator.cs:212`, `SupplierCalculator.cs:382`, `SupplierCalculator.cs:433`, `SupplierCalculator.cs:751`, `SupplierCalculator.cs:818`, `SupplierCalculator.cs:1042` …(+3) |
| 45 | `public RoundRecord? Round(int id)` | `Orange-Tendering-System/Services/Intelligence/IntelligenceModels.cs:125` | — | `SupplierCalculator.cs:204`, `IntelligenceExportService.cs:51`, `IntelligenceExportService.cs:165` |
| 46 | `public string SupplierName(int id)` | `Orange-Tendering-System/Services/Intelligence/IntelligenceModels.cs:130` | — | `IntelligenceService.cs:499`, `SupplierCalculator.cs:209`, `SupplierCalculator.cs:594`, `SupplierCalculator.cs:698`, `SupplierCalculator.cs:904`, `SupplierCalculator.cs:1143` …(+6) |
| 47 | `public static OverviewData Build(ProcurementSnapshot snapshot, int minimumOffers)` | `Orange-Tendering-System/Services/Intelligence/OverviewCalculator.cs:12` | — | `IntelligenceService.cs:53`, `RadarChecks/Program.cs:82`, `RadarChecks/Program.cs:198`, `RadarChecks/Program.cs:202` |
| 48 | `public static List<SupplierRow> BuildStats(ProcurementSnapshot snapshot)` | `Orange-Tendering-System/Services/Intelligence/SupplierCalculator.cs:18` | One SupplierRow per supplier in scope. Used by the Suppliers tab and by AI Studio. | `OverviewCalculator.cs:18`, `SupplierCalculator.cs:236`, `SupplierCalculator.cs:717`, `SupplierCalculator.cs:1119`, `TenderCalculator.cs:166`, `IntelligenceAiService.cs:62` …(+3) |
| 49 | `public static List<PricePosition> BuildPricePositions(ProcurementSnapshot snapshot)` | `Orange-Tendering-System/Services/Intelligence/SupplierCalculator.cs:165` | Each offer compared with the median offer of the same bidding cycle and the same currency. Example: offers 900, 1000, 1100 JOD -> median 1000 -> positions -10%, 0%, +10%. | `SupplierCalculator.cs:37`, `SupplierCalculator.cs:559`, `SupplierCalculator.cs:769`, `RadarChecks/Program.cs:147` |
| 50 | `public static SuppliersData Build(ProcurementSnapshot snapshot, SupplierFilters filters, int competitionMinimum, int minimumOffers)` | `Orange-Tendering-System/Services/Intelligence/SupplierCalculator.cs:229` | — | `IntelligenceService.cs:59`, `RadarChecks/Program.cs:93`, `RadarChecks/Program.cs:150`, `RadarChecks/Program.cs:151`, `RadarChecks/Program.cs:152`, `RadarChecks/Program.cs:153` …(+3) |
| 51 | `private static SupplierFilters CleanFilters(SupplierFilters filters, List<string> departments, List<string> categories)` | `Orange-Tendering-System/Services/Intelligence/SupplierCalculator.cs:361` | Unknown filter values (for example a department that is no longer in the period) are ignored. | `SupplierCalculator.cs:250` |
| 52 | `private static List<SupplierRow> ApplyFilters(ProcurementSnapshot snapshot, List<SupplierRow> allRows, SupplierFilters filters)` | `Orange-Tendering-System/Services/Intelligence/SupplierCalculator.cs:374` | — | `SupplierCalculator.cs:253` |
| 53 | `private static List<string> FindDepartments(ProcurementSnapshot snapshot)` | `Orange-Tendering-System/Services/Intelligence/SupplierCalculator.cs:428` | Departments of the tenders that have a closed bidding cycle in the period. | `SupplierCalculator.cs:239` |
| 54 | `private static List<MonthCount> BuildParticipationTrend(ProcurementSnapshot snapshot, HashSet<int> shownIds)` | `Orange-Tendering-System/Services/Intelligence/SupplierCalculator.cs:444` | Distinct suppliers that submitted an offer, per month in which the cycle closed (empty months included). | `SupplierCalculator.cs:336` |
| 55 | `private static void BuildLandscape(SuppliersData data, ProcurementSnapshot snapshot, List<SupplierRow> rows)` | `Orange-Tendering-System/Services/Intelligence/SupplierCalculator.cs:489` | Chart "Participation vs win rate": one dot per supplier with an offer, one colour (series) per category. Win rate needs official awards; without awards the chart uses the response rate instead. | `SupplierCalculator.cs:341` |
| 56 | `private static void BuildPositionsChart(SuppliersData data, ProcurementSnapshot snapshot, HashSet<int> shownIds)` | `Orange-Tendering-System/Services/Intelligence/SupplierCalculator.cs:556` | Chart "Price position over time": the four suppliers with the most comparable offers. | `SupplierCalculator.cs:342` |
| 57 | `private static void BuildRelationships(SuppliersData data, ProcurementSnapshot snapshot, List<SupplierRow> rows, HashSet<int> shownIds)` | `Orange-Tendering-System/Services/Intelligence/SupplierCalculator.cs:635` | Market relationships: supplier pairs that met in closed bidding cycles, and the groups they form. | `SupplierCalculator.cs:343` |
| 58 | `public static SupplierDetails? BuildDetails(ProcurementSnapshot snapshot, int supplierId)` | `Orange-Tendering-System/Services/Intelligence/SupplierCalculator.cs:710` | — | `IntelligenceService.cs:66`, `RadarChecks/Program.cs:165`, `RadarChecks/Program.cs:167` |
| 59 | `public static List<OfferComparison> CompareOffers(ProcurementSnapshot snapshot, int supplierA, int supplierB)` | `Orange-Tendering-System/Services/Intelligence/SupplierCalculator.cs:806` | One row per tender in which BOTH suppliers submitted an offer in the same closed cycle. When a tender has several such cycles, the latest comparable one is used. | `IntelligenceService.cs:78`, `RadarChecks/Program.cs:69`, `RadarChecks/Program.cs:74`, `RadarChecks/Program.cs:78` |
| 60 | `private static OfferComparison CompareTwoOffers(ProcurementSnapshot snapshot, RoundRecord round, TenderRecord tender, BidRecord a, BidRecord b)` | `Orange-Tendering-System/Services/Intelligence/SupplierCalculator.cs:856` | — | `SupplierCalculator.cs:839` |
| 61 | `private static ComparedOffer DescribeOffer(ProcurementSnapshot snapshot, RoundRecord round, BidRecord bid)` | `Orange-Tendering-System/Services/Intelligence/SupplierCalculator.cs:900` | — | `SupplierCalculator.cs:863`, `SupplierCalculator.cs:864` |
| 62 | `private static string? FindPriceIssue(BidRecord a, BidRecord b)` | `Orange-Tendering-System/Services/Intelligence/SupplierCalculator.cs:917` | Returns null when the two offers can be compared, otherwise the reason they cannot. | `SupplierCalculator.cs:868` |
| 63 | `private static bool HasInvalidLine(BidRecord bid)` | `Orange-Tendering-System/Services/Intelligence/SupplierCalculator.cs:944` | — | `SupplierCalculator.cs:933` |
| 64 | `private static string Specifications(BidRecord bid)` | `Orange-Tendering-System/Services/Intelligence/SupplierCalculator.cs:957` | All offered specifications of the bid, item by item, as one normalized text. | `SupplierCalculator.cs:937` |
| 65 | `private static string NormalizeText(string? value)` | `Orange-Tendering-System/Services/Intelligence/SupplierCalculator.cs:968` | "  red   steel " -> "RED STEEL" | `SupplierCalculator.cs:937`, `SupplierCalculator.cs:962` |
| 66 | `private static string? NormalizeCurrency(string? currency)` | `Orange-Tendering-System/Services/Intelligence/SupplierCalculator.cs:974` | — | `SupplierCalculator.cs:905`, `SupplierCalculator.cs:919`, `SupplierCalculator.cs:920` |
| 67 | `private static decimal? SecondsBetween(DateTime? from, DateTime? to)` | `Orange-Tendering-System/Services/Intelligence/SupplierCalculator.cs:984` | Exact seconds (with fractions) from one recorded time to another; null when either is missing. | `SupplierCalculator.cs:884`, `SupplierCalculator.cs:908` |
| 68 | `private static DateTimeOffset? ToAmmanTime(DateTime? utc)` | `Orange-Tendering-System/Services/Intelligence/SupplierCalculator.cs:995` | Database times are UTC; the comparison shows them in Amman time. | `SupplierCalculator.cs:865`, `SupplierCalculator.cs:907` |
| 69 | `public static SimulationResult Simulate(ProcurementSnapshot snapshot, List<int> excludedIds, int minimumOffers)` | `Orange-Tendering-System/Services/Intelligence/SupplierCalculator.cs:1009` | — | `IntelligenceService.cs:106`, `RadarChecks/Program.cs:170`, `RadarChecks/Program.cs:173` |
| 70 | `private static double? ConcentrationIndex(List<BidRecord> bids)` | `Orange-Tendering-System/Services/Intelligence/SupplierCalculator.cs:1151` | Herfindahl-Hirschman Index of the offers: sum of (each supplier's share of offers)². Example: 4 offers, supplier A has 2 and B and C have 1 -> 0.5² + 0.25² + 0.25² = 0.375. 1.0 = one supplier only. | `SupplierCalculator.cs:1097`, `SupplierCalculator.cs:1098` |
| 71 | `private static Dictionary<string, int> BuildCategoryColors(ProcurementSnapshot snapshot)` | `Orange-Tendering-System/Services/Intelligence/SupplierCalculator.cs:1181` | Category colour = position of the category in the alphabetical list (0..7, repeating). -1 = grey. Example: categories "Furniture", "IT", "Medical" -> colours 0, 1, 2. | `SupplierCalculator.cs:38`, `SupplierCalculator.cs:240` |
| 72 | `private static int CategoryColor(Dictionary<string, int> colors, string category)` | `Orange-Tendering-System/Services/Intelligence/SupplierCalculator.cs:1201` | — | `SupplierCalculator.cs:47`, `SupplierCalculator.cs:264`, `SupplierCalculator.cs:320` |
| 73 | `private static void AssignSupplierColors(List<SupplierRow> rows)` | `Orange-Tendering-System/Services/Intelligence/SupplierCalculator.cs:1211` | Supplier colour: the eight most active suppliers get their own colour (0..7); everyone else is grey (-1). | `SupplierCalculator.cs:159` |
| 74 | `public static SupplierPair MakePair(ProcurementSnapshot snapshot, int a, int b, int sharedRounds)` | `Orange-Tendering-System/Services/Intelligence/SupplierCalculator.cs:1225` | — | `SupplierCalculator.cs:645`, `SupplierCalculator.cs:786`, `SupplierCalculator.cs:790`, `TenderCalculator.cs:155` |
| 75 | `private static SupplierOption ToOption(SupplierRow row)` | `Orange-Tendering-System/Services/Intelligence/SupplierCalculator.cs:1236` | — | `SupplierCalculator.cs:348`, `SupplierCalculator.cs:354` |
| 76 | `public static string CategoryOf(string? category)` | `Orange-Tendering-System/Services/Intelligence/SupplierCalculator.cs:1254` | A supplier without a registered category is shown in the "Uncategorized" group. | `SupplierCalculator.cs:46`, `SupplierCalculator.cs:1186`, `RadarChecks/Program.cs:135` |
| 77 | `private static bool IsApproved(SupplierStatus status)` | `Orange-Tendering-System/Services/Intelligence/SupplierCalculator.cs:1263` | — | `SupplierCalculator.cs:49` |
| 78 | `private static void AddMoney(List<MoneyValue> totals, string currency, decimal value)` | `Orange-Tendering-System/Services/Intelligence/SupplierCalculator.cs:1269` | Adds a value to the running total of its currency (one MoneyValue per currency): currencies are never added together. | `SupplierCalculator.cs:121`, `SupplierCalculator.cs:1105` |
| 79 | `public static TendersData Build(ProcurementSnapshot snapshot, int minimumOffers)` | `Orange-Tendering-System/Services/Intelligence/TenderCalculator.cs:14` | — | `IntelligenceService.cs:112`, `RadarChecks/Program.cs:177`, `RadarChecks/Program.cs:204` |
| 80 | `private static bool HasOpenedOffers(TenderDetails tender)` | `Orange-Tendering-System/Services/Intelligence/TenderCalculator.cs:52` | — | `TenderCalculator.cs:35` |
| 81 | `public static List<RoundCompetition> BuildCompetition(ProcurementSnapshot snapshot, int minimumOffers)` | `Orange-Tendering-System/Services/Intelligence/TenderCalculator.cs:65` | Supplier participation per closed bidding cycle (oldest first): invited vs submitted vs compliant. | `OverviewCalculator.cs:79`, `TenderCalculator.cs:45` |
| 82 | `private static List<DepartmentCompetition> BuildDepartments(List<RoundCompetition> rounds)` | `Orange-Tendering-System/Services/Intelligence/TenderCalculator.cs:117` | Average offers per closed cycle, per department (lowest average first). | `TenderCalculator.cs:46` |
| 83 | `private static List<SupplierPair> BuildRecurringPairs(ProcurementSnapshot snapshot)` | `Orange-Tendering-System/Services/Intelligence/TenderCalculator.cs:147` | The five supplier pairs that met most often in closed cycles. | `TenderCalculator.cs:47` |
| 84 | `private static List<AwardDistribution> BuildAwardDistributions(ProcurementSnapshot snapshot)` | `Orange-Tendering-System/Services/Intelligence/TenderCalculator.cs:162` | Official award value per currency, by supplier and by department. HHI (concentration) = sum of squared shares. Example: shares 60% and 40% -> 0.36 + 0.16 = 0.52. | `TenderCalculator.cs:48` |
| 85 | `private static void AddShare(List<AwardShare> shares, string name, int? supplierId, decimal value)` | `Orange-Tendering-System/Services/Intelligence/TenderCalculator.cs:217` | — | `TenderCalculator.cs:190`, `TenderCalculator.cs:191` |
| 86 | `public static List<TenderDetails> BuildAllTenders(ProcurementSnapshot snapshot)` | `Orange-Tendering-System/Services/Intelligence/TenderCalculator.cs:242` | — | `OverviewCalculator.cs:17`, `TenderCalculator.cs:22`, `RadarChecks/Program.cs:192`, `RadarChecks/Program.cs:251` |
| 87 | `public static TenderDetails? BuildTender(ProcurementSnapshot snapshot, int tenderId)` | `Orange-Tendering-System/Services/Intelligence/TenderCalculator.cs:253` | — | `IntelligenceService.cs:119`, `TenderCalculator.cs:248`, `TenderCalculator.cs:260`, `TenderCalculator.cs:276`, `IntelligenceExportService.cs:57`, `RadarChecks/Program.cs:183` |
| 88 | `private static Dictionary<int, AwardRecord> LatestAwardPerTender(ProcurementSnapshot snapshot)` | `Orange-Tendering-System/Services/Intelligence/TenderCalculator.cs:263` | — | `TenderCalculator.cs:244`, `TenderCalculator.cs:260` |
| 89 | `private static TenderDetails BuildTender(ProcurementSnapshot snapshot, TenderRecord tender, Dictionary<int, AwardRecord> awardByTender)` | `Orange-Tendering-System/Services/Intelligence/TenderCalculator.cs:276` | — | `IntelligenceService.cs:119`, `TenderCalculator.cs:248`, `TenderCalculator.cs:253`, `TenderCalculator.cs:260`, `IntelligenceExportService.cs:57`, `RadarChecks/Program.cs:183` |
| 90 | `private static void AddStage(TenderDetails details, TenderRecord tender, string key, string label, DateTime? at, string source)` | `Orange-Tendering-System/Services/Intelligence/TenderCalculator.cs:373` | — | `TenderCalculator.cs:358`, `TenderCalculator.cs:359`, `TenderCalculator.cs:360`, `TenderCalculator.cs:361`, `TenderCalculator.cs:362`, `TenderCalculator.cs:363` |
| 91 | `private static double? DaysFromCreation(TenderRecord tender, DateTime? end)` | `Orange-Tendering-System/Services/Intelligence/TenderCalculator.cs:387` | — | `TenderCalculator.cs:301`, `TenderCalculator.cs:302` |
| 92 | `private static TenderRound BuildRound(ProcurementSnapshot snapshot, RoundRecord round, AwardRecord? award)` | `Orange-Tendering-System/Services/Intelligence/TenderCalculator.cs:396` | — | `TenderCalculator.cs:368` |
| 93 | `public async Task<ExportFile> BuildAsync(ExportRequest request, CancellationToken ct)` | `Orange-Tendering-System/Services/Intelligence/IntelligenceExportService.cs:28` | — | `IntelligenceController.cs:153`, `RadarChecks/Program.cs:260`, `RadarChecks/Program.cs:267`, `RadarChecks/Program.cs:270`, `RadarChecks/Program.cs:274`, `RadarChecks/Program.cs:280` …(+1) |
| 94 | `private async Task AddLifecycleTableAsync(ReportModel model, TenderDetails tender, ProcurementSnapshot snapshot, CancellationToken ct)` | `Orange-Tendering-System/Services/Intelligence/IntelligenceExportService.cs:133` | One row per recorded workflow event: cycles, offers, committee cycles, reports, final decision, award. | `IntelligenceExportService.cs:86` |
| 95 | `private static void AddEvent(List<string[]> rows, TenderDetails tender, string stage, string eventName, string status, DateTime? at, string source, int? roundId)` | `Orange-Tendering-System/Services/Intelligence/IntelligenceExportService.cs:226` | — | `IntelligenceExportService.cs:162`, `IntelligenceExportService.cs:167`, `IntelligenceExportService.cs:168`, `IntelligenceExportService.cs:169`, `IntelligenceExportService.cs:173`, `IntelligenceExportService.cs:178` …(+9) |
| 96 | `private static string ReadableSource(string field)` | `Orange-Tendering-System/Services/Intelligence/IntelligenceExportService.cs:240` | "TenderRounds.ClosedAt" -> "Tender rounds · closed at": readable, still traceable to the database column. | `IntelligenceExportService.cs:236` |
| 97 | `private static void AddOffersTable(ReportModel model, TenderDetails tender, int? roundId)` | `Orange-Tendering-System/Services/Intelligence/IntelligenceExportService.cs:257` | The offers of one closed cycle (roundId), or of every closed cycle of the tender (roundId = null). | `IntelligenceExportService.cs:90` |

### 3.4 Every JavaScript function of the three tabs (generated)

| # | Method (signature) | Defined at | What it does (from the source comment / summary) | Called from |
|---|---|---|---|---|
| 1 | `function tr(english, arabic)` | `Orange-Tendering-System/wwwroot/js/intelligence-page.js:22` | tr('Suppliers', 'الموردون') returns the text in the page language. | `intelligence-page.js:92`, `intelligence-page.js:94`, `intelligence-page.js:114`, `intelligence-page.js:173`, `intelligence-page.js:175`, `intelligence-page.js:199` …(+310) |
| 2 | `function escapeHtml(value)` | `Orange-Tendering-System/wwwroot/js/intelligence-page.js:30` | Text from the database is always escaped before it is put inside innerHTML (protects against HTML injection). | `intelligence-page.js:221`, `intelligence-page.js:466`, `intelligence-page.js:467`, `intelligence-page.js:473`, `intelligence-page.js:475`, `intelligence-page.js:477` …(+81) |
| 3 | `function formatNumber(value, digits)` | `Orange-Tendering-System/wwwroot/js/intelligence-page.js:41` | 1234.5 -> "1,234.5"   (null -> "—") | `intelligence-page.js:52`, `intelligence-overview.js:52`, `intelligence-overview.js:102`, `intelligence-overview.js:131`, `intelligence-suppliers.js:1069`, `intelligence-tenders.js:190` …(+11) |
| 4 | `function formatInteger(value)` | `Orange-Tendering-System/wwwroot/js/intelligence-page.js:51` | — | `intelligence-page.js:466`, `intelligence-overview.js:65`, `intelligence-suppliers.js:129`, `intelligence-suppliers.js:137`, `intelligence-suppliers.js:247`, `intelligence-suppliers.js:343` …(+10) |
| 5 | `function formatPercent(ratio, digits)` | `Orange-Tendering-System/wwwroot/js/intelligence-page.js:56` | 0.834 -> "83%" | `intelligence-suppliers.js:143`, `intelligence-suppliers.js:231`, `intelligence-suppliers.js:244`, `intelligence-suppliers.js:344`, `intelligence-suppliers.js:432`, `intelligence-suppliers.js:440` …(+12) |
| 6 | `function formatSignedPercent(ratio, digits)` | `Orange-Tendering-System/wwwroot/js/intelligence-page.js:64` | 0.05 -> "+5.0%", -0.1 -> "-10.0%" | `intelligence-suppliers.js:443`, `intelligence-suppliers.js:470`, `intelligence-suppliers.js:485`, `intelligence-suppliers.js:536`, `intelligence-suppliers.js:538`, `intelligence-suppliers.js:693` …(+4) |
| 7 | `function formatMoney(currency, value)` | `Orange-Tendering-System/wwwroot/js/intelligence-page.js:73` | ("JOD", 12500) -> "JOD 12,500" | `intelligence-suppliers.js:429`, `intelligence-suppliers.js:484`, `intelligence-suppliers.js:615`, `intelligence-suppliers.js:692`, `intelligence-suppliers.js:726`, `intelligence-suppliers.js:739` …(+13) |
| 8 | `function formatDate(value)` | `Orange-Tendering-System/wwwroot/js/intelligence-page.js:78` | "2026-03-05T10:00:00" -> "05 Mar 2026" | `intelligence-suppliers.js:434`, `intelligence-suppliers.js:458`, `intelligence-suppliers.js:484`, `intelligence-suppliers.js:691`, `intelligence-suppliers.js:738`, `intelligence-tenders.js:193` …(+4) |
| 9 | `function formatDateTime(value)` | `Orange-Tendering-System/wwwroot/js/intelligence-page.js:85` | — | `intelligence-tenders.js:284` |
| 10 | `function cycleLabel(roundNumber)` | `Orange-Tendering-System/wwwroot/js/intelligence-page.js:90` | Cycle 1 is the initial bidding; later cycles are re-tendering cycles. | `intelligence-suppliers.js:458`, `intelligence-suppliers.js:991`, `intelligence-suppliers.js:1092`, `intelligence-tenders.js:238`, `intelligence-tenders.js:260`, `intelligence-tenders.js:352` …(+4) |
| 11 | `function buildUrl(url, params)` | `Orange-Tendering-System/wwwroot/js/intelligence-page.js:102` | buildUrl('/Intelligence/Suppliers', { from: '2026-01-01', category: 'IT' }) -> '/Intelligence/Suppliers?from=2026-01-01&category=IT' | `intelligence-page.js:128` |
| 12 | `async function readErrorMessage(response)` | `Orange-Tendering-System/wwwroot/js/intelligence-page.js:113` | The error message sent by the controller ({ error: "..." }), or a general message. | `intelligence-page.js:130`, `intelligence-page.js:145` |
| 13 | `async function getJson(url, params)` | `Orange-Tendering-System/wwwroot/js/intelligence-page.js:127` | GET request that returns JSON. Throws an Error with a readable message when the server answers with an error. | `intelligence-page.js:503`, `intelligence-overview.js:27`, `intelligence-suppliers.js:59`, `intelligence-suppliers.js:360`, `intelligence-suppliers.js:825`, `intelligence-tenders.js:52` …(+1) |
| 14 | `async function postRequest(url, body)` | `Orange-Tendering-System/wwwroot/js/intelligence-page.js:137` | POST request with a JSON body. The RequestVerificationToken header is the anti-forgery token required by [AutoValidateAntiforgeryToken] on IntelligenceController. | `intelligence-page.js:151`, `intelligence-page.js:158` |
| 15 | `async function postJson(url, body)` | `Orange-Tendering-System/wwwroot/js/intelligence-page.js:150` | — | `intelligence-suppliers.js:1054`, `intelligence-ai.js:19`, `intelligence-ai.js:78`, `intelligence-ai.js:108` |
| 16 | `async function downloadFile(request)` | `Orange-Tendering-System/wwwroot/js/intelligence-page.js:156` | Asks the server for an Excel/PDF file and saves it (used by the Tenders tab). | `intelligence-tenders.js:810` |
| 17 | `function periodParams()` | `Orange-Tendering-System/wwwroot/js/intelligence-page.js:184` | The date inputs of the context bar. Empty = no limit. | `intelligence-page.js:214`, `intelligence-page.js:503`, `intelligence-overview.js:24`, `intelligence-overview.js:38`, `intelligence-suppliers.js:48`, `intelligence-suppliers.js:358` …(+6) |
| 18 | `function showStatus(text, isError)` | `Orange-Tendering-System/wwwroot/js/intelligence-page.js:191` | — | `intelligence-page.js:175`, `intelligence-page.js:202`, `intelligence-page.js:205`, `intelligence-page.js:208`, `intelligence-page.js:506`, `intelligence-overview.js:23` …(+6) |
| 19 | `function showContext(context)` | `Orange-Tendering-System/wwwroot/js/intelligence-page.js:198` | Every tab answer contains a "context" object (PageContext in IntelligenceModels.cs). | `intelligence-overview.js:28`, `intelligence-suppliers.js:65`, `intelligence-tenders.js:53` |
| 20 | `function renderPeriodChip()` | `Orange-Tendering-System/wwwroot/js/intelligence-page.js:213` | The chip "2026-01-01 → 2026-06-30 ×" under the period inputs. | `intelligence-page.js:232` |
| 21 | `function onPeriodChanged(refresh)` | `Orange-Tendering-System/wwwroot/js/intelligence-page.js:231` | A new period makes every loaded tab out of date: forget their data and reload the visible tab. | `intelligence-page.js:226`, `intelligence-page.js:538`, `intelligence-page.js:539`, `intelligence-page.js:540`, `intelligence-tenders.js:87` |
| 22 | `function loadCurrentTab(refresh)` | `Orange-Tendering-System/wwwroot/js/intelligence-page.js:239` | — | `intelligence-page.js:236` |
| 23 | `function showToast(text)` | `Orange-Tendering-System/wwwroot/js/intelligence-page.js:250` | — | `intelligence-page.js:173`, `intelligence-tenders.js:86`, `intelligence-tenders.js:91` |
| 24 | `function showTab(tab)` | `Orange-Tendering-System/wwwroot/js/intelligence-page.js:267` | Shows one tab panel and hides the three others, then lets the tab file load or redraw its data. | `intelligence-page.js:321`, `intelligence-page.js:492`, `intelligence-page.js:526`, `intelligence-page.js:533`, `intelligence-page.js:548`, `intelligence-suppliers.js:43` …(+2) |
| 25 | `function readTabFromHash()` | `Orange-Tendering-System/wwwroot/js/intelligence-page.js:297` | '/Intelligence#tenders' -> 'tenders'. No hash (or an unknown one) opens the Overview. | `intelligence-page.js:532`, `intelligence-page.js:533`, `intelligence-page.js:548` |
| 26 | `function onTabKeyDown(event)` | `Orange-Tendering-System/wwwroot/js/intelligence-page.js:306` | Arrow keys move between the tabs (accessibility pattern for tab lists). In Arabic (RTL) the arrows are mirrored. | `intelligence-page.js:529` |
| 27 | `function getChart(elementId)` | `Orange-Tendering-System/wwwroot/js/intelligence-page.js:333` | Returns the ECharts object of a chart <div>, creating it the first time. | `intelligence-suppliers.js:238`, `intelligence-suppliers.js:274`, `intelligence-suppliers.js:568`, `intelligence-suppliers.js:645`, `intelligence-tenders.js:363`, `intelligence-tenders.js:567` …(+2) |
| 28 | `function cssValue(name)` | `Orange-Tendering-System/wwwroot/js/intelligence-page.js:347` | Colours come from the CSS variables, so the charts follow the light / dark theme. | `intelligence-page.js:354`, `intelligence-page.js:356`, `intelligence-page.js:362`, `intelligence-page.js:364`, `intelligence-page.js:393`, `intelligence-page.js:397` …(+40) |
| 29 | `function categoryColor(index)` | `Orange-Tendering-System/wwwroot/js/intelligence-page.js:352` | Category colour number from the server (0..7) -> --c1..--c8. -1 = grey. | `intelligence-suppliers.js:175`, `intelligence-suppliers.js:224`, `intelligence-suppliers.js:341`, `intelligence-suppliers.js:398`, `intelligence-suppliers.js:580`, `intelligence-suppliers.js:1032` |
| 30 | `function supplierColor(index)` | `Orange-Tendering-System/wwwroot/js/intelligence-page.js:360` | Supplier colour number from the server (0..7) -> --s1..--s8. -1 = grey. | `intelligence-suppliers.js:657`, `intelligence-tenders.js:729` |
| 31 | `function withAlpha(hex, alpha)` | `Orange-Tendering-System/wwwroot/js/intelligence-page.js:368` | '#ff7900' + 0.3 -> 'rgba(255,121,0,0.3)' | `intelligence-suppliers.js:316`, `intelligence-suppliers.js:318`, `intelligence-suppliers.js:596`, `intelligence-suppliers.js:664`, `intelligence-suppliers.js:668`, `intelligence-tenders.js:588` …(+3) |
| 32 | `function gradient(vertical, fromColor, toColor)` | `Orange-Tendering-System/wwwroot/js/intelligence-page.js:381` | A colour gradient for bars and areas. vertical = top to bottom, otherwise left to right. | `intelligence-suppliers.js:318`, `intelligence-suppliers.js:668`, `intelligence-tenders.js:436`, `intelligence-tenders.js:588`, `intelligence-tenders.js:670` |
| 33 | `function chartBase()` | `Orange-Tendering-System/wwwroot/js/intelligence-page.js:389` | Settings shared by every chart: transparent background, page font, tooltip style. | `intelligence-suppliers.js:242`, `intelligence-suppliers.js:295`, `intelligence-suppliers.js:603`, `intelligence-suppliers.js:681`, `intelligence-tenders.js:442`, `intelligence-tenders.js:591` …(+2) |
| 34 | `function chartAxis()` | `Orange-Tendering-System/wwwroot/js/intelligence-page.js:408` | Axis style shared by every chart. | `intelligence-suppliers.js:301`, `intelligence-suppliers.js:309`, `intelligence-suppliers.js:621`, `intelligence-suppliers.js:626`, `intelligence-suppliers.js:695`, `intelligence-suppliers.js:697` …(+6) |
| 35 | `function chartLegend()` | `Orange-Tendering-System/wwwroot/js/intelligence-page.js:418` | — | `intelligence-suppliers.js:604`, `intelligence-suppliers.js:682`, `intelligence-tenders.js:592` |
| 36 | `function showChartMessage(chart, text)` | `Orange-Tendering-System/wwwroot/js/intelligence-page.js:423` | Replaces a chart with a centred message (empty data). | `intelligence-suppliers.js:280`, `intelligence-suppliers.js:284`, `intelligence-suppliers.js:573`, `intelligence-suppliers.js:650`, `intelligence-tenders.js:374`, `intelligence-tenders.js:411` …(+3) |
| 37 | `function toggleChartTable(button, html)` | `Orange-Tendering-System/wwwroot/js/intelligence-page.js:431` | The "Table" link of a chart card: adds a table with the same numbers under the chart, or removes it on the next click. | `intelligence-suppliers.js:1151`, `intelligence-tenders.js:875` |
| 38 | `function renderKpiTiles(containerId, tiles)` | `Orange-Tendering-System/wwwroot/js/intelligence-page.js:461` | Draws KPI tiles. Each tile: { label, value, unit, sub, help, icon, tone, bar }. value can be a number (formatted) or a text; bar (0..1) draws the thin progress line at the bottom. | `intelligence-overview.js:57`, `intelligence-suppliers.js:134`, `intelligence-tenders.js:255` |
| 39 | `function shareSuppliersWithAiStudio(suppliers)` | `Orange-Tendering-System/wwwroot/js/intelligence-page.js:495` | — | `intelligence-page.js:504`, `intelligence-suppliers.js:66` |
| 40 | `async function loadSupplierListForAiStudio()` | `Orange-Tendering-System/wwwroot/js/intelligence-page.js:501` | When the page opens on another tab, AI Studio still needs its supplier list. | `intelligence-page.js:550` |
| 41 | `function redrawCurrentTab()` | `Orange-Tendering-System/wwwroot/js/intelligence-page.js:511` | Redraws the visible tab with the data it already has (used after a light/dark theme change). | — (event / entry point) |
| 42 | `function startIntelligencePage()` | `Orange-Tendering-System/wwwroot/js/intelligence-page.js:521` | — | `intelligence-page.js:555` |
| 43 | `function openOverviewTab()` | `Orange-Tendering-System/wwwroot/js/intelligence-overview.js:9` | Called by showTab: load the data the first time, otherwise just draw what we already have. | `intelligence-page.js:287`, `intelligence-page.js:513` |
| 44 | `function forgetOverviewData()` | `Orange-Tendering-System/wwwroot/js/intelligence-overview.js:18` | Called by onPeriodChanged: the next openOverviewTab() will load fresh data. | `intelligence-page.js:233` |
| 45 | `async function loadOverview(refresh)` | `Orange-Tendering-System/wwwroot/js/intelligence-overview.js:22` | — | `intelligence-page.js:241`, `intelligence-overview.js:11` |
| 46 | `function renderOverview(data)` | `Orange-Tendering-System/wwwroot/js/intelligence-overview.js:37` | — | `intelligence-overview.js:13`, `intelligence-overview.js:30` |
| 47 | `function renderOverviewKpis(data)` | `Orange-Tendering-System/wwwroot/js/intelligence-overview.js:47` | — | `intelligence-overview.js:41` |
| 48 | `function renderOverviewFunnel(data)` | `Orange-Tendering-System/wwwroot/js/intelligence-overview.js:81` | The five lifecycle steps: how many tenders reached each one, and after how many days (median). | `intelligence-overview.js:42` |
| 49 | `function renderOverviewAttention(data)` | `Orange-Tendering-System/wwwroot/js/intelligence-overview.js:114` | "Worth your attention": low-competition cycles, long-running tenders and silent suppliers. Each item has a button that opens the evidence (a tender, or the Suppliers tab filtered on "No offer yet"). | `intelligence-overview.js:43` |
| 50 | `function renderOverviewShortcuts(data)` | `Orange-Tendering-System/wwwroot/js/intelligence-overview.js:150` | — | `intelligence-overview.js:44` |
| 51 | `function setupOverviewEvents()` | `Orange-Tendering-System/wwwroot/js/intelligence-overview.js:165` | — | `intelligence-page.js:544` |
| 52 | `function openSuppliersTab()` | `Orange-Tendering-System/wwwroot/js/intelligence-suppliers.js:26` | — | `intelligence-page.js:289`, `intelligence-page.js:515` |
| 53 | `function forgetSuppliersData()` | `Orange-Tendering-System/wwwroot/js/intelligence-suppliers.js:34` | — | `intelligence-page.js:234` |
| 54 | `function showSuppliersWithActivity(activity)` | `Orange-Tendering-System/wwwroot/js/intelligence-suppliers.js:40` | Used by the Overview "never submitted an offer" item: open this tab with the activity filter set. | `intelligence-overview.js:172` |
| 55 | `async function loadSuppliers(refresh)` | `Orange-Tendering-System/wwwroot/js/intelligence-suppliers.js:46` | — | `intelligence-page.js:243`, `intelligence-suppliers.js:28`, `intelligence-suppliers.js:121` |
| 56 | `function renderSuppliers(data)` | `Orange-Tendering-System/wwwroot/js/intelligence-suppliers.js:79` | Draws every part of the tab from one SuppliersData answer. | `intelligence-suppliers.js:30`, `intelligence-suppliers.js:68` |
| 57 | `function renderSupplierFilters(data)` | `Orange-Tendering-System/wwwroot/js/intelligence-suppliers.js:98` | — | `intelligence-suppliers.js:80` |
| 58 | `function setSupplierFilter(name, value)` | `Orange-Tendering-System/wwwroot/js/intelligence-suppliers.js:119` | A filter changed: ask the server again (the server applies the filters and recalculates every number). | `intelligence-suppliers.js:267`, `intelligence-suppliers.js:269`, `intelligence-suppliers.js:1119`, `intelligence-suppliers.js:1120`, `intelligence-suppliers.js:1121`, `intelligence-suppliers.js:1126` |
| 59 | `function renderSupplierKpis(data)` | `Orange-Tendering-System/wwwroot/js/intelligence-suppliers.js:124` | — | `intelligence-suppliers.js:81` |
| 60 | `function renderSupplierUniverse(data)` | `Orange-Tendering-System/wwwroot/js/intelligence-suppliers.js:167` | Gives the 3D engine the latest /Intelligence/Suppliers answer. The engine only draws: every number came from the server. | `intelligence-suppliers.js:82` |
| 61 | `function onUniverseSupplierClick(supplierId)` | `Orange-Tendering-System/wwwroot/js/intelligence-suppliers.js:194` | A click on a dot opens its details; clicking the selected dot again (or Escape) closes them. | `intelligence-suppliers.js:173` |
| 62 | `function showSelectedSupplierInUniverse()` | `Orange-Tendering-System/wwwroot/js/intelligence-suppliers.js:203` | Tells the 3D engine which supplier the details panel shows, and its competitors once they are loaded. | `intelligence-suppliers.js:353`, `intelligence-suppliers.js:362`, `intelligence-suppliers.js:375` |
| 63 | `function renderCategoryDonut(data)` | `Orange-Tendering-System/wwwroot/js/intelligence-suppliers.js:218` | — | `intelligence-suppliers.js:83` |
| 64 | `function toggleCategoryFilter(category)` | `Orange-Tendering-System/wwwroot/js/intelligence-suppliers.js:265` | — | `intelligence-suppliers.js:174`, `intelligence-suppliers.js:260`, `intelligence-suppliers.js:1132` |
| 65 | `function renderParticipationTrend(data)` | `Orange-Tendering-System/wwwroot/js/intelligence-suppliers.js:273` | — | `intelligence-suppliers.js:84` |
| 66 | `function renderSupplierTable(data)` | `Orange-Tendering-System/wwwroot/js/intelligence-suppliers.js:327` | — | `intelligence-suppliers.js:85` |
| 67 | `async function openSupplierDetails(supplierId)` | `Orange-Tendering-System/wwwroot/js/intelligence-suppliers.js:350` | — | `intelligence-suppliers.js:71`, `intelligence-suppliers.js:198`, `intelligence-suppliers.js:638`, `intelligence-suppliers.js:709`, `intelligence-suppliers.js:1145`, `intelligence-suppliers.js:1159` …(+1) |
| 68 | `function closeSupplierDetails()` | `Orange-Tendering-System/wwwroot/js/intelligence-suppliers.js:370` | — | `intelligence-suppliers.js:196`, `intelligence-suppliers.js:1164` |
| 69 | `function markSelectedRow()` | `Orange-Tendering-System/wwwroot/js/intelligence-suppliers.js:378` | — | `intelligence-suppliers.js:352`, `intelligence-suppliers.js:373` |
| 70 | `function renderSupplierDetails()` | `Orange-Tendering-System/wwwroot/js/intelligence-suppliers.js:385` | — | `intelligence-suppliers.js:86`, `intelligence-suppliers.js:361`, `intelligence-suppliers.js:374`, `intelligence-suppliers.js:1171` |
| 71 | `function detailsLine(label, value)` | `Orange-Tendering-System/wwwroot/js/intelligence-suppliers.js:423` | One "label: value" line of the details overview. | `intelligence-suppliers.js:436`, `intelligence-suppliers.js:437`, `intelligence-suppliers.js:438`, `intelligence-suppliers.js:439`, `intelligence-suppliers.js:440`, `intelligence-suppliers.js:441` …(+3) |
| 72 | `function detailsOverviewHtml(details)` | `Orange-Tendering-System/wwwroot/js/intelligence-suppliers.js:427` | — | `intelligence-suppliers.js:418` |
| 73 | `function detailsParticipationHtml(details)` | `Orange-Tendering-System/wwwroot/js/intelligence-suppliers.js:449` | Bidding history: one line per closed cycle with an offer. Clicking a line opens that tender on the Tenders tab. | `intelligence-suppliers.js:412` |
| 74 | `function detailsPricingHtml(details)` | `Orange-Tendering-System/wwwroot/js/intelligence-suppliers.js:466` | Every comparable offer: how far it was from the median of its cycle (green = cheaper, amber = more expensive). | `intelligence-suppliers.js:414` |
| 75 | `function detailsRelationshipsHtml(details)` | `Orange-Tendering-System/wwwroot/js/intelligence-suppliers.js:491` | Competitors: suppliers that submitted offers in the same closed cycles. Clicking one opens its details. | `intelligence-suppliers.js:416` |
| 76 | `function setInsightTab(tab)` | `Orange-Tendering-System/wwwroot/js/intelligence-suppliers.js:509` | — | `intelligence-suppliers.js:1191`, `intelligence-suppliers.js:1196` |
| 77 | `function renderSupplierInsights(data)` | `Orange-Tendering-System/wwwroot/js/intelligence-suppliers.js:526` | — | `intelligence-suppliers.js:87`, `intelligence-suppliers.js:522` |
| 78 | `function renderLandscapeChart(data)` | `Orange-Tendering-System/wwwroot/js/intelligence-suppliers.js:556` | Scatter chart: x = cycles with an offer, y = win rate (or response rate), dot size = awarded value, colour = category. | `intelligence-suppliers.js:546` |
| 79 | `function renderPositionsChart(data)` | `Orange-Tendering-System/wwwroot/js/intelligence-suppliers.js:644` | Line chart: each offer of the four most-priced suppliers, as % above (+) or below (-) its cycle median. | `intelligence-suppliers.js:540` |
| 80 | `function supplierChartTableHtml(name)` | `Orange-Tendering-System/wwwroot/js/intelligence-suppliers.js:715` | The "Table" link under a chart shows the same numbers as an accessible table (and hides it on the next click). | `intelligence-suppliers.js:1151` |
| 81 | `function renderSupplierRelationships(data)` | `Orange-Tendering-System/wwwroot/js/intelligence-suppliers.js:748` | — | `intelligence-suppliers.js:88` |
| 82 | `function renderComparisonControls()` | `Orange-Tendering-System/wwwroot/js/intelligence-suppliers.js:785` | — | `intelligence-suppliers.js:89`, `intelligence-suppliers.js:818`, `intelligence-suppliers.js:836` |
| 83 | `function fillComparisonSupplierSelect(selectId, suppliers, selectedId, otherId)` | `Orange-Tendering-System/wwwroot/js/intelligence-suppliers.js:801` | Fills one supplier drop-down; the supplier chosen in the other drop-down is left out. | `intelligence-suppliers.js:787`, `intelligence-suppliers.js:788` |
| 84 | `async function loadComparison()` | `Orange-Tendering-System/wwwroot/js/intelligence-suppliers.js:814` | — | `intelligence-suppliers.js:853`, `intelligence-suppliers.js:1205`, `intelligence-suppliers.js:1209` |
| 85 | `function compareSuppliers(supplierA, supplierB)` | `Orange-Tendering-System/wwwroot/js/intelligence-suppliers.js:849` | Used by "Compare offers" in the details panel and in the relationships table. | `intelligence-suppliers.js:1143`, `intelligence-suppliers.js:1185`, `intelligence-tenders.js:879` |
| 86 | `function selectedComparisonRow()` | `Orange-Tendering-System/wwwroot/js/intelligence-suppliers.js:857` | — | `intelligence-suppliers.js:932` |
| 87 | `function formatDuration(seconds)` | `Orange-Tendering-System/wwwroot/js/intelligence-suppliers.js:867` | 93784.5 seconds -> "1 d 2 h 3 min 4.5 s" | `intelligence-suppliers.js:917`, `intelligence-suppliers.js:919`, `intelligence-suppliers.js:1006` |
| 88 | `function comparisonAmount(currency, total)` | `Orange-Tendering-System/wwwroot/js/intelligence-suppliers.js:880` | — | `intelligence-suppliers.js:906`, `intelligence-suppliers.js:999` |
| 89 | `function ammanDate(value)` | `Orange-Tendering-System/wwwroot/js/intelligence-suppliers.js:888` | Dates and times of the comparison are shown in Amman time (the server sends them with the +03:00 offset). | `intelligence-suppliers.js:923`, `intelligence-suppliers.js:984` |
| 90 | `function ammanTime(value)` | `Orange-Tendering-System/wwwroot/js/intelligence-suppliers.js:892` | — | `intelligence-suppliers.js:984` |
| 91 | `function comparisonPriceHtml(row, offer, key)` | `Orange-Tendering-System/wwwroot/js/intelligence-suppliers.js:897` | One offer in the price card: name, amount, a bar (relative to the higher offer) and a link to the offer record. | `intelligence-suppliers.js:997` |
| 92 | `function comparisonTimingHtml(row, offer, key)` | `Orange-Tendering-System/wwwroot/js/intelligence-suppliers.js:911` | One offer in the timing card: submission time and its distance to the deadline. | `intelligence-suppliers.js:987`, `intelligence-suppliers.js:988` |
| 93 | `function renderComparison(message)` | `Orange-Tendering-System/wwwroot/js/intelligence-suppliers.js:931` | — | `intelligence-suppliers.js:90`, `intelligence-suppliers.js:820`, `intelligence-suppliers.js:823`, `intelligence-suppliers.js:838`, `intelligence-suppliers.js:841`, `intelligence-suppliers.js:844` …(+1) |
| 94 | `function renderScenarioPick(data)` | `Orange-Tendering-System/wwwroot/js/intelligence-suppliers.js:1019` | — | `intelligence-suppliers.js:91` |
| 95 | `async function runSimulation()` | `Orange-Tendering-System/wwwroot/js/intelligence-suppliers.js:1040` | — | `intelligence-suppliers.js:1220` |
| 96 | `function beforeAfterHtml(label, before, after)` | `Orange-Tendering-System/wwwroot/js/intelligence-suppliers.js:1061` | — | `intelligence-suppliers.js:1068`, `intelligence-suppliers.js:1069` |
| 97 | `function simulationHtml(simulation)` | `Orange-Tendering-System/wwwroot/js/intelligence-suppliers.js:1065` | — | `intelligence-suppliers.js:1055` |
| 98 | `function setupSuppliersEvents()` | `Orange-Tendering-System/wwwroot/js/intelligence-suppliers.js:1118` | — | `intelligence-page.js:545` |
| 99 | `function openTendersTab()` | `Orange-Tendering-System/wwwroot/js/intelligence-tenders.js:22` | — | `intelligence-page.js:291`, `intelligence-page.js:517`, `intelligence-tenders.js:41` |
| 100 | `function forgetTendersData()` | `Orange-Tendering-System/wwwroot/js/intelligence-tenders.js:32` | — | `intelligence-page.js:235` |
| 101 | `function openTenderFromAnotherTab(tenderId, roundId)` | `Orange-Tendering-System/wwwroot/js/intelligence-tenders.js:38` | Used by the Overview, the supplier details and the offer comparison: open one tender (and one cycle) on this tab. | `intelligence-overview.js:170`, `intelligence-suppliers.js:1011`, `intelligence-suppliers.js:1173` |
| 102 | `async function loadTenders(refresh)` | `Orange-Tendering-System/wwwroot/js/intelligence-tenders.js:47` | — | `intelligence-page.js:245`, `intelligence-tenders.js:24` |
| 103 | `function applyPendingTender()` | `Orange-Tendering-System/wwwroot/js/intelligence-tenders.js:69` | Opens the tender requested by another tab. When it is outside the chosen period, the period is cleared first. | `intelligence-tenders.js:26`, `intelligence-tenders.js:55` |
| 104 | `function findTenderInList(tenderId)` | `Orange-Tendering-System/wwwroot/js/intelligence-tenders.js:95` | — | `intelligence-tenders.js:58`, `intelligence-tenders.js:71` |
| 105 | `function selectTender(tenderId)` | `Orange-Tendering-System/wwwroot/js/intelligence-tenders.js:107` | — | `intelligence-tenders.js:820`, `intelligence-tenders.js:826` |
| 106 | `async function loadTenderDetails(tenderId)` | `Orange-Tendering-System/wwwroot/js/intelligence-tenders.js:116` | — | `intelligence-tenders.js:62`, `intelligence-tenders.js:78`, `intelligence-tenders.js:113`, `intelligence-tenders.js:650` |
| 107 | `function renderTendersTab()` | `Orange-Tendering-System/wwwroot/js/intelligence-tenders.js:137` | — | `intelligence-tenders.js:28`, `intelligence-tenders.js:61`, `intelligence-tenders.js:77`, `intelligence-tenders.js:92` |
| 108 | `function tenderStateTone(state)` | `Orange-Tendering-System/wwwroot/js/intelligence-tenders.js:151` | Short label of the tender state, shown as a coloured pill. | `intelligence-tenders.js:194`, `intelligence-tenders.js:223` |
| 109 | `function tenderStateText(state)` | `Orange-Tendering-System/wwwroot/js/intelligence-tenders.js:158` | — | `intelligence-tenders.js:190`, `intelligence-tenders.js:222` |
| 110 | `function renderTenderList()` | `Orange-Tendering-System/wwwroot/js/intelligence-tenders.js:165` | — | `intelligence-tenders.js:112`, `intelligence-tenders.js:138`, `intelligence-tenders.js:648`, `intelligence-tenders.js:831` |
| 111 | `function renderTenderDetails()` | `Orange-Tendering-System/wwwroot/js/intelligence-tenders.js:206` | — | `intelligence-tenders.js:119`, `intelligence-tenders.js:134`, `intelligence-tenders.js:139` |
| 112 | `function renderTenderKpis(tender)` | `Orange-Tendering-System/wwwroot/js/intelligence-tenders.js:234` | — | `intelligence-tenders.js:229` |
| 113 | `function renderTenderTimeline(tender)` | `Orange-Tendering-System/wwwroot/js/intelligence-tenders.js:272` | Six milestones, each with its recorded time (or "Not recorded") and the days since the tender was created. | `intelligence-tenders.js:230` |
| 114 | `function openedRounds()` | `Orange-Tendering-System/wwwroot/js/intelligence-tenders.js:303` | Closed cycles of the selected tender that have at least one offer. | `intelligence-tenders.js:317`, `intelligence-tenders.js:344` |
| 115 | `function chosenOfferRound()` | `Orange-Tendering-System/wwwroot/js/intelligence-tenders.js:316` | — | `intelligence-tenders.js:345`, `intelligence-tenders.js:362`, `intelligence-tenders.js:483`, `intelligence-tenders.js:802`, `intelligence-tenders.js:859` |
| 116 | `function chosenOfferGroup(round)` | `Orange-Tendering-System/wwwroot/js/intelligence-tenders.js:326` | — | `intelligence-tenders.js:388`, `intelligence-tenders.js:399` |
| 117 | `function renderTenderOffers()` | `Orange-Tendering-System/wwwroot/js/intelligence-tenders.js:335` | — | `intelligence-tenders.js:231`, `intelligence-tenders.js:843`, `intelligence-tenders.js:848` |
| 118 | `function renderOffersChart(chart, group)` | `Orange-Tendering-System/wwwroot/js/intelligence-tenders.js:423` | Horizontal bars: one per offer, highest at the top, the lowest in strong orange, dashed line = median. | `intelligence-tenders.js:418` |
| 119 | `function renderOfferInsight(round, group)` | `Orange-Tendering-System/wwwroot/js/intelligence-tenders.js:489` | The sentence under the chart and the three "lowest / median / highest" cards. The numbers come from the server. | `intelligence-tenders.js:419` |
| 120 | `function extremeCardHtml(title, amount, supplier, tone)` | `Orange-Tendering-System/wwwroot/js/intelligence-tenders.js:517` | — | `intelligence-tenders.js:512`, `intelligence-tenders.js:513`, `intelligence-tenders.js:514` |
| 121 | `function renderOfferTable(round, group)` | `Orange-Tendering-System/wwwroot/js/intelligence-tenders.js:522` | Every offer of the cycle (also those outside the comparable group), cheapest first. | `intelligence-tenders.js:406` |
| 122 | `function renderOfferDetail(round)` | `Orange-Tendering-System/wwwroot/js/intelligence-tenders.js:541` | The small box under the chart with the record details of the clicked offer. | `intelligence-tenders.js:407`, `intelligence-tenders.js:483`, `intelligence-tenders.js:859` |
| 123 | `function renderCompetitionChart()` | `Orange-Tendering-System/wwwroot/js/intelligence-tenders.js:566` | Bars: grey = invited, orange = submitted (amber when below the minimum). Clicking a bar opens that tender. | `intelligence-tenders.js:140`, `intelligence-tenders.js:649` |
| 124 | `function renderDepartmentChart()` | `Orange-Tendering-System/wwwroot/js/intelligence-tenders.js:655` | — | `intelligence-tenders.js:141` |
| 125 | `function renderRecurringPairs()` | `Orange-Tendering-System/wwwroot/js/intelligence-tenders.js:693` | — | `intelligence-tenders.js:142` |
| 126 | `function renderAwardsChart()` | `Orange-Tendering-System/wwwroot/js/intelligence-tenders.js:708` | Donut of official award value in the first currency (other currencies are listed in the table). | `intelligence-tenders.js:143` |
| 127 | `function tenderChartTableHtml(name)` | `Orange-Tendering-System/wwwroot/js/intelligence-tenders.js:753` | — | `intelligence-tenders.js:875` |
| 128 | `function exportTender(kind)` | `Orange-Tendering-System/wwwroot/js/intelligence-tenders.js:789` | kind: 'lifecycle' (Excel), 'pdf' (PDF report) or 'offers' (Excel of the chosen cycle). | `intelligence-tenders.js:835`, `intelligence-tenders.js:836`, `intelligence-tenders.js:837` |
| 129 | `function setupTendersEvents()` | `Orange-Tendering-System/wwwroot/js/intelligence-tenders.js:817` | — | `intelligence-page.js:546` |

---

## 4. Chronological execution flows

Each flow: user action → HTML/event → JavaScript → HTTP → controller → service → database → calculation → DTO → JSON → JavaScript → screen.

### 4.1 Opening Orange Intelligence

1. Browser `GET /Intelligence` → `IntelligenceController.Index` (`Orange-Tendering-System/Controllers/IntelligenceController.cs:51`). Before it runs: `[Authorize(Roles = SystemRoles.SourcingOfficer)]` (not signed in → 302 to login; wrong role → 403) and `OnActionExecuting` (supplier account → 403).
2. `return View()` renders `Views/Intelligence/Index.cshtml`: the four tab panels, empty elements with `id`s, the URLs in `data-url-*` and the anti-forgery token in `data-csrf` (`Orange-Tendering-System/Views/Intelligence/Index.cshtml:29`).
3. When the HTML is parsed, `DOMContentLoaded` runs `startIntelligencePage` (`Orange-Tendering-System/wwwroot/js/intelligence-page.js:521`): it registers the tab/period listeners, calls `setupOverviewEvents`, `setupSuppliersEvents`, `setupTendersEvents`, then `showTab(readTabFromHash())`.
4. If the page did not open on Suppliers, `loadSupplierListForAiStudio` (`Orange-Tendering-System/wwwroot/js/intelligence-page.js:501`) requests `/Intelligence/Suppliers` once so AI Studio has its supplier list (`radar:data` event).

### 4.2 Loading Overview

```text
showTab('overview')                       intelligence-page.js
 → openOverviewTab() → loadOverview(false) intelligence-overview.js
 → getJson('/Intelligence/Overview', {from, to})            HTTP GET
 → IntelligenceController.Overview(from, to, refresh, ct)
 → IntelligenceService.GetOverviewAsync
     → LoadSnapshotAsync → GetScopeAsync → cache? → LoadFromDatabaseAsync (EF Core, 9 queries)
     → OverviewCalculator.Build(snapshot, MinimumOffers = 3)
         → IntelligenceService.BuildContext · TenderCalculator.BuildAllTenders · SupplierCalculator.BuildStats
         → TenderCalculator.BuildCompetition · IntelligenceService.Median
 ← OverviewData → Json(...) → camelCase JSON
 → renderOverview(data) → renderOverviewKpis / renderOverviewFunnel / renderOverviewAttention / renderOverviewShortcuts
```

Overview KPIs come from `OverviewCalculator.Build` (`Orange-Tendering-System/Services/Intelligence/OverviewCalculator.cs:12`): tender count, completed (winner notified), in progress, suppliers, participating suppliers, submitted offers, **sealed offers (count only)**, median lifecycle days.

### 4.3 Changing the date filter

`change` on `#rd-from` / `#rd-to` → `onPeriodChanged(false)` (`Orange-Tendering-System/wwwroot/js/intelligence-page.js:231`) → `forgetOverviewData`, `forgetSuppliersData`, `forgetTendersData` (the old answers are no longer valid) → `loadCurrentTab` → the visible tab's loader sends `from`/`to`. On the server `DateRange.Parse` (`Orange-Tendering-System/Services/Intelligence/IntelligenceModels.cs:26`) reads `yyyy-MM-dd`, ignores invalid text and swaps a reversed range; `LoadFromDatabaseAsync` keeps the cycles whose `ReferenceDate` (`Deadline ?? ClosedAt ?? CreatedAt`) is inside the range (`DateRange.Contains`, end day included). Different period = different cache key = new database read.

### 4.4 Refreshing the data

`#ctx-refresh` → `onPeriodChanged(true)` → loaders send `refresh=true` → `LoadSnapshotAsync` removes the cache entry (`Orange-Tendering-System/Services/Intelligence/IntelligenceService.cs:137`) and reads the database again. Exports always use `refresh = true` (`Orange-Tendering-System/Services/Intelligence/IntelligenceExportService.cs:43`).

### 4.5 Loading Suppliers

```text
showTab('suppliers') → openSuppliersTab() → loadSuppliers(false)           intelligence-suppliers.js
 → getJson('/Intelligence/Suppliers', {from,to,department,category,activity,search,refresh})
 → IntelligenceController.Suppliers → builds SupplierFilters from the query string
 → IntelligenceService.GetSuppliersAsync → LoadSnapshotAsync
 → SupplierCalculator.Build(snapshot, filters, MinimumSharedRounds = 2, MinimumOffers = 3)
     BuildStats → FindDepartments → CleanFilters → ApplyFilters → KPIs → category chart → BuildParticipationTrend
     → table order → BuildLandscape → BuildPositionsChart → BuildRelationships → AllSuppliers / ScenarioSuppliers (ToOption)
 ← SuppliersData → JSON
 → suppliersData = data; showContext; shareSuppliersWithAiStudio(data.allSuppliers); renderSuppliers(data)
```

`loadSuppliers` numbers each request (`suppliersRequestNumber`) and ignores late answers, so fast typing never draws an old result (`Orange-Tendering-System/wwwroot/js/intelligence-suppliers.js:60`).

### 4.6 Filtering suppliers

`change` on `#sup-department`, `#sup-category`, `#sup-activity` (or a donut slice / legend / 3D legend button) → `setSupplierFilter(name, value)` → `loadSuppliers(false)` → the **server** filters in `SupplierCalculator.ApplyFilters` (`Orange-Tendering-System/Services/Intelligence/SupplierCalculator.cs:374`) and recalculates every KPI and chart for the remaining suppliers. Unknown filter values are dropped by `CleanFilters` (`Orange-Tendering-System/Services/Intelligence/SupplierCalculator.cs:361`). Department filter = suppliers that submitted an offer to a tender of that department.

### 4.7 Searching suppliers

`input` on `#sup-search` → 300 ms debounce (`setTimeout`) → `setSupplierFilter('search', text)` → `ApplyFilters`: name or category contains the text, case-insensitive (`StringComparison.OrdinalIgnoreCase`).

### 4.8 Opening supplier details

Click on a directory row, a 3D dot, a competitor or an alternative → `openSupplierDetails(id)` (`Orange-Tendering-System/wwwroot/js/intelligence-suppliers.js:350`) → `showSelectedSupplierInUniverse()` (3D focus) → `GET /Intelligence/SupplierDetails?supplierId=` → `IntelligenceController.SupplierDetails` → `GetSupplierDetailsAsync` → `SupplierCalculator.BuildDetails` (`Orange-Tendering-System/Services/Intelligence/SupplierCalculator.cs:710`). A supplier that is not in the snapshot (outside scope, or a made-up id) → `null` → **404** "not in your authorized scope". JSON → `renderSupplierDetails()` → Overview / Participation / Pricing / Relationships panels (switching panels sends no request).

### 4.9 Comparing two suppliers

`#cmp-supplier-a/b` or "Compare offers" → `loadComparison()` → `GET /Intelligence/CompareSuppliers?supplierA&supplierB` → controller rejects equal or non-positive ids (**400**) → `CompareSuppliersAsync` loads **all history** (`LoadSnapshotAsync(null, null, …)`) and returns `null` (→ **404**) if either supplier is outside the scope → `SupplierCalculator.CompareOffers` → for every closed cycle where **both** submitted: `CompareTwoOffers` → `FindPriceIssue` decides comparability → per tender keep the latest comparable cycle. JS fills the tender drop-down and `renderComparison()` shows price difference and timing.

### 4.10 Building competition groups

Inside `SupplierCalculator.Build` → `BuildRelationships` (`Orange-Tendering-System/Services/Intelligence/SupplierCalculator.cs:635`): `IntelligenceService.FindSharedRounds` gives, for every supplier pair, the distinct closed cycles where both submitted; pairs with ≥ `MinimumSharedRounds` (2) shared cycles become graph edges; a breadth-first search (queue) collects connected suppliers into groups; suppliers with no edge are counted as `UnconnectedSuppliers`. JS: `renderSupplierRelationships(data)`.

### 4.11 Running a What-if simulation

Tick suppliers in `#sim-pick` → `#sim-run` → `runSimulation()` → `postJson('/Intelligence/Simulate', {from, to, supplierIds})` with the `RequestVerificationToken` header → `[AutoValidateAntiforgeryToken]` checks it → `IntelligenceController.Simulate` → `IntelligenceService.SimulateAsync` (≤ 10 distinct ids, all must be in scope, else `null` → **400**) → `SupplierCalculator.Simulate` → `simulationHtml(result)`.

### 4.12 Loading Tenders

`showTab('tenders')` → `openTendersTab()` → `loadTenders(false)` → `GET /Intelligence/Tenders` → `GetTendersAsync` → `TenderCalculator.Build` → `TendersData` (list newest first, default tender, competition per cycle, departments, recurring pairs, award distributions) → `applyPendingTender()` / `renderTendersTab()`.

### 4.13 Selecting a tender · 4.14 Loading tender details · 4.15 Lifecycle stages

Click in `#ten-list` or `#ten-pick` (or "Open tender" from another tab via `openTenderFromAnotherTab`) → `selectTender(id)` → `loadTenderDetails(id)` → `GET /Intelligence/TenderDetails?tenderId=` → `GetTenderDetailsAsync` → `TenderCalculator.BuildTender` → `null` → **404** when the tender is outside scope/period. The private `BuildTender` (`Orange-Tendering-System/Services/Intelligence/TenderCalculator.cs:276`) adds six stages with `AddStage`, each from **its own column**: `Tenders.CreatedAt`, first `TenderRounds.CreatedAt` (cycle 1), first `TenderInvitations.InvitedAt`, last `TenderRounds.ClosedAt`, `Tenders.AwardedAt`, `FinalDecisionNotices.SuppliersNotifiedAt`. A missing value stays `null` ("not recorded"); nothing is estimated. JS: `renderTenderDetails()` → `renderTenderKpis`, `renderTenderTimeline`, `renderTenderOffers`.

### 4.16 Displaying financial offers · 4.17 Comparing eligible offers

`TenderCalculator.BuildRound` (`Orange-Tendering-System/Services/Intelligence/TenderCalculator.cs:396`): a **sealed** cycle returns only its invitation count. For a closed cycle it lists the offers (cheapest first) and forms **comparable groups** = same currency + same item structure (`IntelligenceService.StructureKey`). Each group gets a median, highest and lowest offer; offers get `RelativeToMedian`. No new request is needed to switch cycle/group in the browser (`renderTenderOffers`).

### 4.18 Award distributions

`TenderCalculator.BuildAwardDistributions` (`Orange-Tendering-System/Services/Intelligence/TenderCalculator.cs:162`): official awards (`IntelligenceService.FindAwards`) grouped **per currency**, then per supplier and per department, with shares and HHI. JS: `renderAwardsChart()`.

### 4.19 Exporting Excel · 4.20 Exporting PDF

Buttons `#ten-export-lifecycle` (Excel lifecycle), `#off-export` (Excel offers), `#ten-export-pdf` (PDF summary) → `exportTender(kind)` → `downloadFile(request)` → `POST /Intelligence/Export` with the token → `IntelligenceController.Export` → `IntelligenceExportService.BuildAsync`:

1. format ∈ {xlsx, pdf, docx}, report ∈ {tender_lifecycle, tender_offers, tender_summary}, else `IntelligenceExportException` → **400**;
2. fresh snapshot (`refresh: true`);
3. tender must be in the snapshot (scope + period); a given cycle must belong to that tender;
4. `TenderCalculator.BuildTender` (the same numbers as the screen);
5. tables (`AddLifecycleTableAsync` runs 3 extra EF Core queries: `CommitteeRounds`, `TenderReports`, `FinalDecisionNotices`; `AddOffersTable` uses only analysed cycles);
6. `ReportExport.ToXlsx / ToPdf / ToDocx` → `File(bytes, contentType, fileName)` → JS saves the blob.

### 4.21 Passing supplier data into the 3D visualization

`renderSuppliers` → `renderSupplierUniverse(data)` (`Orange-Tendering-System/wwwroot/js/intelligence-suppliers.js:167`) → first time `SupplierUniverse.init(...)`, then `SupplierUniverse.update({ suppliers: data.allSuppliers, shownIds, rows: data.suppliers, category: data.filters.category, pairs: data.pairs, competitionMinimum })`. The 3D file draws only; it sends no request and calculates nothing. A click on a dot calls back `onUniverseSupplierClick(id)` → `openSupplierDetails(id)`; a category button calls `toggleCategoryFilter` → server filter.

### 4.22 Opening AI Studio through Ask AI

`#sup-ask-ai` → `window.OrangeIntelligenceAI.chooseSupplier(selectedSupplierId)` (defined in `wwwroot/js/intelligence-ai.js`) → sets the AI Studio `<select>` and fires `change` → `OrangeRadar.setTab('studio')` → `showTab('studio')`. The AI requests themselves are described in the AI guide.

---

## 5. Database and EF Core

### 5.1 Tables and relationships used

```text
Departments 1──* PurchaseRequests 1──1 Tenders 1──* TenderRounds 1──* TenderInvitations *──1 Suppliers
                                         │                                  │
                                         │                                  1
                                         │                                  │
                                         ├── AwardedBidId ──────────────▶ Bids *── (TenderInvitationId) ── 1 TenderInvitations
                                         │                                  1
                                         │                                  │
                                         │                                  * BidItems *──1 TenderItems
                                         ├──* FinalDecisionNotices (Outcome = Award, SuppliersNotifiedAt)
                                         └──* TenderCommitteeMembers (UserId)          AspNetUsers.DepartmentId ── Departments
Documents (SupplierId, TenderId, BidId, VerificationStatus)
```

| Relationship | Foreign key | Used for |
|---|---|---|
| Tender → PurchaseRequest → Department | `Tenders.PurchaseRequestId`, `PurchaseRequests.DepartmentId` | department name and the department filter / user scope |
| TenderRound → Tender | `TenderRounds.TenderId` | cycles of a tender |
| TenderInvitation → TenderRound, Supplier | `TenderInvitations.TenderRoundId`, `.SupplierId` | who was invited to which cycle |
| Bid → TenderInvitation | `Bids.TenderInvitationId` | which supplier/cycle an offer belongs to (supplier and cycle are reached **through** the invitation) |
| BidItem → Bid, TenderItem | `BidItems.BidId`, `.TenderItemId` | offer lines: quantity × unit price, unit, specifications |
| Official award | `Tenders.AwardedBidId` (+ `Tenders.AwardedAt`) | the award is the tender's recorded decision, never "the lowest price" |
| Winner notification | `FinalDecisionNotices.TenderId`, `Outcome`, `SuppliersNotifiedAt` | end of the lifecycle |
| User scope | `AspNetUsers.DepartmentId`, `TenderCommitteeMembers.UserId/TenderId` | non-market users |

### 5.2 Every query, explained

All queries are in `LoadFromDatabaseAsync` (`Orange-Tendering-System/Services/Intelligence/IntelligenceService.cs:208`) and `GetScopeAsync`. All use `AsNoTracking()` (read-only, faster) and `Select(...)` projections (only the needed columns travel from SQL Server). A query **executes** at `ToListAsync` / `FirstOrDefaultAsync`; everything after that is LINQ in C# memory.

| # | Line | Table(s) | SQL side (Where/Select) | C# side afterwards | Output |
|---|---|---|---|---|---|
| 1 | `Orange-Tendering-System/Services/Intelligence/IntelligenceService.cs:214` | Suppliers | `Id, CompanyName, Category, Status` | scoped users: keep only invited suppliers | `List<SupplierRecord>` |
| 2 | `Orange-Tendering-System/Services/Intelligence/IntelligenceService.cs:220` | TenderInvitations ⋈ TenderRounds | `SupplierId, TenderRound.TenderId` (only for scoped users) | filter by allowed tenders | invited supplier ids |
| 3 | `Orange-Tendering-System/Services/Intelligence/IntelligenceService.cs:229` | Tenders ⋈ PurchaseRequests ⋈ Departments | title, status, currency, estimated value, department, created/awarded | keep tenders that have a cycle in period+scope | `List<TenderRecord>` |
| 4 | `Orange-Tendering-System/Services/Intelligence/IntelligenceService.cs:235` | TenderRounds | number, status, deadline, closed, created | `IsUnsealed`, period and scope filter | `List<RoundRecord>` |
| 5 | `Orange-Tendering-System/Services/Intelligence/IntelligenceService.cs:255` | TenderInvitations ⋈ TenderRounds | id, cycle, tender, supplier, status, invited at | keep invitations of kept cycles | `List<InvitationRecord>` |
| 6 | `Orange-Tendering-System/Services/Intelligence/IntelligenceService.cs:264` | Bids ⋈ TenderInvitations ⋈ TenderRounds | `WHERE Status = Submitted`; extras = shipping+installation+maintenance+tax+other | latest revision per invitation; split sealed / visible | counts + visible offers |
| 7 | `Orange-Tendering-System/Services/Intelligence/IntelligenceService.cs:283` | BidItems ⋈ TenderItems | `WHERE BidId IN (visible ids)` — sealed offers' prices are never read | group by offer; total = Σ qty×price + extras | `BidRecord.Lines`, `Total` |
| 8 | `Orange-Tendering-System/Services/Intelligence/IntelligenceService.cs:316` | Documents | `WHERE BidId IS NULL AND SupplierId IS NOT NULL`; supplier, tender, verification | keep in-scope suppliers / tenders | `SupplierDocumentRecord` |
| 9 | `Orange-Tendering-System/Services/Intelligence/IntelligenceService.cs:326` | FinalDecisionNotices | `WHERE Outcome = Award AND SuppliersNotifiedAt IS NOT NULL` | latest per tender in scope | `WinnerNotifiedAt` |
| S1 | `Orange-Tendering-System/Services/Intelligence/IntelligenceService.cs:173` | AspNetUsers | the user's `DepartmentId` | — | department id |
| S2 | `Orange-Tendering-System/Services/Intelligence/IntelligenceService.cs:177` | Tenders ⋈ PurchaseRequests, TenderCommitteeMembers | department's tenders OR tenders whose committee contains the user | — | allowed tender ids |

Why some filtering happens in C#: the cycle list depends on `IsUnsealed(status, deadline, now)` and on the period rule `Deadline ?? ClosedAt ?? CreatedAt`; the "latest revision per invitation" uses `GroupBy(...).OrderByDescending(...).First()`. Doing these in memory on small projected rows keeps the SQL simple and the rules readable in one place.

Navigation properties in `Select` (for example `b.TenderInvitation.TenderRound.TenderId`) are translated by EF Core into SQL `JOIN`s.

---

## 6. Calculations and business rules

Notation: "closed cycle" = a `RoundRecord` with `Unsealed = true`. Offers in `snapshot.Bids` are already the latest submitted revision per invitation in closed cycles.

| Rule | Formula | Code | Example | Edge cases (null vs 0) |
|---|---|---|---|---|
| Offer total | Σ(quantity × unit price) + shipping + installation + maintenance + tax + other | `Orange-Tendering-System/Services/Intelligence/IntelligenceService.cs:303` | 10×80 + 10×20 + 50 = 1 050 | `RecordedTotal` is `null` when an offer has no lines (cannot be priced) |
| Invitations | count of the supplier's invitations to closed cycles | `Orange-Tendering-System/Services/Intelligence/SupplierCalculator.cs:51` | 4 | 0 when never invited |
| Participation | distinct closed cycles with an offer | `Orange-Tendering-System/Services/Intelligence/SupplierCalculator.cs:97` | offers in cycles 7, 7(rev), 9 → 2 | — |
| Response rate | offers answering an invitation ÷ invitations | `Orange-Tendering-System/Services/Intelligence/SupplierCalculator.cs:105` | 3 ÷ 4 = 0.75 → "75%" | **`null`** when there are no invitations (undefined), **0** when invited but never answered |
| Win rate | official awards won ÷ awarded tenders the supplier bid on | `Orange-Tendering-System/Services/Intelligence/SupplierCalculator.cs:126` | 2 ÷ 4 = 0.5 | **`null`** when none of its tenders was awarded |
| Awarded value | Σ awarded offer totals **per currency** | `AddMoney` `Orange-Tendering-System/Services/Intelligence/SupplierCalculator.cs:1269` | JOD 40 000 and USD 5 000 stay separate | empty list = no award |
| Median | middle value after sorting; mean of two middle values | `Orange-Tendering-System/Services/Intelligence/IntelligenceService.cs:399` | [900, 1000, 1100] → 1000; [900, 1000] → 950 | decimal version returns 0 for an empty list (callers skip it); the `double` version returns **null** |
| Price position | offer ÷ median of its cycle **and currency** − 1 | `Orange-Tendering-System/Services/Intelligence/SupplierCalculator.cs:217` | 1100 ÷ 1000 − 1 = +0.10 | needs ≥ 2 priced offers in the same cycle+currency; median ≤ 0 skipped |
| Supplier relative price | average of its price positions | `Orange-Tendering-System/Services/Intelligence/SupplierCalculator.cs:141` | (+0.10 − 0.05) ÷ 2 = +0.025 | `null` with no priced cycle |
| Offer difference (comparison) | \|A − B\| ÷ **higher** offer | `Orange-Tendering-System/Services/Intelligence/SupplierCalculator.cs:876` | 900 vs 1000 → 100 ÷ 1000 = 10% | not computed when `FindPriceIssue` returns a reason |
| Comparability | same currency, both priced, same item structure, same specifications and deviations | `FindPriceIssue` `Orange-Tendering-System/Services/Intelligence/SupplierCalculator.cs:917` | JOD vs USD → `different_currency` | reasons: `missing_currency`, `different_currency`, `missing_price`, `different_structure`, `different_specifications` |
| Low competition | offers in a closed cycle < `MinimumOffers` (3) | `Orange-Tendering-System/Services/Intelligence/TenderCalculator.cs:110` | 2 offers → low | — |
| Shared cycles | distinct closed cycles where both suppliers submitted | `FindSharedRounds` `Orange-Tendering-System/Services/Intelligence/IntelligenceService.cs:511` | key "3-7" → [12, 15] → 2 | two revisions in one cycle count once |
| Competition group | connected components of pairs with ≥ 2 shared cycles (BFS) | `Orange-Tendering-System/Services/Intelligence/SupplierCalculator.cs:652` | A–B (3), B–C (2) → group {A,B,C} | suppliers without such a link are "unconnected" |
| Lifecycle duration | winner notification sent − tender created (days, 1 decimal) | `DaysFromCreation` `Orange-Tendering-System/Services/Intelligence/TenderCalculator.cs:387` | 1 Mar → 15 Mar = 14.0 | `null` until the notification is recorded |
| Median lifecycle | median of completed tenders' durations | `Orange-Tendering-System/Services/Intelligence/OverviewCalculator.cs:51` | 10, 14, 30 → 14 | `null` when no tender is completed |
| What-if | bidders per closed cycle before/after removing chosen suppliers; cycles below 3; cycles with 0 bidders; share of offers removed; award exposure per currency | `Simulate` `Orange-Tendering-System/Services/Intelligence/SupplierCalculator.cs:1009` | 4 bidders, remove 2 → 2 (< 3, "drops below minimum") | labelled "hypothetical, not a forecast" |
| HHI (offers) | Σ (supplier's share of offers)² | `ConcentrationIndex` `Orange-Tendering-System/Services/Intelligence/SupplierCalculator.cs:1151` | 4 offers: 2,1,1 → 0.25+0.0625+0.0625 = 0.375 | `null` with no offers; 1.0 = one supplier |
| HHI (awards) | Σ (supplier's share of awarded value in one currency)² | `Orange-Tendering-System/Services/Intelligence/TenderCalculator.cs:201` | 60% / 40% → 0.52 | `null` when the currency total is 0 |
| Official award | `Tenders.AwardedBidId` points to a visible offer | `FindAwards` `Orange-Tendering-System/Services/Intelligence/IntelligenceService.cs:477` | — | an award never comes from "lowest price"; an award whose offer is sealed is not shown |
| Currency separation | totals grouped by currency; comparisons only within one currency | `AddMoney`, `BuildAwardDistributions`, `BuildPricePositions` (grouped by cycle + currency) | — | different currencies are never added |

---

## 7. Security

| Protection | Where | How |
|---|---|---|
| Sign-in + role | `Orange-Tendering-System/Controllers/IntelligenceController.cs:20` | not signed in → 302 to login (verified: every Intelligence URL redirects); other roles → 403 |
| Supplier accounts | `Orange-Tendering-System/Controllers/IntelligenceController.cs:46` | a supplier never reaches the workspace, even with a second role |
| Data scope | `Orange-Tendering-System/Services/Intelligence/IntelligenceService.cs:152` | market roles see all; others only their department/committee tenders and invited suppliers; the cache key contains the scope |
| Anti-forgery | `Orange-Tendering-System/Controllers/IntelligenceController.cs:21` + `postRequest` in `intelligence-page.js` | every POST must carry `RequestVerificationToken`; missing/wrong token → JSON **400** before the action runs |
| Sealed bids | `Orange-Tendering-System/Services/Intelligence/IntelligenceService.cs:360` and the offer split in `LoadFromDatabaseAsync` | offers of open cycles are counted but their lines/prices are never queried (query 7 only asks for visible ids); `BuildRound` hides even the offer count of a sealed cycle |
| Date filter | `DateRange.Parse` / `Contains` | invalid text ignored; reversed range swapped |
| Invalid / foreign ids | `SupplierDetails`, `CompareSuppliers`, `TenderDetails`, `Simulate`, `Export` | the id is looked up **in the user's snapshot**; not found → 404/400. Changing an id in the URL therefore cannot reveal another scope's data |
| Currency rules | `FindPriceIssue`, `AddMoney` | no cross-currency comparison or total |
| Export validation | `IntelligenceExportService.BuildAsync` | allowed format/report only; tender in scope and period; cycle must belong to the tender; fresh data |
| No caching by the browser | `ResponseCache(NoStore = true)` | answers are not stored by the browser |

**What if a user edits an id in the request?** `GET /Intelligence/SupplierDetails?supplierId=999999` → `BuildDetails` finds no such supplier in the snapshot → `null` → 404 "This supplier is not in your authorized scope." (verified in the regression run). The same pattern protects tenders, comparisons, simulations and exports.

---

## 8. Practical debugging (Visual Studio)

Start: open `Orange-Tendering-System.slnx`, choose the **https** profile, F5. Sign in as the Sourcing Officer, open `https://localhost:7182/Intelligence`. Browser DevTools (F12) → **Network** tab, filter `Intelligence` to see each request, its query string/body, status and JSON **Response**.

| Feature | First breakpoint | Inspect | F11 into | F10 over |
|---|---|---|---|---|
| Overview | `Orange-Tendering-System/Controllers/IntelligenceController.cs:64` | `from`, `to`, `refresh` | `GetOverviewAsync` → `LoadSnapshotAsync` | `DateRange.Parse` |
| Scope + cache | `Orange-Tendering-System/Services/Intelligence/IntelligenceService.cs:134` | `userScope.Key` (`market` for the officer), `cacheKey`, `refresh` | `LoadFromDatabaseAsync` (only on a cache miss) | `cache.TryGetValue` |
| Queries | `Orange-Tendering-System/Services/Intelligence/IntelligenceService.cs:251` | `rounds` (look at `Unsealed`), `roundIds` | — | each `ToListAsync` (watch Output window for SQL) |
| Sealing | `Orange-Tendering-System/Services/Intelligence/IntelligenceService.cs:278` | `sealedBids`, `visibleBids.Count` | — | — |
| Supplier stats | `Orange-Tendering-System/Services/Intelligence/SupplierCalculator.cs:97` | `row`, `invitationIds`, `offersAnsweringInvitations` | — | the loops |
| Filters | `Orange-Tendering-System/Services/Intelligence/SupplierCalculator.cs:253` | `data.Filters`, `rows.Count` | `ApplyFilters` | — |
| Details | `Orange-Tendering-System/Controllers/IntelligenceController.cs:88` | `supplierId`; then `details == null`? | `BuildDetails` | — |
| Comparison | `Orange-Tendering-System/Services/Intelligence/SupplierCalculator.cs:868` | `a.Currency`, `b.Currency`, `row.PriceIssue`, `row.Difference` | `FindPriceIssue` | `DescribeOffer` |
| What-if | `Orange-Tendering-System/Services/Intelligence/SupplierCalculator.cs:1097` | `rounds`, `result.RoundsAffected`, `HhiBefore/After` | `ConcentrationIndex` | — |
| Tender details | `Orange-Tendering-System/Services/Intelligence/TenderCalculator.cs:358` | `details.Stages`, `rounds` | `BuildRound` | `AddStage` |
| Offer groups | `Orange-Tendering-System/Services/Intelligence/TenderCalculator.cs:437` | `key`, `groupBids` | `StructureKey` | — |
| Export | `Orange-Tendering-System/Services/Intelligence/IntelligenceExportService.cs:57` | `request`, `tender`, `model.Tables` | `AddLifecycleTableAsync` | `writer.ToXlsx` |
| JavaScript | DevTools → Sources → `intelligence-suppliers.js` → `loadSuppliers` (`var data = await getJson(...)`) | `params`, `data` | `renderSuppliers` | — |

Async calls: F11 on an `await` line steps into the called method; when it awaits the database, F10 continues after the result arrives. Verify a number against the database with SQL Server Object Explorer, for example `SELECT COUNT(*) FROM TenderInvitations WHERE SupplierId = 4` and compare with `row.Invitations` (remember: only **closed** cycles count).

---

## 9. Committee questions

| # | Level | Question | Short answer | Show |
|---|---|---|---|---|
| 1 | Beginner | What happens when I open `/Intelligence`? | `Index()` returns the Razor view; JS then calls one JSON endpoint per tab. | `Orange-Tendering-System/Controllers/IntelligenceController.cs:51` |
| 2 | Beginner | Where is MVC here? | Model = `IntelligenceModels.cs`, View = `Index.cshtml`, Controller = `IntelligenceController`. The tabs are filled by JSON (AJAX), not by re-rendering the view. | — |
| 3 | Beginner | Why GET for loading and POST for Simulate/Export? | GET reads with parameters in the URL; Simulate/Export send a JSON body (a list of ids / a report request) and are protected by anti-forgery. | `Orange-Tendering-System/Controllers/IntelligenceController.cs:114` |
| 4 | Beginner | What is a DTO here? | A class shaped for the browser (e.g. `SuppliersData`). Entities and the loaded snapshot never leave the server. | `IntelligenceModels.cs` Part B |
| 5 | Intermediate | Where are the EF Core queries? | All in `IntelligenceService.LoadFromDatabaseAsync` (+ two scope queries in `GetScopeAsync`). | `Orange-Tendering-System/Services/Intelligence/IntelligenceService.cs:208` |
| 6 | Intermediate | Why `AsNoTracking`? | Read-only data: no change tracking needed → less memory, faster. | same |
| 7 | Intermediate | When does the SQL run? | At `ToListAsync` / `FirstOrDefaultAsync`. LINQ after that runs in C# memory. | §5.2 |
| 8 | Intermediate | Why `async`/`await` and `CancellationToken`? | The thread is free while SQL Server works; if the browser cancels the request, `ct` stops the query. | any action |
| 9 | Intermediate | How does the cache work and is it safe? | 60 s, key = user scope + period; refresh removes it; exports always reload. Different scopes never share an entry. | `Orange-Tendering-System/Services/Intelligence/IntelligenceService.cs:128` |
| 10 | Intermediate | Why is `IntelligenceService` Scoped? | One object per request: it remembers the user's scope for that request only. | `Program.cs` |
| 11 | Intermediate | How are sealed bids protected? | `IsUnsealed` decides per cycle; offer lines/prices are queried only for unsealed offers; sealed offers are only counted. | `Orange-Tendering-System/Services/Intelligence/IntelligenceService.cs:360` |
| 12 | Intermediate | What if I change `supplierId` in the URL? | The id is looked up in the user's snapshot; not found → 404. | `Orange-Tendering-System/Controllers/IntelligenceController.cs:91` |
| 13 | Intermediate | Response rate vs win rate? | Response = offers ÷ invitations (closed cycles). Win = awards ÷ awarded tenders bid on. Both `null` when undefined. | §6 |
| 14 | Intermediate | Why the median, not the average? | One extreme offer does not move the median. | `Median` |
| 15 | Intermediate | Why can two offers be "not comparable"? | Different currency, missing prices, different items/quantities, or different specifications. | `FindPriceIssue` |
| 16 | Advanced | Why is the official award not the lowest offer? | The award is the recorded decision (`Tenders.AwardedBidId`); evaluation also scores technical criteria. | `FindAwards` |
| 17 | Advanced | What is HHI? | Σ share²: 1 = one supplier only; lower = more spread. Used for offers (what-if) and awarded value. | `ConcentrationIndex` |
| 18 | Advanced | How are competition groups built? | Pairs with ≥ 2 shared closed cycles are edges; BFS finds connected suppliers. Meeting in tenders is market context, not suspicion. | `BuildRelationships` |
| 19 | Advanced | Why are calculators static and database-free? | They are pure functions of the snapshot: easy to test without a database (`Tests/RadarChecks`). | `Tests/RadarChecks/Program.cs` |
| 20 | Advanced | What did you simplify and why? | Removed a forwarding service, a 9-threshold options hierarchy, a duplicate statistics formula and a helper file; queries now live in one method. Results were verified identical on all 65 endpoint answers. | §1.3 |
| 21 | Advanced | Is the whole snapshot loaded into memory a scaling risk? | For this project's data size it is fast and simple; very large markets would need SQL-side aggregation and paging. | limitation |
| 22 | Advanced | How do you prove the numbers are right? | `Tests/RadarChecks`: synthetic market with known answers + an independent reference formula for supplier statistics + real-database export checks. | test file |
| 23 | JS | How does JS send the anti-forgery token? | `postRequest` adds header `RequestVerificationToken` from `data-csrf`. | `intelligence-page.js` |
| 24 | JS | Does JavaScript calculate anything? | No. It formats and draws the numbers from JSON. | — |

---

## 10. Final revision sheet

**Files:** `IntelligenceController.cs` → `IntelligenceService.cs` → `IntelligenceModels.cs` → `SupplierCalculator.cs` / `TenderCalculator.cs` / `OverviewCalculator.cs` → `IntelligenceExportService.cs`.

**Functions:** `LoadSnapshotAsync`, `GetScopeAsync`, `LoadFromDatabaseAsync`, `IsUnsealed`, `BuildStats`, `Build` (each calculator), `BuildDetails`, `CompareOffers`/`FindPriceIssue`, `Simulate`/`ConcentrationIndex`, `BuildTender`/`BuildRound`, `FindAwards`, `FindSharedRounds`, `Median`.

**Formulas:** response = offers ÷ invitations; win = awards ÷ awarded tenders bid on; position = offer ÷ cycle median − 1; difference % = |A−B| ÷ higher; HHI = Σ share²; lifecycle = notified − created.

**Security:** role attribute + supplier block; scope by department/committee; anti-forgery on POST; sealed offers never priced; ids checked against the user's snapshot; currencies never mixed.

**Shortest chain:** `JS loader → getJson → Controller action → IntelligenceService.GetXxxAsync → LoadSnapshotAsync (scope, cache, EF Core) → Calculator.Build → Json → render`.

**Avoid saying:** "the lowest offer wins", "JavaScript calculates the KPIs", "the cache is shared between users", "sealed bids are hidden in the browser" (they are never loaded).

**Demo plan (5 minutes):** 1) Overview KPIs and Network tab JSON; 2) breakpoint in `LoadFromDatabaseAsync`, show the 9 queries and `IsUnsealed`; 3) Suppliers: choose a category (server filter + 3D cluster), open details; 4) Tenders: lifecycle stages from columns, offer groups, export Excel; 5) change a supplier id to 999999 in the Network tab → 404.
