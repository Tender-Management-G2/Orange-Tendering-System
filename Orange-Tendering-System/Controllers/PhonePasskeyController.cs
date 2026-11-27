// ORANGE TENDER MANAGEMENT SYSTEM — Owner: Ahmad — Module: Authentication & Biometrics
// "Use my phone" for fingerprint / passkey, on top of PasskeyService and Services/Biometric/PhoneLink.cs.
//   Computer: shows a QR code (or emails the same kind of link) and asks /Status every 2 seconds until the phone has finished.
//   Phone:    /Biometrics/Phone opens the link, runs WebAuthn on the phone and reports back.
// Security: tokens are random, single use, short lived, kept in the URL fragment (never in server logs), and only their
//   hashes are stored. Opening a link changes nothing: enrollment needs a WebAuthn registration verified by Identity, and
//   sign-in needs the number shown on the computer plus a verified answer from that account's passkey. Only the computer
//   that made the code (its HttpOnly cookie), inside its own identity step, can collect the session, and the account rules
//   (active, approved supplier, lockout) are checked again at that moment.
//   The server never receives a fingerprint: the phone decides whether it uses its fingerprint, face unlock or PIN.
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

namespace Orange_Tendering_System.Controllers;

[Route("Biometrics/Phone")]
public class PhonePasskeyController : Controller
{
    private const string GenericFailure = "Sign-in failed. Check your details and account approval, then try again.";
    private const string LinkEnded = "This code is no longer valid. Create a new QR code on your computer.";

    private readonly UserManager<ApplicationUser> users;
    private readonly PasskeyService passkeys;
    private readonly BiometricSignInService signIn;
    private readonly SupplierPreAuth preAuth;
    private readonly PhoneLinkStore links;
    private readonly BiometricThrottle throttle;
    private readonly IEmailSender email;
    private readonly BiometricOptions options;
    private readonly ILogger<PhonePasskeyController> logger;

    public PhonePasskeyController(
        UserManager<ApplicationUser> users,
        PasskeyService passkeys,
        BiometricSignInService signIn,
        SupplierPreAuth preAuth,
        PhoneLinkStore links,
        BiometricThrottle throttle,
        IEmailSender email,
        IOptions<BiometricOptions> options,
        ILogger<PhonePasskeyController> logger)
    {
        this.users = users;
        this.passkeys = passkeys;
        this.signIn = signIn;
        this.preAuth = preAuth;
        this.links = links;
        this.throttle = throttle;
        this.email = email;
        this.options = options.Value;
        this.logger = logger;
    }

    // ================================================================ The phone page

    // GET /Biometrics/Phone#t=... (the token stays in the fragment; the page script sends it in POST bodies)
    [AllowAnonymous, HttpGet("")]
    public IActionResult Index()
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Response.Headers["X-Robots-Tag"] = "noindex";
        return View();
    }

    // ================================================================ Computer: register a phone (signed in, password re-checked)

    [Authorize, HttpPost("EnrollStart"), ValidateAntiForgeryToken]
    public async Task<IActionResult> EnrollStart([FromBody] EnrollStartRequest request)
    {
        ApplicationUser? user = await users.GetUserAsync(User);
        if (user == null || !await signIn.EnrollmentAllowedAsync(user) || !await signIn.CheckPasswordAsync(user, request.Password))
        {
            return Unauthorized(new { error = "The password is not correct." });
        }
        if (!links.PhoneReachable(Request))
        {
            return PublicAddressRequired();
        }
        if (!throttle.TryUse("phone-link:" + user.Id, options.PhoneLinksPerWindow))
        {
            return TooManyAttempts();
        }

        PhoneLink link = links.Create(HttpContext, PhoneLinkKind.Enroll, user, null);
        string token = links.AddToken(link);
        string url = links.PhoneUrl(Request, token)!;

        await signIn.AuditAsync(user.Id, "PhoneLinkStarted", "passkey-phone", Ip(), "enroll");

        string? maskedEmail = null;
        if (user.EmailConfirmed)
        {
            maskedEmail = PhoneLinkStore.MaskEmail(user.Email);
        }
        // "link" is the address inside the QR code (no more than the picture already gives this browser).
        // The page shows only the picture; automated tests and assistive tools can follow the link directly.
        return Json(new
        {
            qr = PhoneLinkStore.QrDataUri(url),
            link = url,
            expiresIn = PhoneLinkStore.SecondsLeft(link),
            email = maskedEmail
        });
    }

    // The same setup as the QR code, as a link sent to the account's verified mailbox (a second single-use token).
    [Authorize, HttpPost("EnrollEmail"), ValidateAntiForgeryToken]
    public async Task<IActionResult> EnrollEmail()
    {
        ApplicationUser? user = await users.GetUserAsync(User);
        PhoneLink? link = links.FromDesktop(HttpContext);
        if (user == null || link == null || link.Kind != PhoneLinkKind.Enroll || link.UserId != user.Id || !PhoneLinkStore.IsLive(link))
        {
            return Conflict(new { error = LinkEnded });
        }
        if (!user.EmailConfirmed || string.IsNullOrWhiteSpace(user.Email))
        {
            return Conflict(new { error = "This account has no verified email address." });
        }
        if (!links.PhoneReachable(Request))
        {
            return PublicAddressRequired();
        }
        if (!throttle.TryUse("phone-email:" + user.Id, options.PhoneLinkEmailsPerWindow))
        {
            return TooManyAttempts();
        }

        string url = links.PhoneUrl(Request, links.AddToken(link))!;
        int minutes = (int)Math.Round(PhoneLinkStore.SecondsLeft(link) / 60.0);
        if (minutes < 1)
        {
            minutes = 1;
        }

        try
        {
            await email.SendEmailAsync(user.Email, "Set up phone sign-in for Orange Tendering", SetupEmail(url, minutes));
        }
        catch (Exception exception)
        {
            if (!(exception is SmtpException) && !(exception is InvalidOperationException))
            {
                throw;
            }
            // The link itself is never logged.
            logger.LogWarning("Phone setup email could not be sent for user {UserId}: {Reason}", user.Id, exception.Message);
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "The email could not be sent. Scan the QR code instead, or try again later." });
        }

        await signIn.AuditAsync(user.Id, "PhoneLinkEmailSent", "passkey-phone", Ip(), null);
        return Json(new { sent = true, to = PhoneLinkStore.MaskEmail(user.Email) });
    }

    // ================================================================ Computer: approve this sign-in with my phone (inside the identity step)

    [AllowAnonymous, HttpPost("SignInStart"), ValidateAntiForgeryToken]
    public async Task<IActionResult> SignInStart()
    {
        if (throttle.IsBlocked(BiometricThrottle.IpKey(Ip())))
        {
            return TooManyAttempts();
        }

        ApplicationUser? user = await preAuth.UserAsync(HttpContext, users);
        if (user == null)
        {
            return StepEnded();
        }
        if (!await signIn.AccountAllowedAsync(user))
        {
            return Unauthorized(new { restart = true, error = GenericFailure });
        }
        if ((await passkeys.ListAsync(user)).Count == 0)
        {
            return Conflict(new { error = "No fingerprint device or phone is registered for this account yet." });
        }
        if (!links.PhoneReachable(Request))
        {
            return PublicAddressRequired();
        }
        if (!throttle.TryUse("phone-link:" + user.Id, options.PhoneLinksPerWindow))
        {
            return TooManyAttempts();
        }

        // A random number from 10 to 99, shown on the computer and typed on the phone.
        string code = RandomNumberGenerator.GetInt32(10, 100).ToString();
        PhoneLink link = links.Create(HttpContext, PhoneLinkKind.SignIn, user, code);
        string token = links.AddToken(link);
        string url = links.PhoneUrl(Request, token)!;

        await signIn.AuditAsync(user.Id, "PhoneLinkStarted", BiometricSignInService.PasskeyMethod, Ip(), "sign-in");
        return Json(new { qr = PhoneLinkStore.QrDataUri(url), link = url, code = code, expiresIn = PhoneLinkStore.SecondsLeft(link) });
    }

    // Issues the session once the phone approved: only to this browser (link cookie), inside its own live identity step.
    [AllowAnonymous, HttpPost("SignInComplete"), ValidateAntiForgeryToken]
    public async Task<IActionResult> SignInComplete([FromBody] PhoneSignInCompleteRequest request)
    {
        PhoneLink? link = links.FromDesktop(HttpContext);
        string? stepUserId = preAuth.UserId(HttpContext);
        if (link == null || link.Kind != PhoneLinkKind.SignIn || stepUserId == null || stepUserId != link.UserId)
        {
            return StepEnded();
        }
        if (!links.TryMove(link, PhoneLinkStatus.SignedIn, PhoneLinkStatus.Approved))
        {
            return Conflict(new { status = link.Status, error = "Your phone has not approved this sign-in." });
        }

        ApplicationUser? user = await users.FindByIdAsync(link.UserId);
        bool signedIn = false;
        // A password change since the code was made changes the security stamp and ends the link.
        if (user != null && user.SecurityStamp == link.SecurityStamp)
        {
            signedIn = await signIn.SignInAsync(user, BiometricSignInService.PasskeyMethod, Ip());
        }

        preAuth.End(HttpContext);
        links.EndDesktop(HttpContext);
        if (!signedIn || user == null)
        {
            return Unauthorized(new { restart = true, error = GenericFailure });
        }

        throttle.Reset(BiometricThrottle.IpKey(Ip()));
        await signIn.AuditAsync(user.Id, "PhoneSignInCompleted", BiometricSignInService.PasskeyMethod, Ip(), "phone-qr");
        string redirect = await signIn.DestinationAsync(user, request.ReturnUrl, Url);
        return Json(new { succeeded = true, redirect = redirect });
    }

    // ================================================================ Computer: shared

    // Asked every 2 seconds by the computer that made the code.
    [AllowAnonymous, HttpGet("Status")]
    public IActionResult Status()
    {
        Response.Headers.CacheControl = "no-store";
        PhoneLink? link = links.FromDesktop(HttpContext);
        if (link == null)
        {
            return Json(new { status = "none" });
        }

        // Enrollment belongs to the signed-in user; sign-in belongs to the identity step.
        string? owner;
        if (link.Kind == PhoneLinkKind.Enroll)
        {
            owner = users.GetUserId(User);
        }
        else
        {
            owner = preAuth.UserId(HttpContext);
        }
        if (owner != link.UserId)
        {
            return Json(new { status = "none" });
        }

        string? device = link.Device;
        if (link.Status == PhoneLinkStatus.Waiting)
        {
            device = null;
        }
        return Json(new { status = link.Status, expiresIn = PhoneLinkStore.SecondsLeft(link), device = device });
    }

    [AllowAnonymous, HttpPost("Cancel"), ValidateAntiForgeryToken]
    public IActionResult Cancel()
    {
        PhoneLink? link = links.FromDesktop(HttpContext);
        if (link != null)
        {
            links.TryMove(link, PhoneLinkStatus.Cancelled, PhoneLinkStatus.Waiting, PhoneLinkStatus.Opened, PhoneLinkStatus.Approved);
        }
        links.EndDesktop(HttpContext);
        return Json(new { cancelled = true });
    }

    // ================================================================ Phone

    // What the link is for, so the phone can show it before anything happens. Marks the link as opened.
    [AllowAnonymous, HttpPost("Info"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Info([FromBody] PhoneLinkRequest request)
    {
        IActionResult? error;
        PhoneLink? link = FindLiveLink(request.Token, null, out error);
        if (link == null)
        {
            return error!;
        }
        ApplicationUser? user = await FindLinkOwnerAsync(link);
        if (user == null)
        {
            return Gone(LinkEnded);
        }

        string kindText = "signIn";
        if (link.Kind == PhoneLinkKind.Enroll)
        {
            kindText = "enroll";
        }

        if (links.TryMove(link, PhoneLinkStatus.Opened, PhoneLinkStatus.Waiting))
        {
            link.Device = PhoneLinkStore.DeviceLabel(Request.Headers.UserAgent);
            string auditDetail = "sign-in";
            if (link.Kind == PhoneLinkKind.Enroll)
            {
                auditDetail = "enroll";
            }
            await signIn.AuditAsync(user.Id, "PhoneLinkOpened", "passkey-phone", Ip(), auditDetail);
        }

        return Json(new
        {
            kind = kindText,
            account = PhoneLinkStore.MaskEmail(user.Email),
            name = user.FullName,
            expiresIn = PhoneLinkStore.SecondsLeft(link),
            requestedFrom = link.RequestedFrom,
            requestedAt = link.CreatedAt
        });
    }

    [AllowAnonymous, HttpPost("EnrollOptions"), ValidateAntiForgeryToken]
    public async Task<IActionResult> EnrollOptions([FromBody] PhoneLinkRequest request)
    {
        IActionResult? error;
        PhoneLink? link = FindLiveLink(request.Token, PhoneLinkKind.Enroll, out error);
        if (link == null)
        {
            return error!;
        }
        ApplicationUser? user = await FindLinkOwnerAsync(link);
        if (user == null)
        {
            return Gone(LinkEnded);
        }

        // Pending or Approved suppliers (and active internal users) only. Rejected / Suspended cannot add a device.
        if (!await signIn.EnrollmentAllowedAsync(user))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "This account cannot register a device right now." });
        }
        return Content(await passkeys.CreateRegistrationOptionsAsync(user), "application/json");
    }

    [AllowAnonymous, HttpPost("EnrollFinish"), ValidateAntiForgeryToken, RequestSizeLimit(100_000)]
    public async Task<IActionResult> EnrollFinish([FromBody] PhoneLinkRequest request)
    {
        IActionResult? error;
        PhoneLink? link = FindLiveLink(request.Token, PhoneLinkKind.Enroll, out error);
        if (link == null)
        {
            return error!;
        }
        ApplicationUser? user = await FindLinkOwnerAsync(link);
        if (user == null)
        {
            return Gone(LinkEnded);
        }
        if (!await signIn.EnrollmentAllowedAsync(user))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "This account cannot register a device right now." });
        }

        // Claim the link first, so a cancel on the computer or a second phone cannot race the save.
        if (!links.TryMove(link, PhoneLinkStatus.Verifying, PhoneLinkStatus.Waiting, PhoneLinkStatus.Opened))
        {
            return Gone(LinkEnded);
        }

        string device = PhoneLinkStore.DeviceLabel(Request.Headers.UserAgent);
        BiometricResult result = await passkeys.RegisterAsync(user, request.CredentialJson, device);
        if (!result.Succeeded)
        {
            bool ended = links.Fail(link, PhoneLinkStatus.Opened);
            await signIn.AuditAsync(user.Id, "BiometricEnrollFailed", "passkey-phone", Ip(), result.Error);
            string message = result.Error ?? GenericFailure;
            if (ended)
            {
                message = "Too many failed attempts. Create a new QR code on your computer.";
            }
            return BadRequest(new { error = message, restart = ended });
        }

        link.Device = device;
        links.TryMove(link, PhoneLinkStatus.Completed, PhoneLinkStatus.Verifying);
        await signIn.AuditAsync(user.Id, "BiometricEnroll", "passkey-phone", Ip(), device);
        return Json(new { succeeded = true, device = device });
    }

    // Checks the number from the computer, then offers only this account's passkeys to the phone.
    [AllowAnonymous, HttpPost("SignInOptions"), ValidateAntiForgeryToken]
    public async Task<IActionResult> SignInOptions([FromBody] PhoneLinkRequest request)
    {
        IActionResult? error;
        PhoneLink? link = FindLiveLink(request.Token, PhoneLinkKind.SignIn, out error);
        if (link == null)
        {
            return error!;
        }
        ApplicationUser? user = await FindLinkOwnerAsync(link);
        if (user == null)
        {
            return Gone(LinkEnded);
        }

        if (!link.CodeConfirmed)
        {
            if (!SameCode(link.Code, request.Code))
            {
                bool ended = links.Fail(link, link.Status);
                string? auditDetail = null;
                string message = "That number does not match the one on your computer.";
                if (ended)
                {
                    auditDetail = "link ended";
                    message = "Too many wrong numbers. Start again on your computer.";
                }
                await signIn.AuditAsync(user.Id, "PhoneSignInCodeRejected", BiometricSignInService.PasskeyMethod, Ip(), auditDetail);
                return BadRequest(new { restart = ended, error = message });
            }
            link.CodeConfirmed = true;
        }

        if (!await signIn.AccountAllowedAsync(user))
        {
            return Unauthorized(new { error = GenericFailure });
        }
        return Content(await passkeys.CreateLoginOptionsAsync(user), "application/json");
    }

    [AllowAnonymous, HttpPost("SignInApprove"), ValidateAntiForgeryToken, RequestSizeLimit(100_000)]
    public async Task<IActionResult> SignInApprove([FromBody] PhoneLinkRequest request)
    {
        IActionResult? error;
        PhoneLink? link = FindLiveLink(request.Token, PhoneLinkKind.SignIn, out error);
        if (link == null)
        {
            return error!;
        }
        ApplicationUser? user = await FindLinkOwnerAsync(link);
        if (user == null)
        {
            return Gone(LinkEnded);
        }
        if (!link.CodeConfirmed)
        {
            return BadRequest(new { error = "Enter the number shown on your computer first." });
        }
        if (!links.TryMove(link, PhoneLinkStatus.Verifying, PhoneLinkStatus.Waiting, PhoneLinkStatus.Opened))
        {
            return Gone(LinkEnded);
        }

        // The passkey must be valid AND belong to the account of the link.
        BiometricResult result = await passkeys.VerifyLoginAsync(request.CredentialJson);
        if (result.Succeeded && (result.User == null || result.User.Id != link.UserId))
        {
            result = BiometricResult.Fail("passkey of another account");
        }

        if (!result.Succeeded || !await signIn.AccountAllowedAsync(user))
        {
            throttle.Failure(BiometricThrottle.IpKey(Ip()));
            await signIn.RegisterFailureAsync(user, BiometricSignInService.PasskeyMethod, "phone approval: " + (result.Error ?? "account not allowed"), Ip());
            bool ended = links.Fail(link, PhoneLinkStatus.Opened);
            string message = "This phone could not confirm the sign-in. Use the passkey registered for this account.";
            if (ended)
            {
                message = "Too many failed attempts. Start again on your computer.";
            }
            return Unauthorized(new { restart = ended, error = message });
        }

        // Saving the passkey's sign counter may refresh the stamp; SignInComplete compares against this one.
        link.SecurityStamp = user.SecurityStamp;
        links.TryMove(link, PhoneLinkStatus.Approved, PhoneLinkStatus.Verifying);
        await signIn.AuditAsync(user.Id, "PhoneSignInApproved", BiometricSignInService.PasskeyMethod, Ip(), link.Device);
        return Json(new { succeeded = true });
    }

    // "This wasn't me" or "Cancel" on the phone.
    [AllowAnonymous, HttpPost("Deny"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Deny([FromBody] PhoneLinkRequest request)
    {
        PhoneLink? link = links.FromToken(request.Token);
        if (link == null)
        {
            return Json(new { cancelled = true });
        }

        string newStatus = PhoneLinkStatus.Cancelled;
        string auditAction = "PhoneLinkCancelled";
        if (link.Kind == PhoneLinkKind.SignIn)
        {
            newStatus = PhoneLinkStatus.Denied;
            auditAction = "PhoneSignInDenied";
        }

        if (links.TryMove(link, newStatus, PhoneLinkStatus.Waiting, PhoneLinkStatus.Opened))
        {
            await signIn.AuditAsync(link.UserId, auditAction, "passkey-phone", Ip(), "from phone");
        }
        return Json(new { cancelled = true });
    }

    // ================================================================ Helpers

    /// <summary>
    /// The live link of a phone token (and of the expected kind, when one is given).
    /// Returns null and sets "error" to the answer to send when the token is unknown, blocked, or the link already ended.
    /// </summary>
    private PhoneLink? FindLiveLink(string? token, PhoneLinkKind? kind, out IActionResult? error)
    {
        error = null;
        string ipKey = BiometricThrottle.IpKey(Ip());
        if (throttle.IsBlocked(ipKey))
        {
            error = TooManyAttempts();
            return null;
        }

        PhoneLink? link = links.FromToken(token);
        if (link == null || (kind != null && link.Kind != kind))
        {
            // An unknown token counts as a failure: this slows down anyone guessing tokens.
            throttle.Failure(ipKey);
            error = Gone(LinkEnded);
            return null;
        }

        if (!PhoneLinkStore.IsLive(link))
        {
            error = Gone(EndedMessage(link));
            return null;
        }
        return link;
    }

    /// <summary>
    /// The account of the link, or null when the link must end: the password changed (new security stamp),
    /// the account was deactivated, or it is locked out.
    /// </summary>
    private async Task<ApplicationUser?> FindLinkOwnerAsync(PhoneLink link)
    {
        ApplicationUser? user = await users.FindByIdAsync(link.UserId);
        bool stillValid = user != null && user.SecurityStamp == link.SecurityStamp && user.IsActive && !await users.IsLockedOutAsync(user);
        if (!stillValid)
        {
            links.TryMove(link, PhoneLinkStatus.Failed, PhoneLinkStatus.Waiting, PhoneLinkStatus.Opened);
            return null;
        }
        return user;
    }

    private static string EndedMessage(PhoneLink link)
    {
        if (link.Status == PhoneLinkStatus.Expired)
        {
            return "This code has expired. Create a new QR code on your computer.";
        }
        if (link.Status == PhoneLinkStatus.Completed)
        {
            return "This code was already used: the phone is registered. You can close this page.";
        }
        if (link.Status == PhoneLinkStatus.Approved || link.Status == PhoneLinkStatus.SignedIn)
        {
            return "This sign-in was already approved. You can close this page.";
        }
        if (link.Status == PhoneLinkStatus.Verifying)
        {
            return "This code is being used on another device.";
        }
        return LinkEnded;
    }

    // Compares the two numbers in constant time, so the answer time does not reveal the right number.
    private static bool SameCode(string? expected, string? given)
    {
        if (expected == null || given == null)
        {
            return false;
        }
        byte[] expectedBytes = Encoding.UTF8.GetBytes(expected);
        byte[] givenBytes = Encoding.UTF8.GetBytes(given.Trim());
        return CryptographicOperations.FixedTimeEquals(expectedBytes, givenBytes);
    }

    private static string SetupEmail(string url, int minutes)
    {
        string href = System.Net.WebUtility.HtmlEncode(url);
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

    // Opened on localhost / plain HTTP with no Biometric:PublicOrigin: a phone could not open any link.
    private ObjectResult PublicAddressRequired()
    {
        return Conflict(new { error = "Your phone cannot reach this address. Open the site from its public HTTPS address (or set Biometric:PublicOrigin) to use your phone." });
    }

    // 410 Gone: this code cannot be used any more. "restart = true" tells the phone page to stop.
    private ObjectResult Gone(string message)
    {
        return StatusCode(StatusCodes.Status410Gone, new { error = message, restart = true });
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

    private UnauthorizedObjectResult StepEnded()
    {
        return Unauthorized(new { restart = true, error = "Your sign-in step ended. Choose Fingerprint and enter your email again." });
    }
}
