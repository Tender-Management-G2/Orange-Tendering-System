# دليل المصادقة للمبتدئين (Authentication Beginner Guide)

هذا الدليل يشرح **كيف يعمل التسجيل وتسجيل الدخول** في نظام Orange Sourcing & Tender Management، خطوة بخطوة، من أول ضغطة زر حتى قاعدة البيانات ثم الصفحة التالية.

- كل المسارات في هذا الدليل تبدأ من مجلد مشروع الويب: `Orange-Tendering-System/` (مثلاً `Controllers/BiometricsController.cs` تعني `Orange-Tendering-System/Controllers/BiometricsController.cs`).
- كل الكود المقتبس هنا منسوخ من الكود النهائي بعد التبسيط (2026-11-25).
- أسماء الدوال مكتوبة كما هي في الكود حتى تستطيع البحث عنها بـ `Ctrl+F` في Visual Studio.

---

## 0. الصورة الكبيرة

### الطبقات
| الطبقة | الملفات | دورها |
|---|---|---|
| الصفحة (Razor View) | `Areas/Identity/Pages/Account/*.cshtml`, `Views/Biometrics/*.cshtml`, `Views/PhonePasskey/Index.cshtml` | HTML: الحقول والأزرار |
| JavaScript في المتصفح | `wwwroot/js/supplier-registration.js`, `supplier-login.js`, `biometric.js`, `phone-passkey.js`, `biometric-demo.js` | يتحقق من الحقول، يفتح الكاميرا، يكلّم جهاز البصمة (WebAuthn)، يرسل الطلبات بـ `fetch()` |
| PageModel / Controller | `Areas/Identity/Pages/Account/*.cshtml.cs`, `Controllers/BiometricsController.cs`, `Controllers/PhonePasskeyController.cs` | يستقبل الطلب (Model Binding)، يقرر، ويرجع صفحة أو JSON أو Redirect |
| الخدمات (Services) | `Services/Biometric/*.cs`, `Services/IdentityAccess.cs` | القواعد المشتركة: من يسمح له بالدخول، مقارنة الوجه، التحديات (Challenge) |
| ASP.NET Core Identity | `UserManager`, `SignInManager` (جاهزة من Microsoft) | تشفير كلمة المرور (Hash)، الـ Cookie، القفل (Lockout)، رموز OTP واستعادة كلمة المرور، تخزين الـ Passkeys |
| SQL Server | الجداول أدناه | حفظ الحسابات والشركات والوجوه والمفاتيح والسجل |

### الجداول المستخدمة
| الجدول | ماذا يحفظ |
|---|---|
| `AspNetUsers` | الحساب: البريد، **Hash** كلمة المرور (وليس كلمة المرور نفسها)، `EmailConfirmed`, `IsActive`, `SupplierId`, `MustChangePassword`, `EmailOtpSentAt`, `EmailOtpFailedAttempts`, `AccessFailedCount`, `LockoutEnd`, `SecurityStamp` |
| `AspNetRoles`, `AspNetUserRoles` | الأدوار (Supplier, Admin, ...) |
| `Suppliers` | الشركة وحالتها: `Pending`, `Approved`, `Rejected`, `Suspended` |
| `Documents` | معلومات الملفات المرفوعة (الملف نفسه في مجلد خاص على السيرفر) |
| `FaceTemplates` | عينات الوجه **مشفّرة** (128 رقماً لكل عينة، مشفرة بـ Data Protection) |
| `AspNetUserPasskeys` | المفتاح العام (Public Key) لكل بصمة/جهاز. البصمة نفسها لا تصل أبداً |
| `AuditLogs` | سجل كل عملية: تسجيل، دخول، فشل، تسجيل وجه... |
| `Notifications` | إشعار للمدير عند وصول طلب مورّد جديد |

### ما يُحفظ في الذاكرة فقط (ليس في قاعدة البيانات)
`ChallengeStore` (التحديات لمرة واحدة)، `BiometricThrottle` (عداد المحاولات الفاشلة)، `SupplierPreAuth` (خطوة الهوية لعدة دقائق)، `PhoneLinkStore` (رموز QR). إعادة تشغيل السيرفر تمسحها كلها، وهذا مقصود: كلها قصيرة العمر.

### الحماية التي تعمل في كل طلب
1. `ValidateAntiForgeryToken`: كل POST يجب أن يحمل الرمز المخفي `__RequestVerificationToken` (حماية من CSRF). صفحات Razor تضيفه تلقائياً، و`postJson()` في `biometric.js` ترسله في الـ header باسم `RequestVerificationToken`.
2. `IdentityAccess.ValidateCookieAsync` (في `Services/IdentityAccess.cs`): مع كل طلب يحمل Cookie يتأكد أن الحساب ما زال فعّالاً وأن الشركة ما زالت Pending/Approved. إن تغيّر شيء يُخرج المستخدم فوراً.
3. `FirstLoginGate`: من عليه `MustChangePassword` يُرسل إلى صفحة تغيير كلمة المرور.
4. `SupplierApprovalGate`: المورّد لا يدخل `/Supplier` إلا إذا كانت شركته Approved وطريقة الدخول مقبولة.

الترتيب في `Program.cs`:
```csharp
app.UseAuthentication();
app.UseMiddleware<FirstLoginGate>();
app.UseMiddleware<SupplierApprovalGate>();
app.UseAuthorization();
```

---

## 1. تسجيل المورّد (Supplier Registration)

**الصفحة:** `/Identity/Account/Register`

### STEP 1 — فتح الصفحة
- **File:** `Areas/Identity/Pages/Account/Register.cshtml`
- **Function:** لا يوجد `OnGet` في `RegisterModel`، فيعرض Razor الصفحة مباشرة.
- **Trigger:** المستخدم يفتح الرابط "Register as a supplier".
- **Important code:**
```cshtml
<form id="registerForm" method="post" enctype="multipart/form-data" novalidate data-fv-skip>
    <fieldset class="as-step" data-step="0"> ... </fieldset>
    <fieldset class="as-step" data-step="1"> ... </fieldset>
    <fieldset class="as-step" data-step="2"> ... </fieldset>
    <fieldset class="as-step" data-step="3"> ... </fieldset>
```
- **Syntax:** `asp-for="Input.Email"` يولّد `name="Input.Email"` و`id="Input_Email"`، وهذا الاسم هو ما يربطه **Model Binding** بالخاصية `Input.Email` في الـ PageModel. `enctype="multipart/form-data"` ضروري لرفع الملفات.
- **Input:** لا شيء.
- **Processing:** النموذج واحد فيه أربع مراحل (`fieldset`). كل المراحل موجودة في HTML، والـ JavaScript يُظهر واحدة فقط.
- **Database:** لا شيء.
- **Output:** صفحة HTML.
- **Next file:** `wwwroot/js/supplier-registration.js` → `setUpRegistrationPage()`.

### STEP 2 — تجهيز المعالج (Wizard)
- **File:** `wwwroot/js/supplier-registration.js`
- **Function:** `setUpRegistrationPage()` ثم `goToRegistrationStep(0, false)`
- **Trigger:** آخر سطر في الملف يستدعيها فوراً (الملف في نهاية `<body>` فالنموذج موجود).
- **Important code:**
```javascript
function goToRegistrationStep(index, moveFocus) {
    var steps = registrationSteps();
    var lastStep = steps.length - 1;
    currentStep = index;
    document.getElementById('authStudio').dataset.step = String(index);

    for (var i = 0; i < steps.length; i++) {
        steps[i].hidden = i !== index;
    }
```
- **Syntax:** `var` يعرّف متغيراً. `for` يمر على المراحل. `steps[i].hidden = ...` يخفي كل مرحلة رقمها ليس `index`. `!==` تعني "لا يساوي".
- **Input:** رقم المرحلة.
- **Processing:** يُظهر مرحلة واحدة، يحدّث شريط التقدم، ويظهر/يخفي أزرار Back / Continue / Send.
- **Database:** لا شيء.
- **Output:** تغيير في الصفحة فقط.
- **Next:** عند الضغط على Continue → `nextRegistrationStep()`.

### STEP 3 — التحقق من المرحلة في المتصفح
- **File:** `wwwroot/js/supplier-registration.js`
- **Function:** `nextRegistrationStep()` → `validateRegistrationStep(currentStep)` → `checkRegistrationField(name)` → `fieldProblem(name)`
- **Trigger:** زر Continue.
- **Important code:**
```javascript
function nextRegistrationStep() {
    if (validateRegistrationStep(currentStep)) {
        goToRegistrationStep(currentStep + 1, true);
    }
}
```
```javascript
if (name === 'Input.ConfirmPassword') {
    if (!input.value) {
        return 'Enter the password again to confirm it.';
    }
    if (input.value !== rawValue('Input.Password')) {
        return 'The two passwords do not match.';
    }
    return '';
}
```
- **Syntax:** الدالة ترجع نص المشكلة، أو `''` (نص فارغ) إذا كان الحقل سليماً.
- **Processing:** نفس قواعد السيرفر: الحقول المطلوبة، صيغة البريد، قواعد كلمة المرور الخمس (`missingPasswordRules`)، تطابق التأكيد، الهاتف 10 أرقام، الملفات: PDF/PNG/JPEG، حتى 10 MB، ليست فارغة، وحد أقصى 7 ملفات (`fileProblem`).
- **مهم:** هذا التحقق **للراحة فقط**. السيرفر يتحقق من كل شيء مرة أخرى (STEP 6)، لأن أي شخص يستطيع تجاوز JavaScript.
- **Database:** لا شيء.
- **Output:** رسائل حمراء تحت الحقول، أو الانتقال للمرحلة التالية.

### STEP 4 — مرحلة المراجعة
- **Function:** `updateReview()`: تملأ القائمة `#registrationReview` بالقيم المكتوبة (يُستخدم `textContent` وليس `innerHTML`، فلا يمكن حقن HTML).

### STEP 5 — الإرسال بـ fetch
- **File:** `wwwroot/js/supplier-registration.js`
- **Function:** `onRegistrationSubmit(event)`
- **Trigger:** زر "Send verification code".
- **Important code:**
```javascript
var response = await fetch(form.action || location.href, { method: 'POST', body: new FormData(form), credentials: 'same-origin' });

// Success: the server redirected to VerifyEmail (it created the application and sent the code).
if (response.redirected && new URL(response.url).origin === location.origin) {
    setRegistrationSubmitState('accepted');
    location.assign(response.url);
    return;
}
```
- **Syntax:** `async` / `await`: ننتظر رد السيرفر دون تجميد الصفحة. `new FormData(form)` يجمع **كل** الحقول والملفات ورمز Anti-Forgery، تماماً مثل الإرسال العادي.
- **لماذا fetch وليس إرسالاً عادياً؟** إذا رفض السيرفر (مثلاً البريد مستخدم) فإن الإرسال العادي يعيد تحميل الصفحة، فيفقد المتصفح **الملفات المختارة** وتبدأ الرسوم المتحركة من جديد. مع fetch تبقى الملفات، ويَنسخ `showServerValidation(html)` رسائل السيرفر إلى الصفحة الحالية.
- **Next file:** `Areas/Identity/Pages/Account/Register.cshtml.cs` → `OnPostAsync()`.

### STEP 6 — السيرفر يستقبل ويتحقق
- **File:** `Areas/Identity/Pages/Account/Register.cshtml.cs`
- **Function:** `OnPostAsync()`
- **Trigger:** طلب POST من STEP 5.
- **Important code:**
```csharp
[BindProperty]
public InputModel Input { get; set; } = new InputModel();

[BindProperty]
public IFormFile? CommercialRegistrationCertificate { get; set; }
```
```csharp
if (CommercialRegistrationCertificate == null)
{
    ModelState.AddModelError(nameof(CommercialRegistrationCertificate), "Commercial registration certificate is required.");
}
...
if (!ModelState.IsValid)
{
    return Page();
}
```
- **Syntax:** `[BindProperty]` يطلب من **Model Binding** ملء الخاصية من الحقول المرسلة. السمات مثل `[Required]` و`[EmailAddress]` و`[Compare(nameof(Password))]` في `InputModel` تُفحص تلقائياً قبل دخول الدالة، والنتيجة في `ModelState.IsValid`.
- **Processing (بالترتيب):**
  1. إذا كان البريد لمورّد سجّل سابقاً ولم يؤكّد بريده، وكلمة المرور صحيحة، يُحوّل إلى `VerifyEmail` (لا حاجة لرفع الملفات مرة أخرى).
  2. فحص التصنيف، الملفات المطلوبة، شهادة الضريبة إن وُجد رقم ضريبي، العدد ≤ 7، و`SupplierDocumentStorage.IsAllowed(file)` (النوع الحقيقي للملف والحجم).
  3. لا تكرار للبريد ولا لرقم السجل التجاري.
- **Database:** قراءة `AspNetUsers` و`Suppliers`.
- **Output عند الخطأ:** `return Page();` (الصفحة نفسها مع الرسائل؛ STEP 5 ينسخها).

### STEP 7 — الحفظ داخل Transaction
- **File:** `Areas/Identity/Pages/Account/Register.cshtml.cs`
- **Function:** `OnPostAsync()` (تكملة) + `SaveDocumentAsync(...)`
- **Important code:**
```csharp
await using var transaction = await db.Database.BeginTransactionAsync();
try
{
    Models.Entities.Supplier supplier = new Models.Entities.Supplier();
    ...
    supplier.Status = SupplierStatus.Pending;
    db.Suppliers.Add(supplier);
    await db.SaveChangesAsync();   // INSERT Suppliers (gives supplier.Id)

    ApplicationUser user = new ApplicationUser();
    ...
    user.IsActive = false;
    // CreateAsync checks the password rules and stores only a hash of the password (INSERT AspNetUsers).
    IdentityResult created = await users.CreateAsync(user, Input.Password);
```
```csharp
    // Only now, after the email was sent, is everything saved for real.
    await transaction.CommitAsync();
    committed = true;
```
- **Syntax:** `try / catch / finally`: إذا حدث خطأ متوقع (قاعدة بيانات، ملف، بريد) نصل إلى `catch` فنستدعي `RollbackAsync()`، فلا يبقى أي شيء في قاعدة البيانات. وفي `finally`، إذا لم يتم `Commit`، تُحذف الملفات من القرص أيضاً.
- **لماذا `Models.Entities.Supplier` بالاسم الكامل؟** داخل مجلد `Areas` الاسم القصير `Supplier` يعني الـ namespace الخاص بمنطقة المورّد، وليس الكلاس.
- **Database (INSERT):** `Suppliers` (Pending)، `AspNetUsers` (`IsActive = false`, `EmailConfirmed = false`، Hash كلمة المرور)، `AspNetUserRoles` (Supplier)، `Documents`، `AuditLogs` (`SupplierRegistrationStarted`).
- **ثم:**
```csharp
string code = await users.GenerateUserTokenAsync(user, TokenOptions.DefaultEmailProvider, "SupplierEmailOtp");
await emailSender.SendEmailAsync(email, "Verify your supplier email", ...);
```
  `GenerateUserTokenAsync` تولّد **رمزاً من 6 أرقام** مرتبطاً بالـ `SecurityStamp` الحالي للحساب.
- **Output:** `return RedirectToPage("./VerifyEmail", new { email = email });`
- **Next:** المتصفح يفتح `VerifyEmail` (القسم 2).

### فروع الفشل
| الحالة | أين | النتيجة |
|---|---|---|
| حقل فارغ / صيغة خطأ | JS `fieldProblem` ثم السيرفر `ModelState` | رسالة تحت الحقل |
| بريد أو سجل تجاري موجود | `OnPostAsync` | "An account or company with these details already exists..." |
| كلمة مرور ضعيفة | `users.CreateAsync` | رسائل Identity + Rollback |
| فشل البريد | `catch` → `RegistrationErrorMessage(exception)` | Rollback + حذف الملفات + رسالة |
| انقطاع الشبكة | JS `catch` | "Could not contact the registration service..." |

---

## 2. التحقق من البريد برمز OTP

**الصفحة:** `/Identity/Account/VerifyEmail?email=...`

### STEP 1 — عرض الصفحة
- **File:** `Areas/Identity/Pages/Account/VerifyEmail.cshtml.cs`
- **Function:** `OnGet(string? email)`
- **Important code:**
```csharp
public void OnGet(string? email)
{
    Input.Email = email ?? string.Empty;
}
```
- **Syntax:** `??` تعني: "إن كانت القيمة null خذ القيمة التي بعدها".
- **Output:** نموذج فيه البريد ورمز من 6 أرقام. هذا النموذج **Razor form عادي** بدون JavaScript خاص.

### STEP 2 — إرسال الرمز
- **Function:** `OnPostAsync()`
- **Trigger:** زر "Verify email" (POST عادي).
- **Important code:**
```csharp
bool expired = DateTime.UtcNow - user.EmailOtpSentAt.Value > TimeSpan.FromMinutes(10);
bool blocked = user.EmailOtpFailedAttempts >= 5;
if (expired || blocked)
{
    ModelState.AddModelError(string.Empty, InvalidCode);
    return Page();
}

// 2. Identity checks the code. A wrong code is counted (UPDATE AspNetUsers).
bool codeIsCorrect = await users.VerifyUserTokenAsync(user, TokenOptions.DefaultEmailProvider, Purpose, Input.Code);
if (!codeIsCorrect)
{
    user.EmailOtpFailedAttempts++;
    await users.UpdateAsync(user);
```
- **Processing:**
  1. `FindPendingSupplierAsync()`: حساب مورّد لم يؤكّد بريده بعد.
  2. انتهاء الصلاحية بعد **10 دقائق**، والحظر بعد **5 محاولات خاطئة**.
  3. Identity يتحقق من الرمز.
- **Database:** قراءة `AspNetUsers`. عند الخطأ: UPDATE `EmailOtpFailedAttempts`.
- **Output عند الخطأ:** "The code is invalid or expired. Request a new code if needed." (رسالة واحدة لكل الحالات).

### STEP 3 — النجاح: تفعيل الحساب
- **Important code:**
```csharp
await using var transaction = await db.Database.BeginTransactionAsync();
user.EmailConfirmed = true;
user.IsActive = true;
user.EmailOtpSentAt = null;
user.EmailOtpFailedAttempts = 0;
...
// A new security stamp makes the used code invalid, so it cannot be used a second time.
await users.UpdateSecurityStampAsync(user);
```
- **Database:** UPDATE `AspNetUsers`؛ INSERT `Notifications` (لكل Admin)؛ INSERT `AuditLogs` (`SupplierRegistered`, `EmailVerified`).
- **ثم:** `await signInManager.SignInAsync(user, false);` يُنشئ **Cookie** الدخول (غير دائم).
- **Output:** `RedirectToPage("./PendingApproval")`.

### STEP 4 — إعادة إرسال الرمز
- **Function:** `OnPostResendAsync(string email)` (الرابط `?handler=Resend`).
- **قواعد:** دقيقة واحدة على الأقل بين رمزين، و10 دقائق بعد 5 أخطاء. الرد **نفسه دائماً** ("If a pending registration exists...") حتى لا تكشف الصفحة أي البريدات مسجّلة.
- **Processing:** `UpdateSecurityStampAsync` (يُبطل الرمز القديم) ثم رمز جديد، ثم إرسال البريد، ثم `Commit`. إن فشل البريد يتم `Rollback`، فيبقى الرمز القديم صالحاً.
- **Output:** Redirect إلى الصفحة نفسها (نمط Post/Redirect/Get).

---

## 3. موافقة المدير على المورّد (Approval / Rejection / Suspension)

### STEP 1 — صفحة الانتظار
- **File:** `Areas/Identity/Pages/Account/PendingApproval.cshtml.cs`
- **Function:** `OnGetAsync(bool faceRegistered = false)`
- **Important code:**
```csharp
string? method = User.FindFirstValue(BiometricSignInService.AuthenticationMethodClaim);
if (CompanyApproved && BiometricSignInService.IsKnownMethod(method))
{
    return LocalRedirect("/Supplier");
}

// Rejected and Suspended companies cannot use this page.
if (status == SupplierStatus.Pending || status == SupplierStatus.Approved)
{
    return Page();
}
return Forbid();
```
- **Database:** قراءة `FaceTemplates` (العدد)، `AspNetUserPasskeys` (العدد)، `Suppliers` (الحالة).
- **Output:** صفحة فيها تسجيل الوجه أو البصمة (القسمان 5 و7).

### STEP 2 — قرار المدير (خارج وحدة المصادقة، ولم يُعدَّل)
- **File:** `Areas/Admin/Controllers/SuppliersController.cs`
- **Function:** `SubmitReview(SupplierReviewDecisionViewModel model)` → `Services/AdminService.cs` → `ReviewSupplierAsync(model, adminId)`
- **القواعد:** Pending → Approved أو Rejected؛ Approved → Suspended؛ Suspended/Rejected → Approved. الرفض والإيقاف يحتاجان سبباً. لا موافقة قبل تأكيد البريد ولا بدون الوثائق.
- **Database:** UPDATE `Suppliers.Status`، و`Documents` (Verified)، و`AuditLogs`.

### STEP 3 — أثر القرار على الدخول
| الحالة | Email & Password | Face / Fingerprint | `/Supplier` |
|---|---|---|---|
| Pending | يدخل → `PendingApproval` | يمكنه **التسجيل** فقط (Enrollment) | يُحوّل إلى `PendingApproval` |
| Approved | يدخل → `/Supplier` (مع `SupplierPasswordOnly = true`) | يدخل → `/Supplier` | مسموح |
| Rejected / Suspended | مرفوض ("Invalid email or password") | مرفوض | `ValidateCookieAsync` يُخرجه فوراً حتى لو كان داخلاً |

---

## 4. الدخول بالبريد وكلمة المرور (Email & Password)

### STEP 1 — فتح صفحة الدخول
- **File:** `Areas/Identity/Pages/Account/Login.cshtml.cs`
- **Function:** `OnGetAsync(string? returnUrl)`
- **Important code:**
```csharp
ReturnUrl = LocalOrNull(returnUrl);
BiometricStepSeconds = biometricOptions.PreAuthSeconds;
await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
// Opening the page again starts over: an unfinished Face ID / Fingerprint step is abandoned.
preAuth.End(HttpContext);
```
- **Processing:** `LocalOrNull` يقبل `returnUrl` فقط إذا كان عنواناً داخل الموقع (منع **Open Redirect**).
- **Next:** `wwwroot/js/supplier-login.js` → `setUpLoginPage()` (يُستدعى فوراً في آخر الملف).

### STEP 2 — اختيار الطريقة
- **Function:** `renderLoginMethod()`: تُظهر أو تُخفي حقل كلمة المرور وتغيّر نص الزر حسب الطريقة (`password` / `face` / `fingerprint`). الاختيار يُحفظ في `localStorage` (تفضيل فقط).

### STEP 3 — الضغط على "Sign in"
- **File:** `wwwroot/js/supplier-login.js`
- **Function:** `onLoginSubmit(event)` → `validateLoginFields(true)` → `signInWithPasswordOnPage()`
- **Important code:**
```javascript
function onLoginSubmit(event) {
    event.preventDefault(); // the script sends the form itself
    if (loginBusy) {
        return;
    }
    setLoginMessage('');
    var method = chosenMethod();
    if (!validateLoginFields(method === 'password')) {
        tellIllustration('authstudio:signal', { phase: 'incomplete' });
        return;
    }
    if (method === 'password') {
        signInWithPasswordOnPage();
    } else {
        startIdentityStep(method);
    }
}
```
- **Syntax:** `event.preventDefault()` يمنع المتصفح من إرسال النموذج بالطريقة العادية، لأن السكربت سيرسله بنفسه. `loginBusy` يمنع الضغط مرتين.
- **Next:** `postLoginHandler('Credentials', new FormData(loginForm()), false)` → POST إلى `/Identity/Account/Login?handler=Credentials`.

### STEP 4 — السيرفر: `OnPostCredentialsAsync`
- **File:** `Areas/Identity/Pages/Account/Login.cshtml.cs`
- **Function:** `OnPostCredentialsAsync(string? returnUrl)` → `SignInWithPasswordAsync()`
- **Syntax:** في Razor Pages الاسم `OnPost` + `Credentials` + `Async` يعني: هذه الدالة تعمل عندما يكون `?handler=Credentials`.
- **Important code (SignInWithPasswordAsync):**
```csharp
// 2. Check the password. true = a wrong password counts toward the lockout (5 tries, 15 minutes).
var result = await signInManager.CheckPasswordSignInAsync(user, Input.Password, true);
if (result.IsLockedOut)
{
    ...
    refusalMessage = LockedOutMessage(await users.GetLockoutEndDateAsync(user));
    return "locked";
}
if (!result.Succeeded)
{
    logger.LogWarning("Identity sign-in failed.");
    return "rejected";
}
```
```csharp
// 4. The "Supplier / Other User" choice must match the real role. Nobody is signed in on a mismatch.
string chosen = (Input.AccountType ?? string.Empty).Trim().ToLowerInvariant();
if (chosen == "supplier" && !isSupplier)
{
    ...
    return "mismatch";
}
```
- **Processing (بالترتيب):** إيجاد الحساب ← فحص كلمة المرور (Identity يقارن الـ **Hash**) ← فحص الحالة (فعّال، مؤكَّد، شركة Pending/Approved) ← تطابق نوع الحساب ← تسجيل الدخول.
- **النتيجة نص واحد من أربعة:** `"signedIn"`, `"rejected"`, `"locked"`, `"mismatch"`، وهي نفس الكلمات التي يرسلها الـ JSON إلى المتصفح.
- **Database:** قراءة `AspNetUsers`, `AspNetUserRoles`, `Suppliers`. عند الخطأ: UPDATE `AccessFailedCount` (وبعد 5 أخطاء `LockoutEnd`). عند النجاح: INSERT `AuditLogs` (`SignIn`).

### STEP 5 — إصدار الـ Cookie
- **مورّد معتمد:** `biometric.SignInAsync(user, BiometricSignInService.PasswordStepMethod, ip)`: Cookie فيه Claim اسمه `orange:biometric-auth-method` = `supplier-password-step`. هذا الـ Claim هو ما يقرأه `SupplierApprovalGate`.
- **الباقون:**
```csharp
// 5b. Everyone else: the normal Identity cookie (persistent when "Remember me" is ticked).
await signInManager.SignInAsync(user, Input.RememberMe);
```
- **الوجهة:** `MustChangePassword` ← `FirstLoginPasswordChange`؛ مورّد (غير معتمد) ← `PendingApproval`؛ `ReturnUrl` محلي ← هو؛ وإلا ← `WorkspaceUrlAsync(user)` (مثل `/Admin`).

### STEP 6 — الرد إلى المتصفح
```csharp
if (outcome == "signedIn")
{
    return new JsonResult(new { outcome = "signedIn", redirect = redirectUrl });
}
if (outcome == "locked")
{
    return new JsonResult(new { outcome = "locked", error = refusalMessage }) { StatusCode = StatusCodes.Status423Locked };
}
```
- **Next:** `signInWithPasswordOnPage()` يقرأ الرد: عند النجاح `goToAfterSignIn(data.redirect)` (في `biometric.js`)، وعند الفشل `setLoginMessage(...)`.

### بدون JavaScript
النموذج يُرسل بشكل عادي إلى `OnPostAsync()`، الذي يستدعي **نفس** `SignInWithPasswordAsync()` ثم يرجع `LocalRedirect(...)` أو الصفحة مع الرسالة.

### فروع الفشل
| الحالة | الرد | الرسالة |
|---|---|---|
| بريد غير موجود / كلمة مرور خاطئة / حساب غير فعّال | 401 `rejected` | "Invalid email or password. Please try again." (نفس الرسالة دائماً) |
| 5 أخطاء | 423 `locked` | "This account is temporarily locked ... Try again in N minutes." |
| نوع حساب خطأ | 409 `mismatch` | "This is a supplier account. Choose Supplier above..." |
| رمز Anti-Forgery قديم | 400 بدون JSON | `renewAntiForgeryToken()` يجلب رمزاً جديداً ويعيد المحاولة مرة واحدة |
| السيرفر لا يرد خلال 20 ثانية | — | "The server took too long to answer..." |

---

## 5. تسجيل الوجه (Face Enrollment)

**الصفحات:** `PendingApproval` (المورّد)، `EmployeeIdentity` (الموظف)، `/Biometrics/Face` (من قائمة الحساب).

### STEP 1 — زر "Start face enrollment"
- **File:** `wwwroot/js/biometric.js`
- **Function:** `setUpBiometricButtons()` تربط الزر `#btnFaceEnroll` بالدالة `enrollFace()`، وتبدأ تحميل نماذج الوجه مسبقاً (`preloadFaceModels()`).
- **Important code:**
```javascript
async function enrollFace() {
    var password = enrollmentPassword();
    if (!password) {
        showBiometricStatus('Enter your current password to continue.', 'danger');
        return;
    }
    ...
    try {
        await runFaceCheck('/Biometrics/FaceEnrollChallenge', { password: password }, '/Biometrics/FaceEnroll', { replace: replace });
        showBiometricStatus('Face registered successfully.', 'success');
```
- **لماذا كلمة المرور؟** حتى لا يستطيع شخص سرق جلسة مفتوحة أن يضيف وجهه هو إلى الحساب.

### STEP 2 — طلب التحدي (Challenge)
- **File:** `wwwroot/js/biometric.js` → `runFaceCheck(...)` → `postJson('/Biometrics/FaceEnrollChallenge', { password })`
- **Next file:** `Controllers/BiometricsController.cs` → `FaceEnrollChallenge([FromBody] EnrollStartRequest request)`
- **Important code:**
```csharp
ApplicationUser? user = await users.GetUserAsync(User);
if (user == null || !await PasswordConfirmedAsync(user, request.Password))
{
    return Unauthorized(new { error = "The password is not correct." });
}

return Json(faces.CreateChallenge(FaceService.EnrollPurpose, user.Id));
```
- **Syntax:** `[FromBody]` يعني: حوّل JSON الطلب إلى كائن `EnrollStartRequest`.
- **Processing:** `PasswordConfirmedAsync` = `EnrollmentAllowedAsync` (فعّال، مؤكَّد، غير مقفل، شركة Pending/Approved) + `CheckPasswordAsync` (كلمة مرور خاطئة تُحسب في القفل).
- **Service:** `FaceService.CreateChallenge` → `ChallengeStore.Create(purpose, userId, 90)`: رمز عشوائي (24 بايت) صالح **90 ثانية** ولمرة واحدة.
- **Database:** قراءة `AspNetUsers`, `Suppliers`. في الذاكرة: التحدي.
- **Output:** `{ "token": "...", "expiresInSeconds": 90 }`

### STEP 3 — الكاميرا والتقاط الوجه (في المتصفح فقط)
- **File:** `wwwroot/js/biometric.js`
- **Functions:** `startCamera(video)` ← `loadFaceModels()` ← `captureFaceSamples(video, null)`
- **Important code:**
```javascript
if (face !== null && face.detection.box.width >= MIN_FACE_WIDTH) {
    // One usable face: continue (or start) the 7-second hold.
    lastFaceTime = performance.now();
    if (captureStartTime === null) {
        captureStartTime = lastFaceTime;
    }
    var heldFor = lastFaceTime - captureStartTime;

    if (lastFaceTime - lastSampleTime >= SAMPLE_MS || heldFor >= CAPTURE_MS) {
        samples.push({ t: Math.round(heldFor), v: Array.from(face.descriptor) });
        lastSampleTime = lastFaceTime;
    }
```
- **Processing:** المكتبة `face-api.js` (في `wwwroot/lib/face-api`) تجد الوجه وتحوّله إلى **descriptor** من 128 رقماً. نحتاج وجهاً **واحداً** بعرض 110 بكسل على الأقل، ثابتاً **7 ثوانٍ**، مع عينة كل 0.7 ثانية. إذا اختفى الوجه أو ظهر وجه ثانٍ يبدأ العدّ من جديد. إذا لم يُرَ أي وجه خلال 40 ثانية يظهر الخطأ `noFace`.
- **الخصوصية:** **الصورة لا تخرج من الجهاز**. تُرسل الأرقام فقط.
- **Output:** `{ descriptors: [ { t, v[128] }, ... ] }`

### STEP 4 — إرسال العينات
- **Next file:** `Controllers/BiometricsController.cs` → `FaceEnroll([FromBody] FaceSubmission submission)` → `Services/Biometric/FaceService.cs` → `EnrollAsync(user, submission)`
- **Important code (FaceService.EnrollAsync):**
```csharp
// 1. The token must be a live enrollment token issued to this same account.
ChallengeEntry? entry = challenges.Consume(submission.Token, EnrollPurpose);
if (entry == null || entry.UserId != user.Id)
{
    return BiometricResult.Fail("The request expired. Try again.");
}

// 2. The samples must be valid and all of one face.
string? problem = CheckSamples(submission.Descriptors);
```
```csharp
foreach (FaceDescriptorSample sample in samples)
{
    FaceTemplate template = new FaceTemplate();
    template.UserId = user.Id;
    template.ProtectedTemplate = protector.Protect(ToBytes(sample.V));
    template.CreatedAt = now;
    db.FaceTemplates.Add(template);
}
```
- **Processing:**
  1. `Consume` **يحذف** الرمز، فلا يمكن استخدامه مرة ثانية (منع Replay).
  2. `CheckSamples`: من 3 إلى 14 عينة، كل واحدة 128 رقماً حقيقياً (|v| ≤ 3)، مدة ≥ 5 ثوانٍ، أوقات متزايدة، أكبر مسافة بين العينات ≤ 0.55 (وجه واحد)، ولا تكون كل العينات متطابقة تماماً (أرقام مكررة = طلب مزوّر).
  3. `PickSpreadSamples(..., 8)`: 8 عينات موزعة على مدة الالتقاط.
  4. التشفير: `protector.Protect(...)` (ASP.NET Core **Data Protection**).
- **Database:** (إن كان Replace) DELETE `FaceTemplates` القديمة؛ INSERT 8 صفوف `FaceTemplates`. وفي الـ Controller: INSERT `AuditLogs` (`BiometricEnroll` method `face`).
- **Output:** `{ "succeeded": true }` ← الصفحة تنتقل إلى `data-enrolled-url` (مثلاً `PendingApproval?faceRegistered=true`).

### الإلغاء والحذف
- زر "Cancel and close the camera" يجعل `faceCaptureCancelled = true`، فتتوقف حلقة الالتقاط عند الصورة التالية، و`finally` يطفئ الكاميرا (`stopCamera`).
- زر "Remove stored samples" ← `removeFace()` ← `FaceRemove()` ← DELETE `FaceTemplates` + AuditLog.

---

## 6. الدخول بالوجه (Face Login)

### STEP 1 — اختيار Face ID وكتابة البريد
- **File:** `wwwroot/js/supplier-login.js`
- **Function:** `onLoginSubmit` → `startIdentityStep('face')`
- **Important code:**
```javascript
// Only the email and the method are sent: no password.
var formData = new FormData();
formData.append('Input.Email', email.value.trim());
formData.append('method', method);
var answer = await postLoginHandler('IdentityStart', formData, false);
```
- **Next file:** `Login.cshtml.cs` → `OnPostIdentityStartAsync(string? method)`

### STEP 2 — فتح "خطوة الهوية" (SupplierPreAuth)
- **Important code:**
```csharp
ApplicationUser? user = await users.FindByEmailAsync(email);
bool ready = false;
if (user != null && user.EmailConfirmed && await biometric.AccountAllowedAsync(user))
{
    if (face)
    {
        ready = await db.FaceTemplates.AnyAsync(t => t.UserId == user.Id);
    }
    ...
}
...
preAuth.Begin(HttpContext, user);
```
- **Processing:** يقبل الحساب فقط إذا كان مسموحاً له بالدخول **ولديه وجه مسجّل**. وإلا يرجع نفس الرسالة دائماً ("Face ID is not available for this email..."). عدد المحاولات من نفس الـ IP محدود (30 في النافذة الزمنية).
- **Service:** `Services/Biometric/SupplierPreAuth.cs` → `Begin(http, user)`: يحفظ `user.Id` في الذاكرة لمدة **300 ثانية**، ويعطي المتصفح Cookie عشوائية `.Orange.SupplierPreAuth` (HttpOnly, Secure, SameSite=Strict).
- **مهم:** **لا يتم تسجيل الدخول هنا.** الخطوة تقول فقط: "الوجه الذي سيأتي يجب أن يكون لهذا الحساب".
- **Database:** قراءة فقط + INSERT `AuditLogs` (`BiometricStepStarted`).
- **Output:** `{ "outcome": "ready", "method": "face" }`

### STEP 3 — الكاميرا والمقارنة
- **File:** `wwwroot/js/supplier-login.js` → `runFaceLogin()` → `biometric.js` → `runFaceCheck('/Biometrics/FaceChallenge', {}, '/Biometrics/FaceLogin', { returnUrl })`
- **Controller:** `FaceChallenge()` يأخذ الحساب من `preAuth.UserId(HttpContext)` (من الـ Cookie، **لا** من المتصفح)، ثم `FaceLogin(...)`.
- **Important code (BiometricsController.FaceLogin):**
```csharp
// The account is the one of the identity step, never one named by the browser.
ApplicationUser? user = await preAuth.UserAsync(HttpContext, users);
...
BiometricResult result = await faces.VerifyLoginAsync(user, submission);
if (!result.Succeeded || result.User == null)
{
    throttle.Failure(ipKey);
    throttle.Failure(accountKey);
    await signIn.RegisterFailureAsync(user, BiometricSignInService.FaceMethod, result.Error ?? "failed", Ip());
```
- **Service:** `FaceService.VerifyLoginAsync` ← فك تشفير القوالب ← `Match(live, templates)`:
```csharp
match.Matched = bestDistances.Count > 0 && matchingSamples >= requiredSamples && averageDistance <= threshold;
```
  **ثلاث قواعد معاً:** (1) لكل عينة، أقرب مسافة لقالب مسجّل. (2) 60% من العينات على الأقل (وعلى الأقل 3) مسافتها ≤ `FaceMatchThreshold` (0.5). (3) متوسط العينات أيضاً ≤ 0.5. المسافة **إقليدية**: الجذر التربيعي لمجموع مربعات الفروق.
- **Database:** قراءة `FaceTemplates` (وإن كان القالب مشفراً بالمفتاح القديم يُعاد تشفيره UPDATE)، UPDATE `LastUsedAt`. عند الفشل: UPDATE `AccessFailedCount` + INSERT `AuditLogs` (`BiometricSignInFailed`).

### STEP 4 — تسجيل الدخول
- **Service:** `BiometricSignInService.SignInAsync(user, FaceMethod, ip)`:
```csharp
// The claim is stored in the signed cookie. SupplierApprovalGate reads it to know how the supplier signed in.
List<Claim> extraClaims = new List<Claim>();
extraClaims.Add(new Claim(AuthenticationMethodClaim, method));
await signInManager.SignInWithClaimsAsync(user, false, extraClaims);
```
  يعيد فحص قواعد الحساب (`AccountAllowedAsync`: شركة **Approved**)، يصفّر عداد الأخطاء، ويكتب AuditLog `BiometricSignIn`.
- **Output:** `{ "succeeded": true, "redirect": "/Supplier" }` ← `goToAfterSignIn(redirect)`.

### فروع الفشل
| الحالة | الرد |
|---|---|
| لا وجه / وجهان / بعيد | تلميح يتغير ويُعاد العدّ، وبعد 40 ثانية "No face detected..." |
| الوجه لا يطابق | 401 "Face verification failed." + تلميح "Press Continue with Face ID to try again." |
| انتهت خطوة الهوية (5 دقائق) أو الحساب قُفل | 401 `restart: true` ← `backToMethodChoice(...)` |
| الوجه مطابق والشركة ما زالت Pending | "Face recognised, but your company is still awaiting administrator approval." |
| القوالب لا يمكن فك تشفيرها | "Your saved face record could not be opened on this server..." |
| محاولات كثيرة | 429 "Too many attempts..." (`BiometricThrottle`) |

**حدود أمنية حقيقية:** لا يوجد **Liveness Detection**، فصورة أو فيديو للشخص قد ينجح. هذا قرار المالك لمشروع أكاديمي، والصفحة تقوله للمستخدم.

---

## 7. تسجيل البصمة / Passkey (Enrollment)

### STEP 1 — "Use this computer's fingerprint reader"
- **File:** `wwwroot/js/phone-passkey.js` → `setUpPhoneEnrollment()`: يُظهر الزر `#btnPasskeyRegister` فقط إذا قال المتصفح إن للجهاز قارئاً (`hasDeviceAuthenticator()`)، وعلى الهاتف يصبح "Register this phone".
- **File:** `wwwroot/js/biometric.js` → `registerThisDevice()` → `registerPasskey(password, ['client-device'])`
```javascript
async function registerPasskey(password, hints) {
    checkPasskeySupport();
    var options = await postJson('/Biometrics/PasskeyRegisterOptions', { password: password });
    var credentialJson = await createPasskey(options, hints);
    return await postJson('/Biometrics/PasskeyRegister', { credentialJson: credentialJson, label: '' });
}
```

### STEP 2 — الخيارات من السيرفر
- **Controller:** `BiometricsController.PasskeyRegisterOptions` ← `PasswordConfirmedAsync` ← `PasskeyService.CreateRegistrationOptionsAsync(user)` ← `signInManager.MakePasskeyCreationOptionsAsync(...)` (**Identity**).
- **Identity يحفظ التحدي** في Cookie قصيرة العمر محمية (ليست في قاعدة البيانات).
- **الإعدادات** (`BiometricServiceCollectionExtensions.ConfigurePasskeys`): `UserVerificationRequirement = "required"` (الجهاز يجب أن يتحقق من **الشخص**)، و`ResidentKeyRequirement = "required"` (Passkey قابل للاكتشاف).

### STEP 3 — الجهاز ينشئ المفتاح (المتصفح + نظام التشغيل)
- **Function:** `createPasskey(options, hints)` ← `navigator.credentials.create({ publicKey: options })`
- هذه دالة **من المتصفح** وليست من المشروع. Windows Hello / Touch ID / الهاتف يطلب البصمة أو الوجه أو الـ PIN، ثم ينشئ زوج مفاتيح. **المفتاح الخاص لا يغادر الجهاز.** الموقع لا يعرف هل استُخدمت بصمة أم PIN.
- `credentialToJson(credential)` يحوّل الرد إلى JSON (نصوص base64url).

### STEP 4 — الحفظ
- **Controller:** `PasskeyRegister(...)` ← `PasskeyService.RegisterAsync(user, credentialJson, label)`:
```csharp
attestation = await signInManager.PerformPasskeyAttestationAsync(credentialJson);
...
// The options must have been made for this same account.
if (attestation.UserEntity.Id != user.Id)
{
    return BiometricResult.Fail(Expired);
}
...
IdentityResult saved = await users.AddOrUpdatePasskeyAsync(user, attestation.Passkey);
```
- **Identity يتحقق** من: التحدي، الـ Origin، معرّف الموقع (RP ID)، User Verification.
- **Database:** INSERT `AspNetUserPasskeys` (المفتاح **العام**، اسم الجهاز مثل "Windows computer · Edge")؛ INSERT `AuditLogs` (`BiometricEnroll`).
- **الحد الأقصى:** 10 أجهزة لكل حساب.
- **الإزالة:** `removePasskey(button)` ← `PasskeyRemove(id)` ← `PasskeyService.RemoveAsync` (يتأكد أن المفتاح لهذا الحساب).

---

## 8. الدخول بالبصمة / Passkey

### STEP 1 — خطوة الهوية
مثل القسم 6 STEP 1-2 لكن `method = 'fingerprint'`، والشرط أن يكون للحساب Passkey واحد على الأقل.

### STEP 2 — طلب الخيارات والتوقيع
- **File:** `wwwroot/js/supplier-login.js` → `runFingerprintLogin()` → `signInWithBrowserPasskey(hints)` → `biometric.js` → `signInWithPasskey(returnUrl, hints)`
```javascript
async function signInWithPasskey(returnUrl, hints) {
    checkPasskeySupport();
    var options = await postJson('/Biometrics/PasskeyLoginOptions', {});
    var credentialJson = await getPasskey(options, hints);
    return await postJson('/Biometrics/PasskeyLogin', { credentialJson: credentialJson, returnUrl: returnUrl });
}
```
- `hints`: `['client-device']` = قارئ هذا الجهاز أولاً. على الكمبيوتر، إذا كان المتصفح يدعم "استخدام الهاتف" (Bluetooth/QR الخاص بالمتصفح) تصبح `['client-device', 'hybrid']`.
- **Controller:** `PasskeyLoginOptions()`: داخل خطوة الهوية تُعرض مفاتيح **هذا الحساب فقط** (`allowCredentials`).
- **المتصفح:** `navigator.credentials.get(...)`: الجهاز يتحقق من الشخص ويوقّع التحدي بالمفتاح الخاص.

### STEP 3 — التحقق وتسجيل الدخول
- **Controller:** `BiometricsController.PasskeyLogin(...)`
```csharp
BiometricResult result = await passkeys.VerifyLoginAsync(request.CredentialJson);

// After an identity step the passkey must belong to that same account.
string? stepUserId = preAuth.UserId(HttpContext);
if (result.Succeeded && result.User != null && stepUserId != null && result.User.Id != stepUserId)
{
    result = BiometricResult.Fail("passkey of another account");
}
```
- **Service:** `PasskeyService.VerifyLoginAsync` ← `signInManager.PerformPasskeyAssertionAsync` (Identity يتحقق من **التوقيع بالمفتاح العام**، التحدي، Origin، UV، **عدّاد التوقيع**) ← UPDATE `AspNetUserPasskeys` (العدّاد الجديد).
- ثم `BiometricSignInService.SignInAsync(user, PasskeyMethod, ip)` ← Cookie + AuditLog.
- **Output:** `{ succeeded: true, redirect: "/Supplier" }`.

### فروع الفشل
| الحالة | النتيجة |
|---|---|
| المستخدم أغلق نافذة البصمة | `NotAllowedError` → تُعامل "cancelled": تختفي الرسالة ويظهر "Press Continue with Fingerprint to try again..." |
| Passkey لحساب آخر | 401 "Sign-in failed..." + فشل محسوب |
| لا HTTPS | "Open this page over HTTPS to use passkeys." |
| التحدي انتهى (300 ثانية) أو استُخدم | "The request expired. Try again." |

---

## 9. الدخول بالهاتف عبر QR (Phone QR)

**يعمل فقط** عندما يستطيع الهاتف الوصول للموقع: الصفحة مفتوحة على عنوان HTTPS عام، أو `Biometric:PublicOrigin` مضبوط (مثل رابط ngrok). وإلا تظهر: "Your phone cannot reach this address...".

### أ) تسجيل هاتف (Enrollment)
1. **الكمبيوتر:** `phone-passkey.js` → `startPhoneEnrollment()` → POST `/Biometrics/Phone/EnrollStart` { password }.
2. **السيرفر:** `PhonePasskeyController.EnrollStart` ← فحص كلمة المرور ← `links.Create(...)` + `links.AddToken(link)` ← `PhoneLinkStore.QrDataUri(url)`.
   - الرابط: `https://.../Biometrics/Phone#t=TOKEN`. الرمز بعد `#` **لا يُرسل للسيرفر في العنوان** ولا يظهر في السجلات.
   - السيرفر يحفظ **Hash** الرمز فقط، والكمبيوتر يأخذ Cookie سرية `.Orange.PhoneLink` (فقط هو يستطيع متابعة الحالة).
   - صلاحية: 600 ثانية.
3. **الكمبيوتر:** `startWatchingPhoneLink(phoneEnrollmentStatusChanged)` يسأل `/Biometrics/Phone/Status` كل ثانيتين، و`startPhoneCountdown` يعرض الوقت المتبقي.
4. **الهاتف:** يفتح `/Biometrics/Phone` (`Views/PhonePasskey/Index.cshtml`) ← `setUpPhonePage()` ← POST `Info` (ما الغرض؟) ← `showPhoneEnrollment()` ← `EnrollOptions` ← `registerThisPhone()` ← `createPasskey` ← POST `EnrollFinish`.
5. **السيرفر:** `EnrollFinish` ← `PasskeyService.RegisterAsync` ← INSERT `AspNetUserPasskeys` ← الحالة `completed`.
6. **الكمبيوتر:** يرى `completed` ← يعيد تحميل الصفحة.
7. **بديل:** "Send setup link to my email" ← `EnrollEmail` (رمز ثانٍ لنفس الرابط، حد 3 رسائل).

### ب) الدخول بالهاتف (Sign-in)
1. **الكمبيوتر:** بعد خطوة الهوية بالبصمة ← "Show a QR code for my phone" ← `supplier-login.js` → `startPhoneSignIn()` → POST `/Biometrics/Phone/SignInStart`.
2. **السيرفر:** `SignInStart`: الحساب من خطوة الهوية فقط، له Passkey، ثم رقم عشوائي من رقمين (`RandomNumberGenerator.GetInt32(10, 100)`)، صلاحية 180 ثانية.
3. **الهاتف:** يمسح QR ← `showPhoneSignIn(info)` ← يكتب الرقم ← `checkNumberOnPhone()` ← `SignInOptions` (مقارنة الرقم بوقت ثابت `SameCode`، و3 أخطاء تنهي الرابط) ← `approveSignInOnPhone()` ← `getPasskey` ← `SignInApprove`.
4. **السيرفر:** `SignInApprove`: Passkey صحيح **ولنفس الحساب** ← الحالة `approved`.
5. **الكمبيوتر:** `phoneSignInStatusChanged` يرى `approved` ← `completePhoneSignIn()` ← POST `SignInComplete`.
6. **السيرفر:** `SignInComplete`: نفس المتصفح (Cookie الرابط) + نفس خطوة الهوية + `SecurityStamp` لم يتغير ← `SignInAsync(user, PasskeyMethod)` ← Cookie الدخول.
7. **"This wasn't me"** على الهاتف ← `Deny` ← الحالة `denied` ← الكمبيوتر يعرض: "The sign-in was declined on your phone. Nobody was signed in."

**Database:** الروابط في الذاكرة فقط. قاعدة البيانات: `AspNetUserPasskeys` (تسجيل/تحديث العدّاد)، و`AuditLogs` (`PhoneLinkStarted`, `PhoneLinkOpened`, `PhoneSignInApproved`, `PhoneSignInCompleted`, ...).

---

## 10. نسيت كلمة المرور / إعادة تعيينها

### STEP 1 — طلب الرابط
- **File:** `Areas/Identity/Pages/Account/ForgotPassword.cshtml.cs` → `OnPostAsync()` (نموذج Razor عادي)
```csharp
// Identity makes the reset code; it is Base64url-encoded so it can travel inside a link.
string code = await users.GeneratePasswordResetTokenAsync(user);
string encodedCode = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
```
- يُرسل البريد فقط لحساب **موجود ومؤكَّد وفعّال**، لكن الرد **نفسه دائماً**: `ForgotPasswordConfirmation`.
- صلاحية الرابط **ساعة** (`Program.cs`: `TokenLifespan = TimeSpan.FromHours(1)`).
- **Database:** قراءة فقط.

### STEP 2 — كلمة المرور الجديدة
- **File:** `Areas/Identity/Pages/Account/ResetPassword.cshtml.cs`
- `OnGet(code)`: يفك ترميز الرمز ويضعه في حقل مخفي.
- `OnPostAsync()`:
```csharp
// Identity checks that the code is valid, unused and less than one hour old, then saves the new hash.
IdentityResult result = await users.ResetPasswordAsync(user, Input.Code, Input.Password);
```
- **Database:** UPDATE `AspNetUsers` (Hash جديد + `SecurityStamp` جديد، أي **كل الجلسات القديمة تنتهي** عبر `ValidateCookieAsync`)؛ INSERT `AuditLogs` (`PasswordReset`).
- **Output:** `ResetPasswordConfirmation` (صفحة من حزمة Identity UI).

### تغيير كلمة المرور الأولى (FirstLoginPasswordChange)
`FirstLoginPasswordChange.cshtml.cs` → `OnPostAsync()` ← `users.ChangePasswordAsync` ← `MustChangePassword = false` ← `signInManager.RefreshSignInAsync(user)` (Cookie جديدة لأن الـ SecurityStamp تغيّر) ← AuditLog ← `WorkspaceUrlAsync(user)`.

---

## 11. تسجيل الخروج (Logout)

- **الصفحة:** `/Identity/Account/Logout`. هذه الصفحة **من حزمة Microsoft** (`Microsoft.AspNetCore.Identity.UI`)، فلا يوجد لها ملف في المشروع.
- **Trigger:** نموذج POST صغير (مثلاً في `PendingApproval.cshtml`):
```cshtml
<form asp-page="./Logout" method="post"><button type="submit" class="as-quiet">Sign out</button></form>
```
- **Processing:** Identity يستدعي `SignInManager.SignOutAsync()` فيحذف الـ Cookie. الـ POST مع Anti-Forgery يمنع موقعاً آخر من إخراج المستخدم.
- `FirstLoginGate` و`SupplierApprovalGate` يسمحان دائماً بمسار Logout.
- **Database:** لا شيء.

---

## 12. حالات الرفض والإيقاف والانتهاء والفشل (ملخص)

| الحالة | أين يُفحص | ماذا يحدث |
|---|---|---|
| شركة **Rejected / Suspended** | `SignInWithPasswordAsync`، `AccountAllowedAsync`، `ValidateCookieAsync` | لا دخول؛ وإن كان داخلاً يُخرج في الطلب التالي |
| شركة **Pending** | `SupplierApprovalGate` | `/Supplier` ← `PendingApproval`؛ POST ← 403 "Supplier approval is required." |
| حساب غير فعّال (`IsActive = false`) | كل مسارات الدخول + `ValidateCookieAsync` | رفض |
| بريد غير مؤكَّد | `SignInWithPasswordAsync`، `EnrollmentAllowedAsync` | رفض |
| **Lockout** (5 أخطاء، 15 دقيقة) | Identity `CheckPasswordSignInAsync`؛ فشل الوجه/البصمة يُحسب أيضاً (`RegisterFailureAsync`) | رسالة بعدد الدقائق؛ خطوة الهوية تنتهي |
| OTP منتهي (10 دقائق) / 5 أخطاء | `VerifyEmail.OnPostAsync` | "The code is invalid or expired." |
| تحدي الوجه منتهي (90 ث) أو مستخدم | `ChallengeStore.Consume` | "The request expired. Try again." |
| خطوة الهوية منتهية (300 ث) | `SupplierPreAuth.UserId` = null | `restart: true` ← العودة لاختيار الطريقة |
| QR منتهي / مُلغى / مرفوض | `PhoneLinkStore` + `EndedMessage` | 410 Gone + رسالة واضحة |
| محاولات كثيرة | `BiometricThrottle` (10 أخطاء / 10 دقائق لكل IP وحساب) | 429 "Too many attempts..." |
| `MustChangePassword` | `FirstLoginGate` | كل الصفحات ← `FirstLoginPasswordChange` |
| كلمة مرور تغيّرت في مكان آخر | `ValidateCookieAsync` (SecurityStamp، الفحص كل طلب: `ValidationInterval = TimeSpan.Zero`) | الجلسات القديمة تنتهي |

---

## 13. خريطة الاعتماد بين الملفات (Dependency Map)

```
التسجيل
Register.cshtml → supplier-registration.js: setUpRegistrationPage → nextRegistrationStep → validateRegistrationStep
  → onRegistrationSubmit (fetch POST) → Register.cshtml.cs: OnPostAsync
  → UserManager.CreateAsync / AddToRoleAsync / GenerateUserTokenAsync + SupplierDocumentStorage.SaveAsync
  → SQL: Suppliers, AspNetUsers, AspNetUserRoles, Documents, AuditLogs → Redirect → VerifyEmail

OTP
VerifyEmail.cshtml (form POST) → VerifyEmail.cshtml.cs: OnPostAsync → UserManager.VerifyUserTokenAsync
  → SQL: AspNetUsers (UPDATE), Notifications, AuditLogs → SignInManager.SignInAsync (Cookie) → PendingApproval

كلمة المرور
Login.cshtml → supplier-login.js: onLoginSubmit → signInWithPasswordOnPage → postLoginHandler('Credentials')
  → Login.cshtml.cs: OnPostCredentialsAsync → SignInWithPasswordAsync → SignInManager.CheckPasswordSignInAsync
  → SignInManager.SignInAsync | BiometricSignInService.SignInAsync → SQL: AspNetUsers, AuditLogs
  → JSON { outcome, redirect } → biometric.js: goToAfterSignIn → Workspace / PendingApproval

تسجيل الوجه
PendingApproval.cshtml → biometric.js: enrollFace → runFaceCheck → postJson(FaceEnrollChallenge)
  → BiometricsController.FaceEnrollChallenge → BiometricSignInService.EnrollmentAllowedAsync + CheckPasswordAsync → FaceService.CreateChallenge → ChallengeStore.Create
  → startCamera → captureFaceSamples (face-api.js) → postJson(FaceEnroll)
  → BiometricsController.FaceEnroll → FaceService.EnrollAsync → ChallengeStore.Consume + CheckSamples + Protect
  → SQL: FaceTemplates (INSERT), AuditLogs → JSON → page reload

الدخول بالوجه
Login.cshtml → supplier-login.js: startIdentityStep('face') → Login.cshtml.cs: OnPostIdentityStartAsync → SupplierPreAuth.Begin
  → supplier-login.js: runFaceLogin → biometric.js: runFaceCheck → BiometricsController.FaceChallenge → FaceLogin
  → FaceService.VerifyLoginAsync → Match → BiometricSignInService.SignInAsync → SQL: FaceTemplates, AuditLogs
  → JSON { redirect } → goToAfterSignIn → /Supplier

البصمة (تسجيل)
_PhonePasskeySetup.cshtml → biometric.js: registerThisDevice → registerPasskey → PasskeyRegisterOptions → PasskeyService.CreateRegistrationOptionsAsync
  → navigator.credentials.create (المتصفح) → PasskeyRegister → PasskeyService.RegisterAsync → SQL: AspNetUserPasskeys, AuditLogs

البصمة (دخول)
Login.cshtml → supplier-login.js: startIdentityStep('fingerprint') → OnPostIdentityStartAsync → runFingerprintLogin → signInWithBrowserPasskey
  → biometric.js: signInWithPasskey → PasskeyLoginOptions → navigator.credentials.get (المتصفح) → PasskeyLogin
  → PasskeyService.VerifyLoginAsync → BiometricSignInService.SignInAsync → SQL: AspNetUserPasskeys (counter), AuditLogs → /Supplier

QR الهاتف
supplier-login.js: startPhoneSignIn → PhonePasskeyController.SignInStart → PhoneLinkStore.Create/AddToken/QrDataUri
  → phone-passkey.js (على الهاتف): setUpPhonePage → Info → checkNumberOnPhone → SignInOptions → approveSignInOnPhone → getPasskey → SignInApprove
  → (الكمبيوتر) askPhoneLinkStatus → phoneSignInStatusChanged('approved') → completePhoneSignIn → SignInComplete → Cookie → /Supplier
```

---

## 14. الكود الذي لم يُبسَّط أكثر، ولماذا هو ضروري

| الكود | لماذا يبقى |
|---|---|
| `ChallengeStore.Consume` مع `lock` | بدون القفل، طلبان في نفس اللحظة قد يستخدمان نفس الرمز مرتين (Replay). يوجد اختبار لذلك: 100 طلب متوازٍ ونجاح واحد فقط. |
| `FaceService.CheckSamples` | يمنع إرسال أرقام مزوّرة أو وجهين أو عينات مكررة. حذفه يُضعف الأمان. |
| `FaceService.Match` (ثلاث قواعد) | مقارنة واحدة بسيطة إما ترفض صاحب الوجه كثيراً أو تقبل غيره. |
| `OldProtectors()` (مفاتيح التشفير القديمة) | الوجوه المسجّلة قبل 2026-11-15 مشفّرة بمفتاح بنطاق مختلف. حذفه يجعلها غير قابلة للقراءة ("يجب أن تبقى الوجوه المسجّلة صالحة"). |
| `BinaryPrimitives.WriteSingleLittleEndian` | يضمن نفس ترتيب البايتات في كل جهاز، فتبقى القوالب المخزّنة متوافقة. |
| `AuditAsync` بـ `ExecuteSqlInterpolatedAsync` | يحفظ صف السجل وحده دون أن يحفظ بالخطأ تغييرات أخرى معلّقة، وهو آمن من SQL Injection لأن كل `{قيمة}` تصبح Parameter. |
| `credentialToJson` (البناء اليدوي) | المتصفحات القديمة ليس فيها `credential.toJSON()`. |
| `base64urlToBuffer` / `bufferToBase64url` | WebAuthn يتعامل مع `ArrayBuffer`، والسيرفر مع نصوص base64url. |
| `renewAntiForgeryToken` (DOMParser) | بعد إعادة تشغيل السيرفر يصبح رمز الصفحة قديماً، وبدون هذه الدالة سيظهر خطأ للمستخدم رغم أن بياناته صحيحة. |
| `showServerValidation` (DOMParser) في التسجيل | يحافظ على **الملفات المختارة** عند رفض السيرفر؛ إعادة تحميل الصفحة تفقدها. |
| `captureFaceSamples`: canvas، المكتشف الحساس، التحويل للـ CPU | بعض كروت الشاشة تعطي صورة سوداء أو نتائج خاطئة، وهذه الحلول مأخوذة من مشاكل حقيقية. |
| `AbortController` في `postLoginHandler` | بدون مهلة 20 ثانية قد تبقى الصفحة "Checking…" للأبد إذا انقطع الاتصال. |
| `tellIllustration(...)` والأحداث `authstudio:*` | الرسوم المتحركة (`wwwroot/js/auth-studio/`) تستمع لهذه الأسماء بالضبط. هي **زخرفة فقط** ولا تقرر أي شيء، لكن حذف الأحداث يوقف الرسوم. |
| `wwwroot/js/auth-studio/*` (آلاف الأسطر) | رسوم متحركة ثلاثية الأبعاد للتصميم فقط. **لم تُغيَّر** وليست جزءاً من المصادقة؛ لا تحتاج شرحها للجنة. |
| `stopImmediatePropagation` + capture في التسجيل | سكربتات عامة في الموقع (`orange-motion.js`, `orange-ui.js`) تستمع لكل submit، وهذا يمنعها من التدخل في إرسال المعالج. |
| `PhoneLinkStore` (Hash + Cookie + الحالات) | بدونها يستطيع أي متصفح آخر أن "يلتقط" جلسة الدخول التي وافق عليها الهاتف. |

---

## 15. تقرير إعادة الكتابة (ماذا تغيّر)

### ما أُزيل لأنه غير ضروري
- في JavaScript: `(function () { 'use strict'; ... })()` (IIFE)، والكائن `window.OrangeBiometric`، والدالة `guarded(...)` التي تأخذ دوال كمعاملات، و`page.announce`. صارت كل الدوال **دوال عادية بأسماء واضحة** تستدعي بعضها مباشرة.
- الأزرار المخفية `btnFaceLogin` و`btnPasskeyStep`: كانت `supplier-login.js` "تضغطها" برمجياً لتشغيل كود في ملف آخر. الآن تستدعي `runFaceLogin()` و`runFingerprintLogin()` مباشرة.
- الكود الميت: فرع `.ld-scene[data-scene="face"]` (لا توجد صفحة تستخدمه)، وفرع `fallback` في QR الدخول (لم يُستدعَ أبداً)، والحقول `RpInfo`, `RpName`, `FaceLoginRequiresPassword`, `FaceSubmission.Email/Password`, `PasskeyRow.LastUsedAt`.
- في C#: الـ `record`s (`Accepted`, `ChallengeEntry`, `FaceMatch`)، والـ Tuples (`(PhoneLink?, ApplicationUser?, IActionResult?)`)، و`switch` expressions، و`is { ... }` patterns، والدوال بسطر واحد `=>`، وسلاسل `?:` المتداخلة، وسلاسل LINQ في المنطق. حلّ محلها `if / else` و`foreach` و`class` عادية.
- ثلاث نسخ مكررة من حساب "صفحة الدور" صارت دالة واحدة: `BiometricSignInService.WorkspaceUrlAsync`.
- كود الصفحات التجريبية (Face demo, Fingerprint demo) خرج من `biometric.js` إلى ملف خاص: `wwwroot/js/biometric-demo.js`.

### ما بقي كما هو تماماً
- كل عناوين URL، وأسماء الـ handlers، وشكل الـ JSON، ورسائل الأخطاء.
- قاعدة البيانات: **لا Migration جديد**. الجداول والعلاقات والبيانات كما هي، ومفاتيح التشفير والـ `TemplatePurpose` لم تتغير، فالوجوه والـ Passkeys الموجودة تعمل.
- كل قواعد الأمان والأرقام (10 دقائق، 5 محاولات، 90/300 ثانية، العتبة 0.5، 60%...).

### إصلاحات صغيرة أثناء العمل
- بعد "Cancel" في تسجيل الوجه، كانت المحاولة التالية تُلغى فوراً لأن المتغير لم يُصفَّر. الآن `runFaceCheck` يصفّره في كل بداية.
- نص صفحة Face demo كان يقول 70%، والكود الحقيقي 60%. تم تصحيح النص.
- الاختبار `Tests/AuthSmoke/FaceCaptureChecks.js` كان فاشلاً قبل العمل (يتوقع 5 ثوانٍ). أُعيدت كتابته للكود الجديد (7 ثوانٍ) وهو ينجح.
