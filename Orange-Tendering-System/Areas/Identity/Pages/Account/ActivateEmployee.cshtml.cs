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

/// <summary>
/// A new employee opens the link the administrator's invitation emailed (email + code) and chooses a password.
/// Then the employee is signed in and offered the optional Face ID / Fingerprint setup (EmployeeIdentity).
/// </summary>
public class ActivateEmployeeModel : PageModel
{
    private const string InvalidLink = "Activation link is invalid or expired.";

    private readonly UserManager<ApplicationUser> users;
    private readonly SignInManager<ApplicationUser> signInManager;
    private readonly ApplicationDbContext db;

    public ActivateEmployeeModel(UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signInManager, ApplicationDbContext db)
    {
        this.users = users;
        this.signInManager = signInManager;
        this.db = db;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new InputModel();

    public bool Activated { get; set; }

    public class InputModel
    {
        [Required, EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        public string Code { get; set; } = string.Empty;

        [Required, StringLength(100, MinimumLength = 8), DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [Required, Compare(nameof(Password)), DataType(DataType.Password)]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    // GET /Identity/Account/ActivateEmployee?email=...&code=...
    public IActionResult OnGet(string? email, string? code)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(code))
        {
            return BadRequest();
        }
        Input.Email = email;
        Input.Code = code;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        // 1. Only an internal account that the administrator created and that still needs its first password.
        ApplicationUser? user = await users.FindByEmailAsync(Input.Email.Trim());
        if (user == null || user.SupplierId != null || !user.MustChangePassword)
        {
            ModelState.AddModelError(string.Empty, InvalidLink);
            return Page();
        }
        bool invited = await db.AuditLogs.AnyAsync(a =>
            a.EntityName == nameof(ApplicationUser) && a.EntityId == user.Id &&
            (a.Action == "Created" || a.Action == "EmployeeProvisioned"));
        if (!invited)
        {
            ModelState.AddModelError(string.Empty, InvalidLink);
            return Page();
        }

        // 2. The code in the link is a Base64url-encoded Identity password-reset token.
        string token;
        try
        {
            token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(Input.Code));
        }
        catch (FormatException)
        {
            ModelState.AddModelError(string.Empty, InvalidLink);
            return Page();
        }

        // 3. Set the password and activate the account in one transaction.
        await using var transaction = await db.Database.BeginTransactionAsync();
        IdentityResult changed = await users.ResetPasswordAsync(user, token, Input.Password);
        if (!changed.Succeeded)
        {
            foreach (IdentityError error in changed.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }
            return Page();
        }

        user.EmailConfirmed = true;
        user.MustChangePassword = false;
        IdentityResult updated = await users.UpdateAsync(user);
        if (!updated.Succeeded)
        {
            throw new InvalidOperationException("Could not complete employee activation.");
        }

        AuditLog audit = new AuditLog();
        audit.UserId = user.Id;
        audit.Action = "EmployeeActivated";
        audit.EntityName = "ApplicationUser";
        audit.EntityId = user.Id;
        audit.CreatedAt = DateTime.UtcNow;
        db.AuditLogs.Add(audit);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        // 4. The emailed link and the new password prove the account: sign in and offer the optional identity setup.
        if (user.IsActive && await signInManager.CanSignInAsync(user))
        {
            await signInManager.SignInAsync(user, false);
            return RedirectToPage("./EmployeeIdentity", new { activated = true });
        }
        Activated = true;
        return Page();
    }
}
