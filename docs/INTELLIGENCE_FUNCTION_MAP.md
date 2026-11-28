# خريطة الدوال في Orange Intelligence (Function Map)

> **Superseded (2026-11-28).** The authoritative study references for the final code are `docs/ORANGE_INTELLIGENCE_STUDY_GUIDE.md` (Overview, Suppliers, Tenders) and `docs/AI_STUDIO_STUDY_GUIDE.md` (AI Studio). This file describes an earlier stage: `RadarService`, `RadarDataService`, `RadarSnapshot`, `RadarOptions`, `RadarAnalytics` and `IntelligenceHelpers` no longer exist (merged into `Services/Intelligence/IntelligenceService.cs` and `IntelligenceModels.cs`).


مرجع سريع: **من يستدعي من؟** لكل سلسلة تنفيذ في الصفحة `/Intelligence`.

- كل المسارات تبدأ من مجلد مشروع الويب `Orange-Tendering-System/`.
- الأسماء والتوقيعات منسوخة من الكود النهائي (2026-11-28). ابحث عنها بـ `Ctrl+F` في Visual Studio.
- عمود **النوع**: **DB** = يقرأ قاعدة البيانات (أو نسختها المؤقتة 60 ثانية) · **ذاكرة** = يعالج بيانات محمّلة فقط · **HTTP** = طلب من المتصفح إلى السيرفر · **UI** = يرسم في الصفحة فقط · **إطار** = دالة جاهزة من ASP.NET Core / EF Core / المتصفح (ليست من مشروعنا).
- الشرح التفصيلي لكل خطوة: `docs/INTELLIGENCE_BEGINNER_GUIDE.md`.

---

## 1. الدوال المشتركة التي تتكرر في كل السلاسل

| الملف | الكلاس / النطاق | الدالة | المدخلات | المخرجات | الغرض | النوع |
|---|---|---|---|---|---|---|
| `wwwroot/js/intelligence-page.js` | عام (global) | `getJson(url, params)` | رابط + كائن معاملات | JSON | كل طلب GET (يبني الرابط بـ `buildUrl`، يقرأ رسالة الخطأ بـ `readErrorMessage`) | HTTP |
| `wwwroot/js/intelligence-page.js` | عام | `postRequest(url, body)` / `postJson(url, body)` | رابط + كائن | `Response` / JSON | كل طلب POST مع الـ header `RequestVerificationToken` (Anti-Forgery) | HTTP |
| `wwwroot/js/intelligence-page.js` | عام | `periodParams()` | — | `{ from, to }` | قراءة فلتر التاريخ | UI |
| `Services/Intelligence/IntelligenceService.cs` | `IntelligenceService` | `LoadSnapshotAsync` | `string? from, string? to, bool refresh, CancellationToken ct` | `Task<RadarSnapshot>` | تحميل البيانات المصرح بها للفترة | DB |
| `Services/Radar/RadarSnapshot.cs` | `RadarPeriod` | `Parse` | `string? from, string? to` | `RadarPeriod` | نص التاريخ ⇒ فترة | ذاكرة |
| `Services/Radar/RadarService.cs` | `RadarService` | `SnapshotAsync` | `RadarPeriod period, CancellationToken ct` | `Task<RadarSnapshot>` | يمرر إلى `RadarDataService.LoadAsync` | DB |
| `Services/Radar/RadarSnapshot.cs` | `RadarDataService` | `ScopeAsync` | `CancellationToken ct` | `Task<RadarScope>` | نطاق المستخدم (السوق كاملاً أو عطاءات جهته ولجانه) | DB |
| `Services/Radar/RadarSnapshot.cs` | `RadarDataService` | `LoadAsync` | `RadarPeriod period, CancellationToken ct` | `Task<RadarSnapshot>` | الذاكرة المؤقتة لكل مستخدم وفترة، وإلا `BuildAsync` | DB |
| `Services/Radar/RadarSnapshot.cs` | `RadarDataService` | `BuildAsync` (private) | `RadarPeriod period, RadarScope sc, CancellationToken ct` | `Task<RadarSnapshot>` | استعلامات EF Core `AsNoTracking` + قاعدة الختم | DB |
| `Services/Radar/RadarSnapshot.cs` | `RadarDataService` | `IsUnsealed` (static) | `TenderRoundStatus status, DateTime? deadline, DateTime now` | `bool` | هل الدورة مغلقة وقابلة للتحليل؟ (عروض الدورات المفتوحة لا تُقرأ) | ذاكرة |
| `Services/Radar/RadarSnapshot.cs` | `RadarDataService` | `Invalidate` | `RadarPeriod period` | — | حذف النسخة المؤقتة (زر التحديث) | ذاكرة |
| `Services/Intelligence/IntelligenceHelpers.cs` | `IntelligenceHelpers` (static) | `BuildContext` | `RadarSnapshot snapshot` | `PageContext` | النطاق، بيانات تجريبية؟، عدد العطاءات والدورات | ذاكرة |
| `Services/Intelligence/IntelligenceHelpers.cs` | `IntelligenceHelpers` | `Median` | `List<decimal>` أو `List<double>` | الوسيط | — | ذاكرة |
| `Services/Intelligence/IntelligenceHelpers.cs` | `IntelligenceHelpers` | `FindAwards` | `RadarSnapshot snapshot` | `List<AwardRecord>` | الترسيات الرسمية فقط | ذاكرة |
| `Services/Intelligence/IntelligenceHelpers.cs` | `IntelligenceHelpers` | `FindSharedRounds` | `RadarSnapshot snapshot` | `Dictionary<string, List<int>>` | لكل زوج موردين: الدورات المغلقة المشتركة | ذاكرة |
| `Services/Intelligence/IntelligenceHelpers.cs` | `IntelligenceHelpers` | `CleanName`, `CategoryOf`, `CycleLabel`, `AddMoney`, `StructureKey`, `IsDemoData`, `FirstOfPair`, `SecondOfPair`, `IsApproved` | — | — | أدوات صغيرة | ذاكرة |
| ASP.NET Core | `Controller` | `Json(obj)` / `NotFound` / `BadRequest` / `File` / `View` | — | `IActionResult` | تكوين الرد | إطار |

---

## 2. فتح الصفحة `GET /Intelligence`

| الملف المستدعي | الدالة المستدعية | الدالة المستدعاة | ملف التعريف | الكلاس | المعاملات | نوع الإرجاع | الغرض | النوع |
|---|---|---|---|---|---|---|---|---|
| المتصفح | — | `Index()` | `Controllers/IntelligenceController.cs` | `IntelligenceController` | — | `IActionResult` | `[Authorize(Roles = SystemRoles.SourcingOfficer)]` ثم `OnActionExecuting` يمنع المورد | HTTP |
| `IntelligenceController.cs` | `Index` | `View()` | ASP.NET Core | `Controller` | — | `ViewResult` | يعرض `Views/Intelligence/Index.cshtml` | إطار |
| `Index.cshtml` | — | `Antiforgery.GetAndStoreTokens(Context)` | ASP.NET Core | `IAntiforgery` | `HttpContext` | رموز | يضع رمز Anti-Forgery في `data-csrf` | إطار |
| المتصفح | `DOMContentLoaded` | `startIntelligencePage()` | `wwwroot/js/intelligence-page.js` | عام | — | — | ربط الأحداث، ثم `showTab(readTabFromHash())` | UI |
| `intelligence-page.js` | `startIntelligencePage` | `setupOverviewEvents`, `setupSuppliersEvents`, `setupTendersEvents` | ملفات التبويبات | عام | — | — | ربط أزرار كل تبويب | UI |
| `intelligence-page.js` | `startIntelligencePage` | `loadSupplierListForAiStudio()` | `intelligence-page.js` | عام | — | — | إذا لم يبدأ على Suppliers: `GET /Intelligence/Suppliers` ثم `shareSuppliersWithAiStudio(data.allSuppliers)` | HTTP |
| `intelligence-page.js` | `shareSuppliersWithAiStudio` | الحدث `radar:data` | المتصفح | `CustomEvent` | `{ suppliers }` | — | يسمعه `intelligence-ai.js` (`fillSuppliers`) و `intelligence-studio-picker.js` | إطار |

## 3. تحميل Overview

| الملف المستدعي | الدالة المستدعية | الدالة المستدعاة | ملف التعريف | الكلاس | المعاملات | نوع الإرجاع | الغرض | النوع |
|---|---|---|---|---|---|---|---|---|
| `intelligence-page.js` | `showTab('overview')` | `openOverviewTab()` | `wwwroot/js/intelligence-overview.js` | عام | — | — | يرسم البيانات المحفوظة أو يطلبها | UI |
| `intelligence-overview.js` | `openOverviewTab` | `loadOverview(refresh)` | `intelligence-overview.js` | عام | `bool` | — | `getJson(page.dataset.urlOverview, periodParams())` | HTTP |
| المتصفح | `loadOverview` | `Overview(from, to, refresh, ct)` | `Controllers/IntelligenceController.cs` | `IntelligenceController` | `string?, string?, bool, CancellationToken` | `Task<IActionResult>` | GET `/Intelligence/Overview` | HTTP |
| `IntelligenceController.cs` | `Overview` | `GetOverviewAsync` | `Services/Intelligence/IntelligenceService.cs` | `IntelligenceService` | `string? from, string? to, bool refresh, CancellationToken ct` | `Task<OverviewData>` | تحميل ثم حساب | DB |
| `IntelligenceService.cs` | `GetOverviewAsync` | `LoadSnapshotAsync` | `IntelligenceService.cs` | `IntelligenceService` | انظر القسم 1 | `Task<RadarSnapshot>` | البيانات المصرح بها | DB |
| `IntelligenceService.cs` | `GetOverviewAsync` | `OverviewCalculator.Build` | `Services/Intelligence/OverviewCalculator.cs` | `OverviewCalculator` (static) | `RadarSnapshot snapshot, int minimumOffers` | `OverviewData` | المؤشرات، القمع، "يستحق انتباهك" | ذاكرة |
| `OverviewCalculator.cs` | `Build` | `IntelligenceHelpers.BuildContext`, `IntelligenceHelpers.Median` | `IntelligenceHelpers.cs` | `IntelligenceHelpers` | — | — | السياق والوسيط | ذاكرة |
| `intelligence-overview.js` | `loadOverview` | `renderOverview(data)` ⇒ `renderOverviewKpis`, `renderOverviewFunnel`, `renderOverviewAttention`, `renderOverviewShortcuts` | `intelligence-overview.js` | عام | `OverviewData` | — | الرسم فقط | UI |

## 4. تحميل Suppliers

| الملف المستدعي | الدالة المستدعية | الدالة المستدعاة | ملف التعريف | الكلاس | المعاملات | نوع الإرجاع | الغرض | النوع |
|---|---|---|---|---|---|---|---|---|
| `intelligence-page.js` | `showTab('suppliers')` | `openSuppliersTab()` | `wwwroot/js/intelligence-suppliers.js` | عام | — | — | يرسم المحفوظ أو يطلب | UI |
| `intelligence-suppliers.js` | `openSuppliersTab` | `loadSuppliers(refresh)` | `intelligence-suppliers.js` | عام | `bool` | — | `getJson(urlSuppliers, { from, to, department, category, activity, search, refresh })` | HTTP |
| المتصفح | `loadSuppliers` | `Suppliers(from, to, department, category, activity, search, refresh, ct)` | `IntelligenceController.cs` | `IntelligenceController` | نصوص الـ query | `Task<IActionResult>` | يبني `SupplierFilters` | HTTP |
| `IntelligenceController.cs` | `Suppliers` | `GetSuppliersAsync` | `IntelligenceService.cs` | `IntelligenceService` | `string? from, string? to, SupplierFilters filters, bool refresh, CancellationToken ct` | `Task<SuppliersData>` | تحميل ثم حساب | DB |
| `IntelligenceService.cs` | `GetSuppliersAsync` | `SupplierCalculator.Build` | `Services/Intelligence/SupplierCalculator.cs` | `SupplierCalculator` (static) | `RadarSnapshot snapshot, SupplierFilters filters, int competitionMinimum, int minimumOffers` | `SuppliersData` | كل أرقام التبويب | ذاكرة |
| `SupplierCalculator.cs` | `Build` | `BuildStats` | `SupplierCalculator.cs` | `SupplierCalculator` | `RadarSnapshot snapshot` | `List<SupplierRow>` | إحصاءات كل مورد (الدعوات، المشاركات، الترسيات، النسب) | ذاكرة |
| `SupplierCalculator.cs` | `Build` | `CleanFilters`, `ApplyFilters` (private) | `SupplierCalculator.cs` | `SupplierCalculator` | الفلاتر | `SupplierFilters` / `List<SupplierRow>` | الفلترة في السيرفر | ذاكرة |
| `SupplierCalculator.cs` | `Build` | `BuildParticipationTrend`, `BuildLandscape`, `BuildPositionsChart`, `BuildRelationships` (private) | `SupplierCalculator.cs` | `SupplierCalculator` | — | — | الرسوم والعلاقات (`Pairs`, `Groups`) | ذاكرة |
| `SupplierCalculator.cs` | `Build` | `ToOption` (private) | `SupplierCalculator.cs` | `SupplierCalculator` | `SupplierRow row` | `SupplierOption` | `AllSuppliers` و `ScenarioSuppliers` (مع `Category` منذ 2026-11-28) | ذاكرة |
| `intelligence-suppliers.js` | `loadSuppliers` | `showContext`, `shareSuppliersWithAiStudio`, `renderSuppliers(data)` | `intelligence-page.js` / `intelligence-suppliers.js` | عام | `SuppliersData` | — | الحالة + AI Studio + الرسم | UI |
| `intelligence-suppliers.js` | `renderSuppliers` | `renderSupplierFilters`, `renderSupplierKpis`, `renderSupplierUniverse`, `renderCategoryDonut`, `renderParticipationTrend`, `renderSupplierTable`, `renderSupplierDetails`, `renderSupplierInsights`, `renderSupplierRelationships`, `renderComparisonControls`, `renderComparison`, `renderScenarioPick` | `intelligence-suppliers.js` | عام | `SuppliersData` | — | الرسم فقط | UI |

## 5. تطبيق فلاتر الموردين

| الملف المستدعي | الدالة المستدعية | الدالة المستدعاة | ملف التعريف | المعاملات | الغرض | النوع |
|---|---|---|---|---|---|---|
| `intelligence-suppliers.js` | مستمع `change` على `#sup-department` / `#sup-category` / `#sup-activity` | `setSupplierFilter(name, value)` | `intelligence-suppliers.js` | اسم الفلتر + القيمة | يحفظ القيمة ثم `loadSuppliers(false)` ⇒ نفس السلسلة 4 | UI ⇒ HTTP |
| `intelligence-suppliers.js` | مستمع `input` على `#sup-search` (300ms) | `setSupplierFilter('search', text)` | `intelligence-suppliers.js` | النص | نفس السلسلة 4 | UI ⇒ HTTP |
| `intelligence-suppliers.js` | نقرة على شريحة الدائرة / دليلها | `toggleCategoryFilter(category)` | `intelligence-suppliers.js` | اسم الفئة | يضع الفئة أو يزيلها ⇒ `setSupplierFilter` | UI ⇒ HTTP |
| `orange-intelligence-universe.js` | نقرة على زر فئة داخل الكرة أو "×" الشريحة | `callbacks.onCategoryClick(name)` = `toggleCategoryFilter` | `intelligence-suppliers.js` | اسم الفئة | نفس الفلتر؛ الكرة لا تفلتر بنفسها | UI ⇒ HTTP |
| `SupplierCalculator.cs` | `Build` | `ApplyFilters` | `SupplierCalculator.cs` | `RadarSnapshot, List<SupplierRow>, SupplierFilters` | الفئة، النشاط، الجهة (من عروض عطاءات الجهة)، البحث | ذاكرة |

## 6. فتح تفاصيل مورد

| الملف المستدعي | الدالة المستدعية | الدالة المستدعاة | ملف التعريف | الكلاس | المعاملات | نوع الإرجاع | الغرض | النوع |
|---|---|---|---|---|---|---|---|---|
| `intelligence-suppliers.js` | نقرة صف `#sup-table` / زر `data-supplier-open` | `openSupplierDetails(supplierId)` | `intelligence-suppliers.js` | عام | `number` | — | — | UI |
| `orange-intelligence-universe.js` | نقرة/Enter على نقطة | `callbacks.onSupplierClick(id)` = `onUniverseSupplierClick(id)` | `intelligence-suppliers.js` | عام | `number` أو `null` | — | `openSupplierDetails(id)` أو `closeSupplierDetails()` | UI |
| `intelligence-suppliers.js` | `openSupplierDetails` | `showSelectedSupplierInUniverse()` | `intelligence-suppliers.js` | عام | — | — | `SupplierUniverse.selectSupplier(id, competitors)` | UI |
| `intelligence-suppliers.js` | `openSupplierDetails` | `getJson(urlSupplierDetails, { supplierId, from, to })` | `intelligence-page.js` | عام | — | JSON | GET `/Intelligence/SupplierDetails` | HTTP |
| المتصفح | — | `SupplierDetails(supplierId, from, to, ct)` | `IntelligenceController.cs` | `IntelligenceController` | `int, string?, string?, CancellationToken` | `Task<IActionResult>` | `404` إذا كان المورد خارج النطاق | HTTP |
| `IntelligenceController.cs` | `SupplierDetails` | `GetSupplierDetailsAsync` | `IntelligenceService.cs` | `IntelligenceService` | `int supplierId, string? from, string? to, CancellationToken ct` | `Task<SupplierDetails?>` | — | DB |
| `IntelligenceService.cs` | `GetSupplierDetailsAsync` | `SupplierCalculator.BuildDetails` | `SupplierCalculator.cs` | `SupplierCalculator` | `RadarSnapshot snapshot, int supplierId` | `SupplierDetails?` | الصف، الدورات، الأسعار، المنافسون | ذاكرة |
| `intelligence-suppliers.js` | `openSupplierDetails` | `renderSupplierDetails()` ⇒ `detailsOverviewHtml` / `detailsParticipationHtml` / `detailsPricingHtml` / `detailsRelationshipsHtml` | `intelligence-suppliers.js` | عام | — | HTML | الرسم | UI |
| `intelligence-suppliers.js` | زر Ask AI | `window.OrangeIntelligenceAI.chooseSupplier(id)` | `wwwroot/js/intelligence-ai.js` | `window.OrangeIntelligenceAI` | `number` | — | يختار المورد في AI Studio ويفتح تبويبه | UI |

## 7. مقارنة موردين

| الملف المستدعي | الدالة المستدعية | الدالة المستدعاة | ملف التعريف | الكلاس | المعاملات | نوع الإرجاع | الغرض | النوع |
|---|---|---|---|---|---|---|---|---|
| `intelligence-suppliers.js` | `change` على `#cmp-supplier-a/b`، أو `compareSuppliers(a, b)` | `loadComparison()` | `intelligence-suppliers.js` | عام | — | — | `getJson(urlCompareSuppliers, { supplierA, supplierB })` | HTTP |
| المتصفح | — | `CompareSuppliers(supplierA, supplierB, ct)` | `IntelligenceController.cs` | `IntelligenceController` | `int, int, CancellationToken` | `Task<IActionResult>` | `400` لموردين متطابقين، `404` خارج النطاق | HTTP |
| `IntelligenceController.cs` | `CompareSuppliers` | `CompareSuppliersAsync` | `IntelligenceService.cs` | `IntelligenceService` | `int supplierA, int supplierB, CancellationToken ct` | `Task<List<OfferComparison>?>` | كل التاريخ (`RadarPeriod.All`) | DB |
| `IntelligenceService.cs` | `CompareSuppliersAsync` | `SupplierCalculator.CompareOffers` | `SupplierCalculator.cs` | `SupplierCalculator` | `RadarSnapshot snapshot, int supplierA, int supplierB` | `List<OfferComparison>` | أحدث دورة مغلقة مشتركة لكل عطاء؛ `CompareTwoOffers`, `DescribeOffer`, `FindPriceIssue` (private) | ذاكرة |
| `intelligence-suppliers.js` | `loadComparison` | `renderComparisonControls()`, `renderComparison()` | `intelligence-suppliers.js` | عام | — | — | الرسم | UI |

## 8. تشغيل السيناريو (What-if)

| الملف المستدعي | الدالة المستدعية | الدالة المستدعاة | ملف التعريف | الكلاس | المعاملات | نوع الإرجاع | الغرض | النوع |
|---|---|---|---|---|---|---|---|---|
| `intelligence-suppliers.js` | زر `#sim-run` | `runSimulation()` | `intelligence-suppliers.js` | عام | — | — | `postJson(urlSimulate, { from, to, supplierIds })` | HTTP |
| المتصفح | — | `Simulate([FromBody] SimulateRequest request, ct)` | `IntelligenceController.cs` | `IntelligenceController` | `SimulateRequest` | `Task<IActionResult>` | `[AutoValidateAntiforgeryToken]` يتحقق من الرمز | HTTP |
| `IntelligenceController.cs` | `Simulate` | `SimulateAsync` | `IntelligenceService.cs` | `IntelligenceService` | `SimulateRequest request, CancellationToken ct` | `Task<SimulationResult?>` | حتى 10 موردين، كلهم داخل النطاق | DB |
| `IntelligenceService.cs` | `SimulateAsync` | `SupplierCalculator.Simulate` | `SupplierCalculator.cs` | `SupplierCalculator` | `RadarSnapshot snapshot, List<int> excludedIds, int minimumOffers` | `SimulationResult` | إعادة التاريخ دون الموردين؛ `ConcentrationIndex` (HHI) | ذاكرة |
| `intelligence-suppliers.js` | `runSimulation` | `simulationHtml(simulation)` | `intelligence-suppliers.js` | عام | `SimulationResult` | HTML | الرسم | UI |

## 9. تحميل Tenders

| الملف المستدعي | الدالة المستدعية | الدالة المستدعاة | ملف التعريف | الكلاس | المعاملات | نوع الإرجاع | الغرض | النوع |
|---|---|---|---|---|---|---|---|---|
| `intelligence-page.js` | `showTab('tenders')` | `openTendersTab()` | `wwwroot/js/intelligence-tenders.js` | عام | — | — | — | UI |
| `intelligence-tenders.js` | `openTendersTab` | `loadTenders(refresh)` | `intelligence-tenders.js` | عام | `bool` | — | `getJson(urlTenders, params)` | HTTP |
| المتصفح | — | `Tenders(from, to, refresh, ct)` | `IntelligenceController.cs` | `IntelligenceController` | — | `Task<IActionResult>` | GET `/Intelligence/Tenders` | HTTP |
| `IntelligenceController.cs` | `Tenders` | `GetTendersAsync` | `IntelligenceService.cs` | `IntelligenceService` | `string? from, string? to, bool refresh, CancellationToken ct` | `Task<TendersData>` | — | DB |
| `IntelligenceService.cs` | `GetTendersAsync` | `TenderCalculator.Build` | `Services/Intelligence/TenderCalculator.cs` | `TenderCalculator` (static) | `RadarSnapshot snapshot, int minimumOffers` | `TendersData` | القائمة، المنافسة (`BuildCompetition`)، الجهات، الأزواج، الترسيات | ذاكرة |
| `intelligence-tenders.js` | `loadTenders` | `applyPendingTender()`, `renderTendersTab()` | `intelligence-tenders.js` | عام | — | — | اختيار العطاء والرسم | UI |

## 10. فتح تفاصيل عطاء

| الملف المستدعي | الدالة المستدعية | الدالة المستدعاة | ملف التعريف | الكلاس | المعاملات | نوع الإرجاع | الغرض | النوع |
|---|---|---|---|---|---|---|---|---|
| `intelligence-tenders.js` | نقرة في القائمة / `#ten-pick` / `openTenderFromAnotherTab` | `selectTender(tenderId)` ⇒ `loadTenderDetails(tenderId)` | `intelligence-tenders.js` | عام | `number` | — | `getJson(urlTenderDetails, { tenderId, from, to })` | HTTP |
| المتصفح | — | `TenderDetails(tenderId, from, to, ct)` | `IntelligenceController.cs` | `IntelligenceController` | — | `Task<IActionResult>` | `404` خارج النطاق | HTTP |
| `IntelligenceController.cs` | `TenderDetails` | `GetTenderDetailsAsync` | `IntelligenceService.cs` | `IntelligenceService` | `int tenderId, string? from, string? to, CancellationToken ct` | `Task<TenderDetails?>` | — | DB |
| `IntelligenceService.cs` | `GetTenderDetailsAsync` | `TenderCalculator.BuildTender` | `TenderCalculator.cs` | `TenderCalculator` | `RadarSnapshot snapshot, int tenderId` | `TenderDetails?` | دورة الحياة (`AddStage`)، الدورات (`BuildRound`)، الترسية (`LatestAwardPerTender`) | ذاكرة |
| `intelligence-tenders.js` | `loadTenderDetails` | `renderTenderDetails()` ⇒ `renderTenderKpis`, `renderTenderTimeline`, `renderTenderOffers` | `intelligence-tenders.js` | عام | — | — | الرسم | UI |

## 11. مقارنة العروض داخل الدورة

لا يوجد طلب جديد: الأرقام موجودة في `TenderDetails.Rounds[].Groups` (حسبها `TenderCalculator.BuildRound` في C#).

| الملف المستدعي | الدالة المستدعية | الدالة المستدعاة | المعاملات | الغرض | النوع |
|---|---|---|---|---|---|
| `intelligence-tenders.js` | `change` على `#off-round` / `#off-group` | `renderTenderOffers()` | — | تختار `chosenOfferRound()` و `chosenOfferGroup(round)` | UI |
| `intelligence-tenders.js` | `renderTenderOffers` | `renderOffersChart(chart, group)`, `renderOfferInsight(round, group)`, `renderOfferTable(round, group)`, `renderOfferDetail(round)` | الدورة والمجموعة | الرسم (الوسيط/الأقل/الأعلى محسوبة في C#) | UI |

## 12. تصدير Excel و 13. تصدير PDF

| الملف المستدعي | الدالة المستدعية | الدالة المستدعاة | ملف التعريف | الكلاس | المعاملات | نوع الإرجاع | الغرض | النوع |
|---|---|---|---|---|---|---|---|---|
| `intelligence-tenders.js` | أزرار `#ten-export-lifecycle` (Excel)، `#ten-export-pdf` (PDF)، `#off-export` (Excel العروض) | `exportTender(kind)` | `intelligence-tenders.js` | عام | `'lifecycle'` / `'pdf'` / `'offers'` | — | يبني `{ format, reportType, tenderId, roundId, from, to }` | UI |
| `intelligence-tenders.js` | `exportTender` | `downloadFile(request)` | `intelligence-page.js` | عام | `ExportRequest` | — | `postRequest(urlExport, request)` ثم حفظ الـ blob | HTTP |
| المتصفح | — | `Export([FromBody] ExportRequest request, ct)` | `IntelligenceController.cs` | `IntelligenceController` | `ExportRequest` | `Task<IActionResult>` | `File(...)` أو `400` | HTTP |
| `IntelligenceController.cs` | `Export` | `BuildAsync` | `Services/Intelligence/IntelligenceExportService.cs` | `IntelligenceExportService` | `ExportRequest request, CancellationToken ct` | `Task<ExportFile>` | يتحقق من الصيغة والنوع والنطاق | DB |
| `IntelligenceExportService.cs` | `BuildAsync` | `intelligence.LoadSnapshotAsync(from, to, true, ct)` | `IntelligenceService.cs` | `IntelligenceService` | — | `RadarSnapshot` | بيانات حديثة دائماً (`refresh = true`) | DB |
| `IntelligenceExportService.cs` | `BuildAsync` | `TenderCalculator.BuildTender` | `TenderCalculator.cs` | `TenderCalculator` | `RadarSnapshot, int` | `TenderDetails?` | نفس أرقام الشاشة | ذاكرة |
| `IntelligenceExportService.cs` | `BuildAsync` (`tender_lifecycle`) | `AddLifecycleTableAsync` | `IntelligenceExportService.cs` | `IntelligenceExportService` (private) | `ReportModel, TenderDetails, RadarSnapshot, CancellationToken` | `Task` | استعلامات `CommitteeRounds`, `TenderReports`, `FinalDecisionNotices` | DB |
| `IntelligenceExportService.cs` | `BuildAsync` (`tender_offers`) | `AddOffersTable` | `IntelligenceExportService.cs` | `IntelligenceExportService` (private) | `ReportModel, TenderDetails, int?` | — | صف لكل عرض في الدورات المغلقة | ذاكرة |
| `IntelligenceExportService.cs` | `BuildAsync` | `writer.ToXlsx(model)` / `writer.ToPdf(model)` / `writer.ToDocx(model)` | الكاتب المشترك في النظام | `ReportExport` | `ReportModel` | `byte[]` | كتابة الملف (Excel للـ `xlsx`، PDF للـ `pdf`) | ذاكرة |

---

## 14. نقاط ربط عالم الموردين ثلاثي الأبعاد (عرض فقط)

| الملف المستدعي | الدالة المستدعية | الدالة المستدعاة | ملف التعريف | المعاملات | الغرض | النوع |
|---|---|---|---|---|---|---|
| `intelligence-suppliers.js` | `renderSuppliers` | `renderSupplierUniverse(data)` | `intelligence-suppliers.js` | `SuppliersData` | الجسر الوحيد بين البيانات والمحرك | UI |
| `intelligence-suppliers.js` | `renderSupplierUniverse` (أول مرة فقط) | `SupplierUniverse.init(element, callbacks)` | `wwwroot/js/orange-intelligence-universe.js` | `#sup-universe`, `{ onSupplierClick, onCategoryClick, colorOf }` | ينشئ مشهد Three.js مرة واحدة (Three.js بـ `import()` من `wwwroot/lib/three`) | UI |
| `intelligence-suppliers.js` | `renderSupplierUniverse` | `SupplierUniverse.update(view)` | `orange-intelligence-universe.js` | `{ suppliers: allSuppliers, shownIds, rows: suppliers, category: filters.category, pairs, competitionMinimum }` | يحرك النقاط (تجميع الفئة، الإضاءة، الأقواس) | UI |
| `intelligence-suppliers.js` | `openSupplierDetails` / `closeSupplierDetails` | `showSelectedSupplierInUniverse()` ⇒ `SupplierUniverse.selectSupplier(id, competitors)` | `orange-intelligence-universe.js` | رقم المورد + `supplierDetails.competitors` | التدوير نحو المورد وأقواس المنافسين | UI |
| `orange-intelligence-universe.js` | نقرة/Enter على نقطة | `callbacks.onSupplierClick` = `onUniverseSupplierClick` | `intelligence-suppliers.js` | `id` أو `null` | يفتح/يغلق التفاصيل (السلسلة 6) | UI ⇒ HTTP |
| `orange-intelligence-universe.js` | زر فئة داخل الكرة | `callbacks.onCategoryClick` = `toggleCategoryFilter` | `intelligence-suppliers.js` | اسم الفئة | الفلتر في السيرفر (السلسلة 5) | UI ⇒ HTTP |
| `orange-intelligence-universe.js` | لون كل نقطة | `callbacks.colorOf` = `categoryColor(index)` | `intelligence-page.js` | رقم لون الفئة من السيرفر | نفس ألوان الرسوم (`--c1..--c8`) | UI |

## 15. نقاط ربط كرة الصوت وقائمة AI Studio (عرض فقط)

| الملف المستدعي | الدالة المستدعية | الدالة المستدعاة | ملف التعريف | المعاملات | الغرض | النوع |
|---|---|---|---|---|---|---|
| `intelligence-audio.js` | `startCall` (بعد `startMicrophone`) | `window.OrangeVoiceVisual.connect(micAnalyser, speakerAnalyser)` | `wwwroot/js/intelligence-voice-visual.js` | محللا Web Audio | الكرة تقرأ مستوى الصوت الحقيقي | UI |
| `intelligence-audio.js` | `endCall` | `window.OrangeVoiceVisual.connect(null, null)` | `intelligence-voice-visual.js` | — | إيقاف الحركة الصوتية | UI |
| `intelligence-audio.js` | `setState(state, text)` | الخاصية `data-state` على اللوحة | `Views/Intelligence/Index.cshtml` | الحالة | الكرة تقرأها كل إطار (وتستيقظ بـ `MutationObserver`) | UI |
| `intelligence-audio.js` | `endCall(message, failed)` | الخاصية `data-outcome="error"` | `Index.cshtml` | `failed = true` عند الخطأ | لون الخطأ في الكرة | UI |
| `intelligence-ai.js` | `fillSuppliers` (عند `radar:data`) | خيارات `<select data-studio-supplier>` | `Index.cshtml` | `allSuppliers` | مصدر الاختيار الحقيقي | UI |
| `intelligence-studio-picker.js` | `MutationObserver` + `radar:data` | `render()` | `wwwroot/js/intelligence-studio-picker.js` | الخيارات + `category`/`participations` | رسم القائمة المرئية | UI |
| `intelligence-studio-picker.js` | نقرة / Enter على مورد | `choose(id)` ⇒ `select.value = id` + حدث `change` | `intelligence-studio-picker.js` | رقم المورد | يقرؤه `intelligence-ai.js` و `intelligence-audio.js` كما كان | UI |
| `intelligence-ai.js` | `OrangeIntelligenceAI.chooseSupplier(id)` | `supplierSelect.dispatchEvent(new Event('change'))` | `intelligence-ai.js` | — | القائمة المرئية تعرض مورد Ask AI | UI |
