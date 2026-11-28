# دليل Orange Intelligence للمبتدئين (Overview · Suppliers · Tenders)

> **Superseded (2026-11-28).** The authoritative study references for the final code are `docs/ORANGE_INTELLIGENCE_STUDY_GUIDE.md` (Overview, Suppliers, Tenders) and `docs/AI_STUDIO_STUDY_GUIDE.md` (AI Studio). This file describes an earlier stage: `RadarService`, `RadarDataService`, `RadarSnapshot`, `RadarOptions`, `RadarAnalytics` and `IntelligenceHelpers` no longer exist (merged into `Services/Intelligence/IntelligenceService.cs` and `IntelligenceModels.cs`).


هذا الدليل يشرح **كيف تعمل صفحة Orange Intelligence** (`/Intelligence`) خطوة بخطوة: من الصفحة (Razor) إلى JavaScript، ثم الـ Controller، ثم الـ Service، ثم قاعدة البيانات، ثم العودة إلى الشاشة.

- كل المسارات تبدأ من مجلد مشروع الويب `Orange-Tendering-System/` (مثلاً `Controllers/IntelligenceController.cs` تعني `Orange-Tendering-System/Controllers/IntelligenceController.cs`).
- كل الكود المقتبس منسوخ من **الكود النهائي** بعد التبسيط (2026-11-27). أرقام الأسطر تقريبية للبحث، وأسماء الدوال مكتوبة كما هي حتى تبحث عنها بـ `Ctrl+F` أو `Ctrl+,` في Visual Studio.
- الصفحة فيها **أربعة تبويبات**. هذا الدليل يشرح **ثلاثة** بالتفصيل: Overview و Suppliers و Tenders.
- **تحديث 2026-11-28:** أُعيد **عالم الموردين ثلاثي الأبعاد** إلى تبويب Suppliers، وحُسِّن **مظهر** AI Studio (كرة الصوت وقائمة اختيار المورد). هذه ملفات **عرض فقط** معزولة عن منطق العمل (القسم H). منطق Gemini في AI Studio لم يتغير.
- للبحث عن أي دالة: **`docs/INTELLIGENCE_FUNCTION_MAP.md`** يذكر لكل استدعاء: الملف، الكلاس، التوقيع، المدخلات، المخرجات، وهل يقرأ قاعدة البيانات.

---

## 0. الصورة الكبيرة

### الطبقات

| الطبقة | الملفات | دورها |
|---|---|---|
| الصفحة (Razor View) | `Views/Intelligence/Index.cshtml` | HTML فقط: التبويبات، البطاقات، الجداول، أماكن الرسوم. كل عنصر تملؤه JavaScript له `id` |
| JavaScript (مشترك) | `wwwroot/js/intelligence-page.js` | التبويبات، فلتر التاريخ، دوال الطلبات `getJson`/`postJson`، التنسيق، أدوات الرسوم، الجسر إلى AI Studio، بداية الصفحة |
| JavaScript (لكل تبويب) | `intelligence-overview.js`, `intelligence-suppliers.js`, `intelligence-tenders.js` | يطلب بيانات تبويبه بـ `fetch()` ثم يرسمها. **لا يحسب أي رقم** |
| Controller | `Controllers/IntelligenceController.cs` | يستقبل الطلب (GET/POST)، يتحقق من المدخلات، يستدعي الـ Service ويرجع JSON أو ملفاً |
| Service | `Services/Intelligence/IntelligenceService.cs` | خطوتان: (1) تحميل البيانات المصرح بها (2) استدعاء الحاسبة المناسبة |
| الحاسبات (Calculators) | `OverviewCalculator.cs`, `SupplierCalculator.cs`, `TenderCalculator.cs`, `IntelligenceHelpers.cs` | **كل الحسابات هنا**: المؤشرات، النسب، الوسيط، المقارنات، السيناريو |
| التصدير | `Services/Intelligence/IntelligenceExportService.cs` | ملفات Excel و PDF لتبويب العطاءات |
| الكلاسات (DTOs) | `Services/Intelligence/IntelligenceModels.cs` | شكل الـ JSON الذي يصل إلى المتصفح |
| تحميل البيانات (EF Core) | `Services/Radar/RadarSnapshot.cs` (`RadarDataService`) و `Services/Radar/RadarService.cs` | قراءة الجداول من SQL Server مع قاعدة الختم (Sealing) ونطاق المستخدم (Scope). **مشترك مع AI Studio** |

### الجداول المستخدمة (قراءة فقط)

| الجدول | ماذا نقرأ منه |
|---|---|
| `Suppliers` | اسم الشركة، الفئة `Category`، الحالة `Status` (Approved/Pending/...) |
| `Tenders` | العنوان، الحالة، العملة، القيمة التقديرية، تاريخ الإنشاء، `AwardedBidId` و `AwardedAt` (الترسية الرسمية) |
| `PurchaseRequests` + `Departments` | الجهة (Department) صاحبة العطاء |
| `TenderRounds` | دورات العطاء: رقم الدورة، الحالة، الموعد النهائي `Deadline`، `ClosedAt` |
| `TenderInvitations` | من دُعي إلى أي دورة ومتى |
| `Bids` + `BidItems` + `TenderItems` | العروض المقدمة: آخر نسخة (Revision) لكل دعوة، بنود العرض، الكميات والأسعار، التكاليف الإضافية |
| `Documents` | بيانات المستندات فقط (ليس محتواها) |
| `FinalDecisionNotices` | متى أُرسل إشعار الفائز `SuppliersNotifiedAt` |
| `CommitteeRounds`, `TenderReports` | فقط في ملف Excel الخاص بدورة حياة العطاء |
| `AspNetUsers`, `TenderCommitteeMembers` | لتحديد نطاق المستخدم (جهته ولجانه) |

### قواعد لا تُكسر أبداً
1. **قاعدة الختم (Sealing):** عروض الدورة التي ما زالت مفتوحة للتقديم لا تُقرأ أصلاً. الدورة "مغلقة/قابلة للتحليل" إذا كانت حالتها `Closed` أو `Failed` أو `SentToEvaluation`، أو `Open` وانتهى موعدها. الدالة: `RadarDataService.IsUnsealed` في `Services/Radar/RadarSnapshot.cs`.
2. **نطاق المستخدم (Scope):** مسؤول المشتريات يرى السوق كاملاً. غيره يرى عطاءات جهته ولجانه فقط، والموردين المدعوين إليها فقط. الدالة: `RadarDataService.ScopeAsync`.
3. **العملات لا تُخلط:** القيم المالية تُجمع لكل عملة على حدة، والعروض تُقارن فقط داخل نفس العملة ونفس هيكل البنود.
4. **الصلاحيات:** `[Authorize(Roles = SystemRoles.SourcingOfficer)]` على الـ Controller، وحساب المورد ممنوع دائماً (`OnActionExecuting`).
5. **Anti-Forgery:** كل POST يرسل الرمز في الـ header `RequestVerificationToken` (الدالة `postRequest`).
6. **JavaScript لا يحسب:** كل رقم يأتي جاهزاً من C#. JavaScript يرسم فقط.

### ثلاث طبقات للدراسة (ماذا تدرس بعمق وماذا تفهمه معمارياً فقط)

| الطبقة | الملفات | مستوى الدراسة |
|---|---|---|
| 1. الخلفية (Backend) | الـ Controller، الـ Service، `RadarService`، `RadarSnapshot`، الكلاسات، الحاسبات، التصدير | **ادرسها سطراً بسطر**: هي ما ستشرحه للجنة |
| 2. JavaScript الذي يتصل بالسيرفر | `intelligence-page.js` (`getJson`/`postJson`)، `intelligence-overview.js`، `intelligence-suppliers.js`، `intelligence-tenders.js`، `intelligence-ai.js`، `intelligence-audio.js` | **افهم**: أي دالة ترسل أي طلب، وماذا تفعل بالرد |
| 3. الرسوميات المتقدمة | `orange-intelligence-universe.js` (Three.js)، `intelligence-voice-visual.js` (Canvas)، `intelligence-studio-picker.js`، `orange-intelligence-visuals.css` | **معمارياً فقط**: من أين تأتي البيانات، وأي دالة تبدأها وتحدّثها (القسم H). لا تحتاج رياضيات الحركة |

### ترتيب الدراسة المقترح (Backend أولاً)

| # | الملف | لماذا في هذا الترتيب |
|---|---|---|
| 1 | `Controllers/IntelligenceController.cs` | باب الدخول: كل طلب HTTP يبدأ هنا. اقرأ معه القسم **A.6.1** (أين تُعرَّف كل دالة يستدعيها) |
| 2 | `Services/Intelligence/IntelligenceService.cs` | كل دالة = خطوتان: تحميل الـ Snapshot ثم الحاسبة |
| 3 | `Services/Radar/RadarService.cs` | 30 سطراً: `SnapshotAsync` و `Thresholds` |
| 4 | `Services/Radar/RadarSnapshot.cs` | **قاعدة البيانات**: `RadarPeriod.Parse`، `ScopeAsync` (النطاق)، `LoadAsync` (الذاكرة المؤقتة)، `BuildAsync` (استعلامات EF Core)، `IsUnsealed` (الختم) |
| 5 | `Services/Intelligence/IntelligenceModels.cs` | شكل كل JSON يصل إلى المتصفح |
| 6 | `Services/Intelligence/IntelligenceHelpers.cs` | أدوات صغيرة تستخدمها كل الحاسبات (`Median`, `BuildContext`, `FindAwards`, `FindSharedRounds`) — اقرأها قبل الحاسبات حتى لا تتوقف عندها |
| 7 | `Services/Intelligence/OverviewCalculator.cs` | أقصر حاسبة (140 سطراً): تعلّم النمط عليها |
| 8 | `Services/Intelligence/SupplierCalculator.cs` | أكبر حاسبة: الإحصاءات، الفلاتر، العلاقات، المقارنة، السيناريو |
| 9 | `Services/Intelligence/TenderCalculator.cs` | العطاءات ودوراتها والترسيات |
| 10 | `Services/Intelligence/IntelligenceExportService.cs` | Excel و PDF |
| 11 | ملفات JavaScript الأربعة ثم `intelligence-ai.js` و `intelligence-audio.js` | الاتصال بالسيرفر (القسم D) |
| 12 | (اختياري) القسم H | الرسوميات المتقدمة معمارياً فقط |

> ملاحظة على الترتيب: وضعنا `IntelligenceHelpers.cs` **قبل** `OverviewCalculator.cs` لأن `OverviewCalculator.Build` يستدعي `IntelligenceHelpers.BuildContext` و `IntelligenceHelpers.Median` في أول أسطره.

---

## A. خريطة الملفات الكاملة

> AI Studio: `wwwroot/js/intelligence-ai.js`, `wwwroot/js/intelligence-audio.js`, `Services/Radar/IntelligenceAiService.cs` وأفعال `SupplierSummary`/`AskSupplier`/`StartVoice`. منطق Gemini **لم يتغير**؛ أُضيفت فقط أسطر ربط مرئية صغيرة (القسم H.3).

### A.1 `Views/Intelligence/Index.cshtml` (413 سطراً)
- **لماذا موجود:** هيكل الصفحة: الترويسة والتبويبات الأربعة، شريط السياق (النطاق والتاريخ)، ولوحة لكل تبويب.
- **من يستدعيه:** `IntelligenceController.Index()` عبر `return View();`.
- **ماذا يستدعي:** `_SourcingOfficerLayout`، ملفات CSS، ملفات JavaScript في `@section Scripts`.
- **مدخلاته:** لغة الواجهة `UiLanguage.IsArabic(Context)`، رمز Anti-Forgery.
- **مخرجاته:** HTML فيه عناصر فارغة بـ `id` (مثل `ov-kpis`, `sup-table`, `ten-timeline`) تملؤها JavaScript، وروابط الـ API في خصائص `data-url-*`.
- **مقتطف مهم:**
```cshtml
<div class="radar oi" id="radar" data-radar data-tab="overview"
     data-csrf="@Antiforgery.GetAndStoreTokens(Context).RequestToken"
     data-ui-language="@(arabic ? "ar" : "en")"
     data-url-overview="@U("Overview")"
     data-url-suppliers="@U("Suppliers")" data-url-supplier-details="@U("SupplierDetails")"
```
- `string L(string en, string ar) => arabic ? ar : en;` دالة صغيرة تختار النص حسب اللغة: `@L("Overview", "نظرة عامة")`.
- تبويب Suppliers: البطاقة `<article id="sup-universe" data-three="...three.module.min.js">` تحمل عالم الموردين (عناصر `data-uni-*` يملؤها محرك العرض)، وبجانبها لوحة التفاصيل `.oi-details` داخل `.oi-explorer`. دليل الموردين أصبح تحت لمحة السوق.
- قسم AI Studio: القائمة المنسدلة `<select data-studio-supplier>` **ما زالت موجودة** (مخفية) وهي مصدر الاختيار الحقيقي، وفوقها قائمة بحث مرئية `data-picker-*`. كرة الصوت أصبحت `<canvas data-voice-canvas>`.

### A.2 `wwwroot/js/intelligence-page.js` (555 سطراً) — اقرأه أولاً
| الجزء | الدوال | الوظيفة |
|---|---|---|
| مساعدات | `tr`, `escapeHtml`, `formatNumber`, `formatInteger`, `formatPercent`, `formatSignedPercent`, `formatMoney`, `formatDate`, `formatDateTime`, `cycleLabel` | الترجمة، حماية HTML، تنسيق الأرقام والتواريخ |
| الطلبات | `buildUrl`, `readErrorMessage`, `getJson`, `postRequest`, `postJson`, `downloadFile` | كل اتصال بالسيرفر يمر من هنا |
| شريط السياق | `periodParams`, `showStatus`, `showContext`, `renderPeriodChip`, `onPeriodChanged`, `loadCurrentTab`, `showToast` | فلتر التاريخ ورسائل الحالة |
| التبويبات | `showTab`, `readTabFromHash`, `onTabKeyDown` | إظهار لوحة واحدة وإخفاء الباقي |
| الرسوم | `getChart`, `cssValue`, `categoryColor`, `supplierColor`, `withAlpha`, `gradient`, `chartBase`, `chartAxis`, `chartLegend`, `showChartMessage`, `toggleChartTable`, `renderKpiTiles` | أدوات مشتركة لـ ECharts وبطاقات المؤشرات |
| AI Studio + البداية | `shareSuppliersWithAiStudio`, `loadSupplierListForAiStudio`, `redrawCurrentTab`, `startIntelligencePage` | الجسر إلى AI Studio وبداية الصفحة |
- **من يستدعيه:** المتصفح عند تحميل الصفحة؛ `document.addEventListener('DOMContentLoaded', startIntelligencePage)`.
- **ماذا يستدعي:** دوال ملفات التبويبات (`openOverviewTab`, `openSuppliersTab`, `openTendersTab`, `setup...Events`, `forget...Data`, `loadOverview`...).

### A.3 `wwwroot/js/intelligence-overview.js` (175 سطراً)
- **الدوال:** `openOverviewTab`, `forgetOverviewData`, `loadOverview`, `renderOverview`, `renderOverviewKpis`, `renderOverviewFunnel`, `renderOverviewAttention`, `renderOverviewShortcuts`, `setupOverviewEvents`.
- **الطلب:** `GET /Intelligence/Overview?from=&to=&refresh=`.
- **الرد:** `OverviewData` (JSON).
- **يكتب في:** `#ov-lead`, `#ov-kpis`, `#ov-life-note`, `#ov-funnel`, `#ov-attention`, `#ov-shortcuts`.

### A.4 `wwwroot/js/intelligence-suppliers.js` (1221 سطراً)
- **الدوال الرئيسية:** `openSuppliersTab`, `loadSuppliers`, `renderSuppliers`, `renderSupplierFilters`, `setSupplierFilter`, `renderSupplierKpis`, `renderSupplierUniverse`, `onUniverseSupplierClick`, `showSelectedSupplierInUniverse`, `renderCategoryDonut`, `renderParticipationTrend`, `renderSupplierTable`, `openSupplierDetails`, `renderSupplierDetails` (+ `detailsOverviewHtml`, `detailsParticipationHtml`, `detailsPricingHtml`, `detailsRelationshipsHtml`), `setInsightTab`, `renderSupplierInsights`, `renderLandscapeChart`, `renderPositionsChart`, `supplierChartTableHtml`, `renderSupplierRelationships`, `renderComparisonControls`, `loadComparison`, `compareSuppliers`, `renderComparison`, `renderScenarioPick`, `runSimulation`, `simulationHtml`, `setupSuppliersEvents`.
- **الطلبات:** `GET /Intelligence/Suppliers`, `GET /Intelligence/SupplierDetails`, `GET /Intelligence/CompareSuppliers`, `POST /Intelligence/Simulate`.

### A.5 `wwwroot/js/intelligence-tenders.js` (882 سطراً)
- **الدوال الرئيسية:** `openTendersTab`, `openTenderFromAnotherTab`, `loadTenders`, `applyPendingTender`, `selectTender`, `loadTenderDetails`, `renderTendersTab`, `renderTenderList`, `renderTenderDetails`, `renderTenderKpis`, `renderTenderTimeline`, `renderTenderOffers`, `renderOffersChart`, `renderOfferInsight`, `renderOfferTable`, `renderOfferDetail`, `renderCompetitionChart`, `renderDepartmentChart`, `renderRecurringPairs`, `renderAwardsChart`, `tenderChartTableHtml`, `exportTender`, `setupTendersEvents`.
- **الطلبات:** `GET /Intelligence/Tenders`, `GET /Intelligence/TenderDetails`, `POST /Intelligence/Export`.

### A.6 `Controllers/IntelligenceController.cs` (233 سطراً)
| Action | النوع | يستدعي | يرجع |
|---|---|---|---|
| `Index()` | GET | — | `View()` |
| `Overview(from, to, refresh)` | GET | `intelligence.GetOverviewAsync` | `Json(OverviewData)` |
| `Suppliers(from, to, department, category, activity, search, refresh)` | GET | `intelligence.GetSuppliersAsync` | `Json(SuppliersData)` |
| `SupplierDetails(supplierId, from, to)` | GET | `intelligence.GetSupplierDetailsAsync` | `Json` أو `404` |
| `CompareSuppliers(supplierA, supplierB)` | GET | `intelligence.CompareSuppliersAsync` | `Json(List<OfferComparison>)`، `400` أو `404` |
| `Simulate([FromBody] SimulateRequest)` | POST | `intelligence.SimulateAsync` | `Json(SimulationResult)` أو `400` |
| `Tenders(from, to, refresh)` | GET | `intelligence.GetTendersAsync` | `Json(TendersData)` |
| `TenderDetails(tenderId, from, to)` | GET | `intelligence.GetTenderDetailsAsync` | `Json` أو `404` |
| `Export([FromBody] ExportRequest)` | POST | `exports.BuildAsync` | `File(...)` أو `400` |
| `SupplierSummary`, `AskSupplier`, `StartVoice` | POST | `IntelligenceAiService` | **AI Studio — لم يتغير** |

- **مقتطف (البنّاء التقليدي):**
```csharp
public IntelligenceController(IntelligenceService intelligence, IntelligenceExportService exports, IntelligenceAiService ai,
    ILogger<IntelligenceController> logger)
{
    this.intelligence = intelligence;
    this.exports = exports;
    this.ai = ai;
    this.logger = logger;
}
```

### A.6.1 الـ Controller سطراً بسطر: أين تُعرَّف كل دالة يستدعيها

كل سطر في الجدول = استدعاء واحد تراه داخل `IntelligenceController.cs`. العمود "النوع":
**DB** = يقرأ قاعدة البيانات (أو ذاكرتها المؤقتة) · **ذاكرة** = يعالج بيانات محمّلة في الذاكرة فقط · **إطار** = دالة جاهزة من ASP.NET Core وليست من مشروعنا.

| الاستدعاء داخل الـ Controller | أين يُعرَّف (الملف · الكلاس) | التوقيع | المدخلات | المخرجات | ماذا يستدعي بعده | النوع |
|---|---|---|---|---|---|---|
| `intelligence.GetOverviewAsync(from, to, refresh, ct)` في `Overview` | `Services/Intelligence/IntelligenceService.cs` · `IntelligenceService` | `public async Task<OverviewData> GetOverviewAsync(string? from, string? to, bool refresh, CancellationToken ct)` | التاريخ من/إلى، هل نتجاهل الذاكرة المؤقتة | `OverviewData` | `LoadSnapshotAsync` ثم `OverviewCalculator.Build` | DB ثم ذاكرة |
| `new SupplierFilters()` في `Suppliers` | `Services/Intelligence/IntelligenceModels.cs` · `SupplierFilters` | كلاس بأربع خصائص | — | كائن فارغ نملؤه من الـ query | — | ذاكرة |
| `intelligence.GetSuppliersAsync(from, to, filters, refresh, ct)` | `IntelligenceService.cs` · `IntelligenceService` | `public async Task<SuppliersData> GetSuppliersAsync(string? from, string? to, SupplierFilters filters, bool refresh, CancellationToken ct)` | الفترة + الفلاتر | `SuppliersData` | `LoadSnapshotAsync` ثم `SupplierCalculator.Build` | DB ثم ذاكرة |
| `intelligence.GetSupplierDetailsAsync(supplierId, from, to, ct)` | `IntelligenceService.cs` | `public async Task<SupplierDetails?> GetSupplierDetailsAsync(int supplierId, string? from, string? to, CancellationToken ct)` | رقم المورد + الفترة | `SupplierDetails` أو `null` (خارج النطاق) | `LoadSnapshotAsync` ثم `SupplierCalculator.BuildDetails` | DB ثم ذاكرة |
| `intelligence.CompareSuppliersAsync(supplierA, supplierB, ct)` | `IntelligenceService.cs` | `public async Task<List<OfferComparison>?> CompareSuppliersAsync(int supplierA, int supplierB, CancellationToken ct)` | رقما الموردين | قائمة مقارنات أو `null` | `radar.SnapshotAsync(RadarPeriod.All, ct)` ثم `SupplierCalculator.CompareOffers` | DB ثم ذاكرة |
| `intelligence.SimulateAsync(request, ct)` | `IntelligenceService.cs` | `public async Task<SimulationResult?> SimulateAsync(SimulateRequest request, CancellationToken ct)` | `SimulateRequest` (من، إلى، أرقام الموردين) | `SimulationResult` أو `null` | `LoadSnapshotAsync` ثم `SupplierCalculator.Simulate` | DB ثم ذاكرة |
| `intelligence.GetTendersAsync(from, to, refresh, ct)` | `IntelligenceService.cs` | `public async Task<TendersData> GetTendersAsync(string? from, string? to, bool refresh, CancellationToken ct)` | الفترة | `TendersData` | `LoadSnapshotAsync` ثم `TenderCalculator.Build` | DB ثم ذاكرة |
| `intelligence.GetTenderDetailsAsync(tenderId, from, to, ct)` | `IntelligenceService.cs` | `public async Task<TenderDetails?> GetTenderDetailsAsync(int tenderId, string? from, string? to, CancellationToken ct)` | رقم العطاء + الفترة | `TenderDetails` أو `null` | `LoadSnapshotAsync` ثم `TenderCalculator.BuildTender` | DB ثم ذاكرة |
| `exports.BuildAsync(request, ct)` في `Export` | `Services/Intelligence/IntelligenceExportService.cs` · `IntelligenceExportService` | `public async Task<ExportFile> BuildAsync(ExportRequest request, CancellationToken ct)` | الصيغة، نوع التقرير، العطاء، الدورة | `ExportFile` (bytes + اسم + نوع) | `intelligence.LoadSnapshotAsync(..., true, ct)` ثم `TenderCalculator.BuildTender` ثم `writer.ToXlsx/ToPdf/ToDocx` | DB ثم ذاكرة |
| `Json(obj)` | ASP.NET Core · `Controller` | `JsonResult Json(object? data)` | أي كائن | رد HTTP 200 بصيغة JSON (camelCase) | — | إطار |
| `NotFound(new { error = ... })` / `BadRequest(...)` | ASP.NET Core · `ControllerBase` | — | رسالة خطأ | رد 404 / 400 | — | إطار |
| `File(bytes, contentType, fileName)` | ASP.NET Core · `ControllerBase` | — | محتوى الملف | رد تنزيل ملف | — | إطار |
| `View()` في `Index` | ASP.NET Core · `Controller` | — | — | يعرض `Views/Intelligence/Index.cshtml` | — | إطار |
| `User.IsInRole(SystemRoles.Supplier)` في `OnActionExecuting` | ASP.NET Core (`ClaimsPrincipal`) + ثابت من مشروعنا `SystemRoles` | `bool IsInRole(string role)` | اسم الدور | هل الحساب مورد؟ | `Forbid()` (403) إذا كان مورداً | إطار |

وما يحدث **داخل** `LoadSnapshotAsync` (نفس الملف `IntelligenceService.cs`):

| الاستدعاء | أين يُعرَّف | التوقيع | ماذا يفعل | النوع |
|---|---|---|---|---|
| `RadarPeriod.Parse(from, to)` | `Services/Radar/RadarSnapshot.cs` · `RadarPeriod` | `public static RadarPeriod Parse(string? from, string? to)` | يحوّل نص التاريخ إلى فترة (فارغ = بلا حد) | ذاكرة |
| `radarData.ScopeAsync(ct)` (عند `refresh`) | `RadarSnapshot.cs` · `RadarDataService` | `public async Task<RadarScope> ScopeAsync(CancellationToken ct)` | يحدد نطاق المستخدم (السوق كاملاً، أو عطاءات جهته ولجانه) | DB |
| `radarData.Invalidate(period)` | `RadarSnapshot.cs` · `RadarDataService` | `public void Invalidate(RadarPeriod period)` | يحذف النسخة المؤقتة لهذه الفترة | ذاكرة |
| `radar.SnapshotAsync(period, ct)` | `Services/Radar/RadarService.cs` · `RadarService` | `public Task<RadarSnapshot> SnapshotAsync(RadarPeriod period, CancellationToken ct)` | يمرر إلى `RadarDataService.LoadAsync` | — |
| `data.LoadAsync(period, ct)` | `RadarSnapshot.cs` · `RadarDataService` | `public async Task<RadarSnapshot> LoadAsync(RadarPeriod period, CancellationToken ct)` | يرجع النسخة المؤقتة أو يستدعي `BuildAsync` | DB أو ذاكرة |
| `BuildAsync(period, sc, ct)` | `RadarSnapshot.cs` · `RadarDataService` (private) | `private async Task<RadarSnapshot> BuildAsync(RadarPeriod period, RadarScope sc, CancellationToken ct)` | استعلامات EF Core (`AsNoTracking`) مع قاعدة الختم `IsUnsealed` | **DB** |

### A.7 `Services/Intelligence/IntelligenceService.cs` (126 سطراً)
- **لماذا موجود:** نقطة دخول واحدة للتبويبات الثلاثة. كل دالة عامة = خطوتان: تحميل الـ Snapshot ثم استدعاء الحاسبة.
- **يستدعيه:** `IntelligenceController`، و `IntelligenceExportService` (لتحميل البيانات).
- **يستدعي:** `RadarService.SnapshotAsync`, `RadarDataService.ScopeAsync/Invalidate`, `OverviewCalculator`, `SupplierCalculator`, `TenderCalculator`.
- **مقتطف:**
```csharp
public async Task<OverviewData> GetOverviewAsync(string? from, string? to, bool refresh, CancellationToken ct)
{
    RadarSnapshot snapshot = await LoadSnapshotAsync(from, to, refresh, ct);
    OverviewData overview = OverviewCalculator.Build(snapshot, MinimumOffers);
    return overview;
}
```

### A.8 الحاسبات (Calculators) — `Services/Intelligence/`
| الملف | الدوال العامة | ماذا يحسب |
|---|---|---|
| `OverviewCalculator.cs` (140) | `Build` | مؤشرات النظرة العامة، القمع (Funnel)، قائمة "يستحق انتباهك" |
| `SupplierCalculator.cs` (1244) | `BuildStats`, `BuildPricePositions`, `Build`, `BuildDetails`, `CompareOffers`, `Simulate`, `MakePair` | إحصاءات كل مورد، الفلاتر، الرسوم، العلاقات، المقارنة، السيناريو |
| `TenderCalculator.cs` (532) | `Build`, `BuildCompetition`, `BuildAllTenders`, `BuildTender` | قائمة العطاءات، دورة الحياة، الدورات، مجموعات العروض، المنافسة، الترسيات |
| `IntelligenceHelpers.cs` (241) | `CleanName`, `CategoryOf`, `CycleLabel`, `Median`, `StructureKey`, `IsDemoData`, `BuildContext`, `FindAwards`, `AddMoney`, `FindSharedRounds`, `FirstOfPair`, `SecondOfPair`, `IsApproved` | أدوات مشتركة |
- **المدخل:** دائماً `RadarSnapshot` (نسخة في الذاكرة من البيانات المصرح بها).
- **المخرج:** كلاس من `IntelligenceModels.cs`.
- **لا تقرأ قاعدة البيانات** ⇒ تعمل نفسها داخل الاختبارات (`Tests/RadarChecks`).

### A.9 `Services/Intelligence/IntelligenceModels.cs` (527)
كلاسات عادية بخصائص `{ get; set; }`: `PageContext`, `MoneyValue`, `AwardRecord`, `SupplierPair`, `OverviewData`, `LifecycleStep`, `AttentionItem`, `SupplierFilters`, `SupplierRow`, `PricePosition`, `CategoryCount`, `MonthCount`, `SupplierOption`, `LandscapeSeries`, `PositionSeries`, `CompetitionGroup`, `SuppliersData`, `SupplierDetails`, `SupplierRound`, `ComparedOffer`, `OfferComparison`, `SimulateRequest`, `SimulatedRound`, `SupplierAlternative`, `SimulationResult`, `TendersData`, `TenderListItem`, `RoundCompetition`, `DepartmentCompetition`, `AwardShare`, `AwardDistribution`, `TenderDetails`, `TenderStage`, `TenderRound`, `TenderOffer`, `OfferGroup`, `ExportRequest`, `ExportFile`, `IntelligenceExportException`.
- ASP.NET Core يحوّل أسماء الخصائص إلى camelCase: `TenderCount` في C# تصبح `tenderCount` في JavaScript.
- **تغيير 2026-11-28 (الوحيد في الخلفية):** أُضيفت الخاصية `Category` إلى `SupplierOption`، وسطر واحد في `SupplierCalculator.ToOption` (`option.Category = row.Category;`). السبب: قائمة `AllSuppliers` (كل الموردين المصرح بهم) يرسمها عالم الموردين وقائمة AI Studio، وكلاهما يحتاج اسم الفئة لكل مورد حتى المورد الذي أخفاه فلتر. لم يتغير أي حساب.

### A.10 `Services/Intelligence/IntelligenceExportService.cs` (298)
- `BuildAsync(ExportRequest)`: يتحقق من الصيغة (`xlsx`/`pdf`/`docx`) ونوع التقرير (`tender_lifecycle`, `tender_offers`, `tender_summary`)، يحمّل بيانات **حديثة** (`refresh = true`)، يتحقق أن العطاء والدورة ضمن نطاق المستخدم، يبني `ReportModel`، ثم يكتبه بـ `ReportExport` (الكاتب المشترك في النظام).
- `AddLifecycleTableAsync`: ثلاث استعلامات EF Core إضافية (`CommitteeRounds`, `TenderReports`, `FinalDecisionNotices`).
- `AddOffersTable`: صف لكل عرض في الدورات المغلقة.

### A.11 تحميل البيانات (مشترك، لم يُبسَّط لأنه يحمي الأمان ويخدم AI Studio)
- `Services/Radar/RadarService.cs` (30 سطراً): `SnapshotAsync(period)` و `Thresholds`.
- `Services/Radar/RadarSnapshot.cs` (216): `RadarPeriod.Parse` (قراءة التاريخ)، `RadarDataService.ScopeAsync` (النطاق)، `LoadAsync` (ذاكرة مؤقتة 60 ثانية)، `BuildAsync` (استعلامات EF Core)، `IsUnsealed` (قاعدة الختم).
- `Services/Radar/RadarAnalytics.cs` (84): **يستخدمه AI Studio فقط** (`Suppliers()`). أبقيناه كما هو حرفياً، والاختبار يتأكد أن `SupplierCalculator.BuildStats` يعطي نفس الأرقام.

### A.12 CSS
- `wwwroot/css/orange-radar.css` (181): ألوان (tokens) فاتح/داكن، الأزرار، البطاقات، الجداول، السيناريو، الـ Toast. مشترك مع AI Studio.
- `wwwroot/css/orange-intelligence.css` (519): الترويسة والتبويبات، بطاقات المؤشرات، القمع، العطاءات، العروض، المقارنة، دليل الموردين، وكتلة AI Studio (`.ais-*`). حُذفت منها قواعد الكرة القديمة المصنوعة بـ CSS فقط.
- `wwwroot/css/orange-intelligence-visuals.css` (206): **عرض فقط**: عالم الموردين، كرة الصوت، قائمة اختيار المورد في AI Studio (فاتح/داكن، RTL، الهاتف، تقليل الحركة).

### A.13 الاختبارات
- `Tests/RadarChecks/Program.cs`: 44 فحصاً بدون قاعدة بيانات، و 57 مع `--exports` على قاعدة الفريق المحلية.

### A.14 ملفات العرض المتقدمة (لا تحسب، لا تتصل بالسيرفر)
| الملف | الأسطر | من يبدأه | الواجهة العامة |
|---|---|---|---|
| `wwwroot/js/orange-intelligence-universe.js` | 983 | `renderSupplierUniverse` في `intelligence-suppliers.js` | `window.SupplierUniverse.init / update / selectSupplier / resize / dispose` |
| `wwwroot/js/intelligence-voice-visual.js` | 247 | يبدأ وحده عند تحميل الصفحة؛ `intelligence-audio.js` يعطيه المحللين | `window.OrangeVoiceVisual.connect(mic, speaker)` |
| `wwwroot/js/intelligence-studio-picker.js` | 140 | يبدأ وحده؛ يستمع إلى `radar:data` ويرسم `<select data-studio-supplier>` | لا واجهة عامة: يكتب في الـ `<select>` ويطلق `change` |
| `wwwroot/lib/three/three.module.min.js` | مكتبة | `import()` داخل `orange-intelligence-universe.js` عند أول رسم | Three.js r187 (موجودة سابقاً في المشروع) |

---

## B. تسلسل التنفيذ خطوة بخطوة

> في كل خطوة: **الملف ← الدالة ← المُشغِّل ← الكود ← الشرح ← المدخلات ← المعالجة ← قاعدة البيانات ← الرد ← الخطوة التالية.**

### B.1 فتح `/Intelligence#overview`

**1) المتصفح يطلب الصفحة** ← `Controllers/IntelligenceController.cs` ← `Index()`
```csharp
[HttpGet]
public IActionResult Index()
{
    ViewData["Title"] = "Orange Intelligence";
    ViewData["BodyClass"] = "radar-page";
    return View();
}
```
- `[HttpGet]`: هذه الدالة ترد على طلبات GET فقط.
- قبلها تعمل الحماية: `[Authorize(Roles = SystemRoles.SourcingOfficer)]` (إن لم يكن المستخدم مسؤول مشتريات ⇒ تحويل لصفحة الدخول أو رفض)، ثم `OnActionExecuting` يمنع أي حساب مورد:
```csharp
if (User.IsInRole(SystemRoles.Supplier)) context.Result = Forbid();
```
- `return View();` ⇒ ASP.NET يبحث عن `Views/Intelligence/Index.cshtml`.

**2) Razor يبني HTML** ← `Index.cshtml`: يكتب روابط الـ API في `data-url-*` ورمز `data-csrf`. لوحة Overview ظاهرة، والباقي عليه `hidden`.

**3) المتصفح يحمّل السكربتات بالترتيب:** `echarts.min.js` ← `intelligence-page.js` ← `intelligence-overview.js` ← `intelligence-suppliers.js` ← `intelligence-tenders.js` ← `intelligence-ai.js` ← `intelligence-audio.js`.

**4) بداية الصفحة** ← `intelligence-page.js` ← `startIntelligencePage()` (المُشغِّل: حدث `DOMContentLoaded`)
```javascript
setupOverviewEvents();
setupSuppliersEvents();
setupTendersEvents();

showTab(readTabFromHash());
if (currentTab !== 'suppliers') {
  loadSupplierListForAiStudio();
}
```
- `readTabFromHash()` تقرأ `#overview` من العنوان؛ إن لم يوجد hash ⇒ `'overview'` (التبويب الافتراضي).
- `loadSupplierListForAiStudio()`: AI Studio يحتاج قائمة الموردين حتى لو لم نفتح تبويب Suppliers.

**5) إظهار التبويب** ← `showTab('overview')`
```javascript
var panels = document.querySelectorAll('[data-tab-panel]');
for (var j = 0; j < panels.length; j++) {
  panels[j].hidden = panels[j].dataset.tabPanel !== tab;
}
history.replaceState(null, '', '#' + tab);

if (tab === 'overview') {
  openOverviewTab();
}
```
- `dataset.tabPanel` = قيمة الخاصية `data-tab-panel` في HTML.
- `history.replaceState` يغيّر العنوان إلى `#overview` بدون إعادة تحميل.
- التالي: `openOverviewTab()` في `intelligence-overview.js`.

### B.2 تحميل بيانات Overview

**1) JavaScript** ← `intelligence-overview.js` ← `loadOverview(refresh)`
```javascript
var params = periodParams();
params.refresh = refresh ? 'true' : 'false';
try {
  overviewData = await getJson(page.dataset.urlOverview, params);
  showContext(overviewData.context);
  if (currentTab === 'overview') {
    renderOverview(overviewData);
  }
} catch (error) {
  showStatus(tr('The analysis could not load: ', 'تعذر تحميل التحليل: ') + error.message, true);
}
```
- `periodParams()` ⇒ `{ from: '', to: '' }` من حقلي التاريخ.
- `await` ينتظر الرد بدون تجميد الصفحة.

**2) الطلب HTTP** ← `getJson(url, params)` في `intelligence-page.js`
```javascript
var response = await fetch(buildUrl(url, params), { credentials: 'same-origin' });
if (!response.ok || response.redirected) {
  throw new Error(await readErrorMessage(response));
}
return await response.json();
```
- الطلب الفعلي: `GET /Intelligence/Overview?from=&to=&refresh=false` مع Cookie تسجيل الدخول.

**3) الـ Controller** ← `Overview(...)`
```csharp
[HttpGet]
public async Task<IActionResult> Overview(string? from, string? to, bool refresh, CancellationToken ct)
{
    OverviewData overview = await intelligence.GetOverviewAsync(from, to, refresh, ct);
    return Json(overview);
}
```
- **Model Binding:** ASP.NET يملأ `from`, `to`, `refresh` تلقائياً من الـ query string بنفس الأسماء.
- `CancellationToken ct`: إذا أغلق المستخدم الصفحة يُلغى العمل.

**4) الـ Service** ← `IntelligenceService.LoadSnapshotAsync`
```csharp
RadarPeriod period = RadarPeriod.Parse(from, to);
if (refresh)
{
    await radarData.ScopeAsync(ct);
    radarData.Invalidate(period);
}
RadarSnapshot snapshot = await radar.SnapshotAsync(period, ct);
return snapshot;
```
- `RadarPeriod.Parse`: يقرأ `yyyy-MM-dd`؛ الفارغ = بلا حد، وإذا كان "من" بعد "إلى" يبدّلهما.
- `refresh`: زر التحديث يحذف النسخة المخزنة مؤقتاً.

**5) قاعدة البيانات (EF Core)** ← `Services/Radar/RadarSnapshot.cs` ← `RadarDataService.LoadAsync` ثم `BuildAsync`
```csharp
var key = $"orange-radar:snapshot:{sc.Key}:{period.From:O}:{period.To:O}";
if (cache.TryGetValue(key, out RadarSnapshot? cached) && cached != null) return cached;
var snapshot = await BuildAsync(period, sc, ct);
cache.Set(key, snapshot, TimeSpan.FromSeconds(Math.Clamp(options.Value.CacheSeconds, 0, 600)));
```
- ذاكرة مؤقتة (IMemoryCache) لمدة 60 ثانية لكل مستخدم وفترة ⇒ التبويبات الثلاثة تتشارك نفس النسخة.
- داخل `BuildAsync` استعلامات قراءة فقط `AsNoTracking()`، مثلاً:
```csharp
var suppliers = await db.Suppliers.AsNoTracking()
    .Select(s => new RSupplier(s.Id, s.CompanyName, s.Category, s.Status, s.Email, s.Phone, s.Address, s.TaxNumber, s.RegistrationNumber, s.CreatedAt))
    .ToListAsync(ct);
```
- العروض: فقط `BidStatus.Submitted`، آخر Revision لكل دعوة، وفقط للدورات غير المختومة:
```csharp
var sealedBids = effective.Count(b => !unsealed.Contains(b.RoundId));
var visible = effective.Where(b => unsealed.Contains(b.RoundId)).ToList();
```
  إجمالي العرض = Σ(الكمية × سعر الوحدة) + الشحن + التركيب + الصيانة + الضريبة + أخرى.

**6) الحساب** ← `OverviewCalculator.Build(snapshot, minimumOffers)` (انظر القسم E).

**7) الرد:** JSON مثل:
```json
{ "context": { "scope": "Whole market", "analyzedRounds": 9, ... },
  "tenderCount": 10, "completedTenders": 0, "inProgressTenders": 9,
  "supplierCount": 12, "participatingSuppliers": 8, "submittedOffers": 27, "sealedOffers": 2,
  "medianLifecycleDays": null, "lifecycle": [ { "key": "created", "reached": 10, "share": 1, "medianDays": 0 }, ... ],
  "attention": [ { "kind": "low-competition", "count": 4, "minimumOffers": 3, "tenderId": 6, ... } ] }
```

**8) العرض** ← `renderOverview(data)` ← `renderOverviewKpis` / `renderOverviewFunnel` / `renderOverviewAttention` / `renderOverviewShortcuts`. مثال من القمع:
```javascript
html += '<li style="--w:' + (step.share * 100) + '%">' +
  '<span class="oi-funnel-step">' + (i + 1) + '</span>' +
  '<div><b>' + labels[step.key] + '</b><small>' + detail + '</small></div>' +
  '<i aria-hidden="true"><u></u></i><strong>' + step.reached + '</strong></li>';
...
document.getElementById('ov-funnel').innerHTML = html;
```
- `--w` متغير CSS: عرض الشريط البرتقالي = نسبة العطاءات التي وصلت للمرحلة.

### B.3 تغيير فلتر التاريخ
1. **المُشغِّل:** حدث `change` على `#rd-from` أو `#rd-to` (مسجّل في `startIntelligencePage`):
```javascript
document.getElementById('rd-from').addEventListener('change', function () { onPeriodChanged(false); });
```
2. `onPeriodChanged(refresh)` في `intelligence-page.js`:
```javascript
renderPeriodChip();
forgetOverviewData();
forgetSuppliersData();
forgetTendersData();
loadCurrentTab(refresh);
```
- كل التبويبات تنسى بياناتها القديمة، ويُعاد تحميل التبويب الظاهر فقط. التبويبات الأخرى تُحمَّل عند فتحها.
3. الطلب الجديد: `GET /Intelligence/Overview?from=2026-01-01&to=2026-06-30&refresh=false`.
4. في C#: `RadarPeriod.Contains(round.ReferenceDate)` يحدد الدورات الداخلة في الفترة (تاريخ المرجع = الموعد النهائي، وإلا تاريخ الإغلاق، وإلا الإنشاء).
5. زر ✕ في الشريحة (`#ctx-clear-period`) يفرغ الحقلين ويستدعي `onPeriodChanged(false)`.
6. زر التحديث `#ctx-refresh` ⇒ `onPeriodChanged(true)` ⇒ `refresh=true` ⇒ `radarData.Invalidate(period)`.

### B.4 الانتقال إلى Suppliers
1. **المُشغِّل:** نقرة على زر التبويب `data-tab-link="suppliers"` ⇒ المستمع العام في `startIntelligencePage`:
```javascript
var tabButton = event.target.closest('[data-tab-link], [data-tab-jump]');
if (tabButton) {
  showTab(tabButton.dataset.tabLink || tabButton.dataset.tabJump);
}
```
- `closest(...)` يصعد من العنصر الذي نُقر عليه (قد يكون أيقونة SVG داخل الزر) حتى يجد الزر.
2. `showTab('suppliers')` ⇒ `openSuppliersTab()`:
```javascript
if (suppliersData === null) {
  loadSuppliers(false);
} else {
  renderSuppliers(suppliersData);
}
```

### B.5 تحميل الموردين
1. `loadSuppliers(refresh)` يبني الطلب من الفترة + الفلاتر:
```javascript
params.department = supplierFilters.department;
params.category = supplierFilters.category;
params.activity = supplierFilters.activity;
params.search = supplierFilters.search;
...
suppliersRequestNumber++;
var myRequest = suppliersRequestNumber;
var data = await getJson(page.dataset.urlSuppliers, params);
if (myRequest !== suppliersRequestNumber) {
  return;
}
```
- رقم الطلب يمنع رسم رد قديم إذا كتب المستخدم بسرعة وأُرسل أكثر من طلب.
2. الطلب: `GET /Intelligence/Suppliers?from=&to=&department=&category=&activity=all&search=&refresh=false`.
3. Controller ← `Suppliers(...)` يبني `SupplierFilters` من الـ query ثم `intelligence.GetSuppliersAsync(...)`.
4. Service ← `LoadSnapshotAsync` ثم `SupplierCalculator.Build(snapshot, filters, CompetitionLinkMinRounds, MinimumOffers)`.
5. `SupplierCalculator.Build` بالترتيب: `BuildStats` ← قوائم الفلاتر ← `CleanFilters` ← `ApplyFilters` ← المؤشرات ← رسم الفئات ← `BuildParticipationTrend` ← ترتيب الجدول ← `BuildLandscape` ← `BuildPositionsChart` ← `BuildRelationships` ← القوائم المنسدلة.
6. الرد: `SuppliersData`. في JavaScript:
```javascript
suppliersData = data;
supplierFilters = data.filters;
showContext(data.context);
shareSuppliersWithAiStudio(data.allSuppliers);
if (currentTab === 'suppliers') {
  renderSuppliers(data);
}
```
- `shareSuppliersWithAiStudio` (`intelligence-page.js`): يرسل الحدث `radar:data` الذي ينتظره `intelligence-ai.js` لملء قائمة الموردين في AI Studio (و `intelligence-studio-picker.js` لرسمها).
7. `renderSuppliers(data)` يرسم كل الأجزاء: الفلاتر، المؤشرات، **عالم الموردين** (`renderSupplierUniverse` ← `SupplierUniverse.update` في `orange-intelligence-universe.js`)، الدائرة (Donut)، خط الاتجاه، الجدول، التفاصيل، الرسمين، العلاقات، المقارنة، السيناريو.

### B.6 البحث والفلترة
- **قائمة منسدلة:** `change` على `#sup-category` ⇒ `setSupplierFilter('category', value)`:
```javascript
function setSupplierFilter(name, value) {
  supplierFilters[name] = value;
  loadSuppliers(false);
}
```
  أي أن **الفلترة تتم في السيرفر**، و**كل الأرقام تُعاد حسابها في C#** للموردين المفلترين.
- **البحث:** `input` على `#sup-search` مع انتظار 300ms (Debounce):
```javascript
clearTimeout(searchTimer);
searchTimer = setTimeout(function () { setSupplierFilter('search', event.target.value.trim()); }, 300);
```
- **نقرة على شريحة من الدائرة أو من دليلها** ⇒ `toggleCategoryFilter(category)`.
- **في C#** ← `SupplierCalculator.ApplyFilters`:
```csharp
if (filters.Category != "" && row.Category != filters.Category)
{
    continue;
}
if (filters.Activity == "active" && row.Participations == 0)
{
    continue;
}
```
  فلتر الجهة = الموردون الذين قدّموا عرضاً لعطاء تلك الجهة. القيم غير المعروفة تُهمل في `CleanFilters`.
- من Overview: زر "Suppliers →" في بند "never submitted an offer" ⇒ `showSuppliersWithActivity('silent')`.

### B.7 فتح تفاصيل مورد
1. **المُشغِّل:** نقرة على صف في `#sup-table` (أو اسم في العلاقات/السيناريو/جدول الرسم)، **أو نقرة على نقطة في عالم الموردين** (المحرك يستدعي `onUniverseSupplierClick(id)`) ⇒ `openSupplierDetails(supplierId)`.
   - `openSupplierDetails` تستدعي `showSelectedSupplierInUniverse()` مرتين: فوراً (تدوير الكرة نحو المورد) وبعد وصول التفاصيل (رسم أقواس إلى `details.competitors`).
2. الطلب: `GET /Intelligence/SupplierDetails?supplierId=12&from=&to=`.
3. Controller:
```csharp
SupplierDetails? details = await intelligence.GetSupplierDetailsAsync(supplierId, from, to, ct);
if (details == null)
{
    return NotFound(new { error = "This supplier is not in your authorized scope." });
}
return Json(details);
```
- `?` بعد النوع = قد تكون القيمة `null`. المورد خارج النطاق ⇒ `404`.
4. `SupplierCalculator.BuildDetails(snapshot, supplierId)` يبني:
   - `Supplier`: صف الإحصاءات.
   - `Rounds`: الدورات المغلقة التي قدم فيها عرضاً (الأحدث أولاً)، عدد العروض فيها، وهل رُسي عليه.
   - `Prices`: موقع كل عرض مقابل وسيط دورته.
   - `Competitors`: الموردون الذين التقى بهم وعدد الدورات المشتركة.
5. JavaScript: `renderSupplierDetails()` يختار المحتوى حسب `detailsTab` (`overview`/`participation`/`pricing`/`relationships`). النقر على تبويب داخلي يغيّر `detailsTab` ويعيد الرسم **بدون طلب جديد**.
6. أزرار التذييل: **Ask AI** ⇒ `window.OrangeIntelligenceAI.chooseSupplier(id)` (دالة AI Studio نفسها: تختار المورد وتفتح تبويب AI Studio). **Compare offers** ⇒ `compareSuppliers(id, ...)`.
7. نقرة على دورة في Participation ⇒ `openTenderFromAnotherTab(tenderId, roundId)` (يفتح تبويب العطاءات على تلك الدورة).

### B.8 مقارنة موردين
1. **المُشغِّل:** اختيار المورد الأول/الثاني في `#cmp-supplier-a/b`، أو زر "Compare offers" في جدول العلاقات (`data-compare-pair`).
2. `loadComparison()` ⇒ `GET /Intelligence/CompareSuppliers?supplierA=3&supplierB=7`.
3. Controller: يرفض نفس المورد مرتين أو قيمة ≤ 0 بـ `400`، ومورداً خارج النطاق بـ `404`.
4. Service: `radar.SnapshotAsync(RadarPeriod.All, ct)` — **كل التاريخ المسجل** (مثل AI Studio)، ثم `SupplierCalculator.CompareOffers`.
5. `CompareOffers`: لكل دورة مغلقة فيها عرض من A وعرض من B ⇒ `CompareTwoOffers`. لكل عطاء نختار دورة **قابلة للمقارنة** أولاً ثم **الأحدث**:
```csharp
OfferComparison best = byTender[tenderId]
    .OrderBy(row => row.PriceIssue != null)
    .ThenByDescending(row => row.RoundNumber)
    .First();
```
6. `FindPriceIssue` يرجع سبباً يمنع مقارنة السعر: `missing_currency` / `different_currency` / `missing_price` / `different_structure` / `different_specifications`، أو `null` إذا كانت المقارنة صحيحة.
7. JavaScript: تختار عطاءً من `#cmp-tender` ⇒ `renderComparison()` يرسم بطاقتين: **Price Comparison** (المبلغان، الفرق، النسبة) و **Submission Timing** (وقتا التقديم بتوقيت عمّان، المدة إلى الموعد النهائي، الفرق بينهما). زر "Open tender details" ⇒ `openTenderFromAnotherTab`.

### B.9 علاقات السوق (Market relationships)
- البيانات تأتي مع `GET /Intelligence/Suppliers` (لا يوجد طلب منفصل): `pairs`, `pairCount`, `groups`, `unconnectedSuppliers`, `competitionMinimum`.
- C#: `IntelligenceHelpers.FindSharedRounds` ثم `SupplierCalculator.BuildRelationships` (أزواج + مجموعات بالبحث بالعرض BFS).
- JavaScript: `renderSupplierRelationships(data)` يرسم جدول الأزواج (أعلى 25) وبطاقات المجموعات.

### B.10 السيناريو "ماذا لو" (What-if)
1. **المُشغِّل:** فتح `<details id="sim-fold">`، اختيار مورد أو أكثر، ثم زر `#sim-run` ⇒ `runSimulation()`.
2. طلب POST مع رمز Anti-Forgery:
```javascript
var simulation = await postJson(page.dataset.urlSimulate, { from: period.from, to: period.to, supplierIds: ids });
```
```javascript
headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': page.dataset.csrf },
```
3. Controller: `Simulate([FromBody] SimulateRequest request)` — `[FromBody]` يقرأ الـ JSON إلى كلاس `SimulateRequest`.
4. Service: `SimulateAsync` يأخذ حتى 10 موردين مختلفين ويرفض أي مورد خارج النطاق (يرجع `null` ⇒ `400`).
5. `SupplierCalculator.Simulate`: يعيد عدّ المقدمين في كل دورة مغلقة بعد استبعادهم (القسم E).
6. JavaScript: `simulationHtml(simulation)` يرسم قبل/بعد. **افتراضي، لا يغيّر أي بيانات.**

### B.11 الانتقال إلى Tenders
`showTab('tenders')` ⇒ `openTendersTab()`:
```javascript
if (tendersData === null) {
  loadTenders(false);
} else if (pendingTender !== null) {
  applyPendingTender();
} else {
  renderTendersTab();
}
```
- `pendingTender`: عطاء طلبه تبويب آخر (Overview أو تفاصيل مورد أو المقارنة).
- `loadTenders` ⇒ `GET /Intelligence/Tenders` ⇒ `TenderCalculator.Build` ⇒ `TendersData` (قائمة العطاءات + رسوم السوق + `defaultTenderId`).
- العطاء الافتراضي: أول عطاء (الأحدث) له دورة مغلقة فيها عروض، وإلا الأحدث.

### B.12 اختيار عطاء
1. **المُشغِّل:** نقرة على زر في `#ten-list` أو اختيار من `#ten-pick` ⇒ `selectTender(tenderId)` ⇒ `loadTenderDetails(tenderId)`.
2. `GET /Intelligence/TenderDetails?tenderId=5&from=&to=` ⇒ `TenderCalculator.BuildTender(snapshot, tenderId)` ⇒ `TenderDetails` أو `404`.
3. JavaScript يتجاهل رداً متأخراً لعطاء آخر:
```javascript
if (details.tenderId !== selectedTenderId) {
  return;
}
```
4. `renderTenderDetails()` ⇒ البطاقة الرئيسية + `renderTenderKpis` + `renderTenderTimeline` + `renderTenderOffers`.
5. البحث في القائمة `#ten-search` يخفي العناصر في المتصفح فقط (لا يغير أي رقم).

### B.13 دورة حياة العطاء
- C# ← `TenderCalculator.BuildTender` ← `AddStage` ست مرات، كل مرحلة من عمودها في قاعدة البيانات:

| Key | المرحلة | المصدر |
|---|---|---|
| `created` | إنشاء العطاء | `Tenders.CreatedAt` |
| `round` | إنشاء الدورة الأولى | `TenderRounds.CreatedAt` (RoundNumber = 1) |
| `invited` | أول دعوة | `TenderInvitations.InvitedAt` (الأقدم) |
| `closed` | إغلاق التقديم | `TenderRounds.ClosedAt` (الأحدث) |
| `awarded` | الترسية الرسمية | `Tenders.AwardedAt` |
| `notified` | إرسال إشعار الفائز | `FinalDecisionNotices.SuppliersNotifiedAt` |
- المرحلة غير المسجلة: `At = null` ⇒ تظهر "Not recorded" ولا تُقدَّر أبداً.
- الحالة `State`: `Completed` (أُرسل الإشعار) ثم `Stopped` (ملغى/مرفوض) ثم `Awarded` ثم `In progress`.
- JavaScript ← `renderTenderTimeline(tender)` يرسم 6 نقاط؛ المكتملة `is-done` والمفقودة `is-missing`.

### B.14 دورات العطاء (Bidding cycles)
- C# ← `TenderCalculator.BuildRound`:
```csharp
// A sealed cycle reports only its invitations: even an offer count would leak bidding activity.
if (!round.Unsealed)
{
    return view;
}
```
- الدورة المختومة: `Analysed = false`, `Offers = 0`, `Bids = []`, `Groups = []` ⇒ في الشاشة: "Sealed — hidden until bidding closes".
- JavaScript ← `openedRounds()`: الدورات `analysed && offers > 0` فقط تظهر في `#off-round`.

### B.15 مقارنة العروض داخل الدورة
1. C# ← `BuildRound`: يجمع العروض التي لها بنود وإجمالي موجب في **مجموعات** حسب `العملة + StructureKey` (نفس البنود والوحدات والكميات). لكل مجموعة: الوسيط، الأقل، الأعلى، ومن يملك الوسيط.
2. JavaScript ← `renderTenderOffers()`:
   - اختيار الدورة (`offerRoundId`، الافتراضي آخر دورة مغلقة).
   - اختيار المجموعة (`offerGroupId`، الافتراضي أكبر مجموعة؛ القائمة `#off-group` تظهر فقط إذا وُجدت مجموعتان أو أكثر).
   - `renderOffersChart(chart, group)`: أعمدة أفقية، الأقل بلون برتقالي قوي، خط متقطع للوسيط.
   - `renderOfferInsight`: الجملة التفسيرية والبطاقات الثلاث (Lowest/Median/Highest).
   - `renderOfferTable`: كل عروض الدورة (حتى خارج المجموعة) مع ملاحظة المقارنة.
   - نقرة على عمود أو رقم عرض ⇒ `renderOfferDetail` + رابط "Open offer record" إلى `/SourcingOfficer/Bids/Details?id=`.
3. رسوم السوق أسفل الصفحة: `renderCompetitionChart` (مدعو مقابل مقدم لكل دورة؛ النقر يفتح العطاء)، `renderDepartmentChart`، `renderRecurringPairs` (النقر يفتح مقارنة الموردين)، `renderAwardsChart`.

### B.16 تصدير Excel
1. **المُشغِّل:** `#ten-export-lifecycle` ⇒ `exportTender('lifecycle')`، أو `#off-export` ⇒ `exportTender('offers')`.
```javascript
request.format = 'xlsx';
request.reportType = 'tender_offers';
request.roundId = round.roundId;
...
downloadFile(request);
```
2. `downloadFile` ⇒ `postRequest(page.dataset.urlExport, request)` ⇒ `POST /Intelligence/Export` مع الـ JSON والرمز.
3. Controller:
```csharp
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
```
4. `IntelligenceExportService.BuildAsync`: تحقق ← بيانات حديثة ← تحقق النطاق ← `ReportModel` ← `writer.ToXlsx(model)`.
5. المتصفح: `response.blob()` ⇒ رابط مؤقت `URL.createObjectURL` ⇒ `link.click()` ⇒ تنزيل الملف ⇒ `showToast`.
- **الأمان:** المتصفح يرسل أرقام التعريف فقط، وكل الصفوف تُبنى من جديد في السيرفر من بيانات المستخدم المصرح بها.

### B.17 تصدير PDF
- `#ten-export-pdf` ⇒ `exportTender('pdf')` ⇒ `format: 'pdf', reportType: 'tender_summary'` ⇒ `AddLifecycleTableAsync` + `AddOffersTable` ⇒ `writer.ToPdf(model)`.

### B.18 الأخطاء والبيانات الفارغة
| الحالة | أين تُعالج | ماذا يرى المستخدم |
|---|---|---|
| خطأ في الطلب (شبكة/500) | `getJson` يرمي `Error` ⇒ `catch` في `load...` | "The analysis could not load: ..." باللون الأحمر في `#ctx-status` |
| رد بخطأ `{ error }` | `readErrorMessage` | رسالة السيرفر نفسها |
| انتهت الجلسة (تحويل لصفحة الدخول) | `response.redirected` في `getJson` | رسالة خطأ بدل رسم صفحة HTML |
| لا عطاءات في النطاق | `showContext` | "No authorized tenders in this scope..." |
| لا دورات مغلقة في الفترة | `showContext` | "No closed bidding cycle falls inside this period..." |
| رسم بلا بيانات | `showChartMessage(chart, text)` | جملة في وسط الرسم |
| مورد/عطاء خارج النطاق | Controller ⇒ `404` | رسالة في اللوحة أو الحالة |
| تصدير غير صالح | `IntelligenceExportException` ⇒ `400` | "Export failed: ..." |
| POST بدون رمز Anti-Forgery | `[AutoValidateAntiforgeryToken]` | الطلب مرفوض (400، وصفحة الحالة في النظام تحوله إلى 405) |
| لا مورد مختار في السيناريو | `runSimulation` | "Choose at least one supplier to exclude." |

---

## C. مرجع الدوال

### C.1 JavaScript

| الملف | الدالة | يستدعيها | تستدعي | الغرض |
|---|---|---|---|---|
| page | `startIntelligencePage` | `DOMContentLoaded` | `setup...Events`, `showTab`, `loadSupplierListForAiStudio` | بداية الصفحة وربط الأحداث |
| page | `showTab` | أزرار التبويب، `hashchange`, AI Studio | `open...Tab` | إظهار تبويب واحد |
| page | `readTabFromHash` | `startIntelligencePage` | — | قراءة `#tab` |
| page | `onTabKeyDown` | `keydown` على التبويبات | `showTab` | الأسهم بين التبويبات |
| page | `getJson` | كل `load...` | `buildUrl`, `fetch`, `readErrorMessage` | GET يرجع JSON |
| page | `postRequest` / `postJson` | `runSimulation`, `downloadFile` | `fetch` | POST مع Anti-Forgery |
| page | `downloadFile` | `exportTender` | `postRequest`, `showToast`, `showStatus` | تنزيل Excel/PDF |
| page | `periodParams` | كل الطلبات | — | قيمتا "من/إلى" |
| page | `showContext` | كل `load...` | `showStatus` | النطاق، العلامة التجريبية، رسالة الحالة |
| page | `onPeriodChanged` | حقول التاريخ، التحديث، ✕ | `forget...Data`, `loadCurrentTab` | تحديث الفترة |
| page | `getChart` | كل دوال الرسم | `echarts.init` | إنشاء/إرجاع رسم ECharts |
| page | `chartBase` / `chartAxis` / `chartLegend` | دوال الرسم | `cssValue` | الإعدادات المشتركة |
| page | `categoryColor` / `supplierColor` | الرسوم والجداول | `cssValue` | رقم اللون ⇐ لون CSS |
| page | `renderKpiTiles` | `render...Kpis` | `escapeHtml`, `formatInteger` | بطاقات المؤشرات |
| page | `toggleChartTable` | روابط "Table" | — | إظهار/إخفاء جدول تحت الرسم |
| page | `shareSuppliersWithAiStudio` | `loadSuppliers`, `loadSupplierListForAiStudio` | `dispatchEvent('radar:data')` | قائمة موردين لـ AI Studio |
| overview | `openOverviewTab` | `showTab` | `loadOverview`/`renderOverview` | فتح التبويب |
| overview | `loadOverview` | `openOverviewTab`, `loadCurrentTab` | `getJson`, `showContext`, `renderOverview` | جلب البيانات |
| overview | `renderOverview` | `loadOverview` | 4 دوال رسم | رسم التبويب |
| overview | `renderOverviewAttention` | `renderOverview` | `escapeHtml` | قائمة الانتباه |
| overview | `setupOverviewEvents` | `startIntelligencePage` | `openTenderFromAnotherTab`, `showSuppliersWithActivity` | أزرار قائمة الانتباه |
| suppliers | `loadSuppliers` | `openSuppliersTab`, `setSupplierFilter` | `getJson`, `renderSuppliers` | جلب البيانات المفلترة |
| suppliers | `renderSuppliers` | `loadSuppliers` | كل دوال الرسم | رسم التبويب |
| suppliers | `setSupplierFilter` | القوائم، البحث، الدائرة | `loadSuppliers` | تغيير فلتر |
| suppliers | `renderSupplierTable` | `renderSuppliers` | `categoryColor` | دليل الموردين |
| suppliers | `openSupplierDetails` | الجدول، الرسوم، العلاقات | `getJson`, `renderSupplierDetails` | تفاصيل مورد |
| suppliers | `renderSupplierDetails` | `openSupplierDetails`, تبويبات التفاصيل | `details...Html` | لوحة التفاصيل |
| suppliers | `renderSupplierInsights` | `renderSuppliers`, `setInsightTab` | `renderLandscapeChart`/`renderPositionsChart` | الرسمان والاستنتاجات |
| suppliers | `renderSupplierRelationships` | `renderSuppliers` | — | الأزواج والمجموعات |
| suppliers | `loadComparison` | القوائم، `compareSuppliers` | `getJson`, `renderComparison` | جلب المقارنة |
| suppliers | `renderComparison` | `loadComparison`, `#cmp-tender` | `comparisonPriceHtml`, `comparisonTimingHtml` | بطاقتا المقارنة |
| suppliers | `runSimulation` | `#sim-run` | `postJson`, `simulationHtml` | السيناريو |
| tenders | `loadTenders` | `openTendersTab` | `getJson`, `renderTendersTab`, `loadTenderDetails` | قائمة العطاءات |
| tenders | `openTenderFromAnotherTab` | Overview، التفاصيل، المقارنة | `showTab`/`openTendersTab` | فتح عطاء محدد |
| tenders | `applyPendingTender` | `openTendersTab`, `loadTenders` | `loadTenderDetails`, `onPeriodChanged` | تطبيق العطاء المطلوب |
| tenders | `selectTender` | القائمة، `#ten-pick` | `loadTenderDetails` | اختيار عطاء |
| tenders | `loadTenderDetails` | `selectTender`, `loadTenders` | `getJson`, `renderTenderDetails` | تفاصيل عطاء |
| tenders | `renderTenderOffers` | `renderTenderDetails`, `#off-round`, `#off-group` | `renderOffersChart`, `renderOfferInsight`, `renderOfferTable` | قسم العروض |
| tenders | `renderCompetitionChart` | `renderTendersTab` | `getChart` | المشاركة لكل دورة |
| tenders | `exportTender` | أزرار التصدير | `downloadFile` | Excel/PDF |

### C.2 C#

| الملف | الدالة | يستدعيها | تستدعي | الغرض |
|---|---|---|---|---|
| Controller | `Overview` | `GET /Intelligence/Overview` | `GetOverviewAsync` | JSON النظرة العامة |
| Controller | `Suppliers` | `GET /Intelligence/Suppliers` | `GetSuppliersAsync` | JSON الموردين |
| Controller | `SupplierDetails` | `GET /Intelligence/SupplierDetails` | `GetSupplierDetailsAsync` | تفاصيل مورد |
| Controller | `CompareSuppliers` | `GET /Intelligence/CompareSuppliers` | `CompareSuppliersAsync` | مقارنة |
| Controller | `Simulate` | `POST /Intelligence/Simulate` | `SimulateAsync` | سيناريو |
| Controller | `Tenders` | `GET /Intelligence/Tenders` | `GetTendersAsync` | JSON العطاءات |
| Controller | `TenderDetails` | `GET /Intelligence/TenderDetails` | `GetTenderDetailsAsync` | تفاصيل عطاء |
| Controller | `Export` | `POST /Intelligence/Export` | `exports.BuildAsync` | ملف |
| IntelligenceService | `LoadSnapshotAsync` | كل الدوال + التصدير | `RadarPeriod.Parse`, `SnapshotAsync` | تحميل البيانات |
| OverviewCalculator | `Build` | `GetOverviewAsync` | `BuildAllTenders`, `BuildStats`, `BuildCompetition` | النظرة العامة |
| SupplierCalculator | `BuildStats` | `Build`, `BuildDetails`, `Simulate`, Overview, Tenders | `FindAwards`, `BuildPricePositions` | إحصاءات الموردين |
| SupplierCalculator | `BuildPricePositions` | `BuildStats`, الرسم، التفاصيل | `Median` | موقع السعر |
| SupplierCalculator | `Build` | `GetSuppliersAsync` | `ApplyFilters`, `BuildLandscape`, `BuildPositionsChart`, `BuildRelationships` | تبويب الموردين |
| SupplierCalculator | `BuildDetails` | `GetSupplierDetailsAsync` | `BuildStats`, `FindSharedRounds` | لوحة التفاصيل |
| SupplierCalculator | `CompareOffers` | `CompareSuppliersAsync` | `CompareTwoOffers`, `FindPriceIssue` | المقارنة |
| SupplierCalculator | `Simulate` | `SimulateAsync` | `ConcentrationIndex`, `BuildStats` | السيناريو |
| TenderCalculator | `Build` | `GetTendersAsync` | `BuildAllTenders`, `BuildCompetition`, `BuildDepartments`, `BuildRecurringPairs`, `BuildAwardDistributions` | تبويب العطاءات |
| TenderCalculator | `BuildTender` | `GetTenderDetailsAsync`, التصدير | `AddStage`, `BuildRound` | تفاصيل عطاء |
| TenderCalculator | `BuildRound` | `BuildTender` | `StructureKey`, `Median` | دورة + عروض + مجموعات |
| IntelligenceExportService | `BuildAsync` | `Export` | `LoadSnapshotAsync`, `BuildTender`, `AddLifecycleTableAsync`, `AddOffersTable`, `ReportExport` | الملف |
| RadarDataService | `ScopeAsync` | `LoadAsync`, التحديث | EF Core | نطاق المستخدم |
| RadarDataService | `BuildAsync` | `LoadAsync` | EF Core | قراءة كل الجداول |
| RadarDataService | `IsUnsealed` | `BuildAsync` | — | قاعدة الختم |

---

## D. كل الروابط بين JavaScript والسيرفر

| عنصر HTML | الحدث | دالة JS | الطلب HTTP | Controller | Service / Calculator | EF Core | الرد | تحديث الشاشة |
|---|---|---|---|---|---|---|---|---|
| فتح الصفحة / تبويب Overview | `DOMContentLoaded` / `click` | `loadOverview` | `GET /Intelligence/Overview` | `Overview` | `GetOverviewAsync` ⇒ `OverviewCalculator.Build` | `RadarDataService.BuildAsync` | `OverviewData` | `renderOverview` |
| `#rd-from`, `#rd-to` | `change` | `onPeriodChanged(false)` | طلب التبويب الحالي مع `from/to` | حسب التبويب | `RadarPeriod.Parse` | snapshot جديد للفترة | — | إعادة الرسم |
| `#ctx-refresh` | `click` | `onPeriodChanged(true)` | `...&refresh=true` | حسب التبويب | `Invalidate` ثم تحميل | قراءة جديدة | — | إعادة الرسم |
| تبويب Suppliers / الفلاتر / البحث | `click`/`change`/`input` | `loadSuppliers` | `GET /Intelligence/Suppliers?...` | `Suppliers` | `SupplierCalculator.Build` | snapshot | `SuppliersData` | `renderSuppliers` |
| صف في `#sup-table` | `click` | `openSupplierDetails` | `GET /Intelligence/SupplierDetails?supplierId=` | `SupplierDetails` | `BuildDetails` | snapshot | `SupplierDetails` | `renderSupplierDetails` |
| `#cmp-supplier-a/b`, `[data-compare-pair]` | `change`/`click` | `loadComparison` | `GET /Intelligence/CompareSuppliers?supplierA=&supplierB=` | `CompareSuppliers` | `CompareOffers` | snapshot (كل التاريخ) | `OfferComparison[]` | `renderComparison` |
| `#sim-run` | `click` | `runSimulation` | `POST /Intelligence/Simulate` | `Simulate` | `SupplierCalculator.Simulate` | snapshot | `SimulationResult` | `simulationHtml` |
| تبويب Tenders | `click` | `loadTenders` | `GET /Intelligence/Tenders` | `Tenders` | `TenderCalculator.Build` | snapshot | `TendersData` | `renderTendersTab` |
| `#ten-list`, `#ten-pick`, عمود في رسم المشاركة | `click`/`change` | `selectTender`/`loadTenderDetails` | `GET /Intelligence/TenderDetails?tenderId=` | `TenderDetails` | `BuildTender` | snapshot | `TenderDetails` | `renderTenderDetails` |
| `#ten-export-lifecycle`, `#off-export`, `#ten-export-pdf` | `click` | `exportTender` ⇒ `downloadFile` | `POST /Intelligence/Export` | `Export` | `IntelligenceExportService.BuildAsync` | snapshot + `CommitteeRounds`, `TenderReports`, `FinalDecisionNotices` | ملف | تنزيل + Toast |
| زر "Ask AI" | `click` | `OrangeIntelligenceAI.chooseSupplier` | — (AI Studio) | — | — | — | — | فتح AI Studio |

---

## E. الحسابات (بأمثلة رقمية)

> مثال موحّد: 5 دورات مغلقة؛ في كل دورة 6 موردين مدعوين و5 قدّموا عروضاً؛ 4 ترسيات رسمية بالدينار (JOD). (هذا هو السوق التجريبي في `Tests/RadarChecks`.)

### E.1 مؤشرات Overview (`OverviewCalculator.Build`)
| المؤشر | المعادلة | المثال |
|---|---|---|
| Tenders in scope | عدد العطاءات التي لها دورة في الفترة | 6 |
| Completed | عطاءات لها `DaysToNotification` | 0 |
| In progress | الحالة `In progress` أو `Awarded` | — |
| Suppliers | `snapshot.Suppliers.Count` | 6 |
| Participating suppliers | عدد الموردين المختلفين في `snapshot.Bids` | 5 |
| Submitted offers | `snapshot.Bids.Count` (المغلقة فقط) | 25 |
| Sealed offers | `snapshot.SealedBids` (يُعد فقط، لا يُعرض) | 2 |
| Median lifecycle | وسيط (تاريخ إرسال الإشعار − تاريخ الإنشاء) للعطاءات المكتملة | 10، 14، 30 يوماً ⇐ **14** |

**القمع (Funnel):** لكل مرحلة: `Reached` = عدد العطاءات التي لها تاريخ مسجل لهذه المرحلة، `Share = Reached / max(1, عدد العطاءات)`، `MedianDays` = وسيط (تاريخ المرحلة − تاريخ الإنشاء). مثال: 7 من 10 عطاءات رُسيت ⇐ الشريط 70%.

**يستحق انتباهك:**
- دورات منخفضة المنافسة: `Submitted < MinimumOffers` (3 افتراضياً في `appsettings` ⇒ `OrangeRadar:Thresholds`).
- أقدم عطاءين "In progress" مع عدد الأيام = `GeneratedAt − CreatedAt`.
- موردون صامتون: `Invitations > 0` و `Participations == 0`.

### E.2 إحصاءات المورد (`SupplierCalculator.BuildStats`)
| الإحصاء | المعادلة | مثال |
|---|---|---|
| Invitations | دعوات المورد إلى **دورات مغلقة** | 5 |
| Participations | عدد الدورات المختلفة التي قدّم فيها عرضاً | 5 |
| Response rate | (عروضه التي تجيب دعوة) ÷ دعواته | 5 ÷ 5 = **100%**؛ مورد مدعو 5 مرات بلا عروض ⇒ 0% |
| Awarded tenders bid | عطاءات قدم فيها عرضاً وانتهت بترسية | 4 |
| Wins | ترسيات رسمية فاز بها | 2 |
| Win rate | Wins ÷ AwardedTendersBid (تقريب 3 منازل) | 2 ÷ 4 = **0.5 = 50%**؛ إذا لم يشارك في عطاء مرسى ⇒ غير معرّف (`null`) |
| Awarded value | مجموع قيم ترسياته **لكل عملة** | JOD 48,650 |
| Relative price | متوسط مواقع أسعاره (E.3) | +0.7% |
| Compliance | عروضه المفحوصة (Compliant/NonCompliant) والمطابقة منها | 5 من 5 |
| First/Last activity | أقل/أكبر `AvailableAt` لعروضه | — |

**مؤشرات تبويب الموردين (للموردين بعد الفلترة):**
- Registered = عدد الموردين المعروضين؛ Approved = حالتهم `Approved`.
- Participating = من لديهم `Participations > 0`.
- Categories = عدد الفئات المختلفة؛ الأكبر = الفئة الأكثر موردين.
- Response rate = Σ Participations ÷ Σ Invitations. المثال: 25 ÷ 30 = **83%** (المورد السادس مدعو 5 مرات بلا عروض فالمقام 30).

### E.3 موقع السعر (`BuildPricePositions`)
- داخل **نفس الدورة ونفس العملة** فقط، وتحتاج عرضين على الأقل بإجمالي موجب.
- `Relative = (Total ÷ Median) − 1` (تقريب 4 منازل).
- مثال: عروض 900، 1000، 1100 JOD ⇒ الوسيط 1000 ⇒ −10%، 0%، +10%.
- الوسيط (`IntelligenceHelpers.Median`): رتّب القيم؛ إذا كان العدد فردياً خذ الأوسط، وإن كان زوجياً فمتوسط الاثنين في الوسط. مثال: 900، 1000، 1100، 1300 ⇒ (1000 + 1100) ÷ 2 = 1050.
- رسم "Price position over time" يعرض أكثر 4 موردين عروضاً قابلة للمقارنة. الاستنتاجات: متوسط المواقع المعروضة، المدى (الأعلى − الأدنى)، المورد ذو أقل متوسط، عدد العروض.

### E.4 رسم المشاركة مقابل الفوز (`BuildLandscape`)
- X = Participations، Y = Win rate × 100 (إن وُجدت ترسيات ولدى أحدهم Win rate)، وإلا Response rate × 100.
- حجم النقطة (عند استخدام Win rate) = 18 + √(القيمة المرساة ÷ أكبر قيمة) × 34. مثال: المورد بربع أكبر قيمة ⇒ 18 + 0.5 × 34 = 35 بكسل.
- الموردون بدون عروض لا يُرسمون (`NotPlotted`).

### E.5 العلاقات والمنافسة
- `FindSharedRounds`: في كل دورة مغلقة، كل زوج من الموردين المقدمين يشترك في تلك الدورة. مفتاح الزوج `"3-7"` (الرقم الأصغر أولاً). عرضان من نفس المورد في نفس الدورة لا يُحسبان مرتين.
- مثال: دورة فيها A، B، C ⇒ الأزواج A-B، A-C، B-C. خمس دورات كهذه ⇒ كل زوج = 5 دورات مشتركة.
- المجموعات: نربط زوجين إذا كانت دوراتهما المشتركة ≥ `CompetitionLinkMinRounds` (2)، ثم نجد المجموعات المترابطة بالبحث بالعرض (Queue). A-B و B-C ⇒ مجموعة واحدة {A، B، C} حتى لو لم يلتقِ A و C مباشرة.
- **الالتقاء في العطاءات سياق سوق عادي، وليس شبهة.**

### E.6 مقارنة موردين (`CompareTwoOffers`)
- الفرق = |A − B|. النسبة = الفرق ÷ **العرض الأعلى**. مثال: 900 و 1000 ⇒ 100 ÷ 1000 = **10%**.
- من الاختبار: 57.125 و 65.375 ⇒ الفرق 8.25، النسبة 8.25 ÷ 65.375 ≈ 12.62%، العرض الأقل "a".
- الوقت: ثوانٍ بين التقديمين = (B − A)؛ موجب ⇒ A قدّم أولاً. المدة للموعد النهائي = الموعد − وقت التقديم.
- توقيت عمّان: `TimeZoneInfo.ConvertTime(..., "Asia/Amman")` (UTC+3).
- لا مقارنة سعرية إذا اختلفت العملة أو البنود/الكميات/الوحدات أو المواصفات/الانحرافات.

### E.7 السيناريو (`Simulate`)
- لكل دورة مغلقة: Before = عدد المقدمين، After = عددهم بعد الاستبعاد.
- `DropsBelowMinimum`: Before ≥ 3 و After < 3. `LosesAllBidders`: After = 0. `LosesAward`: الفائز الرسمي ضمن المستبعدين.
- نسبة العروض المحذوفة = عروض المستبعدين ÷ كل العروض. مثال: 10 من 25 ⇒ 40%.
- **HHI (تركّز العروض)** = Σ (حصة كل مورد من العروض)². مثال: 4 عروض، مورد له 2 واثنان لكل منهما 1 ⇒ 0.5² + 0.25² + 0.25² = **0.375**. 1.0 = مورد واحد فقط. ارتفاع HHI بعد الاستبعاد = سوق أكثر تركّزاً.
- البدائل: موردون معتمدون من نفس فئات المستبعدين، الأكثر مشاركة أولاً (8 كحد أقصى).

### E.8 العطاءات (`TenderCalculator`)
- `DaysToNotification` = (إرسال الإشعار − الإنشاء) بالأيام (منزلة عشرية واحدة). `DaysSoFar` = (وقت التحليل − الإنشاء).
- مجموعة العروض: نفس `Currency` ونفس `StructureKey` (مثلاً `"11:UNIT:10:10|12:UNIT:10:10"` = البند 11 بالوحدة UNIT والكمية المطلوبة 10 والمعروضة 10).
- `RelativeToMedian = (Total − Median) ÷ Median` إذا كانت المجموعة فيها عرضان أو أكثر. مثال: 1100 مقابل وسيط 1000 ⇒ +10%.
- المشاركة لكل دورة: Invited (الدعوات)، Submitted (العروض)، Compliant، و Low = Submitted < 3.
- الجهات: متوسط العروض = Σ العروض ÷ عدد الدورات (منزلتان). مثال: 21 عرضاً في 5 دورات ⇒ 4.2.
- الترسيات: لكل عملة: المجموع، حصة كل مورد = قيمته ÷ المجموع، HHI = Σ الحصص². مثال: حصتان 60% و 40% ⇒ 0.36 + 0.16 = **0.52**.

---

## F. قاموس الصياغة (Syntax) من الكود النهائي

### F.1 C#
| الصياغة | مثال من الكود | المعنى |
|---|---|---|
| `namespace` | `namespace Orange_Tendering_System.Services.Intelligence;` | "مجلد منطقي" للكلاسات |
| `using` | `using Orange_Tendering_System.Services.Radar;` | استخدام كلاسات من namespace آخر |
| `public class` | `public class IntelligenceService` | كلاس عادي يمكن إنشاء كائن منه |
| `public static class` | `public static class SupplierCalculator` | مجموعة دوال بدون كائن: `SupplierCalculator.Build(...)` |
| `private readonly` | `private readonly RadarService radar;` | حقل خاص يُضبط مرة واحدة في البنّاء |
| البنّاء (Constructor) | `public IntelligenceService(RadarService radar, RadarDataService radarData) { this.radar = radar; ... }` | ASP.NET يمرر الخدمات (Dependency Injection من `Program.cs`) |
| `this.` | `this.radar = radar;` | الحقل (وليس المعامل) |
| خاصية | `public int TenderCount { get; set; }` | قيمة تُقرأ وتُكتب، وتصبح حقلاً في JSON |
| قيمة افتراضية | `= new List<LifecycleStep>();` | لا تكون القائمة `null` |
| `async Task<T>` / `await` | `public async Task<OverviewData> GetOverviewAsync(...)` و `await LoadSnapshotAsync(...)` | دالة تنتظر قاعدة البيانات بدون حجز الخيط |
| `?` بعد النوع | `SupplierDetails? details` / `double? WinRate` | قد تكون `null` (لا قيمة) |
| `??` | `request.SupplierIds ?? Array.Empty<int>()` | إن كانت القيمة null استخدم البديل |
| `foreach` | `foreach (RBid bid in snapshot.Bids)` | المرور على كل عنصر |
| `for` | `for (int i = 0; i < categories.Count; i++)` | حلقة بعدّاد |
| `continue` | `if (...) { continue; }` | تخطَّ هذا العنصر |
| `List<T>` | `List<int> supplierIds = new List<int>();` | قائمة قابلة للنمو |
| `Dictionary<K,V>` | `Dictionary<string, List<int>> pairs` | جدول مفتاح ⇐ قيمة؛ `ContainsKey`, `[key]` |
| `HashSet<T>` | `HashSet<int> closedRounds` | مجموعة بدون تكرار و `Contains` سريع |
| `Queue<T>` | `queue.Enqueue(...)` / `queue.Dequeue()` | طابور (أول داخل أول خارج) للبحث بالعرض |
| LINQ قصير | `rows.OrderByDescending(row => row.Participations).ToList()` | ترتيب؛ `row => row.Participations` دالة صغيرة تقول "رتّب حسب هذه" |
| `Math.Round(x, 3)` | `Math.Round((double)row.Wins / row.AwardedTendersBid, 3)` | تقريب؛ `(double)` تحوّل القسمة لعشرية |
| `decimal` | `decimal Total` | أرقام مالية دقيقة |
| Attributes | `[HttpGet]`, `[HttpPost]`, `[FromBody]`, `[Authorize(...)]`, `[AutoValidateAntiforgeryToken]` | تعليمات لـ ASP.NET |
| `IActionResult` | `return Json(overview);` / `NotFound(...)` / `BadRequest(...)` / `File(...)` | أنواع الردود |
| كائن مجهول | `new { error = "..." }` | JSON سريع `{ "error": "..." }` |
| `try/catch` | `catch (IntelligenceExportException e)` | التقاط خطأ متوقع وتحويله إلى 400 |
| كلاس استثناء | `public class IntelligenceExportException : Exception` | `: Exception` = يرث من Exception؛ `: base(message)` يمرر الرسالة |
| `const` | `const string bidding = "Bidding & Supplier Submissions";` | قيمة ثابتة |
| EF Core | `await db.CommitteeRounds.AsNoTracking().Where(...).Select(...).ToListAsync(ct)` | استعلام SQL للقراءة فقط |
| `var` | `var committees = await db...` | النوع يُستنتج (هنا نوع مجهول من `Select(new { ... })`) |
| `record` | `public sealed record SupplierRequest(int SupplierId);` | يظهر فقط في AI Studio و `RadarSnapshot.cs` (لم نغيرهما) |

### F.2 Razor
| الصياغة | مثال | المعنى |
|---|---|---|
| `@{ ... }` | `@{ Layout = "_SourcingOfficerLayout"; }` | كود C# داخل الصفحة |
| `@L(...)` | `@L("Overview", "نظرة عامة")` | طباعة نتيجة دالة |
| `@inject` | `@inject ... IAntiforgery Antiforgery` | الحصول على خدمة داخل الصفحة |
| `@section` | `@section Scripts { ... }` | محتوى يضعه الـ Layout في مكانه |
| `~/` | `src="~/js/intelligence-page.js"` | جذر `wwwroot` |
| `asp-append-version` | `asp-append-version="true"` | يضيف بصمة للملف لتجنب الكاش القديم |
| `@* ... *@` | تعليق Razor | لا يصل للمتصفح |
| `Url.Action` | `Url.Action(action, "Intelligence", ...)` | يبني الرابط `/Intelligence/Overview` |

### F.3 HTML
| الصياغة | مثال | المعنى |
|---|---|---|
| `id` | `<div id="ov-kpis">` | معرف فريد تستخدمه `getElementById` |
| `data-*` | `data-tab-link="suppliers"` | معلومة تقرأها JS بـ `dataset.tabLink` |
| `hidden` | `<section ... hidden>` | مخفي |
| `role`, `aria-*` | `role="tab" aria-selected="true"` | لقارئات الشاشة (Accessibility) |
| `<details>/<summary>` | `<details id="sim-fold">` | قسم قابل للطي بدون JS |
| `<canvas>` داخل div الرسم | `<div class="rd-chart" id="chart-ten-offers">` | ECharts يضيف canvas داخل هذا الـ div |

### F.4 JavaScript
| الصياغة | مثال | المعنى |
|---|---|---|
| `var` | `var overviewData = null;` | متغير |
| `function` | `function renderOverview(data) { ... }` | دالة عامة (تُستدعى من ملف آخر أو من Console) |
| `async function` / `await` | `overviewData = await getJson(...)` | انتظار الشبكة |
| `try/catch` | `try { ... } catch (error) { showStatus(...) }` | معالجة الخطأ |
| `fetch` | `fetch(url, { credentials: 'same-origin' })` | طلب HTTP مع الكوكي |
| `response.ok` | `if (!response.ok ...)` | الحالة 200-299 |
| `JSON.stringify` | `body: JSON.stringify(body)` | كائن ⇐ نص JSON |
| `document.getElementById` | `document.getElementById('ov-funnel')` | إيجاد عنصر |
| `querySelectorAll` | `document.querySelectorAll('[data-tab-panel]')` | كل العناصر المطابقة |
| `addEventListener` | `button.addEventListener('click', function () { ... })` | ربط حدث |
| `event.target.closest(...)` | `event.target.closest('[data-supplier-open]')` | أقرب عنصر أب مطابق (تفويض الأحداث) |
| `dataset` | `button.dataset.overviewTender` | قراءة `data-overview-tender` |
| `innerHTML` / `textContent` | `el.innerHTML = html` | HTML (بعد `escapeHtml` للنصوص) / نص فقط |
| `classList.toggle` | `classList.toggle('is-error', isError === true)` | إضافة/إزالة صنف CSS |
| `setTimeout` / `clearTimeout` | انتظار 300ms في البحث | تأخير |
| المعامل الثلاثي | `refresh ? 'true' : 'false'` | if/else في سطر (مستوى واحد فقط في الكود) |
| `Number(...)` | `Number(button.dataset.tenderOpen)` | نص ⇐ رقم |
| `CustomEvent` | `new CustomEvent('radar:data', { detail: ... })` | حدث خاص لـ AI Studio |
| `window.OrangeRadar` | كائن عام | الجسر الذي يقرأه AI Studio |
| ECharts | `chart.setOption(options, true)` / `chart.on('click', ...)` | رسم وتفاعل |
| `history.replaceState` | `history.replaceState(null, '', '#' + tab)` | تغيير العنوان دون تحميل |

---

## G. قرارات التبسيط

### G.1 Overview
- نفس البطاقات والقمع وقائمة الانتباه والاختصارات والتصميم.
- **الحساب انتقل من JavaScript إلى C#** (`OverviewCalculator`): الوسيط، عدد الأيام، الدورات المنخفضة... كانت تُحسب في المتصفح من ملف بيانات ضخم.
- حُذفت حركة عدّ الأرقام (Count-up animation).

### G.2 Suppliers
> **تحديث 2026-11-28:** عالم الموردين ثلاثي الأبعاد **أُعيد** كملف عرض معزول يقرأ نفس JSON الحالي (القسم H.1). ما يلي يصف إعادة الهيكلة الأولى؛ الشبكة ثنائية الأبعاد ووضع "Tender" وتتبع المسار والإعادة ما زالت محذوفة.

- **عالم الموردين ثلاثي الأبعاد (Three.js، 881 سطراً) والشبكة ثنائية الأبعاد (976 سطراً)** استُبدلا (في إعادة الهيكلة الأولى) بـ:
  - **دليل الموردين** (جدول قابل للبحث) + **لوحة تفاصيل** بنفس التبويبات الأربعة للمفتش القديم (Overview/Participation/Pricing/Relationships) ونفس تصميمها.
  - **علاقات السوق**: جدول الأزواج وعدد الدورات المشتركة + مجموعات المنافسة (كانت تظهر كخطوط في الشبكة).
- **حُذف**: الدوران والتكبير، وضعيات الترتيب (Universe/Category/Competition/Tender)، تتبع المسار بين موردين (Path)، الإعادة التاريخية (Replay)، تصدير الشبكة PNG، الاختيار المتعدد حتى 5 موردين مع تظليل متقاطع في كل الرسوم، البحث العام في شريط السياق.
  - البدائل: المسار ⇐ لوحة العلاقات والمقارنة؛ الإعادة ⇐ خط المشاركة الشهري؛ الاختيار المتعدد ⇐ المقارنة بين موردين + لوحة التفاصيل؛ البحث العام ⇐ بحث الموردين وبحث العطاءات.
- **الفلاتر تُطبَّق في السيرفر** وكل الأرقام تُعاد حسابها في C#.
- بقيت كما هي: المؤشرات، لمحة السوق، رسما التحليلات والاستنتاجات، المقارنة، السيناريو، "Ask AI".

### G.3 Tenders
- نفس التصميم والأقسام: القائمة، البطاقة، المؤشرات، الخط الزمني، التصدير، العروض، المشاركة، الجهات، الأزواج، الترسيات.
- أُضيف بحث صغير في قائمة العطاءات.
- تفاصيل العطاء تأتي بطلب مستقل `TenderDetails` بدل ملف بيانات واحد ضخم.
- مجموعات المقارنة (الوسيط/الأقل/الأعلى) تُحسب في C#.
- خدمة التصدير بُسِّطت إلى التقارير الثلاثة التي تستخدمها الصفحة.

### G.4 الخلفية (Backend)
- `RadarService.WorkspaceAsync` الذي كان يبني كائناً واحداً ضخماً (الملخص، التعاريف، الموردين، المنافسة، الترسيات، البنود، المواقع، الأنماط الجنائية، الكواشف، الرسم البياني، الخط الزمني، الشبكة، العطاءات، السلوك) **حُذف**، واستُبدل بطلب منفصل لكل تبويب.
- حُذفت الملفات: `RadarGraph.cs`, `RadarForensics.cs`, `RadarBehavior.cs`, `RadarTenders.cs`, `RadarOfferComparison.cs`, `RadarExportService.cs`. الأنماط الجنائية (Forensics) والسلوك (Behavior) وبنود الأسعار (Items) **لم تكن معروضة في الصفحة أصلاً**.
- `RadarSnapshot.cs` **لم يتغير** عمداً: يحمي قاعدة الختم والنطاق، ويخدم AI Studio.

### G.5 AI Studio — منطق Gemini لم يُلمس
- `IntelligenceAiService.cs`، أفعال الـ Controller الثلاثة وطلباتها (`SupplierRequest`, `AskRequest`) و `GeminiError`: **لم تتغير**.
- تحديث 2026-11-28 (مظهر فقط): `intelligence-ai.js` +1 سطر (إطلاق `change` بعد `chooseSupplier`)، `intelligence-audio.js` +5 أسطر (تسليم المحللين للكرة، وعلامة `data-outcome="error"`). لا تغيير في الطلبات أو الـ token أو الميكروفون أو التشغيل (القسم H).
- قسم HTML: أُضيفت قائمة الموردين المرئية والـ canvas؛ بقي `<select data-studio-supplier>` وكل خصائص `data-studio-*` و `data-voice-*`.
- `RadarAnalytics.Suppliers` الذي يبني حقائق المورد لـ Gemini: كوده لم يتغير؛ حُذفت فقط الدوال الأخرى في الملف التي لم يعد يستخدمها أحد.
- الصفحة الجديدة تحافظ على العقد الذي يحتاجه: العنصر `[data-radar]` مع `data-csrf`، `window.OrangeRadar.data.suppliers`، `window.OrangeRadar.setTab`، والحدث `radar:data`.

---

## H. الرسوميات المتقدمة (فهم معماري فقط)

هذه الملفات **لا تقرأ قاعدة البيانات، لا ترسل طلبات، لا تحسب إحصاءات، ولا تقرر الصلاحيات**. يكفي أن تعرف من أين تأتي بياناتها ومن يستدعيها.

### H.1 عالم الموردين ثلاثي الأبعاد — `wwwroot/js/orange-intelligence-universe.js`
- **الأصل:** نفس الملف التاريخي (أُضيف في commit `abec812`، آخر نسخة في `3ccc1df`) — أُعيد بعد تكييفه مع الـ JSON الحالي. لم يُستعد `WorkspaceAsync` ولا أي منطق عمل قديم.
- **من أين تأتي البيانات:** `GET /Intelligence/Suppliers` ← `SupplierCalculator.Build` ← `SuppliersData`. لا يوجد مصدر بيانات ثانٍ.
- **من يبدؤه:** `renderSupplierUniverse(data)` في `intelligence-suppliers.js` يستدعي `SupplierUniverse.init(element, callbacks)` مرة واحدة (أي استدعاء لاحق يُهمَل، فلا يوجد Renderer مكرر). Three.js يُحمَّل بـ `import()` من `wwwroot/lib/three` عند أول رسم.
- **من يحدّثه:** كل رد جديد ⇒ `SupplierUniverse.update(view)` حيث:

| خاصية `view` | مصدرها في `SuppliersData` | استخدامها في الرسم |
|---|---|---|
| `suppliers` | `allSuppliers` (كل الموردين المصرح بهم، مع `category` و `categoryColor` و `participations`) | نقطة لكل مورد؛ اللون = الفئة؛ الحجم = دورات مغلقة بعرض |
| `shownIds` | أرقام `suppliers` (الموردون بعد الفلاتر) | النقاط المضيئة؛ الباقي باهت للسياق |
| `rows` | `suppliers` | بطاقة التمرير (الحالة، الترسيات، الاستجابة) |
| `category` | `filters.category` | تجميع الفئة المختارة |
| `pairs`, `competitionMinimum` | `pairs`, `competitionMinimum` | أقواس وضع Competition |

- **كيف يصل اختيار الفئة إلى الكرة:** القائمة `#sup-category` أو شريحة الدائرة أو زر في دليل الألوان داخل الكرة ⇒ `toggleCategoryFilter` / `setSupplierFilter('category', ...)` ⇒ `loadSuppliers` ⇒ **السيرفر** يفلتر ⇒ `renderSupplierUniverse` ⇒ `update({ category })`. المحرك لا يفلتر بنفسه؛ هو فقط ينقل النقاط.
- **ماذا يرى المستخدم عند اختيار فئة:** موردو الفئة ينزلقون واحداً تلو الآخر إلى قبة دائرية أمام الكاميرا، تظهر منطقة متوهجة بلون الفئة مع عنوانها وعددها وشريحة "× إزالة"، باقي الموردين ينتقلون إلى بقية الكرة باهتين، والكاميرا تقترب. مسح الفلتر يعيد الترتيب الأصلي بحركة ناعمة.
- **الأوضاع:** Universe (كرة كاملة) · Categories (كل فئة منطقة) · Competition (أقواس `pairs` الحقيقية). وضع Tender القديم **لم يُعد** لأن JSON الحالي لا يحتوي قائمة المشاركين في كل دورة.
- **كيف تفتح النقطة التفاصيل:** نقرة (أو Enter بلوحة المفاتيح) ⇒ `callbacks.onSupplierClick(id)` = `onUniverseSupplierClick` ⇒ `openSupplierDetails(id)` (نفس دالة صف الجدول) ⇒ `GET /Intelligence/SupplierDetails`. بعد الرد: `showSelectedSupplierInUniverse()` ⇒ `SupplierUniverse.selectSupplier(id, details.competitors)` ⇒ أقواس إلى المنافسين الحقيقيين.
- **العزل عن منطق العمل:** المحرك يستقبل أرقاماً جاهزة فقط. لا `fetch`، لا معدل فوز، لا نسب، لا قواعد ختم. كل ذلك في C#.
- **الأداء:** حلقة الرسم تتوقف عندما يكون التبويب مخفياً أو الكرة خارج الشاشة أو الصفحة في الخلفية؛ كل الموارد تُحرَّر عند `pagehide` (`dispose`)؛ دقة أقل على الأجهزة الضعيفة؛ دعم `prefers-reduced-motion`؛ رسالة بديلة إذا لم يتوفر WebGL. عجلة الفأرة تكبّر فقط بعد النقر على الكرة (أو مع Ctrl) حتى لا تعطل تمرير الصفحة.

### H.2 كرة الصوت — `wwwroot/js/intelligence-voice-visual.js`
- **الأصل:** الكرة الجسيمية والأشرطة الثلاثة والهالة وأقواس التفكير والطيف المعكوس من `orange-radar-voice.js` (آخر نسخة في `c0514c3`، حُذف في `daf0025`). أُضيف: نواة سائلة، تموجات عند كلام المستخدم، شرارات عند كلام Gemini، لون الخطأ، انتقالات لونية ناعمة.
- **من أين تأتي البيانات:** `intelligence-audio.js` يستدعي `window.OrangeVoiceVisual.connect(micAnalyser, speakerAnalyser)` بعد فتح الميكروفون، و `connect(null, null)` عند إنهاء المكالمة.
- **الحالة:** تُقرأ من `data-state` (idle / connecting / listening / thinking / speaking) التي يضعها `setState` في `intelligence-audio.js`، و `data-outcome="error"` بعد فشل.
- **قاعدة الصدق:** الحركة الصوتية تأتي من المحلل الحقيقي فقط (الميكروفون أثناء الاستماع، صوت Gemini أثناء الكلام). بدون إشارة، الكرة "تتنفس" ببطء فقط ولا تقلّد صوتاً. "Hearing you…" تظهر فقط عندما يتجاوز صوت الميكروفون الحقيقي نفس العتبة `0.02` المستخدمة في `intelligence-audio.js`.
- **غير مدعوم حالياً:** كتم الصوت (Mute) — لا يوجد في منطق المكالمة الحالي، فلم تُضف له حالة.

### H.3 قائمة الموردين في AI Studio — `wwwroot/js/intelligence-studio-picker.js`
- **مصدر الحقيقة لم يتغير:** `<select data-studio-supplier>` (مخفي). `intelligence-ai.js` يملؤه (`fillSuppliers`) ويقرؤه، و `intelligence-audio.js` يقرؤه.
- القائمة المرئية ترسم خيارات الـ `<select>` (بحث، أحرف أولى، لون الفئة، عدد الدورات من `allSuppliers`) وعند النقر تكتب `select.value` وتطلق `change`.
- **Ask AI:** `window.OrangeIntelligenceAI.chooseSupplier(id)` يضع القيمة ثم يطلق `change` (السطر الوحيد المضاف في `intelligence-ai.js`) ⇒ القائمة تُظهر المورد المختار.
- لوحة المفاتيح: الأسهم، Home/End، Enter/Space؛ السهم للأسفل من مربع البحث ينقل إلى القائمة.
