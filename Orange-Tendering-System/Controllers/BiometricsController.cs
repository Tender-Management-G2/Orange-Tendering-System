// ORANGE TENDER MANAGEMENT SYSTEM — Owner: Ahmad — Module: Authentication & Biometrics
// Face ID and Fingerprint (WebAuthn passkey) on top of ASP.NET Core Identity.
//   - GET  /Biometrics/Fingerprint and /Biometrics/Face: the enrollment pages of the signed-in user.
//   - POST endpoints: called with fetch() by wwwroot/js/biometric.js. Every POST checks the anti-forgery token.
// Rules:
//   - Enrollment always re-checks the current password first.
//   - Face sign-in works only inside the identity step that the Login page opened for one account (SupplierPreAuth).
//   - Signing in needs an active account and, for suppliers, an Approved company (BiometricSignInService).
//   - Face recognition has NO liveness / spoof protection (owner decision, academic project).
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Biometric;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.Services.Biometric;

namespace Orange_Tendering_System.Controllers;

[Authorize]
public class BiometricsController : Controller
{
    private const string GenericFailure = "Sign-in failed. Check your details and account approval, then try again.";

    private readonly UserManager<ApplicationUser> users;
    private readonly ApplicationDbContext db;
    private readonly PasskeyService passkeys;
    private readonly FaceService faces;
    private readonly BiometricSignInService signIn;
    private readonly SupplierPreAuth preAuth;
    private readonly BiometricThrottle throttle;

    public BiometricsController(
        UserManager<ApplicationUser> users,
        ApplicationDbContext db,
        PasskeyService passkeys,
        FaceService faces,
        BiometricSignInService signIn,
        SupplierPreAuth preAuth,
        BiometricThrottle throttle)
    {
        this.users = users;
        this.db = db;
        this.passkeys = passkeys;
        this.faces = faces;
        this.signIn = signIn;
        this.preAuth = preAuth;
        this.throttle = throttle;
    }

    // ================================================================ Pages

    // GET /Biometrics/Fingerprint
    public async Task<IActionResult> Fingerprint()
    {
        ApplicationUser? user = await users.GetUserAsync(User);
        if (user == null)
        {
            return Challenge();
        }

        BiometricManageViewModel model = new BiometricManageViewModel();
        foreach (UserPasskeyInfo passkey in await passkeys.ListAsync(user))
        {
            PasskeyRow row = new PasskeyRow();
            row.Id = Base64Url.Encode(passkey.CredentialId);
            row.Label = passkey.Name ?? "Device";
            row.AddedAt = passkey.CreatedAt.UtcDateTime;
            model.Passkeys.Add(row);
        }
        return View(model);
    }

    // GET /Biometrics/Face
    public async Task<IActionResult> Face()
    {
        string? userId = users.GetUserId(User);
        if (userId == null)
        {
            return Challenge();
        }

        BiometricManageViewModel model = new BiometricManageViewModel();
        model.FaceTemplateCount = await faces.CountTemplatesAsync(userId);
        return View(model);
    }

    // ================================================================ Fingerprint / passkey

    // Enrollment step 1: check the password, then send the WebAuthn options to the browser.
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> PasskeyRegisterOptions([FromBody] EnrollStartRequest request)
    {
        ApplicationUser? user = await users.GetUserAsync(User);
        if (user == null || !await PasswordConfirmedAsync(user, request.Password))
        {
            return Unauthorized(new { error = "The password is not correct." });
        }

        string optionsJson = await passkeys.CreateRegistrationOptionsAsync(user);
        return Content(optionsJson, "application/json");
    }

    // Enrollment step 2: verify the device's answer and save the passkey.
    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(100_000)]
    public async Task<IActionResult> PasskeyRegister([FromBody] PasskeyRegisterRequest request)
    {
        ApplicationUser? user = await users.GetUserAsync(User);
        if (user == null || !await signIn.EnrollmentAllowedAsync(user))
        {
            return Unauthorized(new { error = GenericFailure });
        }

        // No name to type: the device is named after the browser that registered it ("iPhone · Safari").
        string label = request.Label ?? string.Empty;
        if (string.IsNullOrWhiteSpace(label))
        {
            label = PhoneLinkStore.DeviceLabel(Request.Headers.UserAgent);
        }

        BiometricResult result = await passkeys.RegisterAsync(user, request.CredentialJson, label);
        if (!result.Succeeded)
        {
            await signIn.AuditAsync(user.Id, "BiometricEnrollFailed", "passkey", Ip(), result.Error);
            return BadRequest(new { error = result.Error });
        }

        await signIn.AuditAsync(user.Id, "BiometricEnroll", "passkey", Ip(), null);
        return Json(new { succeeded = true });
    }

    // Sign-in step 1: the WebAuthn options. Inside an identity step only that account's passkeys are offered.
    [AllowAnonymous, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> PasskeyLoginOptions()
    {
        if (throttle.IsBlocked(BiometricThrottle.IpKey(Ip())))
        {
            return TooManyAttempts();
        }

        ApplicationUser? stepUser = await preAuth.UserAsync(HttpContext, users);
        string optionsJson = await passkeys.CreateLoginOptionsAsync(stepUser);
        return Content(optionsJson, "application/json");
    }

    // Sign-in step 2: verify the signed answer, check the account rules, sign in.
    [AllowAnonymous, HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(100_000)]
    public async Task<IActionResult> PasskeyLogin([FromBody] PasskeyLoginRequest request)
    {
        string ipKey = BiometricThrottle.IpKey(Ip());
        if (throttle.IsBlocked(ipKey))
        {
            return TooManyAttempts();
        }

        BiometricResult result = await passkeys.VerifyLoginAsync(request.CredentialJson);

        // After an identity step the passkey must belong to that same account.
        string? stepUserId = preAuth.UserId(HttpContext);
        if (result.Succeeded && result.User != null && stepUserId != null && result.User.Id != stepUserId)
        {
            result = BiometricResult.Fail("passkey of another account");
        }

        if (!result.Succeeded || result.User == null)
        {
            throttle.Failure(ipKey);
            await signIn.RegisterFailureAsync(result.User, BiometricSignInService.PasskeyMethod, result.Error ?? "failed", Ip());
            return Unauthorized(new { error = GenericFailure });
        }

        bool signedIn = await signIn.SignInAsync(result.User, BiometricSignInService.PasskeyMethod, Ip());
        if (!signedIn)
        {
            throttle.Failure(ipKey);
            return Unauthorized(new { error = GenericFailure });
        }

        preAuth.End(HttpContext);
        throttle.Reset(ipKey);
        string redirect = await signIn.DestinationAsync(result.User, request.ReturnUrl, Url);
        return Json(new { succeeded = true, redirect = redirect });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> PasskeyRemove(string id)
    {
        ApplicationUser? user = await users.GetUserAsync(User);
        if (user == null || !await passkeys.RemoveAsync(user, id))
        {
            return NotFound(new { error = "Device not found." });
        }

        await signIn.AuditAsync(user.Id, "BiometricRemove", "passkey", Ip(), null);
        return Json(new { succeeded = true });
    }

    // ================================================================ Face enrollment

    // Enrollment step 1: check the password, then give a one-time token for the capture.
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> FaceEnrollChallenge([FromBody] EnrollStartRequest request)
    {
        ApplicationUser? user = await users.GetUserAsync(User);
        if (user == null || !await PasswordConfirmedAsync(user, request.Password))
        {
            return Unauthorized(new { error = "The password is not correct." });
        }

        return Json(faces.CreateChallenge(FaceService.EnrollPurpose, user.Id));
    }

    // Enrollment step 2: the face samples with the token. FaceService checks them and saves them encrypted.
    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(400_000)]
    public async Task<IActionResult> FaceEnroll([FromBody] FaceSubmission submission)
    {
        ApplicationUser? user = await users.GetUserAsync(User);
        if (user == null || !await signIn.EnrollmentAllowedAsync(user))
        {
            return Unauthorized(new { error = GenericFailure });
        }

        BiometricResult result = await faces.EnrollAsync(user, submission);
        if (!result.Succeeded)
        {
            await signIn.AuditAsync(user.Id, "BiometricEnrollFailed", "face", Ip(), result.Error);
            return BadRequest(new { error = result.Error });
        }

        await signIn.AuditAsync(user.Id, "BiometricEnroll", "face", Ip(), null);
        return Json(new { succeeded = true });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> FaceRemove()
    {
        string? userId = users.GetUserId(User);
        if (userId == null)
        {
            return Challenge();
        }

        await faces.RemoveTemplatesAsync(userId);
        await signIn.AuditAsync(userId, "BiometricRemove", "face", Ip(), null);
        return Json(new { succeeded = true });
    }

    // ================================================================ Face sign-in (Login page)

    // Sign-in step 1: a one-time token for the account of this browser's identity step.
    [AllowAnonymous, HttpPost, ValidateAntiForgeryToken]
    public IActionResult FaceChallenge()
    {
        if (throttle.IsBlocked(BiometricThrottle.IpKey(Ip())))
        {
            return TooManyAttempts();
        }

        string? userId = preAuth.UserId(HttpContext);
        if (userId == null)
        {
            return StepEnded();
        }
        return Json(faces.CreateChallenge(FaceService.LoginPurpose, userId));
    }

    // Sign-in step 2: compare the face samples with the account's templates, then sign in.
    [AllowAnonymous, HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(400_000)]
    public async Task<IActionResult> FaceLogin([FromBody] FaceSubmission submission)
    {
        string ipKey = BiometricThrottle.IpKey(Ip());
        if (throttle.IsBlocked(ipKey))
        {
            return TooManyAttempts();
        }

        // The account is the one of the identity step, never one named by the browser.
        ApplicationUser? user = await preAuth.UserAsync(HttpContext, users);
        if (user == null)
        {
            throttle.Failure(ipKey);
            await signIn.RegisterFailureAsync(null, BiometricSignInService.FaceMethod, "no password step", Ip());
            return StepEnded();
        }

        string accountKey = BiometricThrottle.AccountKey(user.Email);
        if (throttle.IsBlocked(accountKey))
        {
            return TooManyAttempts();
        }

        BiometricResult result = await faces.VerifyLoginAsync(user, submission);
        if (!result.Succeeded || result.User == null)
        {
            throttle.Failure(ipKey);
            throttle.Failure(accountKey);
            await signIn.RegisterFailureAsync(user, BiometricSignInService.FaceMethod, result.Error ?? "failed", Ip());

            // Failed face checks count toward the account lockout. A locked account must start again.
            if (await signIn.IsLockedOutAsync(user))
            {
                preAuth.End(HttpContext);
                return StepEnded();
            }

            // Capture problems are found before any comparison, so they are safe to show. A mismatch stays generic.
            return Unauthorized(new { error = result.Error ?? GenericFailure });
        }

        bool signedIn = await signIn.SignInAsync(result.User, BiometricSignInService.FaceMethod, Ip());
        if (!signedIn)
        {
            preAuth.End(HttpContext);
            // The face matched, so the person proved who they are: a pending supplier is told why.
            bool pending = false;
            if (result.User.SupplierId != null)
            {
                pending = await db.Suppliers.AsNoTracking()
                    .AnyAsync(s => s.Id == result.User.SupplierId && s.Status == SupplierStatus.Pending);
            }
            string message = GenericFailure;
            if (pending)
            {
                message = "Face recognised, but your company is still awaiting administrator approval.";
            }
            return Unauthorized(new { restart = true, error = message });
        }

        preAuth.End(HttpContext);
        throttle.Reset(ipKey);
        throttle.Reset(accountKey);
        string redirect = await signIn.DestinationAsync(result.User, submission.ReturnUrl, Url);
        return Json(new { succeeded = true, redirect = redirect });
    }

    // ================================================================ Face sign-in from the Pending Approval page

    // An approved supplier already signed in with the password confirms the face to enter the workspace.
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> FaceSetupChallenge([FromBody] EnrollStartRequest request)
    {
        ApplicationUser? user = await users.GetUserAsync(User);
        if (user == null)
        {
            return Unauthorized(new { error = GenericFailure });
        }
        if (!await users.IsInRoleAsync(user, SystemRoles.Supplier))
        {
            return Unauthorized(new { error = GenericFailure });
        }
        if (throttle.IsBlocked(BiometricThrottle.IpKey(Ip())))
        {
            return Unauthorized(new { error = GenericFailure });
        }
        if (!await signIn.AccountAllowedAsync(user))
        {
            return Unauthorized(new { error = GenericFailure });
        }
        if (!await db.FaceTemplates.AnyAsync(t => t.UserId == user.Id))
        {
            return Unauthorized(new { error = GenericFailure });
        }
        if (!await signIn.CheckPasswordAsync(user, request.Password))
        {
            return Unauthorized(new { error = GenericFailure });
        }

        return Json(faces.CreateChallenge(FaceService.LoginPurpose, user.Id));
    }

    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(400_000)]
    public async Task<IActionResult> FaceSetupLogin([FromBody] FaceSubmission submission)
    {
        ApplicationUser? user = await users.GetUserAsync(User);
        string ipKey = BiometricThrottle.IpKey(Ip());
        if (user == null || throttle.IsBlocked(ipKey))
        {
            return Unauthorized(new { error = GenericFailure });
        }

        string accountKey = BiometricThrottle.AccountKey(user.Email);
        if (throttle.IsBlocked(accountKey))
        {
            return TooManyAttempts();
        }

        BiometricResult result = await faces.VerifyLoginAsync(user, submission);
        if (!result.Succeeded || result.User == null)
        {
            throttle.Failure(ipKey);
            throttle.Failure(accountKey);
            await signIn.RegisterFailureAsync(user, BiometricSignInService.FaceMethod, result.Error ?? "failed", Ip());
            return Unauthorized(new { error = result.Error ?? GenericFailure });
        }

        bool signedIn = await signIn.SignInAsync(user, BiometricSignInService.FaceMethod, Ip());
        if (!signedIn)
        {
            return Unauthorized(new { error = GenericFailure });
        }

        throttle.Reset(ipKey);
        throttle.Reset(accountKey);
        string redirect = await signIn.DestinationAsync(user, "/Supplier", Url);
        return Json(new { succeeded = true, redirect = redirect });
    }

    // ================================================================ Small helpers

    // Enrollment is allowed for this account, and the password typed on the page is correct.
    private async Task<bool> PasswordConfirmedAsync(ApplicationUser user, string? password)
    {
        if (!await signIn.EnrollmentAllowedAsync(user))
        {
            return false;
        }
        return await signIn.CheckPasswordAsync(user, password);
    }

    private string? Ip()
    {
        if (HttpContext.Connection.RemoteIpAddress == null)
        {
            return null;
        }
        return HttpContext.Connection.RemoteIpAddress.ToString();
    }

    private ObjectResult TooManyAttempts()
    {
        return StatusCode(StatusCodes.Status429TooManyRequests, new { error = "Too many attempts. Wait a few minutes and try again." });
    }

    // There is no live identity step in this browser (never started, expired, cancelled or used).
    // "restart = true" tells the page to go back to the start of the sign-in.
    private UnauthorizedObjectResult StepEnded()
    {
        return Unauthorized(new { restart = true, error = "Your sign-in step ended. Choose Face ID or Fingerprint and enter your email again." });
    }
}
