using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;

namespace Orange_Tendering_System.Areas.Identity.Pages.Account;

public class VerifyEmailModel(
    UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signIn,
    ApplicationDbContext db,
    IEmailSender emailSender,
    ILogger<VerifyEmailModel> logger) : PageModel
{
    private const string Purpose = "SupplierEmailOtp";
    [BindProperty] public InputModel Input { get; set; } = new();

    public class InputModel
    {
        [Required, EmailAddress] public string Email { get; set; } = string.Empty;
        [Required, RegularExpression("^[0-9]{6}$", ErrorMessage = "Enter the six-digit code.")]
        public string Code { get; set; } = string.Empty;
    }

    public void OnGet(string? email) => Input.Email = email ?? string.Empty;

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();
        var user = await FindPendingSupplierAsync();
        if (user == null || user.EmailOtpSentAt == null ||
            DateTime.UtcNow - user.EmailOtpSentAt > TimeSpan.FromMinutes(10) ||
            user.EmailOtpFailedAttempts >= 5)
        {
            ModelState.AddModelError(string.Empty, "The code is invalid or expired. Request a new code if needed.");
            return Page();
        }

        if (!await users.VerifyUserTokenAsync(user, TokenOptions.DefaultEmailProvider, Purpose, Input.Code))
        {
            user.EmailOtpFailedAttempts++;
            await users.UpdateAsync(user);
            logger.LogWarning("Supplier email OTP attempt failed for user {UserId}.", user.Id);
            ModelState.AddModelError(string.Empty, "The code is invalid or expired. Request a new code if needed.");
            return Page();
        }

        await using var transaction = await db.Database.BeginTransactionAsync();
        user.EmailConfirmed = true;
        user.IsActive = true;
        user.EmailOtpSentAt = null;
        user.EmailOtpFailedAttempts = 0;
        var updated = await users.UpdateAsync(user);
        if (!updated.Succeeded) throw new InvalidOperationException("Could not update supplier OTP state.");
        await users.UpdateSecurityStampAsync(user);
        var supplier = await db.Suppliers.SingleAsync(s => s.Id == user.SupplierId);
        var admins = await users.GetUsersInRoleAsync(SystemRoles.Admin);
        foreach (var admin in admins)
            db.Notifications.Add(new Notification
            {
                UserId = admin.Id, Title = "Supplier awaiting review",
                Message = $"{supplier.CompanyName} submitted a supplier registration.",
                Link = "/Admin/Suppliers/Review/" + supplier.Id,
                CreatedAt = DateTime.UtcNow
            });
        db.AuditLogs.Add(new AuditLog
        {
            UserId = user.Id, Action = "SupplierRegistered", EntityName = "Supplier",
            EntityId = supplier.Id.ToString(), CreatedAt = DateTime.UtcNow
        });
        db.AuditLogs.Add(new AuditLog
        {
            UserId = user.Id, Action = "EmailVerified", EntityName = "ApplicationUser",
            EntityId = user.Id, CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        logger.LogInformation("Supplier email verified for user {UserId}.", user.Id);
        await signIn.SignInAsync(user, isPersistent: false);
        return RedirectToPage("./PendingApproval");
    }

    public async Task<IActionResult> OnPostResendAsync(string email)
    {
        Input.Email = email;
        ModelState.Clear();
        if (string.IsNullOrWhiteSpace(email)) return Page();
        // Post/Redirect/Get: back to the plain Verify Email URL, so the verify form and a page refresh behave normally.
        var user = await FindPendingSupplierAsync();
        if (user == null)
        {
            TempData["Notice"] = "If a pending registration exists and resending is allowed, a new code will be sent.";
            return RedirectToPage(new { email });
        }
        var elapsed = user.EmailOtpSentAt is DateTime sent ? DateTime.UtcNow - sent : TimeSpan.MaxValue;
        if (user.EmailOtpSentAt != null &&
            (elapsed < TimeSpan.FromMinutes(1) ||
             (user.EmailOtpFailedAttempts >= 5 && elapsed < TimeSpan.FromMinutes(10))))
        {
            TempData["Notice"] = "If a pending registration exists and resending is allowed, a new code will be sent.";
            return RedirectToPage(new { email });
        }

        await using var transaction = await db.Database.BeginTransactionAsync();
        var stamped = await users.UpdateSecurityStampAsync(user);
        if (!stamped.Succeeded) throw new InvalidOperationException("Could not rotate OTP state.");
        user.EmailOtpSentAt = DateTime.UtcNow;
        user.EmailOtpFailedAttempts = 0;
        var updated = await users.UpdateAsync(user);
        if (!updated.Succeeded) throw new InvalidOperationException("Could not save OTP state.");
        var code = await users.GenerateUserTokenAsync(user, TokenOptions.DefaultEmailProvider, Purpose);
        try
        {
            await emailSender.SendEmailAsync(user.Email!, "Verify your supplier email",
                $"<p>Your new Orange Tendering verification code is <strong>{code}</strong>.</p><p>It expires in 10 minutes.</p>");
            await transaction.CommitAsync();
            TempData["Notice"] = "If a pending registration exists and resending is allowed, a new code will be sent.";
            return RedirectToPage(new { email });
        }
        catch (Exception exception)
        {
            await transaction.RollbackAsync();
            if (exception is System.Net.Mail.SmtpException smtpException)
                logger.LogWarning("Supplier OTP delivery failed with SMTP status {Status}.", smtpException.StatusCode);
            else
                logger.LogError(exception, "Supplier OTP delivery failed.");
            ModelState.AddModelError(string.Empty, "Could not send a new code. Please try again later.");
        }
        return Page();
    }

    private async Task<ApplicationUser?> FindPendingSupplierAsync()
    {
        var user = await users.FindByEmailAsync(Input.Email.Trim());
        return user is { SupplierId: not null, EmailConfirmed: false } ? user : null;
    }
}
