# تقرير إنشاء ودمج Workv5

التاريخ: 20 نوفمبر 2026 (توقيت عمّان). هذا التقرير يصف العمل المنفذ حتى commit `ebfb7e5` مقارنةً برأس `Ahmad3` الأصلي `492a257`، قبل إضافة هذا الملف التوثيقي.

## النتيجة ومصادرها

| المصدر | الرأس الذي فُحص | ما حدث في Workv5 |
| --- | --- | --- |
| `Ahmad3` | `492a257` | أُنشئ `Workv5` منه مباشرةً؛ بقي تاريخ Ahmad3 وملفاته أساس المشروع الحديث. |
| تعديلات محلية فوق `Ahmad3` | 12 ملفاً غير محفوظة عند البداية | حُفظت في `6809107` قبل أي دمج. |
| `tariq5` | `969124e` | ثبت أنه سلف مباشر لـ `Ahmad3`، لذا يدخل تاريخه، بما فيه تغييرات Evaluation Committee، دون cherry-pick إضافي. |
| `amal3` | `d8e8178` | طُبق commit `Admin Reports` بطريقة cherry-pick مع توثيق المصدر؛ commit الناتج `7357c33`. |
| `haya` | رأس الفرع `275e952`؛ آخر commit مختص بتقارير الأدمن `ad31853` | نُقلت ميزات تقارير الأدمن من `ad31853` إلى مسار مستقل بجانب تقارير Amal؛ commit الناتج `ebfb7e5`. لم يُدمج رأس haya كله. |

فرع Git المرفوع هو `origin/Workv5`. عند آخر تحقق قبل هذا التقرير، كان `HEAD` و`origin/Workv5` كلاهما `ebfb7e5` وكانت شجرة العمل نظيفة.

## جميع تعارضات الدمج

حدث تعارض واحد أثناء تطبيق Amal3. وظهرت سبعة تعارضات أثناء **محاولة** تطبيق `ad31853` من haya. أُلغيت محاولة cherry-pick الخاصة بـ haya بالكامل باستخدام `git cherry-pick --abort`؛ لم تُحفظ ملفاتها المتعارضة أو حالة وسيطة منها. بدل ذلك نُقلت صفحات وخدمات تقارير الأدمن إلى ملفات مستقلة مسماة `Haya...`.

| المصدر | الملف المتعارض | سبب التعارض وكيف عولج |
| --- | --- | --- |
| Amal3 | `Orange-Tendering-System/ViewModels/WorkspaceNavigation.cs` | أضاف Ahmad3 أيقونة `radar`، وأضاف Amal3 أيقونات `unlock` و`lock` و`trend` و`dismiss` و`award` و`inbox` و`alert` و`pause` و`search` و`calendar` و`return` في الموضع نفسه. حُفظت جميع الأيقونات مع الفواصل الصحيحة، ثم اكتمل cherry-pick. |
| haya | `Orange-Tendering-System/Controllers/AnalyticsController.cs` | تنفيذان مختلفان لمسارات وتقويم صلاحيات التقارير. أُبقي متحكم Amal، وأضيف `Controllers/HayaAdminReportsController.cs` للإدارة فقط. |
| haya | `Orange-Tendering-System/Services/ReportExport.cs` | تنفيذان مختلفان لتنسيق PDF وExcel. أُبقي مُصدّر Amal، وأضيف `Services/HayaReportExport.cs` لتقارير haya. |
| haya | `Orange-Tendering-System/Services/ReportingService.cs` | نماذج وفلاتر وبناء تقارير مختلفان. أُبقيت خدمة Amal، وأضيفت `Services/HayaReportingService.cs` مع أسماء أنواع مستقلة. |
| haya | `Orange-Tendering-System/Views/Analytics/Index.cshtml` | صفحة فهرس Amal مختلفة عن مركز haya. أُبقيت صفحة Amal، ونُقلت صفحة haya إلى `Views/HayaAdminReports/Index.cshtml` ومركزها إلى `AdminLanding.cshtml`. |
| haya | `Orange-Tendering-System/Views/Analytics/Report.cshtml` | نماذج عرض مختلفة. أُبقي عرض Amal، ونُسخ عرض haya إلى `Views/HayaAdminReports/Report.cshtml`. |
| haya | `Orange-Tendering-System/Views/Shared/_AdminLayout.cshtml` | تعارضت روابط التنقل وشروط البحث. أُبقيت روابط Amal وأضيف رابط مستقل باسم `Admin Reports`، مع استثناء صفحته الرئيسية من شريط بحث القوائم العام. |
| haya | `Orange-Tendering-System/Views/Shared/_ReportBody.cshtml` | جزئيتا العرض تتوقعان نموذجين مختلفين. أُبقيت جزئية Amal المشتركة، ونُقلت جزئية haya إلى `Views/HayaAdminReports/_ReportBody.cshtml`. |

هذه السبعة **تعارضات مرصودة** في محاولة haya، وليست ملفات حُلّت بقبول أحد طرفي Git آلياً. لا توجد علامات `<<<<<<<` أو `=======` أو `>>>>>>>` متبقية في الملفات الناتجة.

## الملفات التي تغيرت

الرمز `M` يعني تعديل ملف قائم، و`A` يعني إضافة ملف. القوائم التالية تغطي جميع الملفات الثمانين المختلفة الناتجة عن commits الثلاثة فوق `Ahmad3`؛ يظهر `Program.cs` و`_AdminLayout.cshtml` في مجموعتي Amal وhaya لأن كلتيهما عدلتهما.

### 1. تعديلات Ahmad3 المحلية المحفوظة في `6809107` — 12 ملفاً

هذه التعديلات تخص نموذج عرض المورد، حفظ المسودة، مستندات وصور المنتجات، وسير العمل، مع فحص دخاني مواكب:

```text
M Orange-Tendering-System/Areas/Supplier/Controllers/DocumentsController.cs
M Orange-Tendering-System/Areas/Supplier/Controllers/OffersController.cs
M Orange-Tendering-System/Areas/Supplier/Views/Offers/Create.cshtml
M Orange-Tendering-System/Services/SupplierDocumentService.cs
M Orange-Tendering-System/Services/SupplierOfferSubmission.cs
M Orange-Tendering-System/Services/TenderWorkflowService.cs
M Orange-Tendering-System/Tests/AuthSmoke/WorkflowRedesignChecks.cs
M Orange-Tendering-System/Views/Shared/_WorkflowList.cshtml
M Orange-Tendering-System/Views/Shared/_WorkflowPopup.cshtml
M Orange-Tendering-System/wwwroot/css/supplier-offer-create.css
M Orange-Tendering-System/wwwroot/css/workflow-cycle.css
M Orange-Tendering-System/wwwroot/js/supplier-offer-form.js
```

التغيير الوظيفي الأبرز: حفظ عرض غير مكتمل كمسودة منفصل عن الإرسال النهائي؛ التحقق من صور المنتجات وإظهارها لصاحب العرض فقط؛ جعل الملاحظات الإضافية اختيارية؛ وتحديث الواجهة والفحص الدخاني لهذه الحالات. هذه التعديلات كانت موجودة وغير محفوظة عند بدء مهمة الدمج، ولم تُنشأ من فرع آخر أثناءها.

### 2. Amal3 في `7357c33` — 47 ملفاً

طُبق commit المصدر `d8e8178` كاملاً بعد حل تعارض `WorkspaceNavigation.cs` المذكور أعلاه. أضاف تحليلات الإدارة، صلاحيات الوصول للتقارير، العقود، ترحيل قاعدة البيانات، العرض والتصدير، بيانات العرض التجريبي، والاختبارات المرافقة.

```text
M .gitignore
M Orange-Tendering-System/Areas/Admin/Controllers/MaintenanceController.cs
M Orange-Tendering-System/Areas/Admin/Views/Maintenance/Form.cshtml
M Orange-Tendering-System/Areas/SourcingManager/Controllers/AnalyticsController.cs
M Orange-Tendering-System/Areas/TopCommittee/Controllers/ReportsController.cs
M Orange-Tendering-System/Areas/TopCommittee/Controllers/StatisticsController.cs
M Orange-Tendering-System/Areas/TopCommittee/Controllers/TopCommitteeReportsRouteAttribute.cs
M Orange-Tendering-System/Areas/TopCommittee/Services/TopCommitteeCompatibility.cs
M Orange-Tendering-System/Areas/TopCommittee/Views/Shared/_ReportingContent.cshtml
M Orange-Tendering-System/Controllers/AnalyticsController.cs
M Orange-Tendering-System/DEMO-DATA.md
M Orange-Tendering-System/Data/ApplicationDbContext.cs
A Orange-Tendering-System/Data/Migrations/20261119181750_AdminAnalyticsAccessAndContracts.Designer.cs
A Orange-Tendering-System/Data/Migrations/20261119181750_AdminAnalyticsAccessAndContracts.cs
M Orange-Tendering-System/Data/Migrations/ApplicationDbContextModelSnapshot.cs
A Orange-Tendering-System/Models/Entities/AdminReportSettings.cs
M Orange-Tendering-System/Program.cs
A Orange-Tendering-System/Services/FinalTenderContractService.cs
A Orange-Tendering-System/Services/ReportAccessService.cs
M Orange-Tendering-System/Services/ReportExport.cs
A Orange-Tendering-System/Services/ReportingService.Decisions.cs
M Orange-Tendering-System/Services/ReportingService.cs
M Orange-Tendering-System/Services/Reports/ReportDocumentService.cs
M Orange-Tendering-System/Services/TenderWorkflowService.Reports.cs
A Orange-Tendering-System/Tests/DemoDataSeeder/AnalyticsDemoSeed.cs
M Orange-Tendering-System/Tests/DemoDataSeeder/Program.cs
A Orange-Tendering-System/ViewModels/AnalyticsMetricAppearance.cs
A Orange-Tendering-System/ViewModels/AnalyticsModels.cs
A Orange-Tendering-System/ViewModels/AnalyticsReportCharts.cs
M Orange-Tendering-System/ViewModels/UiText.cs
M Orange-Tendering-System/ViewModels/WorkspaceNavigation.cs
A Orange-Tendering-System/Views/Analytics/Access.cshtml
A Orange-Tendering-System/Views/Analytics/AuditEntry.cshtml
A Orange-Tendering-System/Views/Analytics/Contracts.cshtml
A Orange-Tendering-System/Views/Analytics/Dashboard.cshtml
M Orange-Tendering-System/Views/Analytics/Index.cshtml
M Orange-Tendering-System/Views/Analytics/Report.cshtml
A Orange-Tendering-System/Views/Analytics/_AnalyticsDonut.cshtml
A Orange-Tendering-System/Views/Analytics/_AnalyticsFilters.cshtml
A Orange-Tendering-System/Views/Analytics/_DashboardData.cshtml
A Orange-Tendering-System/Views/Analytics/_ReportCatalog.cshtml
A Orange-Tendering-System/Views/Analytics/_ReportCharts.cshtml
M Orange-Tendering-System/Views/Shared/_AdminLayout.cshtml
M Orange-Tendering-System/Views/Shared/_ReportBody.cshtml
A Orange-Tendering-System/wwwroot/css/analytics.css
A Orange-Tendering-System/wwwroot/js/admin-analytics.js
M docs/system-integration/tests/reports_test.py
```

أكثر الملفات إعادةً للصياغة في هذا commit هي `Controllers/AnalyticsController.cs`، و`Services/ReportingService.cs`، و`Services/ReportExport.cs`، و`Views/Shared/_ReportBody.cshtml`، و`Views/Analytics/Report.cshtml`. ملف الترحيل `20261119181750_AdminAnalyticsAccessAndContracts.Designer.cs` مولّد ويضم 3647 سطراً جديداً؛ لم تُنشأ هذه الأسطر يدوياً أثناء حل التعارض.

### 3. نقل تقارير haya في `ebfb7e5` — 23 ملفاً

نُقلت صفحات وتقارير `ad31853` إلى مسار إدارة مستقل. أُعيدت تسمية أنواع الخدمة والنماذج لتجنّب الاصطدام بأنواع Amal (`HayaReportingService` و`HayaReportModel` وما يتبعها)، وغُيّرت روابط الصفحات والتصدير إلى `HayaAdminReports`. تعرض الواجهة مركزاً للتقارير، صفحات الطلبات والمناقصات والموردين والتقييم والحوكمة واللجنة العليا، إدارة نسخ التقارير، وتصدير PDF/Excel. المدخل الجديد متاح لدور `Admin` فقط.

```text
A Orange-Tendering-System/Controllers/HayaAdminReportsController.cs
M Orange-Tendering-System/Program.cs
A Orange-Tendering-System/Services/HayaReportExport.cs
A Orange-Tendering-System/Services/HayaReportingService.cs
A Orange-Tendering-System/ViewModels/HayaReportExportLinksViewModel.cs
A Orange-Tendering-System/ViewModels/HayaReportsAnalyticsLandingViewModel.cs
A Orange-Tendering-System/ViewModels/HayaSupplierReportPagerViewModel.cs
A Orange-Tendering-System/Views/HayaAdminReports/AdminLanding.cshtml
A Orange-Tendering-System/Views/HayaAdminReports/GovernanceReport.cshtml
A Orange-Tendering-System/Views/HayaAdminReports/Index.cshtml
A Orange-Tendering-System/Views/HayaAdminReports/OperationalReport.cshtml
A Orange-Tendering-System/Views/HayaAdminReports/PurchaseRequestReport.cshtml
A Orange-Tendering-System/Views/HayaAdminReports/Report.cshtml
A Orange-Tendering-System/Views/HayaAdminReports/ReportsManagement.cshtml
A Orange-Tendering-System/Views/HayaAdminReports/SupplierReport.cshtml
A Orange-Tendering-System/Views/HayaAdminReports/TopCommitteeReport.cshtml
A Orange-Tendering-System/Views/HayaAdminReports/_ReportBody.cshtml
A Orange-Tendering-System/Views/HayaAdminReports/_ReportExportLinks.cshtml
A Orange-Tendering-System/Views/HayaAdminReports/_SupplierReportPager.cshtml
M Orange-Tendering-System/Views/Shared/_AdminLayout.cshtml
A Orange-Tendering-System/wwwroot/css/haya-admin-reports.css
A Orange-Tendering-System/wwwroot/js/haya-report-table-tools.js
M Orange-Tendering-System/wwwroot/js/orange-ui.js
```

في `Program.cs` سُجلت خدمتا `HayaReportingService` و`HayaReportExport`. وفي `_AdminLayout.cshtml` أضيف رابط `Admin Reports`، وعُدّل شرط شريط البحث. وُسع تعرّف `orange-ui.js` على أعمدة الحالات، مثل التحقق والاعتماد والتوصية والتصويت والصلاحية. نسخ ملف CSS إضافات haya الخاصة بالتقارير دون استبدال CSS المشروع الحديث.

## ما لم يُدمج من haya ولماذا

رأس `haya` النهائي `275e952` لم يُطبّق كـ commit كامل. آخر تغييرات **تقارير الأدمن** كانت في `ad31853`، أما `275e952` فأضاف صفحة استجابة اللجنة العليا لمدير التوريد، ونموذجها، وتنسيقها، وأدخل أيضاً ملفات DLL مبنية تحت `.codex-seed-run/` وتعديلين على `appsettings*.json`. لم تُنقل صفحة مدير التوريد أو الملفات التنفيذية أو تعديلات الإعدادات؛ لأن نطاق الطلب هنا تقارير الأدمن، ولأن نقل رأس مشروع أقدم كاملاً كان سيعيد كتابة أجزاء من Ahmad3/Amal3. لذلك يظهر عمل haya في `Workv5` كـ commit نقل وظيفي مستقل، وليس كسلف Git لـ `Workv5`.

## التحقق والقيود

- تحقق Git أن `origin/Ahmad3` و`origin/tariq5` سلفان لـ `Workv5`، وأن `HEAD` يطابق `origin/Workv5` عند الرفع.
- اجتاز `git diff --check` فحص المسافات قبل commits الدمج.
- نجح `dotnet build Orange-Tendering-System.slnx --no-restore -p:UseAppHost=false -p:OutputPath=bin/Workv5-validation/ -v:q` بصفر أخطاء وتحذير واحد في `Areas/TopCommittee/Controllers/DecisionsController.cs` (`CS9107`).
- محاولة بناء عادية سابقة فشلت لأن عملية تطبيق كانت تستخدم `Orange-Tendering-System.exe`، ولذلك استُخدم مسار إخراج مستقل للبناء الناجح.
- في الدمج الأصلي لم يُشغّل اختبار قاعدة بيانات أو اختبار متصفح لصفحات تقارير haya المنقولة. نجاح بنائها يثبت صحة التجميع، ولا يثبت تشغيل كل تقرير مع البيانات الحية.
- يحتفظ `Workv5` حالياً بتنفيذي تقارير متوازيين: تقارير Amal عبر `/Analytics` وتقارير haya عبر `/HayaAdminReports`. هذا يتجنب فقدان أي واجهة أثناء الدمج، لكنه يكرر بعض منطق التقارير والتصدير ويحتاج قراراً لاحقاً إذا أُريد توحيدهما في مسار واحد.

## ملحق: مراجعة صفحات لجنة التقييم بعد تنبيه المستخدم

وجود `tariq5` (`969124e`) في تاريخ `Workv5` لم يكن ضماناً لتطابق واجهة لجنة التقييم. أُجريت مقارنة مباشرة بين رأسي الفرعين بعد التنبيه:

- ملفات صفحات Dashboard وCriteria وEvaluations وWeightApprovals وWorkflow/Index، وكذلك `_EvaluationCommitteeLayout.cshtml`، متطابقة من حيث المحتوى.
- داخل `Areas/EvaluationCommittee` تغيّر `Controllers/WorkflowController.cs` و`Views/Reports/Review.cshtml`، وأُضيفت `Views/Workflow/Meeting.cshtml` ذات 259 سطراً بعد Tariq5.
- كانت تجربة التقييم الفعلية تعتمد أيضاً على `Views/Shared/_WorkflowPopup.cshtml`. في Tariq5 كان نموذج درجات الموردين والتوقيع داخل النافذة، بينما استبدله Ahmad3 برابط إلى صفحة `Meeting`. لهذا ظهر اختلاف كبير رغم تطابق أغلب ملفات صفحات المنطقة.
- أُعيد نموذج الدرجات والتوقيع إلى نافذة التقييم أثناء فتح فترة التسجيل، مع حقول التوصية وسببها وتبرير الاختلاف عن أعلى درجة، وقيّد الإدخال إلى منزلة عشرية واحدة وفق خدمة Workv5. بقيت صفحة الاجتماع التفصيلية متاحة كرابط إضافي وللمراجعة قبل الاجتماع وبعد الإرسال.
- الملفات المعدّلة لهذا التصحيح: `Orange-Tendering-System/Views/Shared/_WorkflowPopup.cshtml` و`Orange-Tendering-System/Views/Shared/_WorkflowList.cshtml` و`Orange-Tendering-System/Tests/AuthSmoke/WorkflowRedesignChecks.cs`. لم يُستبدل متحكم اللجنة أو منطقها بملفات Tariq5 القديمة؛ إذ يعتمد التدفق الحالي على التوصيات والتقرير الآلي.
- نجح بناء التطبيق ومشروع `AuthSmoke`. ثم نجح فحص `--workflow-redesign-checks` على SQL Server محلي بقاعدة اختبار جديدة `OrangeAuthCheck_Redesign_1E1FA48F`: **57 PASS، 0 FAIL**، بما فيها تحقق HTTP من ظهور نموذج النافذة وقواعد التوصية والدقة. يترك برنامج الفحص قاعدة الاختبار لفحصها لاحقاً؛ لم يغيّر قاعدة التطبيق العاملة.
