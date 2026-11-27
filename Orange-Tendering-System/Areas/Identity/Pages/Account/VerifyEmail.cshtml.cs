using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.Services;

namespace Orange_Tendering_System.Areas.Identity.Pages.Account;

/// <summary>
/// Step 2 of supplier registration: the six-digit email code (OTP).
/// Rules: the code expires after 10 minutes, 5 wrong codes block it, and a new code can be requested once a minute.
/// A correct code activates the account, notifies the administrators and signs the supplier in (Pending Approval page).
/// </summary>
public class VerifyEmailModel : PageModel
{
    // The same purpose name that Register.cshtml.cs uses to create the code.
    private const string Purpose = "SupplierEmailOtp";
    private const string InvalidCode = "The code is invalid or expired. Request a new code if needed.";
    // The same answer whether the email exists or not, so the page does not reveal registered emails.
    private const string ResendNotice = "If a pending registration exists and resending is allowed, a new code will be sent.";

    private readonly UserManager<ApplicationUser> users;
    private readonly SignInManager<ApplicationUser> signInManager;
    private readonly ApplicationDbContext db;
    private readonly IEmailSender emailSender;
    private readonly ILogger<VerifyEmailModel> logger;

    public VerifyEmailModel(
        UserManager<ApplicationUser> users,
        SignInManager<ApplicationUser> signInManager,
        ApplicationDbContext db,
        IEmailSender emailSender,
        ILogger<VerifyEmailModel> logger)
    {
        this.users = users;
        this.signInManager = signInManager;
        this.db = db;
        this.emailSender = emailSender;
        this.logger = logger;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new InputModel();

    public class InputModel
    {
        [Required, EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required, RegularExpression("^[0-9]{6}$", ErrorMessage = "Enter the six-digit code.")]
        public string Code { get; set; } = string.Empty;
    }

    // GET /Identity/Account/VerifyEmail?email=...
    public void OnGet(string? email)
    {
        Input.Email = email ?? string.Empty;
    }

    // POST /Identity/Account/VerifyEmail — check the code.
    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        // 1. A pending supplier with a code that is not expired and not blocked.
        ApplicationUser? user = await FindPendingSupplierAsync();
        if (user == null || user.EmailOtpSentAt == null)
        {
            ModelState.AddModelError(string.Empty, InvalidCode);
            return Page();
        }
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
            logger.LogWarning("Supplier email OTP attempt failed for user {UserId}.", user.Id);
            ModelState.AddModelError(string.Empty, InvalidCode);
            return Page();
        }

        // 3. Activate the account, notify every Admin and write the audit, all in one transaction.
        await using var transaction = await db.Database.BeginTransactionAsync();
        user.EmailConfirmed = true;
        user.IsActive = true;
        user.EmailOtpSentAt = null;
        user.EmailOtpFailedAttempts = 0;
        IdentityResult updated = await users.UpdateAsync(user);
        if (!updated.Succeeded)
        {
            throw new InvalidOperationException("Could not update supplier OTP state.");
        }
        // A new security stamp makes the used code invalid, so it cannot be used a second time.
        await users.UpdateSecurityStampAsync(user);

        Models.Entities.Supplier supplier = await db.Suppliers.SingleAsync(s => s.Id == user.SupplierId);
        IList<ApplicationUser> admins = await users.GetUsersInRoleAsync(SystemRoles.Admin);
        foreach (ApplicationUser admin in admins)
        {
            Notification notification = new Notification();
            notification.UserId = admin.Id;
            notification.Title = "Supplier awaiting review";
            notification.Message = supplier.CompanyName + " submitted a supplier registration.";
            notification.Link = "/Admin/Suppliers/Review/" + supplier.Id;
            notification.CreatedAt = DateTime.UtcNow;
            db.Notifications.Add(notification);
        }

        AuditLog registered = new AuditLog();
        registered.UserId = user.Id;
        registered.Action = "SupplierRegistered";
        registered.EntityName = "Supplier";
        registered.EntityId = supplier.Id.ToString();
        registered.CreatedAt = DateTime.UtcNow;
        db.AuditLogs.Add(registered);

        AuditLog verified = new AuditLog();
        verified.UserId = user.Id;
        verified.Action = "EmailVerified";
        verified.EntityName = "ApplicationUser";
        verified.EntityId = user.Id;
        verified.CreatedAt = DateTime.UtcNow;
        db.AuditLogs.Add(verified);

        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        logger.LogInformation("Supplier email verified for user {UserId}.", user.Id);

        // 4. Sign in (the Identity cookie) and go to the identity enrollment step.
        await signInManager.SignInAsync(user, false);
        TempData.Orange(OrangeFeedback.Popup("Registration complete!", "Your e-mail is verified. Enroll your face or a passkey, then wait for administrator approval.", OrangeVariant.Approve));
        return RedirectToPage("./PendingApproval");
    }

    // POST /Identity/Account/VerifyEmail?handler=Resend — send a new code.
    public async Task<IActionResult> OnPostResendAsync(string email)
    {
        Input.Email = email;
        ModelState.Clear();
        if (string.IsNullOrWhiteSpace(email))
        {
            return Page();
        }

        // Post/Redirect/Get: always go back to the plain Verify Email address, so a refresh does not post again.
        ApplicationUser? user = await FindPendingSupplierAsync();
        if (user == null)
        {
            TempData["Notice"] = ResendNotice;
            return RedirectToPage(new { email = email });
        }

        // Wait at least 1 minute between codes, and 10 minutes after 5 wrong codes.
        if (user.EmailOtpSentAt != null)
        {
            TimeSpan elapsed = DateTime.UtcNow - user.EmailOtpSentAt.Value;
            bool tooSoon = elapsed < TimeSpan.FromMinutes(1);
            bool stillBlocked = user.EmailOtpFailedAttempts >= 5 && elapsed < TimeSpan.FromMinutes(10);
            if (tooSoon || stillBlocked)
            {
                TempData["Notice"] = ResendNotice;
                return RedirectToPage(new { email = email });
            }
        }

        await using var transaction = await db.Database.BeginTransactionAsync();
        // A new security stamp makes the previous code invalid.
        IdentityResult stamped = await users.UpdateSecurityStampAsync(user);
        if (!stamped.Succeeded)
        {
            throw new InvalidOperationException("Could not rotate OTP state.");
        }
        user.EmailOtpSentAt = DateTime.UtcNow;
        user.EmailOtpFailedAttempts = 0;
        IdentityResult updated = await users.UpdateAsync(user);
        if (!updated.Succeeded)
        {
            throw new InvalidOperationException("Could not save OTP state.");
        }

        string code = await users.GenerateUserTokenAsync(user, TokenOptions.DefaultEmailProvider, Purpose);
        try
        {
            await emailSender.SendEmailAsync(user.Email!, "Verify your supplier email",
                "<p>Your new Orange Tendering verification code is <strong>" + code + "</strong>.</p><p>It expires in 10 minutes.</p>");
            await transaction.CommitAsync();
            TempData["Notice"] = ResendNotice;
            return RedirectToPage(new { email = email });
        }
        catch (Exception exception)
        {
            // The email failed: undo the new stamp, so the previous code keeps working.
            await transaction.RollbackAsync();
            if (exception is System.Net.Mail.SmtpException)
            {
                logger.LogWarning("Supplier OTP delivery failed with SMTP status {Status}.", ((System.Net.Mail.SmtpException)exception).StatusCode);
            }
            else
            {
                logger.LogError(exception, "Supplier OTP delivery failed.");
            }
            ModelState.AddModelError(string.Empty, "Could not send a new code. Please try again later.");
            return Page();
        }
    }

    // The account of the typed email, but only a supplier that has not verified its email yet.
    private async Task<ApplicationUser?> FindPendingSupplierAsync()
    {
        ApplicationUser? user = await users.FindByEmailAsync(Input.Email.Trim());
        if (user == null || user.SupplierId == null || user.EmailConfirmed)
        {
            return null;
        }
        return user;
    }
}
