using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Orange_Tendering_System.Models.Biometric;
using Orange_Tendering_System.Models.Entities;

namespace Orange_Tendering_System.Services.Biometric;

/// <summary>
/// The short "identity step" of a Face ID or Fingerprint sign-in.
///
/// On the Login page the person chooses Face ID or Fingerprint and types an email (Login.cshtml.cs, OnPostIdentityStartAsync).
/// If that account may use the method, Begin() remembers the account on the server for a few minutes and gives the browser
/// a random HttpOnly cookie that points to it. The face check (/Biometrics/FaceLogin), the passkey check
/// (/Biometrics/PasskeyLogin) and the phone QR code then work only for that one account.
///
/// Nothing is signed in here. The step ends on success, on cancel, on lockout, when the Login page opens again, or when it expires.
/// The name "SupplierPreAuth" is historical: employees use the same step since 2026-11-17.
/// </summary>
public class SupplierPreAuth
{
    public const string Purpose = "supplier-preauth";
    public const string CookieName = ".Orange.SupplierPreAuth";

    private readonly ChallengeStore challenges;
    private readonly BiometricOptions options;

    public SupplierPreAuth(ChallengeStore challenges, IOptions<BiometricOptions> options)
    {
        this.challenges = challenges;
        this.options = options.Value;
    }

    /// <summary>Starts the step for this account (a second step in the same browser replaces the first).</summary>
    public void Begin(HttpContext http, ApplicationUser user)
    {
        challenges.Remove(http.Request.Cookies[CookieName], Purpose);

        string token = challenges.Create(Purpose, user.Id, options.PreAuthSeconds);
        http.Response.Cookies.Append(CookieName, token, CookieSettings(TimeSpan.FromSeconds(options.PreAuthSeconds)));
    }

    /// <summary>The id of the account in this browser's live step, or null when there is no live step.</summary>
    public string? UserId(HttpContext http)
    {
        ChallengeEntry? entry = challenges.Peek(http.Request.Cookies[CookieName], Purpose);
        if (entry == null)
        {
            return null;
        }
        return entry.UserId;
    }

    /// <summary>The account in this browser's live step, or null.</summary>
    public async Task<ApplicationUser?> UserAsync(HttpContext http, UserManager<ApplicationUser> users)
    {
        string? userId = UserId(http);
        if (userId == null)
        {
            return null;
        }
        return await users.FindByIdAsync(userId);
    }

    /// <summary>Ends the step: the server forgets it and the cookie is deleted.</summary>
    public void End(HttpContext http)
    {
        string? token = http.Request.Cookies[CookieName];
        if (token == null)
        {
            return;
        }

        challenges.Remove(token, Purpose);
        http.Response.Cookies.Delete(CookieName, CookieSettings(null));
    }

    // HttpOnly: JavaScript cannot read it. Secure: HTTPS only. Strict: never sent by another site's page.
    private static CookieOptions CookieSettings(TimeSpan? lifetime)
    {
        CookieOptions cookie = new CookieOptions();
        cookie.HttpOnly = true;
        cookie.Secure = true;
        cookie.SameSite = SameSiteMode.Strict;
        cookie.IsEssential = true;
        cookie.Path = "/";
        cookie.MaxAge = lifetime;
        return cookie;
    }
}
