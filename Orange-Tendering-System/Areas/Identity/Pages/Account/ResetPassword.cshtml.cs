using System.ComponentModel.DataAnnotations;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models.Entities;

namespace Orange_Tendering_System.Areas.Identity.Pages.Account;

/// <summary>
/// Step 2 of password recovery. The person opens the one-hour link from ForgotPassword (it carries the reset code),
/// types the email and a new password, and Identity checks the code and saves the new password hash.
/// </summary>
public class ResetPasswordModel : PageModel
{
    private readonly UserManager<ApplicationUser> users;
    private readonly ApplicationDbContext db;

    public ResetPasswordModel(UserManager<ApplicationUser> users, ApplicationDbContext db)
    {
        this.users = users;
        this.db = db;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new InputModel();

    public class InputModel
    {
        [Required, EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required, DataType(DataType.Password)]
        [StringLength(100, ErrorMessage = "The {0} must be at least {2} and at max {1} characters long.", MinimumLength = 6)]
        public string Password { get; set; } = string.Empty;

        [DataType(DataType.Password), Display(Name = "Confirm password")]
        [Compare("Password", ErrorMessage = "The password and confirmation password do not match.")]
        public string ConfirmPassword { get; set; } = string.Empty;

        /// <summary>The reset code from the link (kept in a hidden field).</summary>
        [Required]
        public string Code { get; set; } = string.Empty;
    }

    // GET /Identity/Account/ResetPassword?code=... (the code is Base64url-encoded in the link)
    public IActionResult OnGet(string? code)
    {
        if (code == null)
        {
            return BadRequest("A code must be supplied for password reset.");
        }

        try
        {
            Input.Code = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code));
        }
        catch (FormatException)
        {
            return BadRequest("Invalid reset link.");
        }
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        ApplicationUser? user = await users.FindByEmailAsync(Input.Email);
        if (user == null)
        {
            // The same answer as a success, so the page does not reveal which emails exist.
            return RedirectToPage("./ResetPasswordConfirmation");
        }

        // Identity checks that the code is valid, unused and less than one hour old, then saves the new hash.
        IdentityResult result = await users.ResetPasswordAsync(user, Input.Code, Input.Password);
        if (!result.Succeeded)
        {
            foreach (IdentityError error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }
            return Page();
        }

        AuditLog audit = new AuditLog();
        audit.UserId = user.Id;
        audit.Action = "PasswordReset";
        audit.EntityName = "ApplicationUser";
        audit.EntityId = user.Id;
        audit.CreatedAt = DateTime.UtcNow;
        db.AuditLogs.Add(audit);
        await db.SaveChangesAsync();
        return RedirectToPage("./ResetPasswordConfirmation");
    }
}
