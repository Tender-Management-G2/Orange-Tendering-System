# عرض المصادقة في Visual Studio أمام اللجنة (Demo Walkthrough)

الهدف: أن تفتح Visual Studio، تضغط زراً في المتصفح، وتتوقف عند كل خطوة مهمة وتشرحها. الشرح الكامل لكل خطوة موجود في `docs/AUTHENTICATION_BEGINNER_GUIDE.md`.

المسارات تبدأ من مجلد المشروع `Orange-Tendering-System/`.

---

## التحضير (مرة واحدة)

1. افتح `Orange-Tendering-System.slnx` في Visual Studio.
2. اختر ملف التشغيل **https** ثم اضغط **F5** (Debug). الموقع: `https://localhost:7182`.
3. لعرض التسجيل مع رمز OTP محلي: شغّل صندوق البريد المحلي من مجلد المشروع:
   ```powershell
   node Tools/DevMail/server.js
   ```
   ثم افتح `http://127.0.0.1:8025` لترى الرسائل.
4. لرؤية خطوات JavaScript: في المتصفح اضغط **F12** ← تبويب **Sources** ← افتح الملف (`supplier-login.js` مثلاً) وضع Breakpoint بالضغط على رقم السطر. (Visual Studio يوقف كود C#، والمتصفح يوقف كود JavaScript.)
5. لرؤية الطلبات: **F12** ← **Network**. اضغط على الطلب لترى الـ JSON المرسل (Payload) والرد (Response).
6. لرؤية قاعدة البيانات: **View ← SQL Server Object Explorer** ← قاعدة `OrangeTenderingTeamDev` ← الجداول.

> نصيحة: عند التوقف استخدم **F10** (السطر التالي)، و**F11** (ادخل داخل الدالة)، و**Shift+F11** (اخرج من الدالة)، و**F5** (تابع). نافذة **Locals** تعرض المتغيرات.

---

## العرض 1: تسجيل مورّد جديد

| ماذا | أين |
|---|---|
| الملف الأول | `wwwroot/js/supplier-registration.js` (في المتصفح، F12 ← Sources) |
| الدالة الأولى | `onRegistrationSubmit` |
| Breakpoint (JS) | السطر `var response = await fetch(...)` داخل `onRegistrationSubmit` |
| المتغير | `form` (النموذج)، وفي Network: الطلب `Register` من نوع `multipart/form-data` |
| Breakpoint (C#) | `Areas/Identity/Pages/Account/Register.cshtml.cs` أول سطر في `OnPostAsync()` |
| راقب | `Input.Email`, `Input.CompanyName`, `CommercialRegistrationCertificate.FileName`, `ModelState.IsValid` |
| Breakpoint ثانٍ | `IdentityResult created = await users.CreateAsync(user, Input.Password);` |
| F11 | لا يدخل (كود Microsoft Identity). اشرح: "CreateAsync يتحقق من قواعد كلمة المرور ويحفظ **Hash** فقط". بعد F10 راقب `created.Succeeded` |
| Breakpoint ثالث | `await SaveDocumentAsync(...)` ← اضغط **F11** فتدخل `SaveDocumentAsync` |
| Breakpoint رابع | `await transaction.CommitAsync();` |
| الجداول التي تتغير | `Suppliers` (Status = Pending)، `AspNetUsers` (IsActive = 0, EmailConfirmed = 0, PasswordHash)، `AspNetUserRoles`، `Documents`، `AuditLogs` |
| الرد | `302 Redirect` إلى `/Identity/Account/VerifyEmail?email=...` |
| الصفحة التالية | Verify your email |

**جملة للشرح:** "إذا فشل أي شيء قبل `CommitAsync`، يتم `RollbackAsync` فلا يبقى أي صف في قاعدة البيانات، و`finally` يحذف الملفات من القرص."

**أرِ حالة فشل:** سجّل مرة ثانية بنفس البريد ← ضع Breakpoint عند `if (existingUser != null || companyExists)` ← الصفحة تعرض الرسالة **مع بقاء الملفات المختارة** (بفضل `showServerValidation`).

---

## العرض 2: رمز OTP

| ماذا | أين |
|---|---|
| الملف | `Areas/Identity/Pages/Account/VerifyEmail.cshtml.cs` |
| Breakpoint | `bool codeIsCorrect = await users.VerifyUserTokenAsync(...)` |
| راقب | `Input.Code`, `user.EmailOtpSentAt`, `user.EmailOtpFailedAttempts`, `expired`, `blocked` |
| جرّب أولاً | رمز خاطئ `000000` ← `codeIsCorrect = false` ← في الجدول `EmailOtpFailedAttempts` يزيد 1 |
| ثم | الرمز الصحيح من `http://127.0.0.1:8025` |
| Breakpoint ثانٍ | `await users.UpdateSecurityStampAsync(user);` ← اشرح: "الختم الجديد يجعل الرمز المستخدم باطلاً، فلا يمكن استعماله مرة ثانية" |
| الجداول | `AspNetUsers` (EmailConfirmed = 1, IsActive = 1)، `Notifications`، `AuditLogs` |
| الرد | Cookie دخول + Redirect إلى `PendingApproval` |

---

## العرض 3: الدخول بالبريد وكلمة المرور

| ماذا | أين |
|---|---|
| الملف الأول | `wwwroot/js/supplier-login.js` |
| Breakpoint (JS) | `onLoginSubmit` ثم `signInWithPasswordOnPage` |
| Network | الطلب `Login?handler=Credentials` |
| Breakpoint (C#) | `Login.cshtml.cs` ← `OnPostCredentialsAsync` ← السطر `string outcome = await SignInWithPasswordAsync();` ← **F11** |
| داخل `SignInWithPasswordAsync` | Breakpoint على `var result = await signInManager.CheckPasswordSignInAsync(user, Input.Password, true);` |
| راقب | `user.Email`, `result.Succeeded`, `result.IsLockedOut`, `isSupplier`, `supplierStatus`, `chosen` |
| الجداول | خطأ: `AspNetUsers.AccessFailedCount` يزيد. نجاح: `AuditLogs` (SignIn) |
| الرد | JSON `{ "outcome": "signedIn", "redirect": "/Admin" }` (أو `rejected` / `locked` / `mismatch`) |
| JS بعد الرد | `goToAfterSignIn(data.redirect)` في `wwwroot/js/biometric.js` |
| الصفحة التالية | مساحة عمل الدور، أو `PendingApproval` للمورّد غير المعتمد |

**أرِ:** كلمة مرور خاطئة 5 مرات ← `outcome = "locked"` ← الرسالة "Try again in 15 minutes".
**أرِ:** اختر "Other User" مع حساب مورّد ← `outcome = "mismatch"` ولم يُسجَّل الدخول.

---

## العرض 4: تسجيل الوجه

| ماذا | أين |
|---|---|
| الصفحة | `PendingApproval` (بعد OTP) أو `/Biometrics/Face` |
| JS | `wwwroot/js/biometric.js` ← `enrollFace` ← `runFaceCheck` |
| Breakpoint (JS) | داخل `runFaceCheck`: السطر `var challenge = await postJson(challengeUrl, challengeBody);` ثم `var capture = await captureFaceSamples(video, null);` |
| راقب (JS) | `challenge.token`، ثم `capture.descriptors.length` (عادة 10 إلى 11 عينة)، و`capture.descriptors[0].v.length` = **128** |
| Breakpoint (C#) | `Controllers/BiometricsController.cs` ← `FaceEnrollChallenge` (كلمة المرور) ثم `FaceEnroll` ← السطر `BiometricResult result = await faces.EnrollAsync(user, submission);` ← **F11** |
| داخل `FaceService.EnrollAsync` | Breakpoint على `string? problem = CheckSamples(submission.Descriptors);` ثم **F11** لترى القواعد |
| راقب | `submission.Descriptors.Count`, `entry.UserId`, `samples.Count` (= 8) |
| الجدول | `FaceTemplates`: 8 صفوف جديدة، العمود `ProtectedTemplate` **مشفّر** (أرِ أنه بايتات غير مقروءة) |
| الرد | `{ "succeeded": true }` ← الانتقال إلى `PendingApproval?faceRegistered=true` |

**جملة للشرح:** "الصورة لا تغادر المتصفح. ما يصل للسيرفر 128 رقماً لكل عينة، ونحفظها مشفّرة. لا يوجد Liveness Detection، فهذا مشروع أكاديمي وليس نظاماً بنكياً."

---

## العرض 5: الدخول بالوجه

| ماذا | أين |
|---|---|
| JS | `supplier-login.js` ← `startIdentityStep('face')` ← `runFaceLogin` |
| Breakpoint 1 (C#) | `Login.cshtml.cs` ← `OnPostIdentityStartAsync` ← `preAuth.Begin(HttpContext, user);` |
| اشرح | "هنا لم يُسجَّل أحد. نحن فقط نتذكر **أي حساب** سيأتي وجهه، لمدة 5 دقائق، في Cookie عشوائية HttpOnly." |
| Breakpoint 2 | `BiometricsController.FaceLogin` ← `ApplicationUser? user = await preAuth.UserAsync(HttpContext, users);` |
| Breakpoint 3 | `Services/Biometric/FaceService.cs` ← `Match(...)` ← السطر `match.Matched = ...` |
| راقب | `bestDistances` (كل رقم أقل من 0.5 = قريب)، `matchingSamples`, `requiredSamples`, `averageDistance`, `threshold` |
| Breakpoint 4 | `Services/Biometric/BiometricSignInService.cs` ← `SignInAsync` ← `await signInManager.SignInWithClaimsAsync(user, false, extraClaims);` |
| الجداول | `FaceTemplates.LastUsedAt` (UPDATE)، `AuditLogs` (`BiometricSignIn`, method `biometric-face`) |
| الرد | `{ "succeeded": true, "redirect": "/Supplier" }` |
| الصفحة التالية | `/Supplier` (يمر عبر `SupplierApprovalGate` في `Services/IdentityAccess.cs`) |

**عرض إضافي بدون حساب:** `/face-demo` (في Development فقط) يعرض المسافات والعتبة والنتيجة على الشاشة، ولا يُسجّل أي دخول.

---

## العرض 6: تسجيل البصمة (Passkey)

| ماذا | أين |
|---|---|
| JS | `biometric.js` ← `registerThisDevice` ← `registerPasskey` |
| Breakpoint (JS) | `var credentialJson = await createPasskey(options, hints);` |
| راقب | `options.challenge`, `options.rp.id` (= `localhost`)، `options.authenticatorSelection.userVerification` (= `required`) |
| ما يحدث هنا | **Windows Hello** يطلب البصمة أو PIN. هذا من نظام التشغيل وليس من مشروعنا |
| Breakpoint (C#) | `BiometricsController.PasskeyRegister` ← `passkeys.RegisterAsync(...)` ← F11 ← `PasskeyService.RegisterAsync` |
| راقب | `attestation.Succeeded`, `attestation.UserEntity.Id == user.Id` |
| الجدول | `AspNetUserPasskeys` (صف جديد: CredentialId + المفتاح العام + الاسم "Windows computer · Edge") |

**جملة للشرح:** "البصمة لا تغادر الجهاز. الجهاز ينشئ مفتاحاً خاصاً يبقى عنده، ويعطينا المفتاح **العام** فقط. عند الدخول يوقّع الجهاز تحدياً عشوائياً، ونحن نتحقق من التوقيع."

---

## العرض 7: الدخول بالبصمة

| ماذا | أين |
|---|---|
| JS | `supplier-login.js` ← `startIdentityStep('fingerprint')` ← `runFingerprintLogin` ← `signInWithBrowserPasskey` ← `biometric.js: signInWithPasskey` |
| Breakpoint 1 | `BiometricsController.PasskeyLoginOptions` ← راقب `stepUser` (الحساب من خطوة الهوية) |
| Breakpoint 2 | `BiometricsController.PasskeyLogin` ← `BiometricResult result = await passkeys.VerifyLoginAsync(...)` |
| Breakpoint 3 | السطر `if (result.Succeeded && result.User != null && stepUserId != null && result.User.Id != stepUserId)` ← اشرح: "Passkey لحساب آخر يُرفض" |
| الجداول | `AspNetUserPasskeys` (عدّاد التوقيع UPDATE)، `AuditLogs` |
| الرد / الصفحة | `{ redirect: "/Supplier" }` ← مساحة المورّد |

---

## العرض 8: QR الهاتف

يحتاج عنواناً عاماً للموقع (مثلاً ngrok في `Biometric:PublicOrigin` في User Secrets). بدونه تظهر: "Your phone cannot reach this address...". **هذه أيضاً حالة يمكن عرضها.**

| ماذا | أين |
|---|---|
| الكمبيوتر (JS) | `supplier-login.js` ← `startPhoneSignIn` |
| Breakpoint (C#) | `Controllers/PhonePasskeyController.cs` ← `SignInStart` ← راقب `code` (الرقم من رقمين) و`url` (فيه `#t=`) |
| Network | طلب `/Biometrics/Phone/Status` كل ثانيتين (`askPhoneLinkStatus` في `phone-passkey.js`) |
| الهاتف | يفتح `/Biometrics/Phone#t=...` ← يكتب الرقم ← البصمة |
| Breakpoint | `SignInApprove` (موافقة الهاتف) ثم `SignInComplete` (الكمبيوتر يأخذ الجلسة) |
| راقب في `SignInComplete` | `link.Status`, `stepUserId == link.UserId`, `user.SecurityStamp == link.SecurityStamp` |

---

## العرض 9: نسيت كلمة المرور

| ماذا | أين |
|---|---|
| Breakpoint | `ForgotPassword.cshtml.cs` ← `string code = await users.GeneratePasswordResetTokenAsync(user);` |
| البريد | الرابط في `http://127.0.0.1:8025` (صالح ساعة) |
| Breakpoint | `ResetPassword.cshtml.cs` ← `IdentityResult result = await users.ResetPasswordAsync(...)` |
| الجداول | `AspNetUsers` (PasswordHash و SecurityStamp جديدان)، `AuditLogs` (`PasswordReset`) |
| اشرح | "الختم الجديد يُنهي كل الجلسات القديمة: `ValidateCookieAsync` يفحص الختم مع كل طلب." |

---

## العرض 10: البوابات (Gates)

| ماذا | أين |
|---|---|
| الملف | `Services/IdentityAccess.cs` |
| Breakpoint | `SupplierApprovalGate.InvokeAsync` ← السطر `bool approved = await db.Users...` |
| جرّب | مورّد Pending يفتح `/Supplier` ← `approved = false` ← Redirect إلى `PendingApproval` |
| Breakpoint | `ValidateCookieAsync` ← السطر `bool allowed = false;` |
| جرّب | من SQL Server Object Explorer غيّر `Suppliers.Status` إلى `Suspended` ← حدّث الصفحة ← `allowed = false` ← المستخدم خرج فوراً |

---

## قاموس الصيغ المستخدمة (Glossary)

### C#
| الصيغة | المعنى | مثال من الكود |
|---|---|---|
| `public class X : PageModel` | كلاس يرث من كلاس آخر | `public class LoginModel : PageModel` |
| Constructor + `private readonly` | Dependency Injection: ASP.NET يعطينا الخدمات عند إنشاء الكلاس، ونحفظها في حقول لا تتغير | `public LoginModel(UserManager<ApplicationUser> users, ...) { this.users = users; }` |
| `this.users = users;` | `this.users` هو الحقل، و`users` هو المعامل | في كل Constructor |
| `[BindProperty]` | Model Binding: املأ هذه الخاصية من الحقول المرسلة | `public InputModel Input { get; set; }` |
| `[Required]`, `[EmailAddress]`, `[StringLength(100, MinimumLength = 8)]`, `[Compare(nameof(Password))]` | قواعد تحقق تُفحص تلقائياً، والنتيجة في `ModelState.IsValid` | `Register.cshtml.cs` |
| `[FromBody]` | حوّل JSON الطلب إلى كائن | `FaceLogin([FromBody] FaceSubmission submission)` |
| `[HttpPost, ValidateAntiForgeryToken]` | هذه الدالة لـ POST فقط ويجب أن تحمل رمز الحماية | كل Controllers |
| `[Authorize]` / `[AllowAnonymous]` | يجب أن يكون مسجّلاً / مسموح للجميع | `BiometricsController` |
| `OnGet`, `OnPostAsync`, `OnPostCredentialsAsync` | Razor Pages: الاسم يحدد متى تعمل الدالة. `Credentials` = `?handler=Credentials` | `Login.cshtml.cs` |
| `async Task<IActionResult>` + `await` | دالة غير متزامنة: تنتظر قاعدة البيانات أو البريد دون حجز السيرفر | كل الدوال |
| `string?` | النص قد يكون `null` | `string? userId = preAuth.UserId(HttpContext);` |
| `??` | إن كان null خذ القيمة الثانية | `string entityId = userId ?? "unknown";` |
| `user!` | "أنا متأكد أنه ليس null" (للمترجم فقط) | `string url = links.PhoneUrl(Request, token)!;` |
| `const` | ثابت لا يتغير | `private const string Rejected = "...";` |
| `List<T>`, `foreach` | قائمة والمرور عليها | `foreach (FaceTemplate row in rows)` |
| `out` | الدالة ترجع قيمة إضافية عبر معامل | `Base64Url.TryDecode(text, out bytes)` |
| `params string[] from` | عدد متغير من المعاملات | `links.TryMove(link, PhoneLinkStatus.Cancelled, PhoneLinkStatus.Waiting, PhoneLinkStatus.Opened)` |
| `lock (lockObject) { }` | قطعة كود لا يدخلها طلبان في نفس اللحظة | `ChallengeStore.Consume` |
| `try / catch / finally` | جرّب؛ عند الخطأ افعل كذا؛ وفي كل الأحوال افعل كذا | `Register.cshtml.cs` |
| `await using var transaction = ...` | Transaction تُغلق تلقائياً في نهاية الدالة | `Register`, `VerifyEmail` |
| `return Page();` / `RedirectToPage(...)` / `LocalRedirect(...)` / `Json(...)` / `Unauthorized(...)` / `StatusCode(429, ...)` | أنواع الرد | في كل الملفات |
| `new { error = "..." }` | كائن بدون اسم كلاس، يتحول إلى JSON | `Unauthorized(new { error = GenericFailure })` |
| `$"... {x} ..."` | نص مع قيم بداخله | `AuditAsync` (ويتحول كل `{x}` إلى SQL Parameter) |
| `db.FaceTemplates.Where(t => t.UserId == userId)` | استعلام EF Core. `t => ...` معناها "لكل صف t، الشرط هو..." ويتحول إلى SQL `WHERE` | `FaceService` |
| `AnyAsync`, `CountAsync`, `SingleOrDefaultAsync`, `ToListAsync`, `ExecuteDeleteAsync` | استعلامات EF Core: هل يوجد؟ كم؟ صف واحد؟ قائمة؟ حذف مباشر | الخدمات |
| `SaveChangesAsync()` | نفّذ الـ INSERT/UPDATE المعلّقة | في كل مكان يحفظ |

### JavaScript
| الصيغة | المعنى | مثال |
|---|---|---|
| `var x = ...;` | متغير | `var loginBusy = false;` |
| `function name() { }` | دالة عادية | `function chosenMethod() { ... }` |
| `async function` + `await` | دالة تنتظر طلباً أو جهازاً دون تجميد الصفحة | `async function runFaceLogin()` |
| `document.getElementById('id')` | جد عنصر HTML بالـ id | `document.getElementById('bio-status')` |
| `element.addEventListener('click', name)` | عند الضغط شغّل الدالة | `addEventListener('click', enrollFace)` |
| `function (event) { ... }` | دالة بدون اسم تُعطى لـ addEventListener | تغيير نوع الحساب |
| `event.preventDefault()` | امنع التصرف الافتراضي (إرسال النموذج) | `onLoginSubmit` |
| `fetch(url, { method: 'POST', body: ... })` | أرسل طلباً للسيرفر | `postJson`, `postLoginHandler` |
| `JSON.stringify(x)` / `response.json()` | كائن ← نص JSON / نص JSON ← كائن | `postJson` |
| `new FormData(form)` | كل حقول النموذج (والملفات) كما في الإرسال العادي | `onRegistrationSubmit` |
| `try { } catch (error) { } finally { }` | معالجة الأخطاء | كل الدوال التي تطلب شيئاً |
| `throw new Error('...')` | أوقف وأرسل خطأ إلى `catch` | `startCamera` |
| `element.hidden = true` | أخفِ العنصر | `goToRegistrationStep` |
| `classList.add / remove / toggle` | أضف أو أزل CSS class | `showBiometricStatus` |
| `textContent` | النص داخل العنصر (آمن: لا ينفّذ HTML) | كل الرسائل |
| `setTimeout(fn, ms)` / `setInterval(fn, ms)` | نفّذ بعد مدة / كل مدة | العدّ التنازلي و`askPhoneLinkStatus` |
| `localStorage.setItem / getItem` | تذكّر قيمة في المتصفح (تفضيلات فقط) | `remember`, `restoreRemembered` |
| `new CustomEvent(name, { detail })` + `dispatchEvent` | أرسل رسالة للرسوم المتحركة | `tellIllustration` |
| `/^[^\s@]+@[^\s@]+\.[^\s@]{2,}$/` | Regular Expression لصيغة البريد | `EMAIL_PATTERN` |
| `navigator.credentials.create / get` | **من المتصفح**: WebAuthn (إنشاء Passkey / التوقيع به) | `createPasskey`, `getPasskey` |
| `navigator.mediaDevices.getUserMedia` | **من المتصفح**: فتح الكاميرا (يطلب الإذن) | `startCamera` |
| `faceapi.detectAllFaces(...)` | **من مكتبة face-api.js**: إيجاد الوجوه وحساب الـ descriptor | `captureFaceSamples` |

---

## ماذا تقول إن سُئلت...

- **"أين تُحفظ كلمة المرور؟"** لا تُحفظ. يُحفظ **Hash** فقط في `AspNetUsers.PasswordHash` بواسطة Identity (`CreateAsync`، `CheckPasswordSignInAsync`).
- **"أين تُحفظ البصمة؟"** لا تُحفظ أبداً. الجهاز يتحقق منها، ونحن نحفظ **المفتاح العام** في `AspNetUserPasskeys`.
- **"أين يُحفظ الوجه؟"** صور لا. 8 عينات × 128 رقماً، **مشفّرة** بـ Data Protection في `FaceTemplates`.
- **"ماذا لو سرق أحد طلب الوجه وأعاده؟"** الرمز (Challenge) يُستخدم مرة واحدة (`ChallengeStore.Consume`) ويعيش 90 ثانية، والعينات المتطابقة تماماً تُرفض.
- **"هل يمكن خداع Face ID بصورة؟"** نعم، لا يوجد Liveness Detection، وهذا مكتوب في الصفحة وفي الكود (حد معروف لمشروع أكاديمي).
- **"هل تعرفون أن المستخدم استخدم بصمة وليس PIN؟"** لا. WebAuthn يخبرنا فقط أن الجهاز **تحقق من الشخص** (User Verification)، والجهاز هو من يقرر الطريقة.
- **"لماذا fetch وليس نموذجاً عادياً في صفحة الدخول؟"** لأن Face ID والبصمة يحتاجان البقاء في نفس الصفحة (الكاميرا، نافذة Windows Hello، QR)، ولكي لا تُفقد الملفات في التسجيل. بدون JavaScript يعمل الدخول بكلمة المرور كنموذج عادي (`OnPostAsync`).
