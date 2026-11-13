using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.ViewModels;

namespace Orange_Tendering_System.Areas.Identity.Pages.Account;

[Authorize]
public class FirstLoginPasswordChangeModel(
    UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signIn,
    ApplicationDbContext db) : PageModel
{
    [BindProperty] public InputModel Input { get; set; } = new();
    public class InputModel
    {
        [Required, DataType(DataType.Password)] public string CurrentPassword { get; set; } = string.Empty;
        [Required, StringLength(100, MinimumLength = 8), DataType(DataType.Password)] public string NewPassword { get; set; } = string.Empty;
        [Required, Compare(nameof(NewPassword)), DataType(DataType.Password)] public string ConfirmPassword { get; set; } = string.Empty;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await users.GetUserAsync(User);
        return user?.MustChangePassword == true ? Page() : Redirect("/Workspace");
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();
        var user = await users.GetUserAsync(User);
        if (user == null) return Challenge();
        var result = await users.ChangePasswordAsync(user, Input.CurrentPassword, Input.NewPassword);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors) ModelState.AddModelError(string.Empty, error.Description);
            return Page();
        }
        user.MustChangePassword = false;
        var updated = await users.UpdateAsync(user);
        if (!updated.Succeeded) throw new InvalidOperationException("Could not clear first-login password state.");
        await signIn.RefreshSignInAsync(user);
        db.AuditLogs.Add(new AuditLog
        {
            UserId = user.Id, Action = "FirstPasswordChanged", EntityName = "ApplicationUser",
            EntityId = user.Id, CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var roles = await users.GetRolesAsync(user);
        var workspace = WorkspaceNavigation.Workspaces
            .Where(w => roles.Contains(w.Role))
            .OrderBy(w => w.Role == SystemRoles.SourcingManager ? -1 : 0)
            .FirstOrDefault();
        return LocalRedirect(workspace == null ? "/Workspace" : "/" + workspace.Area);
    }
}
