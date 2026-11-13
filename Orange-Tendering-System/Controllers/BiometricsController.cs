// ORANGE TENDER MANAGEMENT SYSTEM
// Owner: Ahmad
// Module: Authentication & Biometrics
// Purpose: Fingerprint (WebAuthn passkey) and face-recognition sign-in on top of ASP.NET Core Identity.
//   - GET Fingerprint / Face: enrollment pages for the signed-in user.
//   - JSON endpoints called by wwwroot/js/biometric.js. Every POST is protected by the anti-forgery token.
// Security: face authentication/enrollment is disabled server-side. Browser face evidence is untrusted.
// Passkeys require real WebAuthn proof and Identity account restrictions; only public keys are stored.
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
        BiometricThrottle throttle,
        IOptions<BiometricOptions> options) : Controller
    {
        private const string GenericFailure = "Sign-in failed. Check your details and try again, or use your password.";

        // ---------- Pages ----------

        // GET /Biometrics/Fingerprint
        public async Task<IActionResult> Fingerprint()
        {
            var userId = users.GetUserId(User)!;
            return View(new BiometricManageViewModel
            {
                Passkeys = await db.UserPasskeyCredentials.AsNoTracking()
                    .Where(c => c.UserId == userId).OrderBy(c => c.AddedAt)
                    .Select(c => new PasskeyRow { Id = c.Id, Label = c.DeviceLabel ?? "Device", AddedAt = c.AddedAt, LastUsedAt = c.LastUsedAt })
                    .ToListAsync()
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

            var existing = await db.UserPasskeyCredentials.Where(c => c.UserId == user.Id).Select(c => c.CredentialId).ToListAsync();
            var (token, publicKey) = passkeys.CreateRegistrationOptions(user, existing, Rp());
            return Json(new { token, options = publicKey });
        }

        [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(100_000)]
        public async Task<IActionResult> PasskeyRegister([FromBody] PasskeyRegisterRequest request)
        {
            var user = (await users.GetUserAsync(User))!;
            if (!await signIn.AccountAllowedAsync(user)) return Unauthorized(new { error = GenericFailure });
            var result = await passkeys.RegisterAsync(user, request, Rp());
            await signIn.AuditAsync(user.Id, result.Succeeded ? "BiometricEnroll" : "BiometricEnrollFailed", "passkey", Ip(), result.Error);
            return result.Succeeded ? Json(new { succeeded = true }) : BadRequest(new { error = result.Error });
        }

        [AllowAnonymous, HttpPost, ValidateAntiForgeryToken]
        public IActionResult PasskeyLoginOptions()
        {
            if (throttle.IsBlocked(BiometricThrottle.IpKey(Ip()))) return TooManyAttempts();
            var (token, publicKey) = passkeys.CreateLoginOptions(Rp());
            return Json(new { token, options = publicKey });
        }

        [AllowAnonymous, HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(100_000)]
        public async Task<IActionResult> PasskeyLogin([FromBody] PasskeyLoginRequest request)
        {
            var ipKey = BiometricThrottle.IpKey(Ip());
            if (throttle.IsBlocked(ipKey)) return TooManyAttempts();

            var result = await passkeys.VerifyLoginAsync(request, Rp());
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
        public async Task<IActionResult> PasskeyRemove(long id)
        {
            var userId = users.GetUserId(User)!;
            var credential = await db.UserPasskeyCredentials.FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId);
            if (credential is null) return NotFound(new { error = "Device not found." });

            db.UserPasskeyCredentials.Remove(credential);
            await db.SaveChangesAsync();
            await signIn.AuditAsync(userId, "BiometricRemove", "passkey", Ip(), null);
            return Json(new { succeeded = true });
        }

        // ---------- Face recognition ----------

        // Fail closed even if a caller removes the disabled UI or submits forged descriptors,
        // liveness flags, passwords or provider decisions. No client face evidence is trusted.
        private ObjectResult FaceDisabled() => StatusCode(StatusCodes.Status501NotImplemented,
            new { error = FaceService.DisabledReason });

        [HttpPost, ValidateAntiForgeryToken]
        public IActionResult FaceEnrollChallenge([FromBody] EnrollStartRequest request) => FaceDisabled();

        [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(400_000)]
        public IActionResult FaceEnroll([FromBody] FaceSubmission submission) => FaceDisabled();

        [AllowAnonymous, HttpPost, ValidateAntiForgeryToken]
        public IActionResult FaceChallenge() => FaceDisabled();

        [AllowAnonymous, HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(400_000)]
        public IActionResult FaceLogin([FromBody] FaceSubmission submission) => FaceDisabled();

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
            return await signIn.AccountAllowedAsync(user) && await signIn.CheckPasswordAsync(user, password);
        }

        private RpInfo Rp()
        {
            var o = options.Value;
            return new RpInfo(o.RpId ?? Request.Host.Host, o.Origins ?? [$"{Request.Scheme}://{Request.Host}"], o.RpName);
        }

        private string? Ip() => HttpContext.Connection.RemoteIpAddress?.ToString();

        private ObjectResult TooManyAttempts() =>
            StatusCode(StatusCodes.Status429TooManyRequests, new { error = "Too many attempts. Wait a few minutes and try again." });
    }
}
