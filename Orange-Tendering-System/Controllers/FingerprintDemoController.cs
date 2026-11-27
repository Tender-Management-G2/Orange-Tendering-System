// ORANGE TENDER MANAGEMENT SYSTEM — Owner: Ahmad — Module: Authentication & Biometrics
// DEVELOPMENT-ONLY fingerprint / passkey test bench at /fingerprint-demo (script: wwwroot/js/biometric-demo.js).
// Each demo browser gets one reusable, INACTIVE Identity account (no Supplier, no role, never signed in), so the real
// PasskeyService registration and verification can be tried safely. Outside Development every action is 404.
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Orange_Tendering_System.Models.Biometric;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.Services.Biometric;

namespace Orange_Tendering_System.Controllers;

[AllowAnonymous, Route("fingerprint-demo")]
public class FingerprintDemoController : Controller
{
    private const string CookieName = "OrangePasskeyDemo";

    private readonly UserManager<ApplicationUser> users;
    private readonly PasskeyService passkeys;
    private readonly IDataProtector protector;
    private readonly IWebHostEnvironment environment;

    public FingerprintDemoController(UserManager<ApplicationUser> users, PasskeyService passkeys, IDataProtectionProvider protection, IWebHostEnvironment environment)
    {
        this.users = users;
        this.passkeys = passkeys;
        this.protector = protection.CreateProtector("Orange.PasskeyDemo.v1");
        this.environment = environment;
    }

    // Runs before every action: the demo exists only in Development.
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        if (!environment.IsDevelopment())
        {
            context.Result = NotFound();
        }
    }

    // GET /fingerprint-demo
    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        ApplicationUser? user = await DemoUserAsync();
        if (DemoId() == null)
        {
            // An encrypted random id in an HttpOnly cookie identifies this demo browser.
            CookieOptions cookie = new CookieOptions();
            cookie.HttpOnly = true;
            cookie.Secure = true;
            cookie.SameSite = SameSiteMode.Strict;
            cookie.Path = "/fingerprint-demo";
            cookie.MaxAge = TimeSpan.FromDays(30);
            cookie.IsEssential = true;
            Response.Cookies.Append(CookieName, protector.Protect(Guid.NewGuid().ToString("N")), cookie);
        }

        int count = 0;
        if (user != null)
        {
            count = (await passkeys.ListAsync(user)).Count;
        }
        return View(count);
    }

    [HttpPost("register-options"), ValidateAntiForgeryToken]
    public async Task<IActionResult> RegisterOptions()
    {
        string? id = DemoId();
        if (id == null)
        {
            return BadRequest(new { error = "Open the demo page first." });
        }

        ApplicationUser? user = await DemoUserAsync();
        if (user == null)
        {
            user = new ApplicationUser();
            user.Id = "passkey-demo-" + id;
            user.UserName = "passkey-demo-" + id;
            user.Email = "passkey-demo-" + id + "@example.invalid";
            user.FullName = "Fingerprint demo";
            user.IsActive = false;
            user.EmailConfirmed = false;
            IdentityResult created = await users.CreateAsync(user);
            if (!created.Succeeded)
            {
                // A second tab may have created the same account at the same moment.
                user = await DemoUserAsync();
                if (user == null)
                {
                    return BadRequest(new { error = "Could not create the demo account." });
                }
            }
        }
        return Content(await passkeys.CreateRegistrationOptionsAsync(user), "application/json");
    }

    [HttpPost("register"), ValidateAntiForgeryToken, RequestSizeLimit(100_000)]
    public async Task<IActionResult> Register([FromBody] PasskeyRegisterRequest request)
    {
        ApplicationUser? user = await DemoUserAsync();
        if (user == null)
        {
            return BadRequest(new { error = "Open the demo page and start registration first." });
        }

        BiometricResult result = await passkeys.RegisterAsync(user, request.CredentialJson, "Demo phone / authenticator");
        if (!result.Succeeded)
        {
            return BadRequest(new { error = result.Error });
        }
        int count = (await passkeys.ListAsync(user)).Count;
        return Json(new { succeeded = true, count = count });
    }

    [HttpPost("login-options"), ValidateAntiForgeryToken]
    public async Task<IActionResult> LoginOptions()
    {
        ApplicationUser? user = await DemoUserAsync();
        if (user == null || (await passkeys.ListAsync(user)).Count == 0)
        {
            return BadRequest(new { error = "No Passkey Registered" });
        }
        return Content(await passkeys.CreateLoginOptionsAsync(user), "application/json");
    }

    [HttpPost("verify"), ValidateAntiForgeryToken, RequestSizeLimit(100_000)]
    public async Task<IActionResult> Verify([FromBody] PasskeyLoginRequest request)
    {
        ApplicationUser? user = await DemoUserAsync();
        if (user == null)
        {
            return BadRequest(new { error = "No Passkey Registered" });
        }

        BiometricResult result = await passkeys.VerifyLoginAsync(request.CredentialJson);
        if (result.Succeeded && result.User != null && result.User.Id == user.Id)
        {
            return Json(new { succeeded = true });
        }
        return BadRequest(new { error = "Authentication Failed" });
    }

    [HttpPost("reset"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Reset()
    {
        ApplicationUser? user = await DemoUserAsync();
        if (user != null)
        {
            foreach (UserPasskeyInfo passkey in await passkeys.ListAsync(user))
            {
                bool removed = await passkeys.RemoveAsync(user, Base64Url.Encode(passkey.CredentialId));
                if (!removed)
                {
                    return BadRequest(new { error = "Could not remove a test passkey. Try again." });
                }
            }
        }
        return Json(new { succeeded = true });
    }

    // The demo id from the encrypted cookie, or null when it is missing or was changed.
    private string? DemoId()
    {
        string? value = Request.Cookies[CookieName];
        if (value == null)
        {
            return null;
        }
        try
        {
            string id = protector.Unprotect(value);
            Guid parsed;
            if (Guid.TryParseExact(id, "N", out parsed))
            {
                return id;
            }
            return null;
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    private async Task<ApplicationUser?> DemoUserAsync()
    {
        string? id = DemoId();
        if (id == null)
        {
            return null;
        }
        return await users.FindByIdAsync("passkey-demo-" + id);
    }
}
