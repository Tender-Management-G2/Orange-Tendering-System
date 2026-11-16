// ORANGE TENDER MANAGEMENT SYSTEM
// Owner: Ahmad
// Module: Authentication & Biometrics
// Purpose: "Use my phone" for fingerprint / passkey sign-in, on top of BiometricsController and PasskeyService.
//   Computer: shows a QR code (or emails the same kind of link) and polls until the phone has finished.
//   Phone:    /Biometrics/Phone opens the link, runs the WebAuthn ceremony on the phone and reports back.
// Security: tokens are random, single use, short lived and kept in the URL fragment (never in server logs);
//   only their hashes are stored (Services/Biometric/PhoneLink.cs). Opening a link changes nothing by itself:
//   enrollment needs a WebAuthn registration verified by Identity for the link's own account, and a sign-in
//   needs the number shown on the computer plus a verified assertion of that account's passkey. Only the
//   computer that made the code (its HttpOnly cookie) and its live password step can collect the session,
//   and the usual account rules (active, approved supplier, lockout) are checked again at that moment.
//   The application never receives a fingerprint or face image: the phone decides how it verifies the
//   person (fingerprint, face unlock or PIN) and the server cannot tell which.
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Orange_Tendering_System.Models.Biometric;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.Services.Biometric;

namespace Orange_Tendering_System.Controllers
{
    [Route("Biometrics/Phone")]
    public class PhonePasskeyController(
        UserManager<ApplicationUser> users,
        PasskeyService passkeys,
        BiometricSignInService signIn,
        SupplierPreAuth preAuth,
        PhoneLinkStore links,
        BiometricThrottle throttle,
        IEmailSender email,
        IOptions<BiometricOptions> options,
        ILogger<PhonePasskeyController> logger) : Controller
    {
        private const string GenericFailure = "Sign-in failed. Check your details and account approval, then try again.";
        private const string LinkEnded = "This code is no longer valid. Create a new QR code on your computer.";

        // ---------- Phone page ----------

        // GET /Biometrics/Phone#t=... (the token stays in the fragment; the page script sends it in POST bodies)
        [AllowAnonymous, HttpGet("")]
        public IActionResult Index()
        {
            NoStore();
            Response.Headers["Referrer-Policy"] = "no-referrer";
            Response.Headers["X-Robots-Tag"] = "noindex";
            return View();
        }

        // ---------- Computer: register a phone (signed-in account, after a password re-check) ----------

        [Authorize, HttpPost("EnrollStart"), ValidateAntiForgeryToken]
        public async Task<IActionResult> EnrollStart([FromBody] EnrollStartRequest request)
        {
            var user = (await users.GetUserAsync(User))!;
            if (!await signIn.EnrollmentAllowedAsync(user) || !await signIn.CheckPasswordAsync(user, request.Password))
                return Unauthorized(new { error = "The password is not correct." });

            if (!links.PhoneReachable(Request)) return PublicAddressRequired();
            if (!throttle.TryUse("phone-link:" + user.Id, options.Value.PhoneLinksPerWindow)) return TooManyAttempts();
            var (link, token) = links.Create(HttpContext, PhoneLinkKind.Enroll, user);
            var url = links.PhoneUrl(Request, token)!;

            await signIn.AuditAsync(user.Id, "PhoneLinkStarted", "passkey-phone", Ip(), "enroll");
            // "link" is the address inside the QR code (no more than the picture already gives this browser);
            // the page shows only the picture. Automated tests and assistive tools can follow it directly.
            return Json(new
            {
                qr = PhoneLinkStore.QrDataUri(url),
                link = url,
                expiresIn = PhoneLinkStore.SecondsLeft(link),
                email = user.EmailConfirmed ? PhoneLinkStore.MaskEmail(user.Email) : null
            });
        }

        // The same setup as the QR code, as a link in the account's verified mailbox (a second single-use token).
        [Authorize, HttpPost("EnrollEmail"), ValidateAntiForgeryToken]
        public async Task<IActionResult> EnrollEmail()
        {
            var user = (await users.GetUserAsync(User))!;
            var link = links.FromDesktop(HttpContext);
            if (link is null || link.Kind != PhoneLinkKind.Enroll || link.UserId != user.Id || !PhoneLinkStore.IsLive(link))
                return Conflict(new { error = LinkEnded });
            if (!user.EmailConfirmed || string.IsNullOrWhiteSpace(user.Email))
                return Conflict(new { error = "This account has no verified email address." });
            if (!links.PhoneReachable(Request)) return PublicAddressRequired();
            if (!throttle.TryUse("phone-email:" + user.Id, options.Value.PhoneLinkEmailsPerWindow)) return TooManyAttempts();

            var url = links.PhoneUrl(Request, links.AddToken(link))!;

            var minutes = Math.Max(1, (int)Math.Round(PhoneLinkStore.SecondsLeft(link) / 60.0));
            try
            {
                await email.SendEmailAsync(user.Email, "Set up phone sign-in for Orange Tendering", SetupEmail(url, minutes));
            }
            catch (Exception exception) when (exception is SmtpException or InvalidOperationException)
            {
                // The link itself is never logged.
                logger.LogWarning("Phone setup email could not be sent for user {UserId}: {Reason}", user.Id, exception.Message);
                return StatusCode(StatusCodes.Status502BadGateway, new { error = "The email could not be sent. Scan the QR code instead, or try again later." });
            }

            await signIn.AuditAsync(user.Id, "PhoneLinkEmailSent", "passkey-phone", Ip(), null);
            return Json(new { sent = true, to = PhoneLinkStore.MaskEmail(user.Email) });
        }

        // ---------- Computer: approve this sign-in with my phone (inside the password step) ----------

        [AllowAnonymous, HttpPost("SignInStart"), ValidateAntiForgeryToken]
        public async Task<IActionResult> SignInStart()
        {
            if (throttle.IsBlocked(BiometricThrottle.IpKey(Ip()))) return TooManyAttempts();
            var user = await preAuth.UserAsync(HttpContext, users);
            if (user is null) return PasswordStepRequired();
            if (!await signIn.AccountAllowedAsync(user)) return Unauthorized(new { restart = true, error = GenericFailure });
            if ((await passkeys.ListAsync(user)).Count == 0)
                return Conflict(new { error = "No fingerprint device or phone is registered for this account yet." });
            if (!links.PhoneReachable(Request)) return PublicAddressRequired();
            if (!throttle.TryUse("phone-link:" + user.Id, options.Value.PhoneLinksPerWindow)) return TooManyAttempts();

            var code = RandomNumberGenerator.GetInt32(10, 100).ToString();
            var (link, token) = links.Create(HttpContext, PhoneLinkKind.SignIn, user, code);
            var url = links.PhoneUrl(Request, token)!;

            await signIn.AuditAsync(user.Id, "PhoneLinkStarted", BiometricSignInService.PasskeyMethod, Ip(), "sign-in");
            return Json(new { qr = PhoneLinkStore.QrDataUri(url), link = url, code, expiresIn = PhoneLinkStore.SecondsLeft(link) });
        }

        // Issues the session once the phone approved. Only this browser (link cookie) inside its own live password step.
        [AllowAnonymous, HttpPost("SignInComplete"), ValidateAntiForgeryToken]
        public async Task<IActionResult> SignInComplete([FromBody] PhoneSignInCompleteRequest request)
        {
            var link = links.FromDesktop(HttpContext);
            var stepUserId = preAuth.UserId(HttpContext);
            if (link is null || link.Kind != PhoneLinkKind.SignIn || stepUserId is null || stepUserId != link.UserId)
                return PasswordStepRequired();
            if (!links.TryMove(link, PhoneLinkStatus.SignedIn, PhoneLinkStatus.Approved))
                return Conflict(new { status = link.Status, error = "Your phone has not approved this sign-in." });

            var user = await users.FindByIdAsync(link.UserId);
            if (user is null || user.SecurityStamp != link.SecurityStamp ||
                !await signIn.SignInAsync(user, BiometricSignInService.PasskeyMethod, Ip()))
            {
                preAuth.End(HttpContext);
                links.EndDesktop(HttpContext);
                return Unauthorized(new { restart = true, error = GenericFailure });
            }

            preAuth.End(HttpContext);
            links.EndDesktop(HttpContext);
            throttle.Reset(BiometricThrottle.IpKey(Ip()));
            await signIn.AuditAsync(user.Id, "PhoneSignInCompleted", BiometricSignInService.PasskeyMethod, Ip(), "phone-qr");
            return Json(new { succeeded = true, redirect = await signIn.DestinationAsync(user, request.ReturnUrl, Url.IsLocalUrl) });
        }

        // ---------- Computer: shared ----------

        // Polled every few seconds by the computer that made the code.
        [AllowAnonymous, HttpGet("Status")]
        public IActionResult Status()
        {
            NoStore();
            var link = links.FromDesktop(HttpContext);
            var owner = link?.Kind == PhoneLinkKind.Enroll ? users.GetUserId(User) : preAuth.UserId(HttpContext);
            if (link is null || owner != link.UserId) return Json(new { status = "none" });
            return Json(new
            {
                status = link.Status,
                expiresIn = PhoneLinkStore.SecondsLeft(link),
                device = link.Status == PhoneLinkStatus.Waiting ? null : link.Device
            });
        }

        [AllowAnonymous, HttpPost("Cancel"), ValidateAntiForgeryToken]
        public IActionResult Cancel()
        {
            if (links.FromDesktop(HttpContext) is { } link)
                links.TryMove(link, PhoneLinkStatus.Cancelled, PhoneLinkStatus.Waiting, PhoneLinkStatus.Opened, PhoneLinkStatus.Approved);
            links.EndDesktop(HttpContext);
            return Json(new { cancelled = true });
        }

        // ---------- Phone ----------

        // What the link is for, so the phone can show it before anything happens. Marks the link as opened.
        [AllowAnonymous, HttpPost("Info"), ValidateAntiForgeryToken]
        public async Task<IActionResult> Info([FromBody] PhoneLinkRequest request)
        {
            var (link, user, error) = await OpenAsync(request.Token, null);
            if (error is not null) return error;

            if (links.TryMove(link!, PhoneLinkStatus.Opened, PhoneLinkStatus.Waiting))
            {
                link!.Device = PhoneLinkStore.DeviceLabel(Request.Headers.UserAgent);
                await signIn.AuditAsync(user!.Id, "PhoneLinkOpened", "passkey-phone", Ip(), link.Kind == PhoneLinkKind.Enroll ? "enroll" : "sign-in");
            }

            return Json(new
            {
                kind = link!.Kind == PhoneLinkKind.Enroll ? "enroll" : "signIn",
                account = PhoneLinkStore.MaskEmail(user!.Email),
                name = user.FullName,
                expiresIn = PhoneLinkStore.SecondsLeft(link),
                requestedFrom = link.RequestedFrom,
                requestedAt = link.CreatedAt
            });
        }

        [AllowAnonymous, HttpPost("EnrollOptions"), ValidateAntiForgeryToken]
        public async Task<IActionResult> EnrollOptions([FromBody] PhoneLinkRequest request)
        {
            var (_, user, error) = await OpenAsync(request.Token, PhoneLinkKind.Enroll);
            if (error is not null) return error;
            // Pending or Approved suppliers (and active internal users) only; Rejected / Suspended cannot add a device.
            if (!await signIn.EnrollmentAllowedAsync(user!)) return StatusCode(StatusCodes.Status403Forbidden, new { error = "This account cannot register a device right now." });
            return Content(await passkeys.CreateRegistrationOptionsAsync(user!), "application/json");
        }

        [AllowAnonymous, HttpPost("EnrollFinish"), ValidateAntiForgeryToken, RequestSizeLimit(100_000)]
        public async Task<IActionResult> EnrollFinish([FromBody] PhoneLinkRequest request)
        {
            var (link, user, error) = await OpenAsync(request.Token, PhoneLinkKind.Enroll);
            if (error is not null) return error;
            if (!await signIn.EnrollmentAllowedAsync(user!)) return StatusCode(StatusCodes.Status403Forbidden, new { error = "This account cannot register a device right now." });
            // Claim the link first, so a cancel on the computer or a second phone cannot race the save.
            if (!links.TryMove(link!, PhoneLinkStatus.Verifying, PhoneLinkStatus.Waiting, PhoneLinkStatus.Opened))
                return Gone(LinkEnded);

            var device = PhoneLinkStore.DeviceLabel(Request.Headers.UserAgent);
            var result = await passkeys.RegisterAsync(user!, request.CredentialJson, device);
            if (!result.Succeeded)
            {
                var ended = links.Fail(link!, PhoneLinkStatus.Opened);
                await signIn.AuditAsync(user!.Id, "BiometricEnrollFailed", "passkey-phone", Ip(), result.Error);
                return BadRequest(new { error = ended ? "Too many failed attempts. Create a new QR code on your computer." : result.Error, restart = ended });
            }

            link!.Device = device;
            links.TryMove(link, PhoneLinkStatus.Completed, PhoneLinkStatus.Verifying);
            await signIn.AuditAsync(user!.Id, "BiometricEnroll", "passkey-phone", Ip(), device);
            return Json(new { succeeded = true, device });
        }

        // Checks the number from the computer, then offers only this account's passkeys to the phone.
        [AllowAnonymous, HttpPost("SignInOptions"), ValidateAntiForgeryToken]
        public async Task<IActionResult> SignInOptions([FromBody] PhoneLinkRequest request)
        {
            var (link, user, error) = await OpenAsync(request.Token, PhoneLinkKind.SignIn);
            if (error is not null) return error;

            if (!link!.CodeConfirmed)
            {
                if (!SameCode(link.Code, request.Code))
                {
                    var ended = links.Fail(link, link.Status);
                    await signIn.AuditAsync(user!.Id, "PhoneSignInCodeRejected", BiometricSignInService.PasskeyMethod, Ip(), ended ? "link ended" : null);
                    return BadRequest(new
                    {
                        restart = ended,
                        error = ended ? "Too many wrong numbers. Start again on your computer." : "That number does not match the one on your computer."
                    });
                }
                link.CodeConfirmed = true;
            }

            if (!await signIn.AccountAllowedAsync(user!)) return Unauthorized(new { error = GenericFailure });
            return Content(await passkeys.CreateLoginOptionsAsync(user), "application/json");
        }

        [AllowAnonymous, HttpPost("SignInApprove"), ValidateAntiForgeryToken, RequestSizeLimit(100_000)]
        public async Task<IActionResult> SignInApprove([FromBody] PhoneLinkRequest request)
        {
            var (link, user, error) = await OpenAsync(request.Token, PhoneLinkKind.SignIn);
            if (error is not null) return error;
            if (!link!.CodeConfirmed) return BadRequest(new { error = "Enter the number shown on your computer first." });
            if (!links.TryMove(link, PhoneLinkStatus.Verifying, PhoneLinkStatus.Waiting, PhoneLinkStatus.Opened)) return Gone(LinkEnded);

            var result = await passkeys.VerifyLoginAsync(request.CredentialJson);
            if (result.Succeeded && result.User?.Id != link.UserId) result = BiometricResult.Fail("passkey of another account");
            if (!result.Succeeded || !await signIn.AccountAllowedAsync(user!))
            {
                throttle.Failure(BiometricThrottle.IpKey(Ip()));
                await signIn.RegisterFailureAsync(user, BiometricSignInService.PasskeyMethod, "phone approval: " + (result.Error ?? "account not allowed"), Ip());
                var ended = links.Fail(link, PhoneLinkStatus.Opened);
                return Unauthorized(new { restart = ended, error = ended ? "Too many failed attempts. Start again on your computer." : "This phone could not confirm the sign-in. Use the passkey registered for this account." });
            }

            // Updating the passkey's sign counter may refresh the stamp; the computer compares against this one.
            link.SecurityStamp = user!.SecurityStamp;
            links.TryMove(link, PhoneLinkStatus.Approved, PhoneLinkStatus.Verifying);
            await signIn.AuditAsync(user!.Id, "PhoneSignInApproved", BiometricSignInService.PasskeyMethod, Ip(), link.Device);
            return Json(new { succeeded = true });
        }

        // "This wasn't me" / "Cancel" on the phone.
        [AllowAnonymous, HttpPost("Deny"), ValidateAntiForgeryToken]
        public async Task<IActionResult> Deny([FromBody] PhoneLinkRequest request)
        {
            var link = links.FromToken(request.Token);
            if (link is not null && links.TryMove(link, link.Kind == PhoneLinkKind.SignIn ? PhoneLinkStatus.Denied : PhoneLinkStatus.Cancelled,
                    PhoneLinkStatus.Waiting, PhoneLinkStatus.Opened))
                await signIn.AuditAsync(link.UserId, link.Kind == PhoneLinkKind.SignIn ? "PhoneSignInDenied" : "PhoneLinkCancelled", "passkey-phone", Ip(), "from phone");
            return Json(new { cancelled = true });
        }

        // ---------- Helpers ----------

        /// <summary>The live link of a phone token and its account, or the response to send instead.</summary>
        private async Task<(PhoneLink? Link, ApplicationUser? User, IActionResult? Error)> OpenAsync(string? token, PhoneLinkKind? kind)
        {
            var ipKey = BiometricThrottle.IpKey(Ip());
            if (throttle.IsBlocked(ipKey)) return (null, null, TooManyAttempts());

            var link = links.FromToken(token);
            if (link is null || (kind is not null && link.Kind != kind))
            {
                throttle.Failure(ipKey);
                return (null, null, Gone(LinkEnded));
            }

            if (!PhoneLinkStore.IsLive(link)) return (null, null, Gone(EndedMessage(link)));

            // A password change, lockout or deactivation since the code was made ends it.
            var user = await users.FindByIdAsync(link.UserId);
            if (user is null || user.SecurityStamp != link.SecurityStamp || !user.IsActive || await users.IsLockedOutAsync(user))
            {
                links.TryMove(link, PhoneLinkStatus.Failed, PhoneLinkStatus.Waiting, PhoneLinkStatus.Opened);
                return (null, null, Gone(LinkEnded));
            }

            return (link, user, null);
        }

        private static string EndedMessage(PhoneLink link) => link.Status switch
        {
            PhoneLinkStatus.Expired => "This code has expired. Create a new QR code on your computer.",
            PhoneLinkStatus.Completed => "This code was already used: the phone is registered. You can close this page.",
            PhoneLinkStatus.Approved or PhoneLinkStatus.SignedIn => "This sign-in was already approved. You can close this page.",
            PhoneLinkStatus.Verifying => "This code is being used on another device.",
            _ => LinkEnded
        };

        private static bool SameCode(string? expected, string? given) =>
            expected is not null && given is not null &&
            CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(given.Trim()));

        private static string SetupEmail(string url, int minutes)
        {
            var href = System.Net.WebUtility.HtmlEncode(url);
            return $"""
                <div style="font-family:Segoe UI,Arial,sans-serif;color:#292521;max-width:520px">
                  <h2 style="margin:0 0 12px">Set up phone sign-in</h2>
                  <p>Open this link <strong>on your smartphone</strong> to register it for fingerprint / phone sign-in on Orange Tendering.
                  Your phone will ask for its fingerprint, face unlock or screen lock. Your fingerprint never leaves the phone.</p>
                  <p style="margin:24px 0"><a href="{href}" style="background:#FF7900;color:#000;padding:12px 20px;border-radius:8px;text-decoration:none;font-weight:600">Set up this phone</a></p>
                  <p>The link works once and expires in {minutes} minute(s).</p>
                  <p style="color:#6b625a;font-size:13px">If you did not ask for this, ignore this email and change your password: the link adds a sign-in device to your account.</p>
                </div>
                """;
        }

        // Opened on localhost / plain HTTP and no Biometric:PublicOrigin configured: a phone could not open any link.
        private ObjectResult PublicAddressRequired() => Conflict(new
        {
            error = "Your phone cannot reach this address. Open the site from its public HTTPS address (or set Biometric:PublicOrigin) to use your phone."
        });

        private void NoStore() => Response.Headers.CacheControl = "no-store";

        private ObjectResult Gone(string message) => StatusCode(StatusCodes.Status410Gone, new { error = message, restart = true });

        private string? Ip() => HttpContext.Connection.RemoteIpAddress?.ToString();

        private ObjectResult TooManyAttempts() =>
            StatusCode(StatusCodes.Status429TooManyRequests, new { error = "Too many attempts. Wait a few minutes and try again." });

        private UnauthorizedObjectResult PasswordStepRequired() =>
            Unauthorized(new { restart = true, error = "Your sign-in step ended. Choose Fingerprint and enter your email again." });
    }
}
