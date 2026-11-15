using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Biometric;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.Services.Biometric;
using Orange_Tendering_System.ViewModels;

namespace Orange_Tendering_System.Areas.Identity.Pages.Account;

/// <summary>
/// One sign-in page for every account. Everybody starts with email and password. Employees, and suppliers
/// who are still setting up, are signed in by the password. An approved supplier with biometrics enrolled
/// is not: the correct password only opens a short pre-auth transaction (SupplierPreAuth) and the page moves
/// to the biometric step; the session is issued by /Biometrics/FaceLogin (or by a passkey) afterwards.
/// </summary>
public class LoginModel(
    UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signIn,
    ApplicationDbContext db,
    SupplierPreAuth preAuth,
    BiometricSignInService biometric,
    IOptions<BiometricOptions> biometricOptions,
    ILogger<LoginModel> logger) : PageModel
{
    // One message for an unknown email, a wrong password, a locked account and an account that may not sign in.
    private const string Rejected = "Invalid email or password. Please try again.";

    [BindProperty] public InputModel Input { get; set; } = new();
    public string? ReturnUrl { get; private set; }
    public int BiometricStepSeconds => biometricOptions.Value.PreAuthSeconds;

    /// <summary>Set when the password was accepted and the supplier's biometric step comes next.</summary>
    public BiometricStep? Biometric { get; private set; }

    public class InputModel
    {
        [Required, EmailAddress] public string Email { get; set; } = string.Empty;
        [Required, DataType(DataType.Password)] public string Password { get; set; } = string.Empty;
        [Display(Name = "Remember me")] public bool RememberMe { get; set; }
    }

    /// <summary>Which methods the account has enrolled. Told to the browser only after a correct password.</summary>
    public sealed record BiometricStep(bool Face, bool Passkey);

    private sealed record Accepted(string? Redirect, BiometricStep? Biometric);

    public async Task OnGetAsync(string? returnUrl = null)
    {
        ReturnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl : null;
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
        // Opening the page again starts over: an unfinished biometric step is abandoned.
        preAuth.End(HttpContext);
    }

    /// <summary>Plain form post (also the path without JavaScript).</summary>
    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        ReturnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl : null;
        var accepted = await CheckCredentialsAsync();
        if (accepted is null)
        {
            if (ModelState.IsValid) ModelState.AddModelError(string.Empty, Rejected);
            return Page();
        }

        if (accepted.Redirect is not null) return LocalRedirect(accepted.Redirect);
        Biometric = accepted.Biometric;
        return Page();
    }

    /// <summary>The same check for the page script (wwwroot/js/supplier-login.js), answered as JSON so the page can stay in place.</summary>
    public async Task<IActionResult> OnPostCredentialsAsync(string? returnUrl = null)
    {
        ReturnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl : null;
        var accepted = await CheckCredentialsAsync();
        if (accepted is null)
            return new JsonResult(new { outcome = "rejected", error = Rejected }) { StatusCode = StatusCodes.Status401Unauthorized };

        return accepted.Redirect is not null
            ? new JsonResult(new { outcome = "signedIn", redirect = accepted.Redirect })
            : new JsonResult(new { outcome = "biometric", face = accepted.Biometric!.Face, passkey = accepted.Biometric.Passkey });
    }

    /// <summary>The supplier left the biometric step ("Use a different account").</summary>
    public IActionResult OnPostCancel()
    {
        preAuth.End(HttpContext);
        return new JsonResult(new { cancelled = true });
    }

    /// <summary>Null when the sign-in is refused; otherwise where the signed-in user goes, or the biometric step that comes next.</summary>
    private async Task<Accepted?> CheckCredentialsAsync()
    {
        preAuth.End(HttpContext);
        if (!ModelState.IsValid) return null;

        var user = await users.FindByEmailAsync(Input.Email.Trim());
        if (user == null)
        {
            logger.LogWarning("Identity sign-in failed.");
            return null;
        }

        var result = await signIn.CheckPasswordSignInAsync(user, Input.Password, lockoutOnFailure: true);
        if (!result.Succeeded)
        {
            logger.LogWarning(result.IsLockedOut ? "Identity account locked out." : "Identity sign-in failed.");
            return null;
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
            return null;
        }

        // An approved supplier with biometrics enrolled is not signed in by the password. It opens the
        // pre-auth transaction; the supplier workspace needs the face (or passkey) session that follows.
        if (isSupplier && supplierStatus == SupplierStatus.Approved)
        {
            var face = await db.FaceTemplates.AnyAsync(t => t.UserId == user.Id);
            var passkey = (await users.GetPasskeysAsync(user)).Count > 0;
            if (face || passkey)
            {
                // Without a face there is nothing for the password step to lead to: the passkey signs in on its own.
                if (face) preAuth.Begin(HttpContext, user);
                await biometric.AuditAsync(user.Id, "BiometricStepStarted", "password",
                    HttpContext.Connection.RemoteIpAddress?.ToString(), null);
                return new Accepted(null, new BiometricStep(face, passkey));
            }
        }

        await signIn.SignInAsync(user, Input.RememberMe);
        logger.LogInformation("Identity sign-in succeeded for user {UserId}.", user.Id);
        db.AuditLogs.Add(new AuditLog
        {
            UserId = user.Id, Action = "SignIn", EntityName = "ApplicationUser", EntityId = user.Id,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        return new Accepted(await DestinationAsync(user, isSupplier), null);
    }

    private async Task<string> DestinationAsync(ApplicationUser user, bool isSupplier)
    {
        if (user.MustChangePassword)
            return Url.Page("./FirstLoginPasswordChange")!;
        // Password sign-in is for supplier setup only. Protected supplier routes
        // require a fresh face or passkey session in SupplierApprovalGate.
        if (isSupplier)
            return Url.Page("./PendingApproval")!;
        if (ReturnUrl != null)
            return ReturnUrl;

        var roles = await users.GetRolesAsync(user);
        var workspace = WorkspaceNavigation.Workspaces
            .Where(w => roles.Contains(w.Role))
            .OrderBy(w => w.Role == SystemRoles.SourcingManager ? -1 : 0)
            .FirstOrDefault();
        return workspace == null ? "/Workspace" : "/" + workspace.Area;
    }
}
