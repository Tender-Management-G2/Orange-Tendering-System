# تقرير إعادة هيكلة Orange Intelligence (Overview · Suppliers · Tenders)

> **Superseded (2026-11-28).** The authoritative study references for the final code are `docs/ORANGE_INTELLIGENCE_STUDY_GUIDE.md` (Overview, Suppliers, Tenders) and `docs/AI_STUDIO_STUDY_GUIDE.md` (AI Studio). This file describes an earlier stage: `RadarService`, `RadarDataService`, `RadarSnapshot`, `RadarOptions`, `RadarAnalytics` and `IntelligenceHelpers` no longer exist (merged into `Services/Intelligence/IntelligenceService.cs` and `IntelligenceModels.cs`).


- التاريخ: 2026-11-27
- الفرع: `Workv7-from-Workv6` (worktree: `Orange-Tendering-System-finalfinalVersion`)، الأساس: `3ccc1df`. **لم يُعمل commit ولا push.**
- النطاق: تبويبات Overview و Suppliers و Tenders فقط. **AI Studio مستثنى ولم يُعدَّل.**
- المسارات تبدأ من `Orange-Tendering-System/` ما لم يُذكر غير ذلك.

---

## 1. الملفات التي فُحصت قبل التعديل
- MVC: `Views/Intelligence/Index.cshtml`, `Controllers/IntelligenceController.cs`, `Program.cs`, `Areas/SourcingOfficer/Controllers/MarketIntelligenceController.cs` (يحوّل إلى `/Intelligence` فقط).
- JavaScript: `wwwroot/js/orange-radar.js`, `orange-radar-network.js`, `orange-intelligence-universe.js`, `intelligence-ai.js`, `intelligence-audio.js`.
- CSS: `wwwroot/css/orange-radar.css`, `orange-intelligence.css`.
- Services: `Services/Radar/RadarService.cs`, `RadarSnapshot.cs`, `RadarAnalytics.cs`, `RadarGraph.cs`, `RadarTenders.cs`, `RadarOfferComparison.cs`, `RadarForensics.cs`, `RadarBehavior.cs`, `RadarExportService.cs`, `RadarOptions.cs`, `RadarDemoSeeder.cs`, `IntelligenceAiService.cs`, `Services/ReportExport.cs`, `Services/ReportingService.cs`.
- الاختبارات: `Tests/RadarChecks/Program.cs` (خط الأساس: **46/46** بدون قاعدة بيانات، **57/57** مع `--exports`).
- كل مستخدمي خدمات Radar في المستودع (بحث شامل): فقط الـ Controller و AI Studio والاختبارات و `Program.cs`.

## 2. الملفات المعدَّلة
| الملف | التغيير |
|---|---|
| `Views/Intelligence/Index.cshtml` | أُعيد كتابة أقسام التبويبات الثلاثة بنفس التصميم والأصناف، مع `id` لكل عنصر تملؤه JS. حُذفت عناصر العالم ثلاثي الأبعاد والشبكة. **قسم AI Studio منسوخ حرفياً**. Overview أصبح التبويب الافتراضي |
| `Controllers/IntelligenceController.cs` | بنّاء تقليدي بدل Primary Constructor؛ أفعال جديدة لكل تبويب (`Overview`, `Suppliers`, `SupplierDetails`, `CompareSuppliers`, `Tenders`, `TenderDetails`)؛ `Simulate` و `Export` على الخدمة الجديدة. **أفعال AI Studio منسوخة حرفياً** (تحقق بـ diff) |
| `Program.cs` | تسجيل `IntelligenceService` و `IntelligenceExportService` بدل `RadarExportService` |
| `Services/Radar/RadarService.cs` | بقي فقط `SnapshotAsync` و `Thresholds` (بنّاء تقليدي). حُذف `WorkspaceAsync` و `ForensicsAsync` و `Timeline` |
| `Services/Radar/RadarAnalytics.cs` | بقيت فقط الدوال التي يستخدمها AI Studio (`Suppliers`, `Awards`, `RelativePositions`, `Median`) — **كل سطر منها مطابق للأصل حرفياً** |
| `wwwroot/css/orange-radar.css` | حُذفت أنماط العناصر المحذوفة (Deck، Scenes، الشبكة، المفتش، الإعادة، المختبر الجنائي، المحلل). بقيت الأنماط المشتركة دون تغيير |
| `wwwroot/css/orange-intelligence.css` | حُذفت أنماط العالم ثلاثي الأبعاد والشبكة؛ أُضيفت أنماط الدليل ولوحة التفاصيل والعلاقات. **كتلة AI Studio (`.ais-*`) منسوخة حرفياً** |
| `Tests/RadarChecks/Program.cs` | الفحوص نُقلت إلى الكود الجديد بنفس السوق التجريبي ونفس الأرقام المتوقعة + فحوص جديدة |

## 3. الملفات الجديدة
| الملف | الأسطر |
|---|---|
| `Services/Intelligence/IntelligenceModels.cs` | 527 |
| `Services/Intelligence/IntelligenceService.cs` | 126 |
| `Services/Intelligence/IntelligenceHelpers.cs` | 241 |
| `Services/Intelligence/OverviewCalculator.cs` | 140 |
| `Services/Intelligence/SupplierCalculator.cs` | 1244 |
| `Services/Intelligence/TenderCalculator.cs` | 532 |
| `Services/Intelligence/IntelligenceExportService.cs` | 298 |
| `wwwroot/js/intelligence-page.js` | 555 |
| `wwwroot/js/intelligence-overview.js` | 175 |
| `wwwroot/js/intelligence-suppliers.js` | 1162 |
| `wwwroot/js/intelligence-tenders.js` | 882 |
| `docs/INTELLIGENCE_BEGINNER_GUIDE.md`, `docs/INTELLIGENCE_DEMO_WALKTHROUGH.md`, `docs/INTELLIGENCE_REFACTOR_REPORT.md` (في جذر المستودع) | — |

## 4. الملفات المحذوفة
| الملف | الأسطر | السبب |
|---|---|---|
| `wwwroot/js/orange-radar.js` | 969 | استُبدل بالملفات الأربعة الجديدة |
| `wwwroot/js/orange-radar-network.js` | 976 | محرك الشبكة ثنائية الأبعاد (فيزياء، تكبير، مسار، إعادة، PNG) |
| `wwwroot/js/orange-intelligence-universe.js` | 881 | عالم الموردين ثلاثي الأبعاد (Three.js) |
| `Services/Radar/RadarGraph.cs` | 95 | عُقد وحواف الشبكة — أصبحت `FindSharedRounds` + `BuildRelationships` |
| `Services/Radar/RadarForensics.cs` | 132 | كواشف الأنماط الجنائية — **لم تكن معروضة في الصفحة** |
| `Services/Radar/RadarBehavior.cs` | 53 | ملاحظات أزواج السعر/التوقيت — كانت تُرسل للمتصفح دون عرض؛ `StructureKey` انتقلت إلى `IntelligenceHelpers` |
| `Services/Radar/RadarTenders.cs` | 72 | أصبح `TenderCalculator` |
| `Services/Radar/RadarOfferComparison.cs` | 99 | أصبح `SupplierCalculator.CompareOffers` |
| `Services/Radar/RadarExportService.cs` | 291 | أصبح `IntelligenceExportService` (التقارير الثلاثة المستخدمة فقط) |

مكتبة `wwwroot/lib/three` **لم تُحذف** لأن صفحات المصادقة تستخدمها.

## 5. عدد الأسطر قبل وبعد
| الجزء | قبل | بعد | ملاحظة |
|---|---|---|---|
| Razor View | 421 سطر / 46.2 KB | 357 / 35.2 KB | −24% حجماً |
| Controller | 145 | 233 | الزيادة: بنّاء تقليدي، أفعال منفصلة لكل تبويب، تعليقات تشرح كل رابط |
| JavaScript للتبويبات الثلاثة | 2826 سطراً / **251.7 KB** في 3 ملفات | 2774 / **145.4 KB** في 4 ملفات | **−42% حجماً**؛ الأسطر متقاربة لأن الكود القديم كان مضغوطاً جداً (246 سطراً أطول من 200 حرف ⇐ 70 الآن، وكلها سلاسل HTML) |
| CSS | 1242 / 119.5 KB | 762 / 70.1 KB | **−41%** |
| C# للتحليلات (Radar المحذوف/المعدل ⇐ Intelligence) | 1044 / 82.2 KB | 3222 / 131.8 KB | **زاد** عمداً: حلقات `foreach` صريحة وأقواس لكل `if` وكلاسات DTO عادية (527 سطراً) بدل سجلات وسلاسل LINQ مضغوطة |
| الاختبارات | 232 | 254 | |

**الخلاصة الصادقة:** حجم الواجهة (JS + CSS + View) انخفض بنحو 40%، بينما زادت أسطر C# لأن نفس المنطق صار مكتوباً خطوة بخطوة. الهدف كان سهولة الفهم وليس أقل عدد أسطر.

## 6. تقليل التعقيد (قياسات)
### JavaScript (قبل ⇐ بعد)
| البنية | قبل | بعد |
|---|---|---|
| Arrow functions (`=>`) | 760 | **0** |
| Optional chaining (`?.`) | 161 | **0** |
| Template literals (`` ` ``) | 722 علامة | **0** |
| `const` / `let` | 969 / 87 | **0 / 0** (كله `var`) |
| IIFE `(() => {...})()` | 29 | **0** |
| `.map(` / `.filter(` / `.reduce(` | 183 / 113 / 21 | **0 / 0 / 0** |
| `requestAnimationFrame` (حلقات رسوم) | 19 | 2 (إظهار الـ Toast وإعادة الرسم بعد تغيير الثيم) |
| مكتبة Three.js (65 استخداماً) | نعم | **لا** |
| وحدات ES (`type="module"`) للتبويبات | نعم (العالم) | **لا** |

### C# (الملفات المستبدلة ⇐ الجديدة)
| البنية | قبل | بعد |
|---|---|---|
| `record` | 40 | **0** (كلاسات عادية) |
| Primary constructors | 3 | **0** |
| `switch` expressions / tuple patterns | 6 | **0** |
| ثلاثي متداخل (`a ? b : c ? d : e`) | 15 | **0** |
| `=>` (lambdas وتعابير) | 354 | 50 (فقط داخل `OrderBy`/`Where` قصيرة) |
| `??` | 44 | 14 |
| `foreach` | 20 | 125 |

### البنية
- **قبل:** طلب واحد (`Workspace`) يرجع كائناً ضخماً من 16 جزءاً (منها الرسم البياني والكواشف والسلوك والخط الزمني)، وملف JS واحد بحالة مشتركة (`state`) و "event bus" (`radar:selection`, `radar:filters`, `radar:tab`) بين 3 محركات.
- **بعد:** طلب مستقل لكل تبويب/إجراء، كل ملف JS مسؤول عن تبويب واحد، لا حالة مشتركة إلا متغيرات بسيطة لكل تبويب، و**الحدث الوحيد** المتبقي هو `radar:data` الذي يحتاجه AI Studio.
- **كل الحسابات** انتقلت من JavaScript إلى C# (الوسيط، أيام دورة الحياة، الفلاتر، المؤشرات، الاستنتاجات، مجموعات العروض).

## 7. المحركات المرئية التي حُذفت أو بُسِّطت
| قبل | بعد |
|---|---|
| عالم الموردين ثلاثي الأبعاد (Three.js): كرة دوارة، أربع ترتيبات، ملصقات، أقواس متحركة، تكبير، ملء الشاشة | **دليل الموردين** (جدول قابل للبحث) + **لوحة تفاصيل** بالتبويبات الأربعة نفسها |
| شبكة ثنائية الأبعاد بفيزياء: فلاتر روابط، مسار بين موردين، مجموعات، تكبير، PNG | **جدول علاقات السوق** + **مجموعات المنافسة** |
| إعادة تاريخية متحركة (Timeline replay) | خط "Suppliers submitting offers, per month" |
| اختيار حتى 5 موردين مع تظليل متقاطع في كل الرسوم | لوحة تفاصيل مورد + مقارنة موردين |
| حركة عدّ أرقام المؤشرات | أرقام ثابتة |
| بحث عام في شريط السياق | بحث في دليل الموردين + بحث في قائمة العطاءات |
| روابط قديمة (`#network`, `#market`, `#analyst`...) | تفتح Overview (الروابط الأربعة الرسمية تعمل) |

## 8. الوظائف المحتفظ بها
- **Overview:** 4 مؤشرات، قمع دورة الحياة (5 مراحل مع وسيط الأيام)، "Worth your attention" بروابطها (فتح عطاء، فتح الموردين الصامتين)، اختصارات التبويبات، فلتر التاريخ، النطاق، علامة البيانات التجريبية، رسائل الحالة.
- **Suppliers:** فلاتر الجهة/الفئة/النشاط + بحث، 4 مؤشرات، لمحة السوق (الفئات بالنقر للفلترة + الاتجاه الشهري)، دليل الموردين، تفاصيل المورد (نظرة عامة، المشاركة، الأسعار، العلاقات)، "Ask AI"، رسما الأداء مع جداول بديلة والاستنتاجات، علاقات السوق والمجموعات، مقارنة عروض موردين (السعر + التوقيت بتوقيت عمّان + رابط سجل العرض + فتح العطاء)، سيناريو "ماذا لو" مع البدائل.
- **Tenders:** القائمة + الاختيار + بحث، البطاقة، 4 مؤشرات، الخط الزمني (6 مراحل)، الدورات، العروض المالية لكل دورة مغلقة ومجموعة قابلة للمقارنة (رسم، وسيط، أقل/وسيط/أعلى، جدول، تفاصيل عرض، رابط السجل)، المشاركة لكل دورة (بالنقر لفتح العطاء)، الجهات، أكثر أزواج المنافسين (بالنقر للمقارنة)، الترسيات الرسمية لكل عملة مع HHI وجدول، Excel لدورة الحياة، Excel للعروض، تقرير PDF.
- عام: العربية/الإنجليزية، RTL/LTR، الثيم الفاتح/الداكن (إعادة رسم عند `orange:themechange`)، التجاوب مع الجوال، التنقل بالأسهم بين التبويبات، `#overview` / `#suppliers` / `#tenders` / `#studio`، وتغيير الـ hash دون إعادة تحميل.

## 9. دوال أُعيد تصميمها عمداً
| قبل | بعد |
|---|---|
| `RadarService.WorkspaceAsync` (كل شيء في طلب واحد) | `GetOverviewAsync`, `GetSuppliersAsync`, `GetSupplierDetailsAsync`, `CompareSuppliersAsync`, `SimulateAsync`, `GetTendersAsync`, `GetTenderDetailsAsync` |
| `RadarAnalytics.Suppliers/Summary/CompetitionAnalysis/AwardDistributions/Simulate/JointRounds` (LINQ مضغوط) | `SupplierCalculator.BuildStats/Build/Simulate`, `TenderCalculator.BuildCompetition/...`, `IntelligenceHelpers.FindSharedRounds` |
| `RadarTenders.Build` | `TenderCalculator.BuildAllTenders` / `BuildTender` / `BuildRound` (+ المجموعات محسوبة في C#) |
| `RadarOfferComparison.Build` | `SupplierCalculator.CompareOffers` / `CompareTwoOffers` / `FindPriceIssue` |
| `RadarExportService.BuildAsync` (13 نوع تقرير) | `IntelligenceExportService.BuildAsync` (3 أنواع تستخدمها الصفحة) |
| فلترة الموردين في JavaScript (`filteredSupplierIds`) | `SupplierCalculator.ApplyFilters` في السيرفر |
| `renderInsights`, `tenderOffers`, `renderOverview` (حساب + رسم) | رسم فقط؛ الأرقام من الـ JSON |

## 10. التحقق من سلامة AI Studio
> هذا القسم يصف إعادة الهيكلة الأولى (2026-11-27). في 2026-11-28 عُدِّل **مظهر** AI Studio فقط (كرة الصوت وقائمة الموردين) مع 6 أسطر ربط في `intelligence-ai.js` و `intelligence-audio.js`؛ منطق Gemini لم يتغير — انظر القسم 14.
| الفحص | النتيجة |
|---|---|
| `git hash-object` لـ `intelligence-ai.js`, `intelligence-audio.js`, `IntelligenceAiService.cs` قبل وبعد | **مطابق** |
| `diff` قسم AI Studio في الـ Controller (من `// ---------- AI Studio (Gemini) ----------` حتى النهاية) مع النسخة الأصلية | **مطابق** |
| `diff` قسم `<section ... id="oi-studio">` في الـ View مع الأصل | **مطابق** |
| كتلة CSS `.ais-*` (111 سطراً) موجودة حرفياً | **نعم** |
| كل سطر CSS يخص `.ais`, `.oi-select`, `.rd-btn`, `.oi-head`, `.oi-panel`, tokens `.radar` | **لم يتغير** (الأسطر المحذوفة كلها لعناصر محذوفة) |
| كود `RadarAnalytics.Suppliers` الذي يبني حقائق Gemini | **مطابق حرفياً** |
| `RadarService.SnapshotAsync` + `RadarSnapshot.cs` | السلوك نفسه / الملف لم يتغير |
| العقد: `[data-radar]` + `data-csrf`، `window.OrangeRadar.data.suppliers`، `OrangeRadar.setTab`، حدث `radar:data`، `window.OrangeIntelligenceAI.chooseSupplier` | **محفوظ** |
| متصفح: فتح `#studio`، تبديل Summary/Chat/Voice، زر Start Call، رسالة "اختر مورداً"، تعبئة قائمة الموردين (12)، "Ask AI" من التفاصيل يفتح AI Studio بالمورد مختاراً | **نجح** |
| طلبات Gemini الفعلية | **لم تُرسل** في الاختبار (لم يتغير أي كود يخصها) |

## 11. نتائج الاختبارات
| الاختبار | النتيجة |
|---|---|
| `dotnet build Orange-Tendering-System.csproj` | **نجح، 0 تحذيرات، 0 أخطاء** |
| `dotnet run --project Tests/RadarChecks` | **44 نجح، 0 فشل** |
| `dotnet run --project Tests/RadarChecks -- --exports` (قاعدة الفريق المحلية، قراءة فقط) | **57 نجح، 0 فشل** |
| `node --check` لملفات JS الأربعة | **سليمة** |
| فحص متصفح تلقائي (Edge headless + Playwright) على قاعدة `OrangeTenderingTeamDev_RadarDemo` التجريبية | **54 نجح، 0 فشل** |

### ما تغيّر في فحوص RadarChecks
- **نُقلت بنفس الأرقام المتوقعة:** المسجلون مقابل المعتمدين (6/5)، استبعاد الدورة المختومة (5 دورات، 25 عرضاً)، العروض المختومة (2)، معدل الاستجابة 25/30، معدل الفوز 0.5، المورد الصامت (فوز غير معرف، استجابة 0)، المواقع (25، كل منها من 5)، المنافسة (مدعو 6، مقدم 5)، الترسيات لكل عملة (4)، متوسط العروض (5)، السيناريو (5 دورات متأثرة، HHI يرتفع، "not a forecast"، 5 دورات دون الحد)، المقارنة (أحدث دورة قابلة للمقارنة، 8.25، توقيت عمّان)، دورة الحياة (4 فحوص)، قاعدة الختم (3)، الأزواج المكررة، الحد الأعلى للمجموعات، السوق الفارغ، التصدير (Excel/Word/PDF/حماية الصيغ/RTL/رفض عطاء غير متاح).
- **جديد:** إحصاءات تبويب الموردين = حقائق AI Studio (`RadarAnalytics.Suppliers`) لكل مورد — على السوق التجريبي **وعلى قاعدة البيانات الحقيقية**؛ الفلاتر الأربعة؛ الاتجاه الشهري؛ تفاصيل المورد؛ مجموعات العروض والترسية الرسمية؛ عدم مقارنة عملات مختلفة؛ تصدير العروض بدون دورة؛ رفض نوع تقرير غير مدعوم؛ رفض دورة من عطاء آخر.
- **حُذفت مع الكود المحذوف:** فحوص الكواشف الجنائية (`RadarForensics`: 8 فحوص)، `RadarBehavior.Build` (1)، سلامة عُقد/حواف `RadarGraph.Build` (4). وحُذفت فحوص تصدير `supplier_participation` و `supplier_offers` لأن هذه التقارير لم تعد موجودة (الصفحة لم تكن تطلبها)، واستُبدلت بفحوص مكافئة على التقارير الموجودة.

### فحوص المتصفح (54)
رفض الطلب المجهول؛ Overview افتراضي؛ 4 تبويبات؛ مؤشرات/قمع/انتباه/اختصارات؛ دليل الموردين (12)؛ الرسوم؛ الاستنتاجات؛ قائمة AI Studio؛ فلتر النشاط؛ البحث؛ لوحة التفاصيل وتبويباتها الأربعة؛ تبديل الرسم وجدول الرسم؛ العلاقات (11 زوجاً)؛ بطاقتا المقارنة؛ السيناريو عبر POST؛ الخط الزمني (6)؛ المؤشرات؛ رسم المشاركة؛ تنزيل Excel دورة الحياة و PDF و Excel العروض؛ تبديل العطاء؛ بحث العطاءات؛ **لا عروض من 1 دورة مختومة في أي من 10 عطاءات**؛ لا مجموعة تخلط العملات؛ رفض POST بلا رمز؛ 404 لعطاء مجهول؛ شريحة الفترة؛ رسالة الفترة الفارغة؛ 6 فحوص AI Studio؛ `#studio` و `#tenders`؛ العربية RTL وتسميات عربية؛ عرض الجوال بلا تمرير أفقي؛ **لا أخطاء JavaScript**؛ لا طلبات فاشلة غير متوقعة.

## 12. قيود متبقية
1. **تسجيل الدخول في الفحص التلقائي** أظهر رسالة "Sign-in temporarily unavailable (200)" من سكربت صفحة الدخول مع أن الدخول نجح (الكوكي صحيحة). هذه في كود المصادقة (خارج النطاق، لم يُلمس) وكانت معروفة سابقاً.
2. **POST بدون رمز Anti-Forgery** يُرفض بالحالة 400 داخلياً ثم تحوله صفحة الحالة في النظام إلى 405. سلوك عام في المشروع وليس جديداً.
3. **ترجمة بعض النصوص من السيرفر:** ملاحظات المقارنة داخل جدول العروض (`ComparisonNote`)، حالات العطاء الخام (`Status`)، وتسميات ملفات Excel/PDF بقيت بالإنجليزية كما كانت. نصوص الواجهة في JS صارت ثنائية اللغة (كانت أغلبها إنجليزية فقط).
4. **الفلترة في السيرفر** تعني طلباً لكل تغيير فلتر؛ سريع ضمن 60 ثانية من الذاكرة المؤقتة، وأبطأ قليلاً على قاعدة البيانات المستضافة بعد انتهاء صلاحيتها.
5. رسم "Participation vs win rate" قد يقص ملصق النقطة الأخيرة على الحافة اليمنى (نفس إعدادات الرسم الأصلية).
6. حجم `SupplierCalculator.cs` (1244 سطراً) كبير لأنه يجمع كل منطق تبويب الموردين (الإحصاءات، الفلاتر، الرسوم، التفاصيل، المقارنة، السيناريو) مقسماً بأقسام واضحة. يمكن تقسيمه لاحقاً إن رغبتم.
7. `docs/ORANGE_RADAR.md` القديم يصف المحركات المحذوفة؛ المرجع الحالي هو `docs/INTELLIGENCE_BEGINNER_GUIDE.md`.

## 13. كود متقدم بقي ولماذا
| الكود | السبب |
|---|---|
| `Services/Radar/RadarSnapshot.cs` (records، LINQ، `GroupBy`، الذاكرة المؤقتة) | يطبق **قاعدة الختم ونطاق المستخدم** ويخدم AI Studio؛ إعادة كتابته تهدد الأمان و AI Studio. مشروح بالتفصيل في الدليل |
| `Services/Radar/RadarAnalytics.cs` | يستخدمه AI Studio فقط؛ ممنوع تعديله. الاختبار يثبت أن النسخة البسيطة تعطي نفس الأرقام |
| أفعال AI Studio (`record SupplierRequest/AskRequest`) | خارج النطاق |
| LINQ قصير (`OrderBy`, `OrderByDescending`, `ThenBy`, `Take`, `Where` في استعلامات EF Core) | الترتيب واستعلامات قاعدة البيانات؛ كتابتها بحلقات أطول وأصعب فهماً |
| `TimeZoneInfo` (توقيت عمّان) و `decimal` للثواني | دقة المقارنة الزمنية وعرضها بتوقيت العمل |
| ECharts (`setOption` بكائنات إعدادات، `formatter: function ...`) | المكتبة تتطلب هذا الشكل؛ كل رسم في دالة واحدة مستقلة |
| `async/await` و `fetch` | الطريقة القياسية للطلبات |
| `Intl.DateTimeFormat` مع `timeZone` | عرض الوقت بتوقيت عمّان في المتصفح |
| `ResizeObserver` في `getChart` | إعادة رسم الرسم عند تغير حجمه (مثلاً عند إظهار تبويب مخفي) |

---

## 14. تحديث 2026-11-28: استعادة عالم الموردين وتحسين مظهر AI Studio

> القرار: الخلفية تبقى بسيطة، والواجهة المرئية تعود متقدمة، معزولة في ملفات عرض.

### 14.1 المصادر التاريخية
| ما استُعيد | المصدر في git | كيف |
|---|---|---|
| عالم الموردين ثلاثي الأبعاد | `wwwroot/js/orange-intelligence-universe.js` — أُضيف في `abec812`، آخر نسخة في `3ccc1df` (HEAD، 881 سطراً) | قراءة فقط بـ `git show`، ثم تكييف يدوي: الزجاج والشبكة والغبار والحلقات والقاعدة، العقد بالـ Shader، التسميات بلا تداخل، الأوضاع، السحب/التكبير/لوحة المفاتيح، ملء الشاشة |
| كرة الصوت | `wwwroot/js/orange-radar-voice.js` — آخر نسخة في `c0514c3` (قبل `daf0025`) | الكرة الجسيمية، الأشرطة الثلاثة، الهالة، أقواس التفكير، الطيف المعكوس |

### 14.2 ما لم يُستعد عمداً
`WorkspaceAsync` والـ endpoint الضخم `Workspace`، `RadarGraph/Forensics/Behavior/Tenders/OfferComparison`، الشبكة ثنائية الأبعاد، وضع Tender في الكرة (لا يوجد في JSON الحالي قائمة مشاركي كل دورة لكل الموردين)، المفتش الداخلي القديم (حلّت محله لوحة التفاصيل الحالية).

### 14.3 التحسينات المرئية
- **تجميع الفئة:** موردو الفئة المختارة ينزلقون (بتتابع) على سطح الكرة إلى قبة أمام الكاميرا؛ منطقة متوهجة بلون الفئة (Shader للقبعات الكروية مع حافة وتموجات)؛ عنوان الفئة وعددها؛ الباقون إلى بقية الكرة باهتين؛ الكاميرا تقترب؛ المسح يعيد الترتيب الأصلي.
- مسارات انتقال على السطح مع ارتفاع خفيف أثناء الطيران (بدل الاستيفاء الخطي)، ظهور أولي من مركز الكرة.
- ألوان الفئات مطابقة لألوان الرسوم (تحويل sRGB صحيح في الـ Shader).
- أقواس متحركة إلى المنافسين الحقيقيين عند اختيار مورد (`SupplierDetails.Competitors`)، وأقواس `Pairs` في وضع Competition.
- تسميات تتجنب لوحات التحكم وتنقلب حسب الاتجاه (RTL).
- تكبير بالعجلة بعد التركيز فقط (لا يعطل تمرير الصفحة)، تكبير بإصبعين على الهاتف، أزرار تكبير.
- كرة الصوت: نواة سائلة، تموجات عند كلام المستخدم، شرارات عند كلام Gemini، لون الخطأ، انتقال لوني ناعم.
- قائمة موردين قابلة للبحث في AI Studio مع حالة تحميل وفراغ وعدم تطابق، وبطاقة "المورد المختار".

### 14.4 الملفات
| النوع | المسار | التغيير |
|---|---|---|
| جديد | `Orange-Tendering-System/wwwroot/js/orange-intelligence-universe.js` | محرك العرض ثلاثي الأبعاد (983 سطراً) |
| جديد | `Orange-Tendering-System/wwwroot/js/intelligence-voice-visual.js` | كرة الصوت (247) |
| جديد | `Orange-Tendering-System/wwwroot/js/intelligence-studio-picker.js` | قائمة الموردين (140) |
| جديد | `Orange-Tendering-System/wwwroot/css/orange-intelligence-visuals.css` | أنماط العرض (206) |
| جديد | `docs/INTELLIGENCE_FUNCTION_MAP.md` | خريطة الدوال |
| معدَّل | `Orange-Tendering-System/Views/Intelligence/Index.cshtml` | بطاقة الكرة بجانب لوحة التفاصيل، الدليل تحت لمحة السوق، قائمة AI Studio، الـ canvas، السكربتات |
| معدَّل | `Orange-Tendering-System/wwwroot/js/intelligence-suppliers.js` | `renderSupplierUniverse`, `onUniverseSupplierClick`, `showSelectedSupplierInUniverse` + 3 استدعاءات |
| معدَّل | `Orange-Tendering-System/wwwroot/js/intelligence-ai.js` | +1 سطر: `change` بعد `chooseSupplier` |
| معدَّل | `Orange-Tendering-System/wwwroot/js/intelligence-audio.js` | +5 أسطر مرئية: `OrangeVoiceVisual.connect` و `data-outcome` |
| معدَّل | `Orange-Tendering-System/wwwroot/css/orange-intelligence.css` | حذف قواعد الكرة القديمة (CSS فقط)، شبكة `.oi-explorer` |
| معدَّل (خلفية) | `Orange-Tendering-System/Services/Intelligence/IntelligenceModels.cs` | `SupplierOption.Category` |
| معدَّل (خلفية) | `Orange-Tendering-System/Services/Intelligence/SupplierCalculator.cs` | `ToOption`: `option.Category = row.Category;` |
| معدَّل | `docs/INTELLIGENCE_BEGINNER_GUIDE.md`, `docs/INTELLIGENCE_DEMO_WALKTHROUGH.md`, هذا التقرير | التوثيق |

**لم تتغير:** `IntelligenceController.cs`, `IntelligenceService.cs`, `OverviewCalculator.cs`, `TenderCalculator.cs`, `IntelligenceHelpers.cs`, `IntelligenceExportService.cs`, `RadarService.cs`, `RadarSnapshot.cs`, `IntelligenceAiService.cs`, ملفات Overview و Tenders في JavaScript. لا migrations، لا جداول جديدة، لا endpoints جديدة.

### 14.5 سبب التغيير الوحيد في الخلفية
الكرة تحتاج **كل** الموردين المصرح بهم مع فئاتهم حتى تبقي غير المطابقين كسياق باهت عند اختيار فئة. `SuppliersData.Suppliers` مفلترة، و `AllSuppliers` لم تحمل اسم الفئة. أُضيفت خاصية واحدة وسطر واحد؛ القائمة نفسها كانت تُرسل للمتصفح سابقاً (قوائم المقارنة و AI Studio)، فلا بيانات جديدة خارج النطاق.

### 14.6 التحقق (2026-11-28)
- `dotnet build`: نجح (0 أخطاء).
- `Tests/RadarChecks`: **44/44**؛ مع `--exports` على قاعدة الفريق المحلية: **57/57** (ومنها: إحصاءات تبويب الموردين = حقائق AI Studio لكل مورد).
- المتصفح (Edge بدون واجهة، WebGL عبر SwiftShader) على **`https://localhost:7182`** بالإعداد الأصلي (`https`، قاعدة `OrangeTenderingTeamDev`) بحساب مسؤول المشتريات: **26/26 بالإنجليزية و 26/26 بالعربية (RTL)**: الكرة ترسم، النقاط = الموردون المصرح بهم، التسميات، التجميع والتغيير والمسح، الأوضاع الثلاثة، التمرير، النقر يفتح التفاصيل، Enter بلوحة المفاتيح، صف الجدول، Renderer واحد بعد تبديل التبويبات، 0 إطارات حركة والتبويب مخفي، المقارنة، السيناريو، Ask AI يختار نفس المورد، بحث القائمة وفراغها والنقر ولوحة المفاتيح، لا تمرير أفقي على عرض الهاتف، لا أخطاء JavaScript.
- بدون تسجيل دخول على 7182: كل endpoints الصفحة (GET و POST) تعيد التوجيه إلى صفحة الدخول (302).
- لقطات المراجعة البصرية: الوضع الفاتح والداكن، العربية، الهاتف، كل حالات كرة الصوت.

### 14.7 ما لم يُختبر
- **مكالمة Gemini Live حقيقية** (لم تُرسل طلبات Gemini مدفوعة، ولا ميكروفون في المتصفح الآلي). حالات الكرة اختُبرت بإشارة صوتية محلية (Oscillator) موصولة مباشرة بالكرة، وبوضع `data-state` يدوياً — أي طبقة العرض فقط.
- Summary و Chat لم تُرسل إلى Gemini؛ تحققنا فقط أن المورد المختار هو قيمة `<select>` التي يقرؤها `intelligence-ai.js`.
- كتم الصوت (Mute) غير موجود في منطق المكالمة، فلا حالة له.
