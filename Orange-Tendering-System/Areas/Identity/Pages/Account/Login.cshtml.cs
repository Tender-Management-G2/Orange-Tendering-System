using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.ViewModels;

namespace Orange_Tendering_System.Areas.Identity.Pages.Account;

public class LoginModel(
    UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signIn,
    ApplicationDbContext db,
    ILogger<LoginModel> logger) : PageModel
{
    [BindProperty] public InputModel Input { get; set; } = new();
    public string? ReturnUrl { get; private set; }

    public class InputModel
    {
        [Required, EmailAddress] public string Email { get; set; } = string.Empty;
        [Required, DataType(DataType.Password)] public string Password { get; set; } = string.Empty;
        [Display(Name = "Remember me")] public bool RememberMe { get; set; }
    }

    public async Task OnGetAsync(string? returnUrl = null)
    {
        ReturnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl : null;
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        ReturnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl : null;
        if (!ModelState.IsValid) return Page();

        var user = await users.FindByEmailAsync(Input.Email.Trim());
        if (user == null)
        {
            ModelState.AddModelError(string.Empty, "Invalid sign-in attempt.");
            logger.LogWarning("Identity sign-in failed.");
            return Page();
        }

        var result = await signIn.CheckPasswordSignInAsync(user, Input.Password, lockoutOnFailure: true);
        if (!result.Succeeded)
        {
            logger.LogWarning(result.IsLockedOut ? "Identity account locked out." : "Identity sign-in failed.");
            ModelState.AddModelError(string.Empty, "Invalid sign-in attempt.");
            return Page();
        }

        var isSupplier = await users.IsInRoleAsync(user, SystemRoles.Supplier);
        var supplierStatus = user.SupplierId == null ? null : await db.Suppliers
            .Where(s => s.Id == user.SupplierId)
            .Select(s => (SupplierStatus?)s.Status)
            .SingleOrDefaultAsync();
        var supplierAllowed = (!isSupplier && user.SupplierId == null) ||
            (isSupplier && (supplierStatus is SupplierStatus.Pending or SupplierStatus.Approved));
        if (!user.IsActive || !user.EmailConfirmed || !supplierAllowed)
        {
            logger.LogWarning("Identity sign-in denied by account state for user {UserId}.", user.Id);
            ModelState.AddModelError(string.Empty, "Invalid sign-in attempt.");
            return Page();
        }

        // Approved suppliers with biometrics enrolled must use them; the password is only for registration/setup.
        if (isSupplier && supplierStatus == SupplierStatus.Approved &&
            (await db.FaceTemplates.AnyAsync(t => t.UserId == user.Id) ||
             (await users.GetPasskeysAsync(user)).Count > 0))
        {
            ModelState.AddModelError(string.Empty, "This supplier account signs in with face recognition or a passkey. Use the buttons below the form.");
            return Page();
        }

        await signIn.SignInAsync(user, Input.RememberMe);
        logger.LogInformation("Identity sign-in succeeded for user {UserId}.", user.Id);
        db.AuditLogs.Add(new AuditLog
        {
            UserId = user.Id, Action = "SignIn", EntityName = "ApplicationUser", EntityId = user.Id,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        if (user.MustChangePassword)
            return RedirectToPage("./FirstLoginPasswordChange");
        if (isSupplier && supplierStatus == SupplierStatus.Pending)
            return RedirectToPage("./PendingApproval");
        if (ReturnUrl != null)
            return LocalRedirect(ReturnUrl);

        var roles = await users.GetRolesAsync(user);
        var workspace = WorkspaceNavigation.Workspaces
            .Where(w => roles.Contains(w.Role))
            .OrderBy(w => w.Role == SystemRoles.SourcingManager ? -1 : 0)
            .FirstOrDefault();
        return LocalRedirect(workspace == null ? "/Workspace" : "/" + workspace.Area);
    }
}
