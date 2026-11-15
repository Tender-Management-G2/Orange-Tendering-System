// ORANGE TENDER MANAGEMENT SYSTEM
// Owner: Ahmad
// Module: Authentication & Biometrics
// Purpose: Fingerprint (WebAuthn passkey) and face-recognition sign-in on top of ASP.NET Core Identity.
//   - GET Fingerprint / Face: enrollment pages for the signed-in user.
//   - JSON endpoints called by wwwroot/js/biometric.js. Every POST is protected by the anti-forgery token.
// Security: face sign-in compares freshly captured descriptors with encrypted enrolled templates (one-time token
//   per attempt; NO liveness/spoof protection by owner decision). It is a second step only: it works inside the
//   pre-auth transaction the Login page opens after a correct password (SupplierPreAuth), for that account.
//   Passkeys require real WebAuthn proof and sign in on their own.
//   Enrollment is allowed for verified pending suppliers; sign-in requires an Approved supplier company.
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models.Biometric;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.Services.Biometric;

namespace Orange_Tendering_System.Controllers
{
    [Authorize]
    public class BiometricsController(
        UserManager<ApplicationUser> users,
        ApplicationDbContext db,
        PasskeyService passkeys,
        FaceService faces,
        BiometricSignInService signIn,
        SupplierPreAuth preAuth,
        BiometricThrottle throttle) : Controller
    {
        private const string GenericFailure = "Sign-in failed. Check your details and account approval, then try again.";

        // ---------- Pages ----------

        // GET /Biometrics/Fingerprint
        public async Task<IActionResult> Fingerprint()
        {
            var user = (await users.GetUserAsync(User))!;
            return View(new BiometricManageViewModel
            {
                Passkeys = (await passkeys.ListAsync(user)).Select(c => new PasskeyRow
                { Id = Base64Url.Encode(c.CredentialId), Label = c.Name ?? "Device", AddedAt = c.CreatedAt.UtcDateTime }).ToList()
            });
        }

        // GET /Biometrics/Face
        public async Task<IActionResult> Face()
        {
            var userId = users.GetUserId(User)!;
            return View(new BiometricManageViewModel { FaceTemplateCount = await faces.CountTemplatesAsync(userId) });
        }

        // ---------- Fingerprint / device passkey ----------

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> PasskeyRegisterOptions([FromBody] EnrollStartRequest request)
        {
            var user = (await users.GetUserAsync(User))!;
            if (!await ConfirmPasswordAsync(user, request.Password)) return Unauthorized(new { error = "The password is not correct." });

            return Content(await passkeys.CreateRegistrationOptionsAsync(user), "application/json");
        }

        [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(100_000)]
        public async Task<IActionResult> PasskeyRegister([FromBody] PasskeyRegisterRequest request)
        {
            var user = (await users.GetUserAsync(User))!;
            if (!await signIn.EnrollmentAllowedAsync(user)) return Unauthorized(new { error = GenericFailure });
            var result = await passkeys.RegisterAsync(user, request.CredentialJson, request.Label);
            await signIn.AuditAsync(user.Id, result.Succeeded ? "BiometricEnroll" : "BiometricEnrollFailed", "passkey", Ip(), result.Error);
            return result.Succeeded ? Json(new { succeeded = true }) : BadRequest(new { error = result.Error });
        }

        [AllowAnonymous, HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> PasskeyLoginOptions()
        {
            if (throttle.IsBlocked(BiometricThrottle.IpKey(Ip()))) return TooManyAttempts();
            return Content(await passkeys.CreateLoginOptionsAsync(), "application/json");
        }

        [AllowAnonymous, HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(100_000)]
        public async Task<IActionResult> PasskeyLogin([FromBody] PasskeyLoginRequest request)
        {
            var ipKey = BiometricThrottle.IpKey(Ip());
            if (throttle.IsBlocked(ipKey)) return TooManyAttempts();

            var result = await passkeys.VerifyLoginAsync(request.CredentialJson);
            if (!result.Succeeded || result.User is null)
            {
                throttle.Failure(ipKey);
                await signIn.RegisterFailureAsync(result.User, BiometricSignInService.PasskeyMethod, result.Error ?? "failed", Ip());
                return Unauthorized(new { error = GenericFailure });
            }

            if (!await signIn.SignInAsync(result.User, BiometricSignInService.PasskeyMethod, Ip()))
            {
                throttle.Failure(ipKey);
                return Unauthorized(new { error = GenericFailure });
            }

            throttle.Reset(ipKey);
            return Json(new { succeeded = true, redirect = await signIn.DestinationAsync(result.User, request.ReturnUrl, Url.IsLocalUrl) });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> PasskeyRemove(string id)
        {
            var user = (await users.GetUserAsync(User))!;
            if (!await passkeys.RemoveAsync(user, id)) return NotFound(new { error = "Device not found." });
            await signIn.AuditAsync(user.Id, "BiometricRemove", "passkey", Ip(), null);
            return Json(new { succeeded = true });
        }

        // ---------- Face recognition ----------

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> FaceEnrollChallenge([FromBody] EnrollStartRequest request)
        {
            var user = (await users.GetUserAsync(User))!;
            if (!await ConfirmPasswordAsync(user, request.Password)) return Unauthorized(new { error = "The password is not correct." });
            return Json(faces.CreateChallenge(FaceService.EnrollPurpose, user.Id));
        }

        [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(400_000)]
        public async Task<IActionResult> FaceEnroll([FromBody] FaceSubmission submission)
        {
            var user = (await users.GetUserAsync(User))!;
            if (!await signIn.EnrollmentAllowedAsync(user)) return Unauthorized(new { error = GenericFailure });
            var result = await faces.EnrollAsync(user, submission);
            await signIn.AuditAsync(user.Id, result.Succeeded ? "BiometricEnroll" : "BiometricEnrollFailed", "face", Ip(), result.Error);
            return result.Succeeded ? Json(new { succeeded = true }) : BadRequest(new { error = result.Error });
        }

        [AllowAnonymous, HttpPost, ValidateAntiForgeryToken]
        public IActionResult FaceChallenge()
        {
            if (throttle.IsBlocked(BiometricThrottle.IpKey(Ip()))) return TooManyAttempts();
            // The one-time token is tied to the account that passed the password step.
            var userId = preAuth.UserId(HttpContext);
            return userId is null ? PasswordStepRequired() : Json(faces.CreateChallenge(FaceService.LoginPurpose, userId));
        }

        [AllowAnonymous, HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(400_000)]
        public async Task<IActionResult> FaceLogin([FromBody] FaceSubmission submission)
        {
            var ipKey = BiometricThrottle.IpKey(Ip());
            if (throttle.IsBlocked(ipKey)) return TooManyAttempts();

            // The account is the one that just passed the password check, never one named by the browser.
            var user = await preAuth.UserAsync(HttpContext, users);
            if (user is null)
            {
                throttle.Failure(ipKey);
                await signIn.RegisterFailureAsync(null, BiometricSignInService.FaceMethod, "no password step", Ip());
                return PasswordStepRequired();
            }

            var accountKey = BiometricThrottle.AccountKey(user.Email);
            if (throttle.IsBlocked(accountKey)) return TooManyAttempts();

            var result = await faces.VerifyLoginAsync(user, submission);
            if (!result.Succeeded || result.User is null)
            {
                throttle.Failure(ipKey, accountKey);
                await signIn.RegisterFailureAsync(user, BiometricSignInService.FaceMethod, result.Error ?? "failed", Ip());
                // Failed face checks count toward the account lockout; a locked account starts again from the password.
                if (await signIn.IsLockedOutAsync(user))
                {
                    preAuth.End(HttpContext);
                    return PasswordStepRequired();
                }

                // Capture problems are checked before the templates are compared, so they are safe to show; a mismatch is generic.
                return Unauthorized(new { error = result.Error ?? GenericFailure });
            }

            if (!await signIn.SignInAsync(result.User, BiometricSignInService.FaceMethod, Ip()))
            {
                preAuth.End(HttpContext);
                // The password and the face matched, so the person has proven who they are: tell a pending supplier why.
                var pending = result.User.SupplierId != null && await db.Suppliers.AsNoTracking()
                    .AnyAsync(s => s.Id == result.User.SupplierId && s.Status == SupplierStatus.Pending);
                return Unauthorized(new { restart = true, error = pending
                    ? "Face recognised, but your company is still awaiting administrator approval."
                    : GenericFailure });
            }

            preAuth.End(HttpContext);
            throttle.Reset(ipKey, accountKey);
            return Json(new { succeeded = true, redirect = await signIn.DestinationAsync(result.User, submission.ReturnUrl, Url.IsLocalUrl) });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> FaceRemove()
        {
            var userId = users.GetUserId(User)!;
            await faces.RemoveTemplatesAsync(userId);
            await signIn.AuditAsync(userId, "BiometricRemove", "face", Ip(), null);
            return Json(new { succeeded = true });
        }

        // ---------- Helpers ----------

        private async Task<bool> ConfirmPasswordAsync(ApplicationUser user, string? password)
        {
            return await signIn.EnrollmentAllowedAsync(user) && await signIn.CheckPasswordAsync(user, password);
        }

        private string? Ip() => HttpContext.Connection.RemoteIpAddress?.ToString();

        private ObjectResult TooManyAttempts() =>
            StatusCode(StatusCodes.Status429TooManyRequests, new { error = "Too many attempts. Wait a few minutes and try again." });

        // No live password step in this browser (never started, expired, cancelled, or already used).
        private UnauthorizedObjectResult PasswordStepRequired() =>
            Unauthorized(new { restart = true, error = "Enter your email and password again to continue." });
    }
}
