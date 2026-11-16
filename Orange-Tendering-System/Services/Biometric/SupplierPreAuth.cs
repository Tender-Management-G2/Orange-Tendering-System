using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Orange_Tendering_System.Models.Biometric;
using Orange_Tendering_System.Models.Entities;

namespace Orange_Tendering_System.Services.Biometric;

/// <summary>
/// The supplier's Face ID / Fingerprint step. The Login page (handler IdentityStart) opens it when a supplier
/// chooses Face ID or Fingerprint and types the email of an approved supplier with that method set up
/// (owner decision 2026-11-16: the method is chosen first, no password before it). /Biometrics/FaceChallenge,
/// /Biometrics/FaceLogin, the passkey sign-in and the phone QR code work only inside it, for that account.
///
/// It is not a session: nothing is signed in until the face or fingerprint check succeeds. The account id stays on the
/// server (ChallengeStore, a few minutes); the browser only holds a random HttpOnly cookie that points to it.
/// It ends on success, on cancel, on account lockout, when the Login page is opened again, or when it expires.
/// </summary>
public class SupplierPreAuth(ChallengeStore challenges, IOptions<BiometricOptions> options)
{
    public const string Purpose = "supplier-preauth";
    public const string CookieName = ".Orange.SupplierPreAuth";

    public void Begin(HttpContext http, ApplicationUser user)
    {
        // A second step in the same browser replaces the first.
        challenges.Remove(http.Request.Cookies[CookieName], Purpose);
        var lifetime = TimeSpan.FromSeconds(options.Value.PreAuthSeconds);
        var token = challenges.Create(Purpose, RandomNumberGenerator.GetBytes(16), user.Id, lifetime: lifetime);
        http.Response.Cookies.Append(CookieName, token, Cookie(lifetime));
    }

    /// <summary>The account that passed the password step in this browser, or null when there is no live step.</summary>
    public string? UserId(HttpContext http) =>
        challenges.Peek(http.Request.Cookies[CookieName], Purpose)?.UserId;

    public async Task<ApplicationUser?> UserAsync(HttpContext http, UserManager<ApplicationUser> users) =>
        UserId(http) is { } id ? await users.FindByIdAsync(id) : null;

    public void End(HttpContext http)
    {
        if (!http.Request.Cookies.TryGetValue(CookieName, out var token))
            return;

        challenges.Remove(token, Purpose);
        http.Response.Cookies.Delete(CookieName, Cookie(null));
    }

    private static CookieOptions Cookie(TimeSpan? lifetime) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        IsEssential = true,
        Path = "/",
        MaxAge = lifetime
    };
}
