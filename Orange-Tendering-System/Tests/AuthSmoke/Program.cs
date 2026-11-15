using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Data.SqlClient;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Services;
using Orange_Tendering_System.ViewModels;
using System.Security.Claims;

if (args.Contains("--seed-saja-demo")) { await SajaDemoFixture.Run(); return; }
if (args.Contains("--unified-tender-checks")) { await UnifiedTenderChecks.Run(); return; }
if (args.Contains("--verify-saja-demo")) { await SajaDemoFixture.Run(verifyExisting: true); return; }
if (args.Contains("--provision-browser")) { await BrowserProvisioning.Run(); return; }
if (args.Contains("--seed-ui-fixture")) { await UiFixture.Run(); return; }
if (args.Contains("--seed-lifecycle")) { await LifecycleFixture.Run(); return; }
if (args.Contains("--final-decision-checks")) { await FinalDecisionChecks.Run(); return; }
if (args.Contains("--clarification-checks")) { await ClarificationChecks.Run(); return; }
if (args.Contains("--smtp-mode-checks")) { SmtpModeChecks.Run(); return; }
if (args.Contains("--smtp-live-check")) { await SmtpLiveChecks.Run(); return; }
if (args.Contains("--smtp-live-create-check")) { await SmtpLiveChecks.RunCreate(); return; }

var connection = Environment.GetEnvironmentVariable("AUTH_TEST_CONNECTION")
    ?? throw new InvalidOperationException("Set AUTH_TEST_CONNECTION to a migrated, isolated test database.");
if (!new SqlConnectionStringBuilder(connection).InitialCatalog.StartsWith("OrangeAuthCheck_", StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("Authentication smoke tests require an OrangeAuthCheck_ database.");
var root = Environment.GetEnvironmentVariable("AUTH_PROJECT_ROOT")
    ?? throw new InvalidOperationException("Set AUTH_PROJECT_ROOT to the web project directory.");
var suffix = Convert.ToHexString(RandomNumberGenerator.GetBytes(5)).ToLowerInvariant();
var isolatedContentRoot = Path.Combine(Path.GetTempPath(), "OrangeAuthCheck_Files_" + suffix);
Directory.CreateDirectory(isolatedContentRoot);
var adminEmail = $"admin-{suffix}@example.test";
var supplierEmail = $"supplier-{suffix}@example.test";
var adminPassword = "A!a1" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
var newAdminPassword = "B!b2" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
var supplierPassword = "C!c3" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
var resetPassword = "D!d4" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
var mail = new CapturedEmail();
Environment.SetEnvironmentVariable("ConnectionStrings__Default Connection", connection);
Environment.SetEnvironmentVariable("BootstrapAdmin__Email", adminEmail);
Environment.SetEnvironmentVariable("BootstrapAdmin__Password", adminPassword);
Environment.SetEnvironmentVariable("Smtp__Host", "test.local");
Environment.SetEnvironmentVariable("Smtp__Mode", "Smtp");
Environment.SetEnvironmentVariable("Smtp__Port", "587");
Environment.SetEnvironmentVariable("Smtp__From", "sender@example.test");
Environment.SetEnvironmentVariable("Logging__LogLevel__Microsoft.EntityFrameworkCore", "Warning");

using var factory = CreateFactory();
using var admin = Client(factory);
using var supplier = Client(factory);

using (var scope = factory.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    Check(await db.Roles.CountAsync() == SystemRoles.All.Length, "all configured roles seeded");
    var initial = await db.Users.SingleAsync(u => u.Email == adminEmail);
    Check(initial.MustChangePassword && initial.EmailConfirmed, "initial Admin requires password change");
}

var loginToken = await Token(admin, "/Identity/Account/Login");
var login = await admin.PostAsync("/Identity/Account/Login", Form(new()
{
    ["Input.Email"] = adminEmail, ["Input.Password"] = adminPassword,
    ["__RequestVerificationToken"] = loginToken
}));
Check(login.StatusCode == HttpStatusCode.Redirect && login.Headers.Location?.ToString().Contains("FirstLoginPasswordChange") == true,
    "initial Admin redirected to password change");
Check((await admin.GetAsync("/Admin")).Headers.Location?.ToString().Contains("FirstLoginPasswordChange") == true,
    "first-login gate blocks direct Admin URL");
var changeToken = await Token(admin, "/Identity/Account/FirstLoginPasswordChange");
var changed = await admin.PostAsync("/Identity/Account/FirstLoginPasswordChange", Form(new()
{
    ["Input.CurrentPassword"] = adminPassword, ["Input.NewPassword"] = newAdminPassword,
    ["Input.ConfirmPassword"] = newAdminPassword, ["__RequestVerificationToken"] = changeToken
}));
Check(changed.StatusCode == HttpStatusCode.Redirect, "first password changed");
Check((await admin.GetAsync("/Admin")).StatusCode == HttpStatusCode.OK, "Admin workspace opens after change");
var logoutToken = await Token(admin, "/Admin");
var logout = await admin.PostAsync("/Identity/Account/Logout", Form(new()
{
    ["__RequestVerificationToken"] = logoutToken
}));
Check(logout.StatusCode == HttpStatusCode.Redirect, "logout endpoint signs out");
Check((await admin.GetAsync("/Admin")).StatusCode != HttpStatusCode.OK, "logout invalidates cookie");
using (var redirectClient = Client(factory))
{
    var openRedirectToken = await Token(redirectClient, "/Identity/Account/Login?returnUrl=https://evil.example.test/");
    var redirected = await redirectClient.PostAsync("/Identity/Account/Login?returnUrl=https://evil.example.test/", Form(new()
    {
        ["Input.Email"] = adminEmail, ["Input.Password"] = newAdminPassword,
        ["__RequestVerificationToken"] = openRedirectToken
    }));
    Check(redirected.Headers.Location?.ToString() == "/Admin", "external return URL ignored");
}

var registerToken = await Token(supplier, "/Identity/Account/Register");
var invalidRegistration = await supplier.PostAsync("/Identity/Account/Register", Form(new()
{
    ["Input.Email"] = $"invalid-{suffix}@example.test",
    ["__RequestVerificationToken"] = registerToken
}));
Check(invalidRegistration.StatusCode == HttpStatusCode.OK, "required company fields and documents validated");
var registration = new MultipartFormDataContent();
foreach (var field in new Dictionary<string, string>
{
    ["Input.FullName"] = "Test Contact", ["Input.Email"] = supplierEmail,
    ["Input.Password"] = supplierPassword, ["Input.ConfirmPassword"] = supplierPassword,
    ["Input.CompanyName"] = "Test Supplier " + suffix, ["Input.Category"] = "IT & Electronics",
    ["Input.RegistrationNumber"] = "CR-" + suffix,
    ["Input.CompanyEmail"] = "company-" + suffix + "@example.test",
    ["Input.CompanyPhone"] = "+962790000000", ["Input.CompanyAddress"] = "Amman, Jordan",
    ["__RequestVerificationToken"] = registerToken
}) registration.Add(new StringContent(field.Value), field.Key);
var pdf = Encoding.ASCII.GetBytes("%PDF-1.7\nTest legal document\n%%EOF");
registration.Add(FilePart(pdf), "CommercialRegistrationCertificate", "registration.pdf");
registration.Add(FilePart(pdf), "LegalDocuments", "license.pdf");
var registered = await supplier.PostAsync("/Identity/Account/Register", registration);
Check(registered.StatusCode == HttpStatusCode.Redirect && registered.Headers.Location?.ToString().Contains("VerifyEmail") == true,
    "supplier registration redirected to OTP");
using (var scope = factory.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var company = await db.Suppliers.SingleAsync(s => s.RegistrationNumber == "CR-" + suffix);
    var user = await db.Users.SingleAsync(u => u.Email == supplierEmail);
    Check(company.Status == SupplierStatus.Pending && user.SupplierId == company.Id && !user.EmailConfirmed && !user.IsActive,
        "provisional supplier and inactive unconfirmed Identity user");
    Check(await db.Documents.CountAsync(d => d.SupplierId == company.Id) == 2, "supplier document metadata stored");
    Check(await db.UserRoles.CountAsync(r => r.UserId == user.Id) == 1, "supplier has one role");
    Check(!await db.Notifications.AnyAsync(n => n.Link == "/Admin/Suppliers/Review/" + company.Id),
        "unverified application does not notify Admin");
    var review = scope.ServiceProvider.GetRequiredService<AdminService>();
    Check(await review.GetSupplierDetailsAsync(company.Id) == null &&
        !(await review.GetSuppliersAsync(null, null)).Suppliers.Any(s => s.Id == company.Id),
        "unverified supplier is not in Admin review");
    var adminId = await db.Users.Where(u => u.Email == adminEmail).Select(u => u.Id).SingleAsync();
    Check(await review.ReviewSupplierAsync(new SupplierReviewDecisionViewModel
    { SupplierId = company.Id, Decision = SupplierStatus.Approved }, adminId) == "EmailUnverified",
        "Admin cannot approve an unverified supplier");
    var adminPrincipal = new ClaimsPrincipal(new ClaimsIdentity(
        [new Claim(ClaimTypes.NameIdentifier, adminId)], "test"));
    var workflowReview = scope.ServiceProvider.GetRequiredService<SupplierReviewService>();
    Check(!(await workflowReview.List(adminPrincipal)).Any(s => s.Id == company.Id),
        "workflow review hides unverified supplier");
    var workflowDenied = false;
    try { await workflowReview.Decide(company.Id, true, "Document review", adminPrincipal); }
    catch (WorkflowException e) { workflowDenied = e.Message.Contains("verify", StringComparison.OrdinalIgnoreCase); }
    Check(workflowDenied, "workflow review cannot approve an unverified supplier");
}
var duplicateToken = await Token(supplier, "/Identity/Account/Register");
var duplicate = await supplier.PostAsync("/Identity/Account/Register",
    Form(new() { ["Input.Email"] = supplierEmail, ["Input.Password"] = supplierPassword,
        ["__RequestVerificationToken"] = duplicateToken }));
Check(duplicate.StatusCode == HttpStatusCode.Redirect && duplicate.Headers.Location?.ToString().Contains("VerifyEmail") == true,
    "unverified supplier resumes OTP without reuploading files");
using (var unverifiedVisitor = Client(factory))
{
    var unverifiedLoginToken = await Token(unverifiedVisitor, "/Identity/Account/Login");
    var unverifiedLogin = await unverifiedVisitor.PostAsync("/Identity/Account/Login", Form(new()
    {
        ["Input.Email"] = supplierEmail, ["Input.Password"] = supplierPassword,
        ["__RequestVerificationToken"] = unverifiedLoginToken
    }));
    Check(unverifiedLogin.StatusCode == HttpStatusCode.OK, "unverified supplier cannot sign in");
}
var message = mail.Messages.Last(m => m.Subject.Contains("Verify"));
Check(message.To == supplierEmail, "OTP sent through IEmailSender");
var otp = Regex.Match(message.Body, @"\b\d{6}\b").Value;
Check(otp.Length == 6, "six-digit OTP generated");
var verifyToken = await Token(supplier, "/Identity/Account/VerifyEmail?email=" + Uri.EscapeDataString(supplierEmail));
var wrong = await supplier.PostAsync("/Identity/Account/VerifyEmail", Form(new()
{
    ["Input.Email"] = supplierEmail, ["Input.Code"] = "000000",
    ["__RequestVerificationToken"] = verifyToken
}));
Check(wrong.StatusCode == HttpStatusCode.OK, "incorrect OTP rejected");
var verified = await supplier.PostAsync("/Identity/Account/VerifyEmail", Form(new()
{
    ["Input.Email"] = supplierEmail, ["Input.Code"] = otp,
    ["__RequestVerificationToken"] = verifyToken
}));
Check(verified.StatusCode == HttpStatusCode.Redirect &&
    verified.Headers.Location?.ToString().Contains("PendingApproval") == true,
    "valid OTP signs supplier in and redirects to pending approval");
Check((await supplier.GetAsync("/Identity/Account/PendingApproval")).StatusCode == HttpStatusCode.OK,
    "pending approval page opens with Identity cookie");
var pendingWorkspace = await supplier.GetAsync("/Supplier");
Check(pendingWorkspace.StatusCode == HttpStatusCode.Redirect &&
    pendingWorkspace.Headers.Location?.ToString().Contains("PendingApproval") == true,
    "pending supplier cannot enter tender workspace");
var pendingOffer = await supplier.PostAsync("/Supplier/Workflow/SubmitBid/1", Form(new()));
Check(pendingOffer.StatusCode == HttpStatusCode.Forbidden,
    $"pending supplier cannot submit offer request (HTTP {(int)pendingOffer.StatusCode})");
using (var scope = factory.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var user = await db.Users.SingleAsync(u => u.Email == supplierEmail);
    Check(user.EmailConfirmed && user.IsActive, "verified supplier contact activated");
    var review = scope.ServiceProvider.GetRequiredService<AdminService>();
    Check(await review.GetSupplierDetailsAsync(user.SupplierId!.Value) != null,
        "verified supplier enters Admin review");
    Check(await db.Notifications.AnyAsync(n => n.Link == "/Admin/Suppliers/Review/" + user.SupplierId),
        "Admin notified only after OTP verification");
}
using (var replayVisitor = Client(factory))
{
    var replayToken = await Token(replayVisitor, "/Identity/Account/VerifyEmail?email=" + Uri.EscapeDataString(supplierEmail));
    var replay = await replayVisitor.PostAsync("/Identity/Account/VerifyEmail", Form(new()
    {
        ["Input.Email"] = supplierEmail, ["Input.Code"] = otp,
        ["__RequestVerificationToken"] = replayToken
    }));
    Check(replay.StatusCode == HttpStatusCode.OK, "OTP replay rejected");
}
Check((await supplier.GetAsync("/Identity/Account/ConfirmEmail?code=unused")).StatusCode == HttpStatusCode.NotFound,
    "default confirmation-link route cannot bypass OTP");
Check((await supplier.GetAsync("/Identity/Account/ResendEmailConfirmation")).Headers.Location?.ToString().Contains("VerifyEmail") == true,
    "default confirmation resend redirects to OTP flow");

var expiredEmail = $"expired-{suffix}@example.test";
var expiredSuffix = suffix + "x";
var expiredRegisterToken = await Token(supplier, "/Identity/Account/Register");
var expiredRegistration = await supplier.PostAsync("/Identity/Account/Register",
    RegistrationContent(expiredEmail, supplierPassword, expiredSuffix, expiredRegisterToken));
Check(expiredRegistration.StatusCode == HttpStatusCode.Redirect, "second supplier registration created for OTP limits");
var expiredOtp = Regex.Match(mail.Messages.Last(m => m.To == expiredEmail).Body, @"\b\d{6}\b").Value;
using (var scope = factory.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var user = await db.Users.SingleAsync(u => u.Email == expiredEmail);
    user.EmailOtpSentAt = DateTime.UtcNow.AddMinutes(-11);
    await db.SaveChangesAsync();
}
var expiredToken = await Token(supplier, "/Identity/Account/VerifyEmail?email=" + Uri.EscapeDataString(expiredEmail));
var expiredAttempt = await supplier.PostAsync("/Identity/Account/VerifyEmail", Form(new()
{
    ["Input.Email"] = expiredEmail, ["Input.Code"] = expiredOtp,
    ["__RequestVerificationToken"] = expiredToken
}));
Check(expiredAttempt.StatusCode == HttpStatusCode.OK, "expired OTP rejected");
using (var scope = factory.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var user = await db.Users.SingleAsync(u => u.Email == expiredEmail);
    user.EmailOtpSentAt = DateTime.UtcNow;
    await db.SaveChangesAsync();
}
var wrongCode = expiredOtp == "000000" ? "999999" : "000000";
for (var i = 0; i < 5; i++)
    await supplier.PostAsync("/Identity/Account/VerifyEmail", Form(new()
    {
        ["Input.Email"] = expiredEmail, ["Input.Code"] = wrongCode,
        ["__RequestVerificationToken"] = expiredToken
    }));
var limited = await supplier.PostAsync("/Identity/Account/VerifyEmail", Form(new()
{
    ["Input.Email"] = expiredEmail, ["Input.Code"] = expiredOtp,
    ["__RequestVerificationToken"] = expiredToken
}));
Check(limited.StatusCode == HttpStatusCode.OK, "five wrong OTPs block verification");
var mailCount = mail.Messages.Count;
var cooldownResend = await supplier.PostAsync("/Identity/Account/VerifyEmail?handler=Resend", Form(new()
{
    ["email"] = expiredEmail, ["__RequestVerificationToken"] = expiredToken
}));
Check(mail.Messages.Count == mailCount, "OTP resend cooldown enforced");
Check(cooldownResend.StatusCode == HttpStatusCode.Redirect &&
    cooldownResend.Headers.Location?.ToString().Contains("handler=", StringComparison.OrdinalIgnoreCase) == false,
    "resend returns to the plain Verify Email URL");
var verifyPage = await (await supplier.GetAsync("/Identity/Account/VerifyEmail?handler=Resend")).Content.ReadAsStringAsync();
Check(verifyPage.Contains("<form method=\"post\" action=\"/Identity/Account/VerifyEmail\">"),
    "verify form posts to the verify handler even from a ?handler=Resend address");
using (var scope = factory.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var user = await db.Users.SingleAsync(u => u.Email == expiredEmail);
    user.EmailOtpSentAt = DateTime.UtcNow.AddMinutes(-11);
    await db.SaveChangesAsync();
}
await supplier.PostAsync("/Identity/Account/VerifyEmail?handler=Resend", Form(new()
{
    ["email"] = expiredEmail, ["__RequestVerificationToken"] = expiredToken
}));
Check(mail.Messages.Count == mailCount + 1, "OTP resend after cooldown delivered to test sender");
var resentOtp = Regex.Match(mail.Messages.Last(m => m.To == expiredEmail).Body, @"\b\d{6}\b").Value;
var oldOtpAttempt = await supplier.PostAsync("/Identity/Account/VerifyEmail", Form(new()
{
    ["Input.Email"] = expiredEmail, ["Input.Code"] = expiredOtp,
    ["__RequestVerificationToken"] = expiredToken
}));
Check(oldOtpAttempt.StatusCode == HttpStatusCode.OK, "resend invalidates old OTP");
var resentVerified = await supplier.PostAsync("/Identity/Account/VerifyEmail", Form(new()
{
    ["Input.Email"] = expiredEmail, ["Input.Code"] = resentOtp,
    ["__RequestVerificationToken"] = expiredToken
}));
Check(resentVerified.StatusCode == HttpStatusCode.Redirect &&
    resentVerified.Headers.Location?.ToString().Contains("PendingApproval") == true,
    "new OTP signs in after resend");
int rejectedSupplierId;
using (var scope = factory.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    rejectedSupplierId = (await db.Users.SingleAsync(u => u.Email == expiredEmail)).SupplierId!.Value;
}
var reviewAdminToken = await Token(admin, "/Identity/Account/Login");
var reviewAdminLogin = await admin.PostAsync("/Identity/Account/Login", Form(new()
{
    ["Input.Email"] = adminEmail, ["Input.Password"] = newAdminPassword,
    ["__RequestVerificationToken"] = reviewAdminToken
}));
Check(reviewAdminLogin.Headers.Location?.ToString() == "/Admin", "Admin signs in for supplier review");
var supplierIndex = await (await admin.GetAsync("/Admin/Suppliers?statusFilter=Pending")).Content.ReadAsStringAsync();
Check(supplierIndex.Contains("CR-" + expiredSuffix), "verified registration appears on Admin Suppliers page");
var supplierReviewToken = await Token(admin, "/Admin/Suppliers/Review/" + rejectedSupplierId);
var rejected = await admin.PostAsync("/Admin/Suppliers/SubmitReview", Form(new()
{
    ["SupplierId"] = rejectedSupplierId.ToString(), ["Decision"] = "Rejected",
    ["ReviewNotes"] = "Legal documentation does not meet registration requirements.",
    ["__RequestVerificationToken"] = supplierReviewToken
}));
Check(rejected.StatusCode == HttpStatusCode.Redirect, "Admin rejects supplier from the Suppliers review page");
using (var scope = factory.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var account = await db.Users.SingleAsync(u => u.Email == expiredEmail);
    Check((await db.Suppliers.SingleAsync(s => s.Id == rejectedSupplierId)).Status == SupplierStatus.Rejected && !account.IsActive,
        "rejection disables the supplier account");
}
using (var rejectedClient = Client(factory))
{
    var rejectedLoginToken = await Token(rejectedClient, "/Identity/Account/Login");
    var denied = await rejectedClient.PostAsync("/Identity/Account/Login", Form(new()
    {
        ["Input.Email"] = expiredEmail, ["Input.Password"] = supplierPassword,
        ["__RequestVerificationToken"] = rejectedLoginToken
    }));
    Check(denied.StatusCode == HttpStatusCode.OK &&
        (await rejectedClient.GetAsync("/Supplier")).StatusCode != HttpStatusCode.OK,
        "rejected supplier cannot sign in or enter the workspace");
}
var failedEmail = $"delivery-fail-{suffix}@example.test";
mail.FailNext = true;
var failedRegisterToken = await Token(supplier, "/Identity/Account/Register");
var failedRegistration = await supplier.PostAsync("/Identity/Account/Register",
    RegistrationContent(failedEmail, supplierPassword, suffix + "f", failedRegisterToken));
Check(failedRegistration.StatusCode == HttpStatusCode.OK, "registration reports email delivery failure");
using (var scope = factory.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    Check(!await db.Users.AnyAsync(u => u.Email == failedEmail) &&
          !await db.Suppliers.AnyAsync(s => s.RegistrationNumber == "CR-" + suffix + "f"),
        "failed delivery rolls back user and company");
}

var supplierLoginToken = await Token(supplier, "/Identity/Account/Login");
var pendingLogin = await supplier.PostAsync("/Identity/Account/Login", Form(new()
{
    ["Input.Email"] = supplierEmail, ["Input.Password"] = supplierPassword,
    ["__RequestVerificationToken"] = supplierLoginToken
}));
Check(pendingLogin.StatusCode == HttpStatusCode.Redirect &&
    pendingLogin.Headers.Location?.ToString().Contains("PendingApproval") == true &&
    (await supplier.GetAsync("/Supplier")).Headers.Location?.ToString().Contains("PendingApproval") == true,
    "verified pending supplier can resume enrollment but cannot enter the workspace");
using (var scope = factory.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var company = await db.Suppliers.SingleAsync(s => s.RegistrationNumber == "CR-" + suffix);
    var adminId = await db.Users.Where(u => u.Email == adminEmail).Select(u => u.Id).SingleAsync();
    var review = scope.ServiceProvider.GetRequiredService<AdminService>();
    Check((await review.GetSuppliersAsync(null, "Pending")).Suppliers.Any(s => s.Id == company.Id),
        "verified registration appears on Admin Suppliers page");
}
var adminReloginToken = await Token(admin, "/Identity/Account/Login");
var adminRelogin = await admin.PostAsync("/Identity/Account/Login", Form(new()
{
    ["Input.Email"] = adminEmail, ["Input.Password"] = newAdminPassword,
    ["__RequestVerificationToken"] = adminReloginToken
}));
Check(adminRelogin.StatusCode == HttpStatusCode.Redirect, "Admin signs in for supplier review");
int reviewedSupplierId;
int reviewedDocumentId;
using (var scope = factory.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    reviewedSupplierId = await db.Suppliers.Where(s => s.RegistrationNumber == "CR-" + suffix)
        .Select(s => s.Id).SingleAsync();
    reviewedDocumentId = await db.Documents.Where(d => d.SupplierId == reviewedSupplierId)
        .Select(d => d.Id).FirstAsync();
    var document = await db.Documents.SingleAsync(d => d.Id == reviewedDocumentId);
    var review = scope.ServiceProvider.GetRequiredService<AdminService>();
    var adminId = await db.Users.Where(u => u.Email == adminEmail).Select(u => u.Id).SingleAsync();
    var approve = new SupplierReviewDecisionViewModel
        { SupplierId = reviewedSupplierId, Decision = SupplierStatus.Approved };
    document.VerificationStatus = DocumentVerificationStatus.Rejected;
    await db.SaveChangesAsync();
    Check(await review.ReviewSupplierAsync(approve, adminId) == "DocumentsRejected",
        "rejected document blocks supplier approval");
    document.VerificationStatus = DocumentVerificationStatus.Pending;
    document.ExpiresAt = DateTime.UtcNow.AddDays(-1);
    await db.SaveChangesAsync();
    Check(await review.ReviewSupplierAsync(approve, adminId) == "DocumentsNotVerified",
        "expired document blocks supplier approval");
    document.ExpiresAt = null;
    var storedPath = document.StoragePath;
    document.StoragePath = Path.Combine(Path.GetTempPath(), "unavailable-supplier-document.pdf");
    await db.SaveChangesAsync();
    Check(await review.ReviewSupplierAsync(approve, adminId) == "DocumentsUnavailable",
        "unavailable document blocks supplier approval");
    document.StoragePath = storedPath;
    await db.SaveChangesAsync();
}
var reviewPath = $"/Admin/Suppliers/Review/{reviewedSupplierId}";
Check((await (await admin.GetAsync("/Admin/Suppliers")).Content.ReadAsStringAsync())
    .Contains("data-no-modal"), "Supplier review opens as a full page");
var reviewToken = await Token(admin, reviewPath);
Check((await admin.GetAsync($"/Admin/Suppliers/Document/{reviewedDocumentId}")).StatusCode == HttpStatusCode.OK,
    "Admin can open the supplier document");
var decision = await admin.PostAsync("/Admin/Suppliers/SubmitReview", Form(new()
{
    ["SupplierId"] = reviewedSupplierId.ToString(), ["Decision"] = "Approved",
    ["__RequestVerificationToken"] = reviewToken
}));
Check(decision.StatusCode == HttpStatusCode.Redirect, "Approve form submits and redirects");
var reviewResult = await admin.GetAsync(reviewPath);
Check((await reviewResult.Content.ReadAsStringAsync()).Contains("Supplier review completed successfully"),
    "Admin sees supplier approval confirmation");
using (var scope = factory.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    Check(await db.Suppliers.AnyAsync(s => s.Id == reviewedSupplierId && s.Status == SupplierStatus.Approved),
        "Admin approval changes supplier status");
    Check(await db.Documents.Where(d => d.SupplierId == reviewedSupplierId)
        .AllAsync(d => d.VerificationStatus == DocumentVerificationStatus.Verified),
        "Admin approval verifies pending documents");
    Check(await db.Users.AnyAsync(u => u.Email == supplierEmail && u.IsActive),
        "approved supplier account is active");
}
var formCreatedEmail = $"form-created-{suffix}@example.test";
var formCreatedTemporaryPassword = "G!g7" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
var createUserToken = await Token(admin, "/Admin/Users/Create");
var createUserPage = await (await admin.GetAsync("/Admin/Users/Create")).Content.ReadAsStringAsync();
Check(createUserPage.Contains("name=\"Password\"") && createUserPage.Contains("data-password-generate"),
    "Admin Create User shows the temporary password field");
var createdFromForm = await admin.PostAsync("/Admin/Users/Create", Form(new()
{
    ["FullName"] = "Form Created Evaluator", ["Email"] = formCreatedEmail,
    ["Role"] = SystemRoles.Technical, ["IsActive"] = "true",
    ["Password"] = formCreatedTemporaryPassword,
    ["__RequestVerificationToken"] = createUserToken
}));
Check(createdFromForm.StatusCode == HttpStatusCode.Redirect &&
      mail.Messages.Any(m => m.To == formCreatedEmail && m.Subject.Contains("Welcome")),
    "Admin Create User form sends welcome email to entered address");
using (var firstLogin = Client(factory))
{
    var firstToken = await Token(firstLogin, "/Identity/Account/Login");
    var first = await firstLogin.PostAsync("/Identity/Account/Login", Form(new()
    {
        ["Input.Email"] = formCreatedEmail, ["Input.Password"] = formCreatedTemporaryPassword,
        ["__RequestVerificationToken"] = firstToken
    }));
    Check(first.Headers.Location?.ToString().Contains("FirstLoginPasswordChange") == true,
        "Admin-entered temporary password signs the employee in to the password change step");
    Check((await firstLogin.GetAsync("/EvaluationCommittee")).Headers.Location?.ToString().Contains("FirstLoginPasswordChange") == true,
        "temporary password cannot open the workspace");
    var replacement = "P!p9" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
    var firstChangeToken = await Token(firstLogin, "/Identity/Account/FirstLoginPasswordChange");
    var firstChanged = await firstLogin.PostAsync("/Identity/Account/FirstLoginPasswordChange", Form(new()
    {
        ["Input.CurrentPassword"] = formCreatedTemporaryPassword, ["Input.NewPassword"] = replacement,
        ["Input.ConfirmPassword"] = replacement, ["__RequestVerificationToken"] = firstChangeToken
    }));
    Check(firstChanged.StatusCode == HttpStatusCode.Redirect &&
          (await firstLogin.GetAsync("/EvaluationCommittee")).StatusCode == HttpStatusCode.OK,
        "employee changes the temporary password before workspace access");
    using var scope = factory.Services.CreateScope();
    var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    var account = (await users.FindByEmailAsync(formCreatedEmail))!;
    Check(!account.MustChangePassword && !await users.CheckPasswordAsync(account, formCreatedTemporaryPassword) &&
          await users.CheckPasswordAsync(account, replacement), "temporary password is replaced");
}
mail.FailNext = true;
var failedFormEmail = $"form-mail-fail-{suffix}@example.test";
var failedFormToken = await Token(admin, "/Admin/Users/Create");
var failedFromForm = await admin.PostAsync("/Admin/Users/Create", Form(new()
{
    ["FullName"] = "Undelivered Evaluator", ["Email"] = failedFormEmail,
    ["Role"] = SystemRoles.Technical, ["IsActive"] = "false",
    ["Password"] = "H!h8" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)),
    ["__RequestVerificationToken"] = failedFormToken
}));
Check(failedFromForm.StatusCode == HttpStatusCode.OK &&
      (await failedFromForm.Content.ReadAsStringAsync()).Contains("welcome email could not be delivered"),
    "Admin Create User form displays email delivery failure");
using (var scope = factory.Services.CreateScope())
    Check(!await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
        .Users.AnyAsync(u => u.Email == failedFormEmail), "failed email leaves no user account");
var approvedLoginToken = await Token(supplier, "/Identity/Account/Login");
var approvedLogin = await supplier.PostAsync("/Identity/Account/Login", Form(new()
{
    ["Input.Email"] = supplierEmail, ["Input.Password"] = supplierPassword,
    ["__RequestVerificationToken"] = approvedLoginToken
}));
Check(approvedLogin.StatusCode == HttpStatusCode.Redirect && approvedLogin.Headers.Location?.ToString().Contains("PendingApproval") == true,
    "approved supplier without biometrics reaches setup");
Check((await supplier.GetAsync("/Supplier")).Headers.Location?.ToString().Contains("PendingApproval") == true,
    "approved supplier password session cannot open workspace");
Check((await supplier.GetAsync("/Admin")).StatusCode != HttpStatusCode.OK, "supplier cannot enter Admin area");
using (var scope = factory.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var company = await db.Suppliers.SingleAsync(s => s.RegistrationNumber == "CR-" + suffix);
    var adminId = await db.Users.Where(u => u.Email == adminEmail).Select(u => u.Id).SingleAsync();
    var review = scope.ServiceProvider.GetRequiredService<AdminService>();
    Check(await review.ReviewSupplierAsync(new SupplierReviewDecisionViewModel
        { SupplierId = company.Id, Decision = SupplierStatus.Suspended, ReviewNotes = "Test suspension" }, adminId) == "Success",
        "Admin suspends approved supplier");
    Check(!await db.Users.Where(u => u.Email == supplierEmail).Select(u => u.IsActive).SingleAsync(),
        "suspension disables supplier account");
}
Check((await supplier.GetAsync("/Supplier")).StatusCode != HttpStatusCode.OK,
    "existing supplier cookie revoked after suspension");
using (var scope = factory.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var company = await db.Suppliers.SingleAsync(s => s.RegistrationNumber == "CR-" + suffix);
    var adminId = await db.Users.Where(u => u.Email == adminEmail).Select(u => u.Id).SingleAsync();
    var review = scope.ServiceProvider.GetRequiredService<AdminService>();
    Check(await review.ReviewSupplierAsync(new SupplierReviewDecisionViewModel
        { SupplierId = company.Id, Decision = SupplierStatus.Approved }, adminId) == "Success",
        "Admin restores supplier approval");
    Check(await db.Users.Where(u => u.Email == supplierEmail).Select(u => u.IsActive).SingleAsync(),
        "restored supplier account is active");
}
var reLoginToken = await Token(supplier, "/Identity/Account/Login");
var reLogin = await supplier.PostAsync("/Identity/Account/Login", Form(new()
{
    ["Input.Email"] = supplierEmail, ["Input.Password"] = supplierPassword,
    ["__RequestVerificationToken"] = reLoginToken
}));
Check(reLogin.StatusCode == HttpStatusCode.Redirect, "approved supplier can sign in again");
using (var scope = factory.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var user = await db.Users.SingleAsync(u => u.Email == supplierEmail);
    user.IsActive = false;
    await db.SaveChangesAsync();
}
Check((await supplier.GetAsync("/Supplier")).StatusCode != HttpStatusCode.OK,
    "existing cookie revoked after user deactivation");

var employeeEmail = $"employee-{suffix}@example.test";
using (var scope = factory.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var department = new Department { Name = "Auth Test Department " + suffix };
    db.Departments.Add(department);
    await db.SaveChangesAsync();
    var adminUser = await db.Users.SingleAsync(u => u.Email == adminEmail);
    var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
    context.Request.Scheme = "https";
    context.Request.Host = new HostString("localhost");
    context.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
    {
        new Claim(ClaimTypes.NameIdentifier, adminUser.Id),
        new Claim(ClaimTypes.Role, SystemRoles.Admin)
    }, "Test"));
    var result = await scope.ServiceProvider.GetRequiredService<EmployeeProvisioning>()
        .CreateAsync(context, "Test Employee", employeeEmail, department.Id, [SystemRoles.RequestingDepartment]);
    Check(result.Succeeded, "Admin employee provisioning sends activation link");
}
var activationEmail = mail.Messages.Last(m => m.Subject.Contains("Activate"));
var activationUrl = WebUtility.HtmlDecode(Regex.Match(activationEmail.Body, "href=\\\"([^\\\"]+)\\\"").Groups[1].Value);
var activationUri = new Uri(activationUrl);
using (var employee = Client(factory))
{
    var activationToken = await Token(employee, activationUri.PathAndQuery);
    var activationQuery = QueryHelpers.ParseQuery(activationUri.Query);
    var employeePassword = "E!e5" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
    var activated = await employee.PostAsync("/Identity/Account/ActivateEmployee", Form(new()
    {
        ["Input.Email"] = employeeEmail, ["Input.Code"] = activationQuery["code"].ToString(),
        ["Input.Password"] = employeePassword, ["Input.ConfirmPassword"] = employeePassword,
        ["__RequestVerificationToken"] = activationToken
    }));
    Check((await activated.Content.ReadAsStringAsync()).Contains("Your password is set"), "employee activation confirms account");
    var employeeLoginToken = await Token(employee, "/Identity/Account/Login");
    var employeeLogin = await employee.PostAsync("/Identity/Account/Login", Form(new()
    {
        ["Input.Email"] = employeeEmail, ["Input.Password"] = employeePassword,
        ["__RequestVerificationToken"] = employeeLoginToken
    }));
    Check(employeeLogin.Headers.Location?.ToString() == "/Department", "activated employee enters assigned workspace");
}

// Admin creates a temporary password; the employee replaces it through the emailed link.
var adminCreatedEmail = $"admin-created-{suffix}@example.test";
var adminCreatedTemporaryPassword = "E!e5" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
using (var scope = factory.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var departmentId = await db.Departments.Where(d => d.Name == "Auth Test Department " + suffix)
        .Select(d => d.Id).SingleAsync();
    var adminId = await db.Users.Where(u => u.Email == adminEmail).Select(u => u.Id).SingleAsync();
    var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
    context.Request.Scheme = "https";
    context.Request.Host = new HostString("localhost");
    var created = await scope.ServiceProvider.GetRequiredService<AdminService>().CreateUserAsync(
        new CreateUserViewModel
        {
            FullName = "Admin Created Employee", Email = adminCreatedEmail,
            Role = SystemRoles.RequestingDepartment,
            DepartmentId = departmentId, IsActive = true,
            Password = adminCreatedTemporaryPassword
        }, adminId, context);
    Check(created.Succeeded, "Admin Create User saves the employee and sends welcome email");
    var account = await db.Users.SingleAsync(u => u.Email == adminCreatedEmail);
    Check(account.EmailConfirmed && account.MustChangePassword &&
          await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>()
              .CheckPasswordAsync(account, adminCreatedTemporaryPassword),
        "Admin-created employee has a usable temporary password and must change it");
}
var welcomeEmail = mail.Messages.Last(m => m.To == adminCreatedEmail);
Check(welcomeEmail.Subject.Contains("Welcome") && welcomeEmail.Body.Contains("sign in") &&
      welcomeEmail.Body.Contains("ActivateEmployee"), "welcome email includes password and login links");
var welcomeUrl = WebUtility.HtmlDecode(Regex.Match(welcomeEmail.Body, "href=\\\"([^\\\"]*ActivateEmployee[^\\\"]*)\\\"").Groups[1].Value);
var welcomeUri = new Uri(welcomeUrl);
var welcomePassword = "W!w7" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
using (var adminCreated = Client(factory))
{
    var activationToken = await Token(adminCreated, welcomeUri.PathAndQuery);
    var activationQuery = QueryHelpers.ParseQuery(welcomeUri.Query);
    var activated = await adminCreated.PostAsync("/Identity/Account/ActivateEmployee", Form(new()
    {
        ["Input.Email"] = adminCreatedEmail, ["Input.Code"] = activationQuery["code"].ToString(),
        ["Input.Password"] = welcomePassword, ["Input.ConfirmPassword"] = welcomePassword,
        ["__RequestVerificationToken"] = activationToken
    }));
    Check((await activated.Content.ReadAsStringAsync()).Contains("Your password is set"),
        "welcome link sets the password and confirms the account");
    using (var scope = factory.Services.CreateScope())
    {
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var account = (await users.FindByEmailAsync(adminCreatedEmail))!;
        Check(account.EmailConfirmed && !account.MustChangePassword &&
              !await users.CheckPasswordAsync(account, adminCreatedTemporaryPassword) &&
              await users.CheckPasswordAsync(account, welcomePassword),
            "email setup replaces the temporary password");
    }
    var token = await Token(adminCreated, "/Identity/Account/Login");
    var response = await adminCreated.PostAsync("/Identity/Account/Login", Form(new()
    {
        ["Input.Email"] = adminCreatedEmail, ["Input.Password"] = welcomePassword,
        ["__RequestVerificationToken"] = token
    }));
    Check(response.StatusCode == HttpStatusCode.Redirect && response.Headers.Location?.ToString() == "/Department",
        "Admin-created employee signs in with the private password");
    Check((await adminCreated.GetAsync("/Department")).StatusCode == HttpStatusCode.OK,
        "Admin-created employee enters the workspace after email setup");
}
var failedWelcomeEmail = $"welcome-fail-{suffix}@example.test";
using (var scope = factory.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var adminId = await db.Users.Where(u => u.Email == adminEmail).Select(u => u.Id).SingleAsync();
    var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
    context.Request.Scheme = "https";
    context.Request.Host = new HostString("localhost");
    mail.FailNext = true;
    var failed = await scope.ServiceProvider.GetRequiredService<AdminService>().CreateUserAsync(
        new CreateUserViewModel { FullName = "Failed Welcome", Email = failedWelcomeEmail,
            Role = SystemRoles.SourcingOfficer, IsActive = true,
            Password = "F!f6" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)) }, adminId, context);
    Check(!failed.Succeeded && !await db.Users.AnyAsync(u => u.Email == failedWelcomeEmail),
        "undelivered welcome email rolls back account creation");
}

var forgotToken = await Token(admin, "/Identity/Account/ForgotPassword");
var forgot = await admin.PostAsync("/Identity/Account/ForgotPassword", Form(new()
{
    ["Input.Email"] = adminEmail, ["__RequestVerificationToken"] = forgotToken
}));
Check(forgot.StatusCode == HttpStatusCode.Redirect, "password reset requested");
var resetEmail = mail.Messages.Last(m => m.Subject.Contains("Reset"));
var resetUrl = WebUtility.HtmlDecode(Regex.Match(resetEmail.Body, "href=\\\"([^\\\"]+)\\\"").Groups[1].Value);
var resetUri = new Uri(resetUrl);
var encodedCode = QueryHelpers.ParseQuery(resetUri.Query)["code"].ToString();
var resetToken = await Token(admin, resetUri.PathAndQuery);
var reset = await admin.PostAsync("/Identity/Account/ResetPassword", Form(new()
{
    ["Input.Email"] = adminEmail, ["Input.Code"] = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(encodedCode)),
    ["Input.Password"] = resetPassword, ["Input.ConfirmPassword"] = resetPassword,
    ["__RequestVerificationToken"] = resetToken
}));
Check(reset.StatusCode == HttpStatusCode.Redirect, "Identity reset token accepted");
using (var scope = factory.Services.CreateScope())
{
    var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    var user = (await users.FindByEmailAsync(adminEmail))!;
    Check(await users.CheckPasswordAsync(user, resetPassword), "new password stored by Identity");
    Check(!await users.CheckPasswordAsync(user, newAdminPassword), "old password invalidated");
}

using (var lockedClient = Client(factory))
{
    var wrongToken = await Token(lockedClient, "/Identity/Account/Login");
    for (var i = 0; i < 5; i++)
        await lockedClient.PostAsync("/Identity/Account/Login", Form(new()
        {
            ["Input.Email"] = adminEmail, ["Input.Password"] = "Wrong!" + i,
            ["__RequestVerificationToken"] = wrongToken
        }));
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var locked = await db.Users.SingleAsync(u => u.Email == adminEmail);
    Check(locked.LockoutEnd > DateTimeOffset.UtcNow, "five wrong passwords lock the account");
}

using var secondFactory = CreateFactory();
using (var scope = secondFactory.Services.CreateScope())
    Check(await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Roles.CountAsync() == SystemRoles.All.Length,
        "role seed is idempotent");

// Exercise every real role dashboard with real Identity cookies and SQL Server rows.
// The database-name guard above keeps these accounts out of the application database.
string? officerId = null;
foreach (var workspace in Orange_Tendering_System.ViewModels.WorkspaceNavigation.Workspaces)
{
    var email = $"role-{workspace.Area.ToLowerInvariant()}-{suffix}@example.test";
    using (var scope = factory.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var account = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true,
            IsActive = true, FullName = "Isolated dashboard verification" };
        if (workspace.Role == SystemRoles.Supplier)
        {
            var company = new Supplier { CompanyName = "Isolated role check " + suffix,
                Email = email, RegistrationNumber = "ROLE-" + suffix, Status = SupplierStatus.Approved };
            db.Suppliers.Add(company); await db.SaveChangesAsync(); account.SupplierId = company.Id;
        }
        if (workspace.Role == SystemRoles.RequestingDepartment)
        {
            var department = new Department { Name = "Isolated dashboard " + suffix };
            db.Departments.Add(department); await db.SaveChangesAsync(); account.DepartmentId = department.Id;
        }
        Check((await users.CreateAsync(account, supplierPassword)).Succeeded, workspace.Role + " test account created");
        Check((await users.AddToRoleAsync(account, workspace.Role)).Succeeded, workspace.Role + " assigned");
        if (workspace.Role == SystemRoles.SourcingOfficer) officerId = account.Id;
    }
    using var roleClient = Client(factory);
    var roleToken = await Token(roleClient, "/Identity/Account/Login");
    var roleLogin = await roleClient.PostAsync("/Identity/Account/Login", Form(new()
    {
        ["Input.Email"] = email, ["Input.Password"] = supplierPassword,
        ["__RequestVerificationToken"] = roleToken
    }));
    if (workspace.Role == SystemRoles.Supplier)
    {
        Check(roleLogin.StatusCode == HttpStatusCode.Redirect && roleLogin.Headers.Location?.ToString().Contains("PendingApproval") == true,
            "supplier password login routes to biometric setup");
        Check((await roleClient.GetAsync("/" + workspace.Area)).Headers.Location?.ToString().Contains("PendingApproval") == true,
            "supplier password cookie cannot open its dashboard");
    }
    else
    {
        Check(roleLogin.StatusCode == HttpStatusCode.Redirect && roleLogin.Headers.Location?.ToString() == "/" + workspace.Area,
            workspace.Role + " login routes to its dashboard");
        var dashboardResponse = await roleClient.GetAsync("/" + workspace.Area);
        Check(dashboardResponse.StatusCode == HttpStatusCode.OK,
            workspace.Role + " dashboard renders with authenticated cookie");
        if (workspace.Role == SystemRoles.SourcingManager)
        {
            var dashboardHtml = await dashboardResponse.Content.ReadAsStringAsync();
            Check(dashboardHtml.Contains("Pending Reviews") &&
                  dashboardHtml.Contains("/css/sourcing-manager.css") &&
                  dashboardHtml.Contains("/SourcingManager/ManagerReports/Pending"),
                "Sourcing Manager landing page shows Haya dashboard and reports navigation");
        }
    }
    if (workspace.Role != SystemRoles.Admin)
        Check((await roleClient.GetAsync("/Admin")).StatusCode != HttpStatusCode.OK, workspace.Role + " cannot enter Admin");
    using var anonymous = Client(factory);
    Check((await anonymous.GetAsync("/" + workspace.Area)).StatusCode == HttpStatusCode.Redirect,
        workspace.Role + " dashboard blocks anonymous access");
}
using (var scope = factory.Services.CreateScope())
{
    var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    var workflow = scope.ServiceProvider.GetRequiredService<TenderWorkflowService>();
    var officer = (await users.FindByIdAsync(officerId!))!;
    Check(await workflow.IsOfficer(officer), "one active Sourcing Officer has global management authority");
    var extra = new ApplicationUser { UserName = $"extra-officer-{suffix}@example.test", Email = $"extra-officer-{suffix}@example.test", EmailConfirmed = true,
        IsActive = true, FullName = "Isolated duplicate-officer guard check" };
    Check((await users.CreateAsync(extra)).Succeeded && (await users.AddToRoleAsync(extra, SystemRoles.SourcingOfficer)).Succeeded,
        "duplicate Officer fixture exists only in isolated SQL database");
    Check(!await workflow.IsOfficer(officer) && !await workflow.IsOfficer(extra), "multiple active Officers fail closed");
    extra.IsActive = false; Check((await users.UpdateAsync(extra)).Succeeded, "duplicate Officer deactivated");
    Check(await workflow.IsOfficer(officer), "single active Officer authority restored");
}

await BiometricChecks.Run(() => Client(factory), Token, Check, factory.Services, suffix);

using (var productionFactory = CreateFactory().WithWebHostBuilder(builder => builder.UseEnvironment("Production")))
using (var production = Client(productionFactory))
{
    foreach (var path in new[] { "/fingerprint-demo", "/fingerprint-demo/register-options", "/fingerprint-demo/register", "/fingerprint-demo/login-options", "/fingerprint-demo/verify", "/fingerprint-demo/reset", "/face-demo" })
    {
        Check((await production.GetAsync(path)).StatusCode == HttpStatusCode.NotFound, "Production GET " + path + " returns 404");
        var postStatus = (await production.PostAsync(path, new StringContent("{}"))).StatusCode;
        Check(postStatus is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed,
            "Production POST " + path + " is unavailable");
    }
}

Console.WriteLine("Authentication smoke checks passed.");

WebApplicationFactory<Orange_Tendering_System.WebAppMarker> CreateFactory() => new WebApplicationFactory<Orange_Tendering_System.WebAppMarker>().WithWebHostBuilder(builder =>
{
    // Razor pages are compiled into the app. Keep synthetic registration uploads out of
    // the real application's App_Data while loading the real application's settings.
    builder.UseContentRoot(isolatedContentRoot).UseEnvironment("Development");
    builder.ConfigureAppConfiguration((_, config) => config
        .AddJsonFile(Path.Combine(root, "appsettings.json"), optional: false)
        .AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["ConnectionStrings:Default Connection"] = connection,
        ["BootstrapAdmin:Email"] = adminEmail,
        ["BootstrapAdmin:Password"] = adminPassword,
        ["Smtp:Mode"] = "Smtp", ["Smtp:Host"] = "test.local", ["Smtp:Port"] = "587", ["Smtp:From"] = "sender@example.test",
        ["Smtp:Username"] = "unused", ["Smtp:Password"] = "unused",
        ["Biometric:MaxFailuresPerWindow"] = "100"
    }));
    builder.ConfigureTestServices(services =>
    {
        services.RemoveAll<IEmailSender>();
        services.AddSingleton<IEmailSender>(mail);
    });
});

static HttpClient Client(WebApplicationFactory<Orange_Tendering_System.WebAppMarker> factory) => factory.CreateClient(new()
{
    BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = true
});

static async Task<string> Token(HttpClient client, string path)
{
    var response = await client.GetAsync(path);
    var safePath = path.Split('?')[0];
    Check(response.StatusCode == HttpStatusCode.OK, "GET " + safePath);
    var html = await response.Content.ReadAsStringAsync();
    var match = Regex.Match(html, "name=\\\"__RequestVerificationToken\\\"[^>]*value=\\\"([^\\\"]+)\\\"");
    Check(match.Success, "antiforgery token on " + safePath);
    return WebUtility.HtmlDecode(match.Groups[1].Value);
}

static FormUrlEncodedContent Form(Dictionary<string, string> values) => new(values);
static ByteArrayContent FilePart(byte[] bytes)
{
    var content = new ByteArrayContent(bytes);
    content.Headers.ContentType = new("application/pdf");
    return content;
}
static MultipartFormDataContent RegistrationContent(string email, string password, string suffix, string token)
{
    var content = new MultipartFormDataContent();
    foreach (var field in new Dictionary<string, string>
    {
        ["Input.FullName"] = "Test Contact", ["Input.Email"] = email,
        ["Input.Password"] = password, ["Input.ConfirmPassword"] = password,
        ["Input.CompanyName"] = "Test Supplier " + suffix, ["Input.Category"] = "IT & Electronics",
        ["Input.RegistrationNumber"] = "CR-" + suffix,
        ["Input.CompanyEmail"] = "company-" + suffix + "@example.test",
        ["Input.CompanyPhone"] = "+962790000000", ["Input.CompanyAddress"] = "Amman, Jordan",
        ["__RequestVerificationToken"] = token
    }) content.Add(new StringContent(field.Value), field.Key);
    var pdf = Encoding.ASCII.GetBytes("%PDF-1.7\nTest legal document\n%%EOF");
    content.Add(FilePart(pdf), "CommercialRegistrationCertificate", "registration.pdf");
    content.Add(FilePart(pdf), "LegalDocuments", "license.pdf");
    return content;
}
static void Check(bool value, string name)
{
    if (!value) throw new InvalidOperationException("FAILED: " + name);
    Console.WriteLine("PASS " + name);
}

sealed class CapturedEmail : IEmailSender
{
    public ConcurrentQueue<(string To, string Subject, string Body)> Messages { get; } = new();
    public bool FailNext { get; set; }
    public Task SendEmailAsync(string email, string subject, string htmlMessage)
    {
        if (FailNext)
        {
            FailNext = false;
            throw new System.Net.Mail.SmtpException("Synthetic SMTP failure");
        }
        Messages.Enqueue((email, subject, htmlMessage));
        return Task.CompletedTask;
    }
}
