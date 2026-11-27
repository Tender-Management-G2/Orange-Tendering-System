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

namespace Orange_Tendering_System.Areas.Identity.Pages.Account;

/// <summary>
/// One sign-in page for every account. The person first chooses a method:
///   - Email &amp; Password: checked here (OnPostCredentialsAsync from the page script, or OnPostAsync without JavaScript).
///   - Face ID / Fingerprint: OnPostIdentityStartAsync takes only the email and opens a short identity step
///     (SupplierPreAuth). The session is then issued by /Biometrics/FaceLogin or /Biometrics/PasskeyLogin, for that account only.
/// </summary>
public class LoginModel : PageModel
{
    // One message for an unknown email, a wrong password and an account that may not sign in,
    // so the page never reveals which emails exist.
    private const string Rejected = "Invalid email or password. Please try again.";

    private const string FaceUnavailable = "Face ID is not available for this email. Check the email, or sign in with Email & Password and set up Face ID from your account menu.";
    private const string FingerprintUnavailable = "Fingerprint is not available for this email. Check the email, or sign in with Email & Password and set up Fingerprint from your account menu.";

    private readonly UserManager<ApplicationUser> users;
    private readonly SignInManager<ApplicationUser> signInManager;
    private readonly ApplicationDbContext db;
    private readonly SupplierPreAuth preAuth;
    private readonly BiometricSignInService biometric;
    private readonly BiometricThrottle throttle;
    private readonly BiometricOptions biometricOptions;
    private readonly ILogger<LoginModel> logger;

    public LoginModel(
        UserManager<ApplicationUser> users,
        SignInManager<ApplicationUser> signInManager,
        ApplicationDbContext db,
        SupplierPreAuth preAuth,
        BiometricSignInService biometric,
        BiometricThrottle throttle,
        IOptions<BiometricOptions> biometricOptions,
        ILogger<LoginModel> logger)
    {
        this.users = users;
        this.signInManager = signInManager;
        this.db = db;
        this.preAuth = preAuth;
        this.biometric = biometric;
        this.throttle = throttle;
        this.biometricOptions = biometricOptions.Value;
        this.logger = logger;
    }

    // Model Binding fills Input from the form fields named "Input.Email", "Input.Password", ...
    [BindProperty]
    public InputModel Input { get; set; } = new InputModel();

    public string? ReturnUrl { get; set; }

    /// <summary>Shown to the page script (data-expires-in) so it can warn before the identity step expires.</summary>
    public int BiometricStepSeconds { get; set; }

    public class InputModel
    {
        [Required, EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required, DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [Display(Name = "Remember me")]
        public bool RememberMe { get; set; }

        /// <summary>"supplier" or "other": what the person says they are. Only a preference: the server compares it
        /// with the real role after a correct password.</summary>
        public string? AccountType { get; set; }
    }

    // Filled by SignInWithPasswordAsync, read by the two POST handlers below.
    private string? redirectUrl;
    private string? refusalMessage;

    // GET /Identity/Account/Login
    public async Task OnGetAsync(string? returnUrl)
    {
        ReturnUrl = LocalOrNull(returnUrl);
        BiometricStepSeconds = biometricOptions.PreAuthSeconds;
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
        // Opening the page again starts over: an unfinished Face ID / Fingerprint step is abandoned.
        preAuth.End(HttpContext);
    }

    // POST /Identity/Account/Login — the normal form post (used when JavaScript is off). Answers with a page.
    public async Task<IActionResult> OnPostAsync(string? returnUrl)
    {
        ReturnUrl = LocalOrNull(returnUrl);
        BiometricStepSeconds = biometricOptions.PreAuthSeconds;

        string outcome = await SignInWithPasswordAsync();
        if (outcome == "signedIn")
        {
            return LocalRedirect(redirectUrl!);
        }

        if (outcome == "rejected")
        {
            // When the fields themselves are invalid, their own messages are shown instead.
            if (ModelState.IsValid)
            {
                ModelState.AddModelError(string.Empty, Rejected);
            }
        }
        else
        {
            ModelState.AddModelError(string.Empty, refusalMessage!);
        }
        return Page();
    }

    // POST /Identity/Account/Login?handler=Credentials — the same check for wwwroot/js/supplier-login.js. Answers with JSON.
    public async Task<IActionResult> OnPostCredentialsAsync(string? returnUrl)
    {
        ReturnUrl = LocalOrNull(returnUrl);

        string outcome = await SignInWithPasswordAsync();
        if (outcome == "signedIn")
        {
            return new JsonResult(new { outcome = "signedIn", redirect = redirectUrl });
        }
        if (outcome == "locked")
        {
            return new JsonResult(new { outcome = "locked", error = refusalMessage }) { StatusCode = StatusCodes.Status423Locked };
        }
        if (outcome == "mismatch")
        {
            return new JsonResult(new { outcome = "mismatch", error = refusalMessage }) { StatusCode = StatusCodes.Status409Conflict };
        }
        return new JsonResult(new { outcome = "rejected", error = Rejected }) { StatusCode = StatusCodes.Status401Unauthorized };
    }

    /// <summary>
    /// POST /Identity/Account/Login?handler=IdentityStart — the person chose Face ID or Fingerprint and typed an email (no password).
    /// For an allowed account that has this method set up, this opens the identity step for that account. Nothing is signed in here.
    /// Any other email gets the same "not available" answer, so the page does not reveal which accounts exist.
    /// </summary>
    public async Task<IActionResult> OnPostIdentityStartAsync(string? method)
    {
        preAuth.End(HttpContext);

        bool face = method == "face";
        if (!face && method != "fingerprint")
        {
            return BadRequest(new { error = "Choose Face ID or Fingerprint." });
        }

        string? ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        string ipKey = BiometricThrottle.IpKey(ip);
        if (throttle.IsBlocked(ipKey) || !throttle.TryUse("identity-start:" + ipKey, 30))
        {
            return new JsonResult(new { error = "Too many attempts. Wait a few minutes, then try again." }) { StatusCode = StatusCodes.Status429TooManyRequests };
        }

        string email = (Input.Email ?? string.Empty).Trim();
        if (email.Length == 0 || !new EmailAddressAttribute().IsValid(email))
        {
            return BadRequest(new { error = "Enter a valid email address, for example name@company.com." });
        }

        // Is this account ready for the chosen method?
        ApplicationUser? user = await users.FindByEmailAsync(email);
        bool ready = false;
        if (user != null && user.EmailConfirmed && await biometric.AccountAllowedAsync(user))
        {
            if (face)
            {
                ready = await db.FaceTemplates.AnyAsync(t => t.UserId == user.Id);
            }
            else
            {
                ready = (await users.GetPasskeysAsync(user)).Count > 0;
            }
        }

        if (!ready || user == null)
        {
            logger.LogInformation("Identity step refused for a {Method} sign-in.", method);
            string message = FingerprintUnavailable;
            if (face)
            {
                message = FaceUnavailable;
            }
            return new JsonResult(new { outcome = "unavailable", error = message }) { StatusCode = StatusCodes.Status409Conflict };
        }

        preAuth.Begin(HttpContext, user);
        string methodName = "fingerprint";
        if (face)
        {
            methodName = "face";
        }
        await biometric.AuditAsync(user.Id, "BiometricStepStarted", methodName, ip, "email");
        return new JsonResult(new { outcome = "ready", method = method });
    }

    // POST /Identity/Account/Login?handler=Cancel — the person left the Face ID / Fingerprint step.
    public IActionResult OnPostCancel()
    {
        preAuth.End(HttpContext);
        return new JsonResult(new { cancelled = true });
    }

    /// <summary>
    /// Checks email and password and signs in. Returns one of:
    ///   "signedIn" (redirectUrl is set), "rejected", "locked" or "mismatch" (refusalMessage is set).
    /// </summary>
    private async Task<string> SignInWithPasswordAsync()
    {
        preAuth.End(HttpContext);
        if (!ModelState.IsValid)
        {
            return "rejected";
        }

        // 1. Find the account.
        ApplicationUser? user = await users.FindByEmailAsync(Input.Email.Trim());
        if (user == null)
        {
            logger.LogWarning("Identity sign-in failed.");
            return "rejected";
        }

        // 2. Check the password. true = a wrong password counts toward the lockout (5 tries, 15 minutes).
        var result = await signInManager.CheckPasswordSignInAsync(user, Input.Password, true);
        if (result.IsLockedOut)
        {
            logger.LogWarning("Identity sign-in refused: account {UserId} is locked out.", user.Id);
            refusalMessage = LockedOutMessage(await users.GetLockoutEndDateAsync(user));
            return "locked";
        }
        if (!result.Succeeded)
        {
            logger.LogWarning("Identity sign-in failed.");
            return "rejected";
        }

        // 3. Check the account state: active, email confirmed, and a supplier company that is Pending or Approved.
        bool isSupplier = await users.IsInRoleAsync(user, SystemRoles.Supplier);
        SupplierStatus? supplierStatus = null;
        if (user.SupplierId != null)
        {
            supplierStatus = await db.Suppliers
                .Where(s => s.Id == user.SupplierId)
                .Select(s => (SupplierStatus?)s.Status)
                .SingleOrDefaultAsync();
        }

        bool supplierAllowed;
        if (isSupplier)
        {
            supplierAllowed = supplierStatus == SupplierStatus.Pending || supplierStatus == SupplierStatus.Approved;
        }
        else
        {
            supplierAllowed = user.SupplierId == null;
        }

        if (!user.IsActive || !user.EmailConfirmed || !supplierAllowed)
        {
            logger.LogWarning("Identity sign-in denied by account state for user {UserId}.", user.Id);
            return "rejected";
        }

        // 4. The "Supplier / Other User" choice must match the real role. Nobody is signed in on a mismatch.
        string chosen = (Input.AccountType ?? string.Empty).Trim().ToLowerInvariant();
        if (chosen == "supplier" && !isSupplier)
        {
            logger.LogInformation("Account type choice did not match the account for user {UserId}.", user.Id);
            refusalMessage = "This account is not a supplier account. Choose Other User above, then sign in again.";
            return "mismatch";
        }
        if (chosen == "other" && isSupplier)
        {
            logger.LogInformation("Account type choice did not match the account for user {UserId}.", user.Id);
            refusalMessage = "This is a supplier account. Choose Supplier above, then sign in again.";
            return "mismatch";
        }

        // 5a. An approved supplier gets the "password step" session that SupplierApprovalGate accepts for the supplier
        //     workspace (while Biometric:SupplierPasswordOnly is true).
        if (isSupplier && supplierStatus == SupplierStatus.Approved && biometricOptions.SupplierPasswordOnly)
        {
            string? ip = HttpContext.Connection.RemoteIpAddress?.ToString();
            bool signedIn = await biometric.SignInAsync(user, BiometricSignInService.PasswordStepMethod, ip);
            if (!signedIn)
            {
                return "rejected";
            }
            redirectUrl = await biometric.DestinationAsync(user, ReturnUrl, Url);
            return "signedIn";
        }

        // 5b. Everyone else: the normal Identity cookie (persistent when "Remember me" is ticked).
        await signInManager.SignInAsync(user, Input.RememberMe);
        logger.LogInformation("Identity sign-in succeeded for user {UserId}.", user.Id);

        AuditLog audit = new AuditLog();
        audit.UserId = user.Id;
        audit.Action = "SignIn";
        audit.EntityName = "ApplicationUser";
        audit.EntityId = user.Id;
        audit.CreatedAt = DateTime.UtcNow;
        db.AuditLogs.Add(audit);
        await db.SaveChangesAsync();

        // 6. Where to go next.
        if (user.MustChangePassword)
        {
            redirectUrl = Url.Page("./FirstLoginPasswordChange");
        }
        else if (isSupplier)
        {
            // A pending supplier (or one whose password sign-in may not open the workspace) goes to Pending Approval.
            redirectUrl = Url.Page("./PendingApproval");
        }
        else if (ReturnUrl != null)
        {
            redirectUrl = ReturnUrl;
        }
        else
        {
            redirectUrl = await biometric.WorkspaceUrlAsync(user);
        }
        return "signedIn";
    }

    // A local address is kept; any other address (another website) is ignored, to stop open redirects.
    private string? LocalOrNull(string? returnUrl)
    {
        if (Url.IsLocalUrl(returnUrl))
        {
            return returnUrl;
        }
        return null;
    }

    // A locked account is told so, with the minutes left. Otherwise people kept typing the right password for 15 minutes.
    private static string LockedOutMessage(DateTimeOffset? lockoutEnd)
    {
        int minutes = 15;
        if (lockoutEnd != null)
        {
            minutes = (int)Math.Ceiling((lockoutEnd.Value - DateTimeOffset.UtcNow).TotalMinutes);
            if (minutes < 1)
            {
                minutes = 1;
            }
        }

        string unit = "minutes";
        if (minutes == 1)
        {
            unit = "minute";
        }
        return "This account is temporarily locked after too many unsuccessful sign-in attempts. Try again in " + minutes + " " + unit + ".";
    }
}
