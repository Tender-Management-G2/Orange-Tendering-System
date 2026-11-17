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
/// One sign-in page for every account (owner decision 2026-11-16). A supplier first chooses one method:
/// Email &amp; Password, Face ID or Fingerprint. Other users sign in with email and password.
///   - Email &amp; Password: checked here and signed in directly (an approved supplier gets the password-step session).
///   - Face ID / Fingerprint: the email opens a short identity step (SupplierPreAuth, handler IdentityStart); the
///     session is issued afterwards by /Biometrics/FaceLogin or /Biometrics/PasskeyLogin, for that account only.
/// </summary>
public class LoginModel(
    UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signIn,
    ApplicationDbContext db,
    SupplierPreAuth preAuth,
    BiometricSignInService biometric,
    BiometricThrottle throttle,
    IOptions<BiometricOptions> biometricOptions,
    ILogger<LoginModel> logger) : PageModel
{
    // One message for an unknown email, a wrong password and an account that may not sign in.
    private const string Rejected = "Invalid email or password. Please try again.";

    // A locked account is told so (as the default Identity UI does with its Lockout page). With the generic message
    // a person kept typing the right password during the 15 minutes and concluded the account was broken.
    private static string LockedOut(DateTimeOffset? until)
    {
        var minutes = until is { } end ? Math.Max(1, (int)Math.Ceiling((end - DateTimeOffset.UtcNow).TotalMinutes)) : 15;
        return $"This account is temporarily locked after too many unsuccessful sign-in attempts. Try again in {minutes} minute{(minutes == 1 ? "" : "s")}.";
    }

    // The same answer whatever is wrong with the email (unknown, not a supplier, not approved, nothing set up).
    private const string FaceUnavailable = "Face ID is not available for this email. Check the email, or sign in with Email & Password and set up Face ID from your account menu.";
    private const string FingerprintUnavailable = "Fingerprint is not available for this email. Check the email, or sign in with Email & Password and set up Fingerprint from your account menu.";

    [BindProperty] public InputModel Input { get; set; } = new();
    public string? ReturnUrl { get; private set; }
    public int BiometricStepSeconds => biometricOptions.Value.PreAuthSeconds;

    public class InputModel
    {
        [Required, EmailAddress] public string Email { get; set; } = string.Empty;
        [Required, DataType(DataType.Password)] public string Password { get; set; } = string.Empty;
        [Display(Name = "Remember me")] public bool RememberMe { get; set; }

        /// <summary>"supplier" or "other": what the person says they are. A UI preference, never authorization:
        /// the server checks it against the account's real role only after a correct password.</summary>
        public string? AccountType { get; set; }
    }

    /// <summary>Mismatch: the password was right but the chosen account type was not; nothing is signed in.
    /// Locked: the account is locked out; nothing is signed in.</summary>
    private sealed record Accepted(string? Redirect, string? Mismatch = null, string? Locked = null);

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

        if ((accepted.Locked ?? accepted.Mismatch) is { } refused)
        {
            ModelState.AddModelError(string.Empty, refused);
            return Page();
        }
        return LocalRedirect(accepted.Redirect!);
    }

    /// <summary>The same check for the page script (wwwroot/js/supplier-login.js), answered as JSON so the page can stay in place.</summary>
    public async Task<IActionResult> OnPostCredentialsAsync(string? returnUrl = null)
    {
        ReturnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl : null;
        var accepted = await CheckCredentialsAsync();
        if (accepted is null)
            return new JsonResult(new { outcome = "rejected", error = Rejected }) { StatusCode = StatusCodes.Status401Unauthorized };

        if (accepted.Locked is not null)
            return new JsonResult(new { outcome = "locked", error = accepted.Locked }) { StatusCode = StatusCodes.Status423Locked };
        if (accepted.Mismatch is not null)
            return new JsonResult(new { outcome = "mismatch", error = accepted.Mismatch }) { StatusCode = StatusCodes.Status409Conflict };
        return new JsonResult(new { outcome = "signedIn", redirect = accepted.Redirect });
    }

    /// <summary>
    /// The supplier chose Face ID or Fingerprint and typed an email (no password). For an approved, active supplier
    /// with that method set up, this opens the short identity step for that account; /Biometrics/FaceChallenge +
    /// FaceLogin, or PasskeyLoginOptions + PasskeyLogin (and the phone QR code) then work only for it. Nothing is
    /// signed in here. Any other email gets the same "not available" answer and no usable step.
    /// </summary>
    public async Task<IActionResult> OnPostIdentityStartAsync(string? method)
    {
        preAuth.End(HttpContext);
        var face = method == "face";
        if (!face && method != "fingerprint") return BadRequest(new { error = "Choose Face ID or Fingerprint." });

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var ipKey = BiometricThrottle.IpKey(ip);
        if (throttle.IsBlocked(ipKey) || !throttle.TryUse("identity-start:" + ipKey, 30))
            return new JsonResult(new { error = "Too many attempts. Wait a few minutes, then try again." }) { StatusCode = StatusCodes.Status429TooManyRequests };

        var email = Input.Email?.Trim() ?? string.Empty;
        if (!new EmailAddressAttribute().IsValid(email) || email.Length == 0)
            return BadRequest(new { error = "Enter a valid email address, for example name@company.com." });

        var user = await users.FindByEmailAsync(email);
        var ready = user is not null && user.EmailConfirmed &&
            await users.IsInRoleAsync(user, SystemRoles.Supplier) &&
            await biometric.AccountAllowedAsync(user) &&
            (face
                ? await db.FaceTemplates.AnyAsync(t => t.UserId == user.Id)
                : (await users.GetPasskeysAsync(user)).Count > 0);
        if (!ready)
        {
            logger.LogInformation("Identity step refused for a {Method} sign-in.", method);
            return new JsonResult(new { outcome = "unavailable", error = face ? FaceUnavailable : FingerprintUnavailable })
                { StatusCode = StatusCodes.Status409Conflict };
        }

        preAuth.Begin(HttpContext, user!);
        await biometric.AuditAsync(user!.Id, "BiometricStepStarted", face ? "face" : "fingerprint", ip, "email");
        return new JsonResult(new { outcome = "ready", method });
    }

    /// <summary>The supplier left the Face ID / Fingerprint step.</summary>
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
        if (result.IsLockedOut)
        {
            logger.LogWarning("Identity sign-in refused: account {UserId} is locked out.", user.Id);
            return new Accepted(null, Locked: LockedOut(await users.GetLockoutEndDateAsync(user)));
        }
        if (!result.Succeeded)
        {
            logger.LogWarning("Identity sign-in failed.");
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

        // The account-type choice is checked against the real role, after the password: a mismatch signs nobody in.
        var chosen = Input.AccountType?.Trim().ToLowerInvariant();
        if (chosen is "supplier" or "other" && (chosen == "supplier") != isSupplier)
        {
            logger.LogInformation("Account type choice did not match the account for user {UserId}.", user.Id);
            return new Accepted(null, isSupplier
                ? "This is a supplier account. Choose Supplier above, then sign in again."
                : "This account is not a supplier account. Choose Other User above, then sign in again.");
        }

        // "Email & Password" is one of the supplier's three methods: an approved supplier gets the signed
        // password-step session that SupplierApprovalGate accepts while Biometric:SupplierPasswordOnly is on.
        // With the switch off, the password only opens supplier setup (Pending Approval) below.
        if (isSupplier && supplierStatus == SupplierStatus.Approved && biometricOptions.Value.SupplierPasswordOnly)
        {
            var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
            if (!await biometric.SignInAsync(user, BiometricSignInService.PasswordStepMethod, ip)) return null;
            return new Accepted(await biometric.DestinationAsync(user, ReturnUrl, Url.IsLocalUrl));
        }

        await signIn.SignInAsync(user, Input.RememberMe);
        logger.LogInformation("Identity sign-in succeeded for user {UserId}.", user.Id);
        db.AuditLogs.Add(new AuditLog
        {
            UserId = user.Id, Action = "SignIn", EntityName = "ApplicationUser", EntityId = user.Id,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        return new Accepted(await DestinationAsync(user, isSupplier));
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
