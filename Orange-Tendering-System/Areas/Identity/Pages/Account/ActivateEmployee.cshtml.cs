using System.ComponentModel.DataAnnotations;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models.Entities;

namespace Orange_Tendering_System.Areas.Identity.Pages.Account;

public class ActivateEmployeeModel(
    UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signIn,
    ApplicationDbContext db) : PageModel
{
    [BindProperty] public InputModel Input { get; set; } = new();
    public bool Activated { get; private set; }

    public class InputModel
    {
        [Required, EmailAddress] public string Email { get; set; } = string.Empty;
        [Required] public string Code { get; set; } = string.Empty;
        [Required, StringLength(100, MinimumLength = 8), DataType(DataType.Password)] public string Password { get; set; } = string.Empty;
        [Required, Compare(nameof(Password)), DataType(DataType.Password)] public string ConfirmPassword { get; set; } = string.Empty;
    }

    public IActionResult OnGet(string? email, string? code)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(code)) return BadRequest();
        Input.Email = email;
        Input.Code = code;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();
        var user = await users.FindByEmailAsync(Input.Email.Trim());
        var invitation = user is not null && await db.AuditLogs.AnyAsync(a =>
            a.EntityName == nameof(ApplicationUser) && a.EntityId == user.Id &&
            (a.Action == "Created" || a.Action == "EmployeeProvisioned"));
        if (user is not { SupplierId: null, MustChangePassword: true } || !invitation)
        {
            ModelState.AddModelError(string.Empty, "Activation link is invalid or expired.");
            return Page();
        }
        string token;
        try { token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(Input.Code)); }
        catch (FormatException)
        {
            ModelState.AddModelError(string.Empty, "Activation link is invalid or expired.");
            return Page();
        }
        await using var transaction = await db.Database.BeginTransactionAsync();
        var changed = await users.ResetPasswordAsync(user, token, Input.Password);
        if (!changed.Succeeded)
        {
            foreach (var error in changed.Errors) ModelState.AddModelError(string.Empty, error.Description);
            return Page();
        }
        user.EmailConfirmed = true;
        user.MustChangePassword = false;
        var updated = await users.UpdateAsync(user);
        if (!updated.Succeeded) throw new InvalidOperationException("Could not complete employee activation.");
        db.AuditLogs.Add(new AuditLog
        {
            UserId = user.Id, Action = "EmployeeActivated", EntityName = "ApplicationUser",
            EntityId = user.Id, CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        // The emailed link and the new password prove the account: sign in and offer the optional Face ID /
        // Fingerprint setup (EmployeeIdentity). Enrolling there re-checks the password, as everywhere else.
        if (user.IsActive && await signIn.CanSignInAsync(user))
        {
            await signIn.SignInAsync(user, isPersistent: false);
            return RedirectToPage("./EmployeeIdentity", new { activated = true });
        }
        Activated = true;
        return Page();
    }
}
