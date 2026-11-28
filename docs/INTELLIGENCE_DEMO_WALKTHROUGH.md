# شرح عملي بالـ Debugger: Orange Intelligence (Overview · Suppliers · Tenders)

> **Superseded (2026-11-28).** The authoritative study references for the final code are `docs/ORANGE_INTELLIGENCE_STUDY_GUIDE.md` (Overview, Suppliers, Tenders) and `docs/AI_STUDIO_STUDY_GUIDE.md` (AI Studio). This file describes an earlier stage: `RadarService`, `RadarDataService`, `RadarSnapshot`, `RadarOptions`, `RadarAnalytics` and `IntelligenceHelpers` no longer exist (merged into `Services/Intelligence/IntelligenceService.cs` and `IntelligenceModels.cs`).


دليل تطبيقي لتتبع الكود في **Visual Studio** والمتصفح (F12) أمام اللجنة. لا يتطلب دراسة AI Studio (خارج النطاق ولم يُعدَّل).

المسارات تبدأ من `Orange-Tendering-System/`. الشرح الكامل للمفاهيم في `docs/INTELLIGENCE_BEGINNER_GUIDE.md`.

---

## 0. التحضير

1. افتح `Orange-Tendering-System.slnx` في Visual Studio.
2. اختر ملف التشغيل (Launch profile) **`https (Orange Radar demo database)`** — قاعدة بيانات تجريبية فيها موردون وعطاءات وعروض جاهزة للتحليل. (أو `https` العادي إن كانت قاعدة الفريق فيها بيانات.)
3. اضغط **F5** (تشغيل مع الـ Debugger).
4. سجّل الدخول كـ **Other User** بحساب مسؤول المشتريات (Sourcing Officer) الخاص بقاعدة الفريق، ثم افتح `https://localhost:7182/Intelligence`.
5. في المتصفح افتح **F12** وجهّز تبويبين:
   - **Network** مع الفلتر `Intelligence/` وخيار **Fetch/XHR**.
   - **Sources** لوضع Breakpoints في JavaScript (Ctrl+P ثم اكتب `intelligence-overview.js`).
6. نصيحة: دوال JavaScript عامة (global)، فيمكنك كتابتها في **Console** مباشرة، مثلاً: `overviewData`, `suppliersData.suppliers[0]`, `showTab('tenders')`.

### مفاتيح الـ Debugger
| المفتاح | العمل |
|---|---|
| F9 | وضع/إزالة Breakpoint |
| F5 | متابعة حتى الـ Breakpoint التالي |
| F10 | Step Over: السطر التالي بدون الدخول إلى الدالة |
| F11 | Step Into: الدخول داخل الدالة المستدعاة |
| Shift+F11 | Step Out: إكمال الدالة والعودة لمن استدعاها |
| Locals / Watch | رؤية قيم المتغيرات |

---

## 1. تبويب Overview

### أول ملف وأول دالة
- **JavaScript:** `wwwroot/js/intelligence-page.js` ← `startIntelligencePage()` ثم `intelligence-overview.js` ← `loadOverview()`.
- **C#:** `Controllers/IntelligenceController.cs` ← `Overview(...)`.

### الـ Breakpoints
| المكان | السطر | لماذا |
|---|---|---|
| `intelligence-page.js` | `showTab(readTabFromHash());` داخل `startIntelligencePage` | بداية الصفحة |
| `intelligence-overview.js` | `overviewData = await getJson(page.dataset.urlOverview, params);` | قبل الطلب |
| `intelligence-overview.js` | `renderOverview(overviewData);` | بعد وصول الرد |
| `IntelligenceController.cs` | `OverviewData overview = await intelligence.GetOverviewAsync(from, to, refresh, ct);` | وصول الطلب للسيرفر |
| `IntelligenceService.cs` | `RadarSnapshot snapshot = await radar.SnapshotAsync(period, ct);` داخل `LoadSnapshotAsync` | تحميل البيانات |
| `Services/Radar/RadarSnapshot.cs` | `var snapshot = await BuildAsync(period, sc, ct);` داخل `LoadAsync` | استعلامات EF Core (فقط عند عدم وجود نسخة مخزنة) |
| `OverviewCalculator.cs` | `data.MedianLifecycleDays = IntelligenceHelpers.Median(lifecycleDays);` | حساب الوسيط |

### الخطوات
1. **الإجراء:** افتح `/Intelligence` (أو اضغط تبويب Overview، أو غيّر التاريخ).
2. يتوقف المتصفح عند `startIntelligencePage`. اضغط F11 على `showTab` لترى إخفاء اللوحات، ثم F11 على `openOverviewTab` ثم `loadOverview`.
3. في **Network** يظهر: `GET /Intelligence/Overview?from=&to=&refresh=false` — الحالة 200، النوع `fetch`.
4. يتوقف Visual Studio في `Overview(...)`. في **Locals**: `from = null`, `to = null`, `refresh = false`.
5. **F11** يدخلك `GetOverviewAsync` ثم **F11** على `LoadSnapshotAsync`: افحص `period` (`From`, `To`, `Label = "All recorded history"`).
6. **F11** على `radar.SnapshotAsync` ⇒ `RadarService.SnapshotAsync` ⇒ `RadarDataService.LoadAsync`:
   - أول مرة: `cache.TryGetValue` = false ⇒ `BuildAsync` يقرأ الجداول: `Suppliers`, `Tenders` (+ `PurchaseRequests`, `Departments`), `TenderRounds`, `TenderInvitations`, `Bids`, `BidItems`, `Documents`, `FinalDecisionNotices`.
   - مرة ثانية خلال 60 ثانية: يرجع من الذاكرة المؤقتة مباشرة.
   - افحص في `snapshot`: `Suppliers.Count`, `Tenders.Count`, `Bids.Count`, `SealedBids`, `Scope.Label`.
7. **Shift+F11** للعودة، ثم **F11** على `OverviewCalculator.Build`. ضع Watch على: `data.TenderCount`, `data.CompletedTenders`, `lifecycleDays`, `data.Lifecycle`, `data.Attention`.
8. **F10** حتى `return Json(overview);`. في Network ⇒ تبويب **Response/Preview** ترى JSON بأسماء camelCase.
9. يعود المتصفح للتوقف عند `renderOverview(overviewData)`. في Console اكتب `overviewData.lifecycle` و `overviewData.attention`.
10. **F11** على `renderOverviewFunnel` لترى بناء `html` و `document.getElementById('ov-funnel').innerHTML = html;`.

### ماذا تقول للجنة
> "عند فتح التبويب، الدالة `loadOverview` ترسل طلب GET إلى `IntelligenceController.Overview`. الـ Controller يستدعي `IntelligenceService` الذي يقرأ البيانات المصرح بها عبر EF Core مرة واحدة ويحفظها 60 ثانية، ثم `OverviewCalculator` يحسب المؤشرات والقمع وقائمة الانتباه. الرد JSON، و JavaScript يرسمه فقط ولا يحسب شيئاً."

---

## 2. تبويب Suppliers

### أول ملف وأول دالة
- **JavaScript:** `wwwroot/js/intelligence-suppliers.js` ← `openSuppliersTab()` ← `loadSuppliers()`.
- **C#:** `IntelligenceController.Suppliers(...)` ← `IntelligenceService.GetSuppliersAsync` ← `SupplierCalculator.Build`.

### الـ Breakpoints
| المكان | السطر |
|---|---|
| `intelligence-suppliers.js` | `var data = await getJson(page.dataset.urlSuppliers, params);` في `loadSuppliers` |
| `intelligence-suppliers.js` | `renderSuppliers(data);` |
| `intelligence-suppliers.js` | أول سطر في `setSupplierFilter` |
| `intelligence-suppliers.js` | `supplierDetails = await getJson(page.dataset.urlSupplierDetails, params);` في `openSupplierDetails` |
| `IntelligenceController.cs` | `SuppliersData suppliers = await intelligence.GetSuppliersAsync(...)` |
| `SupplierCalculator.cs` | `List<SupplierRow> allRows = BuildStats(snapshot);` في `Build` |
| `SupplierCalculator.cs` | `row.ParticipationRate = Math.Round(...)` في `BuildStats` |
| `SupplierCalculator.cs` | `if (filters.Category != "" && row.Category != filters.Category)` في `ApplyFilters` |
| `SupplierCalculator.cs` | أول سطر في `BuildDetails` |

### السيناريو أ: فتح التبويب
1. **الإجراء:** اضغط **Suppliers**.
2. Network: `GET /Intelligence/Suppliers?from=&to=&department=&category=&activity=all&search=&refresh=false`.
3. في VS داخل `Suppliers(...)` افحص `department`, `category`, `activity`, `search` (Model Binding من الـ query string).
4. **F11** حتى `SupplierCalculator.Build`، ثم **F11** على `BuildStats`. داخل الحلقة `foreach (RSupplier supplier in snapshot.Suppliers)` راقب: `invitationIds.Count`, `roundsWithOffer.Count`, `offersAnsweringInvitations`, `row.ParticipationRate`, `row.WinRate`.
5. **Shift+F11** ثم تابع: `ApplyFilters` ⇒ `rows`؛ المؤشرات (`data.ResponseRate`)؛ `BuildLandscape`؛ `BuildPositionsChart`؛ `BuildRelationships`.
6. في المتصفح عند `renderSuppliers(data)` اكتب في Console: `data.suppliers.length`, `data.responseRate`, `data.pairs[0]`, `data.allSuppliers.length`.
7. لاحظ `shareSuppliersWithAiStudio(data.allSuppliers)`: نفس القائمة تملأ AI Studio.

### السيناريو ب: الفلترة والبحث
1. **الإجراء:** اختر "No offer yet" من قائمة النشاط.
2. يتوقف عند `setSupplierFilter('activity', 'silent')` ⇒ F11 ⇒ `loadSuppliers`.
3. Network: `...activity=silent...`.
4. في C# يتوقف عند `ApplyFilters`: سترى `continue` يتخطى من لديه `Participations > 0`.
5. اكتب في مربع البحث: يُرسل الطلب بعد 300ms من آخر حرف (`setTimeout` في `setupSuppliersEvents`) ⇒ `search=...`.

### السيناريو ج: تفاصيل مورد
1. **الإجراء:** اضغط صفاً في دليل الموردين.
2. Network: `GET /Intelligence/SupplierDetails?supplierId=..&from=&to=`.
3. في `SupplierCalculator.BuildDetails` افحص `details.Rounds` (الأحدث أولاً)، `details.Prices`، `details.Competitors`.
4. جرّب مورداً غير موجود من Console: `await fetch('/Intelligence/SupplierDetails?supplierId=999999').then(r => r.status)` ⇒ **404**.
5. بدّل تبويبات اللوحة (Participation/Pricing/Relationships): **لا طلب جديد** في Network — فقط `renderSupplierDetails()`.

### السيناريو د: المقارنة
1. **الإجراء:** في "Market relationships" اضغط "Compare offers" لزوج، أو اختر موردين في "Supplier Offer Comparison".
2. Network: `GET /Intelligence/CompareSuppliers?supplierA=..&supplierB=..`.
3. Breakpoint في `SupplierCalculator.CompareTwoOffers`: افحص `row.PriceIssue`, `row.Difference`, `row.RelativeDifference`, `row.SecondsApart`.
4. اختر العطاء ⇒ `renderComparison()` يرسم البطاقتين (بدون طلب جديد).

### السيناريو هـ: ماذا لو (What-if)
1. **الإجراء:** افتح "What-if"، اختر مورداً، اضغط "Run scenario".
2. Network: `POST /Intelligence/Simulate` — في **Headers** ابحث عن `RequestVerificationToken`، وفي **Payload**: `{"from":"","to":"","supplierIds":[3]}`.
3. Breakpoint في `SupplierCalculator.Simulate`: راقب `item.Before`, `item.After`, `result.HhiBefore`, `result.HhiAfter`.
4. تجربة أمان من Console (بدون الرمز):
   `await fetch('/Intelligence/Simulate', {method:'POST', headers:{'Content-Type':'application/json'}, body:'{"supplierIds":[1]}'}).then(r => r.status)` ⇒ مرفوض (400، وصفحة الحالة في النظام تُظهره 405). لا يصل الطلب إلى `Simulate` أصلاً.

### ماذا تقول للجنة
> "كل فلتر يرسل طلباً جديداً إلى `IntelligenceController.Suppliers`؛ الفلترة والحساب في `SupplierCalculator` بلغة C#. لوحة التفاصيل لها طلبها الخاص `SupplierDetails`، والمقارنة `CompareSuppliers`، والسيناريو طلب POST محمي برمز Anti-Forgery. حذفنا الرسم ثلاثي الأبعاد واستبدلناه بجدول ولوحة تفاصيل وجدول علاقات، فبقيت المعلومات نفسها بكود أبسط بكثير."

---

## 2.1 عالم الموردين ثلاثي الأبعاد في العرض (تحديث 2026-11-28)

### ما تقوله أولاً
"الكرة **لا تحسب شيئاً**. كل نقطة مورد من `allSuppliers` في رد `/Intelligence/Suppliers`، لونها فئته المسجلة وحجمها عدد دورات العطاء المغلقة التي قدّم فيها عرضاً — رقم حسبه `SupplierCalculator.BuildStats` في C#."

### السيناريو و: اختيار فئة من الكرة (التجميع)
1. Breakpoint في `IntelligenceController.Suppliers` و `SupplierCalculator.ApplyFilters` (عند `if (filters.Category != "" ...)`).
2. في الكرة، دليل الألوان أسفل اليسار: انقر **IT & Electronics** (أو أي فئة).
3. يتوقف الـ Debugger في `Suppliers` ⇒ أظهر `category` في الـ query ⇒ F10 حتى `GetSuppliersAsync` ⇒ F11 إلى `SupplierCalculator.Build` ⇒ `ApplyFilters`.
4. F5: في المتصفح ينزلق موردو الفئة إلى منطقة دائرية متوهجة أمام الكاميرا، وتظهر شريحة "IT & Electronics · 3 suppliers in this cluster ×"، ويُفلتر الجدول والمؤشرات.
5. انقر "×" ⇒ نفس الطلب بفئة فارغة ⇒ تعود الكرة لترتيبها الأصلي.
- **الرسالة للجنة:** "الفلترة حدثت في السيرفر. الكرة استقبلت `filters.category` ونقلت النقاط فقط."

### السيناريو ز: نقرة على نقطة ⇒ التفاصيل
1. Breakpoint في `IntelligenceController.SupplierDetails` و `SupplierCalculator.BuildDetails`.
2. انقر نقطة مورد (أو ركّز الكرة بالنقر ثم الأسهم ثم Enter).
3. السلسلة: `onUniverseSupplierClick(id)` ⇒ `openSupplierDetails(id)` ⇒ `GET /Intelligence/SupplierDetails` ⇒ `GetSupplierDetailsAsync` ⇒ `BuildDetails`.
4. F5: تدور الكرة نحو المورد، تُرسم أقواس متحركة إلى منافسيه (`details.competitors`)، وتفتح لوحة التفاصيل بجانبها.

### أوضاع العرض
- **Universe:** كل الموردين على الكرة · **Categories:** كل فئة منطقة · **Competition:** أقواس `pairs` (موردون التقوا في دورات مغلقة؛ السماكة = عدد الدورات).
- أدوات: إيقاف الحركة (Space)، تكبير/تصغير (+/-، أو عجلة الفأرة بعد النقر على الكرة)، إظهار الأسماء، إعادة الضبط، ملء الشاشة.

## 2.2 AI Studio في العرض (مظهر فقط)
- **قائمة الموردين:** ابحث بالاسم أو الفئة، اختر بالنقر أو بالأسهم + Enter. القائمة تكتب في `<select data-studio-supplier>` الأصلي، لذلك Summary و Chat و Voice تعمل كما كانت.
- **Ask AI من تفاصيل المورد:** يفتح AI Studio والمورد نفسه مختار (`chooseSupplier` ⇒ حدث `change` ⇒ القائمة تُظهره).
- **كرة الصوت:** Ready (تنفس هادئ برتقالي) ⇒ Connecting (أقواس دوّارة) ⇒ Listening (فيروزي؛ تموجات و "Hearing you…" فقط عند صوتك الحقيقي) ⇒ Thinking (بنفسجي) ⇒ Speaking (برتقالي؛ الجسيمات والطيف والشرارات تتبع صوت Gemini الحقيقي) ⇒ خطأ (أحمر). ملف الحركة `intelligence-voice-visual.js` لا يرسل أي طلب.
- **ملاحظة صادقة للجنة:** "الحركة لا تمثل صوتاً غير موجود: بدون إشارة حقيقية الكرة تتنفس فقط."

## 3. تبويب Tenders

### أول ملف وأول دالة
- **JavaScript:** `wwwroot/js/intelligence-tenders.js` ← `openTendersTab()` ← `loadTenders()` ثم `loadTenderDetails()`.
- **C#:** `IntelligenceController.Tenders(...)` و `TenderDetails(...)` و `Export(...)` ← `TenderCalculator` و `IntelligenceExportService`.

### الـ Breakpoints
| المكان | السطر |
|---|---|
| `intelligence-tenders.js` | `tendersData = await getJson(page.dataset.urlTenders, params);` |
| `intelligence-tenders.js` | `var details = await getJson(page.dataset.urlTenderDetails, params);` |
| `intelligence-tenders.js` | أول سطر في `renderTenderOffers` |
| `intelligence-tenders.js` | `downloadFile(request);` في `exportTender` |
| `IntelligenceController.cs` | `TenderDetails? details = await intelligence.GetTenderDetailsAsync(...)` |
| `TenderCalculator.cs` | `if (!round.Unsealed)` في `BuildRound` (قاعدة الختم) |
| `TenderCalculator.cs` | `details.State = "Completed";` في `BuildTender` |
| `IntelligenceExportService.cs` | `RadarSnapshot snapshot = await intelligence.LoadSnapshotAsync(request.From, request.To, true, ct);` |
| `IntelligenceExportService.cs` | `file.Bytes = writer.ToXlsx(model);` |

### السيناريو أ: فتح التبويب واختيار عطاء
1. **الإجراء:** اضغط **Tenders**.
2. Network: `GET /Intelligence/Tenders?...` ثم `GET /Intelligence/TenderDetails?tenderId=..`.
3. في `TenderCalculator.Build` راقب `data.DefaultTenderId` (أول عطاء له دورة مغلقة بعروض).
4. **الإجراء:** اضغط عطاءً آخر في القائمة ⇒ `selectTender` ⇒ طلب `TenderDetails` جديد.
5. في `BuildTender` تابع بـ F10: المراحل الست (`AddStage`)، ثم `State`، ثم `BuildRound` لكل دورة.
6. عند دورة مختومة: يدخل `if (!round.Unsealed) { return view; }` ⇒ لا عروض. **هذا دليل عدم تسريب العروض المختومة.**
7. في Console: `tenderDetails.rounds.map(r => [r.roundNumber, r.analysed, r.offers])`.

### السيناريو ب: العروض والمقارنة داخل الدورة
1. **الإجراء:** غيّر "Closed bidding cycle" أو "Comparable offers".
2. لا طلب جديد: `renderTenderOffers()` يختار من `tenderDetails.rounds[..].groups` الجاهزة.
3. افحص في Console: `chosenOfferRound().groups[0].median`, `.lowest.supplier`, `.highest.supplier`.
4. اضغط عموداً في الرسم ⇒ `renderOfferDetail` + رابط سجل العرض.

### السيناريو ج: التصدير Excel / PDF
1. **الإجراء:** اضغط "Export lifecycle (Excel)".
2. Network: `POST /Intelligence/Export` — Payload: `{"tenderId":8,"from":"","to":"","format":"xlsx","reportType":"tender_lifecycle"}`، والرد نوعه `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`.
3. في `IntelligenceExportService.BuildAsync`: تحقق الصيغة والنوع ⇒ بيانات حديثة ⇒ تحقق النطاق ⇒ `AddLifecycleTableAsync` (افحص `committees`, `reports`, `notices`) ⇒ `writer.ToXlsx`.
4. "PDF report" ⇒ `format: 'pdf'`, `reportType: 'tender_summary'`.
5. تجربة خطأ من Console: طلب عطاء غير موجود عبر `postJson(page.dataset.urlExport, {format:'xlsx', reportType:'tender_offers', tenderId: 999999})` ⇒ خطأ برسالة "The selected tender is unavailable in your reporting scope and period."

### ماذا تقول للجنة
> "قائمة العطاءات ورسوم السوق تأتي من `Tenders`، وتفاصيل عطاء واحد من `TenderDetails`. `TenderCalculator` يبني دورة الحياة من تواريخ مسجلة فقط، ويترك الدورة المختومة فارغة تماماً. العروض تُقارن فقط داخل نفس العملة ونفس هيكل البنود. التصدير يُعاد بناؤه في السيرفر من بيانات المستخدم المصرح بها، والمتصفح يرسل أرقام التعريف فقط."

---

## 4. أسئلة اللجنة المتوقعة وإجابات بسيطة

| السؤال | الإجابة |
|---|---|
| أين تبدأ الصفحة؟ | `IntelligenceController.Index()` يرجع `Views/Intelligence/Index.cshtml`، ثم `startIntelligencePage()` في `intelligence-page.js` عند `DOMContentLoaded`. |
| لماذا أربعة ملفات JavaScript؟ | ملف مشترك + ملف لكل تبويب. أعرف فوراً أين كود كل تبويب. |
| أين تُحسب الأرقام؟ | في C# فقط: `OverviewCalculator`, `SupplierCalculator`, `TenderCalculator`. JavaScript يرسم فقط. |
| كيف تقرؤون قاعدة البيانات؟ | `RadarDataService.BuildAsync` باستعلامات EF Core للقراءة فقط (`AsNoTracking`)، والنتيجة تُخزن 60 ثانية لكل مستخدم وفترة. |
| كيف تمنعون رؤية العروض قبل إغلاق التقديم؟ | `IsUnsealed`: عروض الدورة المفتوحة لا تُقرأ أصلاً، و `BuildRound` يترك الدورة المختومة بلا عروض ولا حتى عددها. |
| لماذا لا تجمعون عملات مختلفة؟ | `AddMoney` يجمع لكل عملة على حدة، والمقارنة تتطلب نفس العملة (`different_currency`). |
| ما الفرق بين معدل الاستجابة ومعدل الفوز؟ | الاستجابة = عروضه ÷ دعواته (للدورات المغلقة). الفوز = ترسياته ÷ العطاءات المرساة التي شارك فيها. |
| كيف يُحسب الوسيط؟ | ترتيب القيم وأخذ الأوسط (أو متوسط الاثنين في الوسط) — `IntelligenceHelpers.Median`. |
| ما هو HHI؟ | مجموع مربعات الحصص؛ كلما اقترب من 1 كان السوق أكثر تركّزاً. |
| ماذا لو انتهت الجلسة؟ | `getJson` يكتشف التحويل (`response.redirected`) ويعرض رسالة خطأ بدل كسر الصفحة. |
| من يستطيع فتح الصفحة؟ | مسؤول المشتريات فقط (`[Authorize(Roles = SourcingOfficer)]`)، وحساب المورد ممنوع دائماً (`OnActionExecuting`). المستخدمون الآخرون محصورون بنطاق جهتهم ولجانهم. |
| كيف تحمون طلبات POST؟ | `[AutoValidateAntiforgeryToken]` + الـ header `RequestVerificationToken` في `postRequest`. |
| ماذا حذفتم ولماذا؟ | الرسم ثلاثي الأبعاد والشبكة ثنائية الأبعاد والإعادة التاريخية: أكثر من 1800 سطر رسوميات. بقيت المعلومات في جداول وقوائم أوضح. |
| هل تغيّر AI Studio؟ | لا. ملفاته ببصمة git نفسها، وأفعاله في الـ Controller وقسمه في الصفحة وأنماطه منسوخة حرفياً. |
| كيف تتأكدون أن الأرقام صحيحة؟ | `Tests/RadarChecks`: 44 فحصاً على سوق تجريبي بأرقام معروفة، و 57 مع قاعدة الفريق، منها فحص أن إحصاءات تبويب الموردين = حقائق AI Studio لكل مورد. |
| لماذا الفلترة في السيرفر وليس في JavaScript؟ | حتى تبقى كل الحسابات في مكان واحد (C#) ويسهل تتبعها واختبارها. |
| لماذا Snapshot في الذاكرة بدل استعلام لكل رقم؟ | استعلامات قليلة مرة واحدة، ثم كل الحسابات في الذاكرة بسرعة، وكل التبويبات ترى نفس الأرقام. |

- **س: لماذا الكرة ثلاثية الأبعاد معقدة بينما الخلفية بسيطة؟** ج: الكرة ملف عرض معزول (`orange-intelligence-universe.js`) لا يحتوي منطق عمل. واجهتها خمس دوال (`init`, `update`, `selectSupplier`, `resize`, `dispose`). كل رقم تعرضه محسوب في `SupplierCalculator`.
- **س: هل تكشف الكرة موردين خارج صلاحياتي؟** ج: لا. نقاطها من `allSuppliers` الذي يبنيه السيرفر من الـ Snapshot المحدود بنطاق المستخدم (`RadarDataService.ScopeAsync`)، وهي نفس القائمة التي كانت تملأ قوائم المقارنة و AI Studio.
