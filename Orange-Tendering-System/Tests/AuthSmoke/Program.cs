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
    Check(await db.Roles.CountAsync() == 7, "seven roles seeded");
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
    pendingLogin.Headers.Location?.ToString().Contains("PendingApproval") == true,
    "verified pending supplier can sign in again without OTP");
using (var scope = factory.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var company = await db.Suppliers.SingleAsync(s => s.RegistrationNumber == "CR-" + suffix);
    company.Status = SupplierStatus.Approved;
    await db.SaveChangesAsync();
}
var approvedLoginToken = await Token(supplier, "/Identity/Account/Login");
var approvedLogin = await supplier.PostAsync("/Identity/Account/Login", Form(new()
{
    ["Input.Email"] = supplierEmail, ["Input.Password"] = supplierPassword,
    ["__RequestVerificationToken"] = approvedLoginToken
}));
Check(approvedLogin.StatusCode == HttpStatusCode.Redirect && approvedLogin.Headers.Location?.ToString() == "/Supplier",
    "approved supplier redirected to Supplier workspace");
Check((await supplier.GetAsync("/Supplier")).StatusCode == HttpStatusCode.OK, "approved supplier workspace opens");
Check((await supplier.GetAsync("/Admin")).StatusCode != HttpStatusCode.OK, "supplier cannot enter Admin area");
using (var scope = factory.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var company = await db.Suppliers.SingleAsync(s => s.RegistrationNumber == "CR-" + suffix);
    company.Status = SupplierStatus.Suspended;
    await db.SaveChangesAsync();
}
Check((await supplier.GetAsync("/Supplier")).StatusCode != HttpStatusCode.OK,
    "existing supplier cookie revoked after suspension");
using (var scope = factory.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var company = await db.Suppliers.SingleAsync(s => s.RegistrationNumber == "CR-" + suffix);
    company.Status = SupplierStatus.Approved;
    await db.SaveChangesAsync();
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

// The Admin Create User screen supplies the password directly. Its accounts must
// already be confirmed because that screen has no email confirmation step.
var adminCreatedEmail = $"admin-created-{suffix}@example.test";
using (var scope = factory.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var departmentId = await db.Departments.Where(d => d.Name == "Auth Test Department " + suffix)
        .Select(d => d.Id).SingleAsync();
    var adminId = await db.Users.Where(u => u.Email == adminEmail).Select(u => u.Id).SingleAsync();
    var created = await scope.ServiceProvider.GetRequiredService<AdminService>().CreateUserAsync(
        new CreateUserViewModel
        {
            FullName = "Admin Created Employee", Email = adminCreatedEmail,
            Password = supplierPassword, Role = SystemRoles.RequestingDepartment,
            DepartmentId = departmentId, IsActive = true
        }, adminId);
    Check(created.Succeeded, "Admin Create User saves the employee");
    var account = await db.Users.SingleAsync(u => u.Email == adminCreatedEmail);
    Check(account.EmailConfirmed, "Admin-created employee is confirmed for password sign-in");
}
using (var adminCreated = Client(factory))
{
    var token = await Token(adminCreated, "/Identity/Account/Login");
    var response = await adminCreated.PostAsync("/Identity/Account/Login", Form(new()
    {
        ["Input.Email"] = adminCreatedEmail, ["Input.Password"] = supplierPassword,
        ["__RequestVerificationToken"] = token
    }));
    Check(response.StatusCode == HttpStatusCode.Redirect && response.Headers.Location?.ToString() == "/Department",
        "Admin-created employee signs in with the assigned role");
    Check((await adminCreated.GetAsync("/Department")).StatusCode == HttpStatusCode.OK,
        "Admin-created employee keeps an authenticated session");
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
    Check(await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Roles.CountAsync() == 7,
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
    Check(roleLogin.StatusCode == HttpStatusCode.Redirect && roleLogin.Headers.Location?.ToString() == "/" + workspace.Area,
        workspace.Role + " login routes to its dashboard");
    Check((await roleClient.GetAsync("/" + workspace.Area)).StatusCode == HttpStatusCode.OK,
        workspace.Role + " dashboard renders with authenticated cookie");
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
        Check((await production.PostAsync(path, new StringContent("{}"))).StatusCode == HttpStatusCode.NotFound, "Production POST " + path + " returns 404");
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
