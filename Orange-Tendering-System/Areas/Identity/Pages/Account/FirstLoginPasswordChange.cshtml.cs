using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.Services.Biometric;

namespace Orange_Tendering_System.Areas.Identity.Pages.Account;

/// <summary>
/// A user whose account has MustChangePassword (for example the first Admin) must choose a private password here.
/// FirstLoginGate (Services/IdentityAccess.cs) sends the user to this page until it is done.
/// </summary>
[Authorize]
public class FirstLoginPasswordChangeModel : PageModel
{
    private readonly UserManager<ApplicationUser> users;
    private readonly SignInManager<ApplicationUser> signInManager;
    private readonly ApplicationDbContext db;
    private readonly BiometricSignInService biometric;

    public FirstLoginPasswordChangeModel(
        UserManager<ApplicationUser> users,
        SignInManager<ApplicationUser> signInManager,
        ApplicationDbContext db,
        BiometricSignInService biometric)
    {
        this.users = users;
        this.signInManager = signInManager;
        this.db = db;
        this.biometric = biometric;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new InputModel();

    public class InputModel
    {
        [Required, DataType(DataType.Password)]
        public string CurrentPassword { get; set; } = string.Empty;

        [Required, StringLength(100, MinimumLength = 8), DataType(DataType.Password)]
        public string NewPassword { get; set; } = string.Empty;

        [Required, Compare(nameof(NewPassword)), DataType(DataType.Password)]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        ApplicationUser? user = await users.GetUserAsync(User);
        if (user != null && user.MustChangePassword)
        {
            return Page();
        }
        return Redirect("/Workspace");
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        ApplicationUser? user = await users.GetUserAsync(User);
        if (user == null)
        {
            return Challenge();
        }

        // Identity checks the current password and the password rules, then saves the new hash.
        IdentityResult result = await users.ChangePasswordAsync(user, Input.CurrentPassword, Input.NewPassword);
        if (!result.Succeeded)
        {
            foreach (IdentityError error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }
            return Page();
        }

        user.MustChangePassword = false;
        IdentityResult updated = await users.UpdateAsync(user);
        if (!updated.Succeeded)
        {
            throw new InvalidOperationException("Could not clear first-login password state.");
        }

        // The password change gave the account a new security stamp: issue a fresh cookie so this session stays valid.
        await signInManager.RefreshSignInAsync(user);

        AuditLog audit = new AuditLog();
        audit.UserId = user.Id;
        audit.Action = "FirstPasswordChanged";
        audit.EntityName = "ApplicationUser";
        audit.EntityId = user.Id;
        audit.CreatedAt = DateTime.UtcNow;
        db.AuditLogs.Add(audit);
        await db.SaveChangesAsync();

        string workspaceUrl = await biometric.WorkspaceUrlAsync(user);
        return LocalRedirect(workspaceUrl);
    }
}
