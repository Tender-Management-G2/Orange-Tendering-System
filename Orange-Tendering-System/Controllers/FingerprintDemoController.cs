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

// One reusable, inactive Identity account per demo browser. No Supplier, roles or sign-in cookie.
[AllowAnonymous, Route("fingerprint-demo")]
public class FingerprintDemoController(
    UserManager<ApplicationUser> users, PasskeyService passkeys,
    IDataProtectionProvider protection, IWebHostEnvironment environment) : Controller
{
    private const string CookieName = "OrangePasskeyDemo";
    private readonly IDataProtector protector = protection.CreateProtector("Orange.PasskeyDemo.v1");

    // Defense in depth; Program.cs also hides the routes before antiforgery filters.
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        if (!environment.IsDevelopment()) context.Result = NotFound();
    }

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var user = await DemoUser();
        if (DemoId() is null)
        {
            Response.Cookies.Append(CookieName, protector.Protect(Guid.NewGuid().ToString("N")), new CookieOptions
            { HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict, Path = "/fingerprint-demo", MaxAge = TimeSpan.FromDays(30), IsEssential = true });
        }
        return View(user is null ? 0 : (await passkeys.ListAsync(user)).Count);
    }

    [HttpPost("register-options"), ValidateAntiForgeryToken]
    public async Task<IActionResult> RegisterOptions()
    {
        if (DemoId() is not { } id) return BadRequest(new { error = "Open the demo page first." });
        var user = await DemoUser();
        if (user is null)
        {
            user = new ApplicationUser { Id = "passkey-demo-" + id, UserName = "passkey-demo-" + id,
                Email = "passkey-demo-" + id + "@example.invalid",
                FullName = "Fingerprint demo", IsActive = false, EmailConfirmed = false };
            var result = await users.CreateAsync(user);
            if (!result.Succeeded)
            {
                user = await DemoUser(); // A second tab may have created the same account.
                if (user is null) return BadRequest(new { error = "Could not create the demo account." });
            }
        }
        return Content(await passkeys.CreateRegistrationOptionsAsync(user), "application/json");
    }

    [HttpPost("register"), ValidateAntiForgeryToken, RequestSizeLimit(100_000)]
    public async Task<IActionResult> Register([FromBody] PasskeyRegisterRequest request)
    {
        var user = await DemoUser();
        if (user is null) return BadRequest(new { error = "Open the demo page and start registration first." });
        var result = await passkeys.RegisterAsync(user, request.CredentialJson, "Demo phone / authenticator");
        return result.Succeeded ? Json(new { succeeded = true, count = (await passkeys.ListAsync(user)).Count }) : BadRequest(new { error = result.Error });
    }

    [HttpPost("login-options"), ValidateAntiForgeryToken]
    public async Task<IActionResult> LoginOptions()
    {
        var user = await DemoUser();
        if (user is null || (await passkeys.ListAsync(user)).Count == 0) return BadRequest(new { error = "No Passkey Registered" });
        return Content(await passkeys.CreateLoginOptionsAsync(user), "application/json");
    }

    [HttpPost("verify"), ValidateAntiForgeryToken, RequestSizeLimit(100_000)]
    public async Task<IActionResult> Verify([FromBody] PasskeyLoginRequest request)
    {
        var user = await DemoUser();
        if (user is null) return BadRequest(new { error = "No Passkey Registered" });
        var result = await passkeys.VerifyLoginAsync(request.CredentialJson);
        return result.Succeeded && result.User?.Id == user.Id
            ? Json(new { succeeded = true }) : BadRequest(new { error = "Authentication Failed" });
    }

    [HttpPost("reset"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Reset()
    {
        var user = await DemoUser();
        if (user is not null)
            foreach (var key in await passkeys.ListAsync(user))
                if (!await passkeys.RemoveAsync(user, Base64Url.Encode(key.CredentialId)))
                    return BadRequest(new { error = "Could not remove a test passkey. Try again." });
        return Json(new { succeeded = true });
    }

    private string? DemoId()
    {
        try
        {
            var value = Request.Cookies[CookieName];
            var id = value is null ? null : protector.Unprotect(value);
            return Guid.TryParseExact(id, "N", out _) ? id : null;
        }
        catch (CryptographicException) { return null; }
    }

    private async Task<ApplicationUser?> DemoUser() => DemoId() is { } id
        ? await users.FindByIdAsync("passkey-demo-" + id) : null;
}
