using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Orange_Tendering_System.Models.Biometric;
using Orange_Tendering_System.Models.Entities;
using QRCoder;

namespace Orange_Tendering_System.Services.Biometric;

public enum PhoneLinkKind { Enroll, SignIn }

/// <summary>States of a phone link, as the computer's page and the phone page see them.</summary>
public static class PhoneLinkStatus
{
    public const string Waiting = "waiting";     // QR code shown, nobody opened it yet
    public const string Opened = "opened";       // the phone page opened it
    public const string Verifying = "verifying"; // the phone's WebAuthn answer is being checked (moments)
    public const string Completed = "completed"; // enrollment: the phone's passkey is saved on the account
    public const string Approved = "approved";   // sign-in: the phone approved; the computer may finish signing in
    public const string SignedIn = "signedIn";   // sign-in: the computer received its session (used up)
    public const string Denied = "denied";       // the person on the phone said it was not them
    public const string Cancelled = "cancelled"; // the computer cancelled or replaced the code
    public const string Failed = "failed";       // too many failed attempts
    public const string Expired = "expired";
}

/// <summary>
/// One QR code / email link. Lives in memory only (like the other one-time biometric challenges): a restart
/// simply ends every open link, and the supplier generates a new code.
/// </summary>
public sealed class PhoneLink
{
    public required string Id { get; init; }
    public required PhoneLinkKind Kind { get; init; }
    public required string UserId { get; init; }

    /// <summary>The account's security stamp when the link was made: a password change or lockout ends the link.</summary>
    public required string? SecurityStamp { get; set; }

    /// <summary>SHA-256 of the secret in the computer's HttpOnly cookie; only that browser can poll or finish.</summary>
    public required byte[] DesktopHash { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }

    /// <summary>Sign-in only: the two-digit number shown on the computer that the phone must type.</summary>
    public string? Code { get; init; }

    /// <summary>The computer that asked (browser and system, never an address), shown on the phone.</summary>
    public string? RequestedFrom { get; init; }

    public string Status { get; set; } = PhoneLinkStatus.Waiting;
    public bool CodeConfirmed { get; set; }
    public int FailedAttempts { get; set; }

    /// <summary>The phone that opened the link (browser and system), shown on the computer.</summary>
    public string? Device { get; set; }
}

/// <summary>
/// "Use my phone" for passkeys: a single-use, short-lived token in a QR code (or an emailed link) that a
/// smartphone opens to register a passkey for the signed-in account, or to approve that account's pending
/// desktop sign-in. The token travels in the URL fragment (#t=...), so it never reaches the server's or the
/// tunnel's request logs; only its SHA-256 is kept here. Opening the link proves nothing: enrollment needs a
/// verified WebAuthn registration on the phone, and sign-in needs the number shown on the computer plus a
/// verified WebAuthn assertion of the account's own passkey. The computer that made the code is bound to it
/// by a separate HttpOnly cookie, so another browser cannot poll it or collect the session.
/// </summary>
public class PhoneLinkStore(IMemoryCache cache, IOptions<BiometricOptions> options)
{
    public const string DesktopCookie = ".Orange.PhoneLink";
    public const string PhonePath = "/Biometrics/Phone";
    private const int MaxFailedAttempts = 3;
    private static readonly TimeSpan Grace = TimeSpan.FromMinutes(2);
    private readonly object gate = new();

    /// <summary>Starts a link for this computer and returns the token for the QR code. Earlier live links of the
    /// same account and kind, and this computer's previous link, are cancelled: only the newest code works.</summary>
    public (PhoneLink Link, string Token) Create(HttpContext http, PhoneLinkKind kind, ApplicationUser user, string? code = null)
    {
        var lifetime = TimeSpan.FromSeconds(kind == PhoneLinkKind.Enroll ? options.Value.PhoneEnrollSeconds : options.Value.PhoneSignInSeconds);
        var secret = Base64Url.Encode(RandomNumberGenerator.GetBytes(32));
        var now = DateTimeOffset.UtcNow;
        var link = new PhoneLink
        {
            Id = Base64Url.Encode(RandomNumberGenerator.GetBytes(16)),
            Kind = kind,
            UserId = user.Id,
            SecurityStamp = user.SecurityStamp,
            DesktopHash = Hash(secret),
            CreatedAt = now,
            ExpiresAt = now + lifetime,
            Code = code,
            RequestedFrom = DeviceLabel(http.Request.Headers.UserAgent)
        };

        string token;
        lock (gate)
        {
            if (cache.TryGetValue(UserKey(user.Id, kind), out string? previousId) && Get(previousId) is { } previous)
                Move(previous, PhoneLinkStatus.Cancelled, PhoneLinkStatus.Waiting, PhoneLinkStatus.Opened, PhoneLinkStatus.Approved);
            if (FromDesktop(http) is { } mine)
                Move(mine, PhoneLinkStatus.Cancelled, PhoneLinkStatus.Waiting, PhoneLinkStatus.Opened, PhoneLinkStatus.Approved);

            cache.Set(LinkKey(link.Id), link, lifetime + Grace);
            cache.Set(UserKey(user.Id, kind), link.Id, lifetime + Grace);
            token = AddTokenLocked(link);
        }

        http.Response.Cookies.Append(DesktopCookie, link.Id + "." + secret, Cookie(lifetime + Grace));
        return (link, token);
    }

    /// <summary>A second token for the same link (the emailed copy of the QR code). Either one finishes the link.</summary>
    public string AddToken(PhoneLink link)
    {
        lock (gate) return AddTokenLocked(link);
    }

    /// <summary>The link a phone token belongs to (any state), or null for an unknown token.</summary>
    public PhoneLink? FromToken(string? token)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 100) return null;
        return cache.TryGetValue(TokenKey(token), out string? id) ? Refresh(Get(id)) : null;
    }

    /// <summary>The link this browser started (checked against its HttpOnly cookie), or null.</summary>
    public PhoneLink? FromDesktop(HttpContext http)
    {
        var value = http.Request.Cookies[DesktopCookie];
        var dot = value?.IndexOf('.') ?? -1;
        if (value is null || dot <= 0) return null;
        var link = Get(value[..dot]);
        return link is not null && CryptographicOperations.FixedTimeEquals(link.DesktopHash, Hash(value[(dot + 1)..]))
            ? Refresh(link)
            : null;
    }

    public void EndDesktop(HttpContext http) => http.Response.Cookies.Delete(DesktopCookie, Cookie(null));

    /// <summary>Moves the link to a new state only from one of the expected states (atomic); false otherwise.</summary>
    public bool TryMove(PhoneLink link, string to, params string[] from)
    {
        lock (gate)
        {
            Refresh(link);
            return Move(link, to, from);
        }
    }

    /// <summary>Counts a failed attempt; the third one ends the link. Returns true when the link was ended.</summary>
    public bool Fail(PhoneLink link, string backTo)
    {
        lock (gate)
        {
            link.FailedAttempts++;
            if (link.FailedAttempts >= MaxFailedAttempts)
            {
                Move(link, PhoneLinkStatus.Failed, PhoneLinkStatus.Waiting, PhoneLinkStatus.Opened, PhoneLinkStatus.Verifying);
                return true;
            }

            Move(link, backTo, PhoneLinkStatus.Verifying);
            return false;
        }
    }

    public static bool IsLive(PhoneLink link) => link.Status is PhoneLinkStatus.Waiting or PhoneLinkStatus.Opened;

    public static int SecondsLeft(PhoneLink link) => Math.Max(0, (int)Math.Ceiling((link.ExpiresAt - DateTimeOffset.UtcNow).TotalSeconds));

    /// <summary>
    /// The address a phone can open, or null when there is none. A page opened over a public HTTPS address uses its
    /// own origin. A page opened on this computer (localhost, or plain HTTP) uses Biometric:PublicOrigin, the public
    /// HTTPS address of the same running application (the ngrok URL): the phone runs the whole WebAuthn ceremony on
    /// that address, so its passkey belongs to that host name (RP ID), while this computer keeps waiting on localhost
    /// for the same in-memory link. Without a public address a phone cannot reach the site, so there is no link.
    /// </summary>
    public string? PhoneUrl(HttpRequest request, string token) =>
        PhoneOrigin(request) is { } origin ? $"{origin}{PhonePath}#t={token}" : null;

    public bool PhoneReachable(HttpRequest request) => PhoneOrigin(request) is not null;

    private string? PhoneOrigin(HttpRequest request)
    {
        if (request.IsHttps && !IsLoopback(request.Host.Host))
            return $"{request.Scheme}://{request.Host}{request.PathBase}";
        return Uri.TryCreate(options.Value.PublicOrigin, UriKind.Absolute, out var origin) && origin.Scheme == Uri.UriSchemeHttps
            ? origin.GetLeftPart(UriPartial.Authority)
            : null;
    }

    /// <summary>A PNG QR code as a data: URI (dark modules on white with a quiet zone, which every phone camera reads).</summary>
    public static string QrDataUri(string text)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        return "data:image/png;base64," + Convert.ToBase64String(new PngByteQRCode(data).GetGraphic(8));
    }

    /// <summary>"Android phone · Chrome": a readable name from the User-Agent, never stored with an address.</summary>
    public static string DeviceLabel(string? userAgent)
    {
        var ua = userAgent ?? string.Empty;
        var device = ua.Contains("iPhone") ? "iPhone"
            : ua.Contains("iPad") ? "iPad"
            : ua.Contains("Android") ? (ua.Contains("Mobile") ? "Android phone" : "Android tablet")
            : ua.Contains("Windows") ? "Windows computer"
            : ua.Contains("Macintosh") ? "Mac"
            : ua.Contains("CrOS") ? "Chromebook"
            : ua.Contains("Linux") ? "Linux computer"
            : "Device";
        var browser = ua.Contains("Edg") ? "Edge"
            : ua.Contains("SamsungBrowser") ? "Samsung Internet"
            : ua.Contains("CriOS") || ua.Contains("Chrome/") ? "Chrome"
            : ua.Contains("FxiOS") || ua.Contains("Firefox/") ? "Firefox"
            : ua.Contains("Safari/") ? "Safari"
            : null;
        return browser is null ? device : device + " · " + browser;
    }

    /// <summary>"a•••@gmail.com": enough for the owner to recognise the account on the phone.</summary>
    public static string MaskEmail(string? email)
    {
        if (string.IsNullOrEmpty(email)) return "your account";
        var at = email.IndexOf('@');
        return at <= 0 ? "your account" : email[0] + "•••" + email[at..];
    }

    // ---------- internals ----------

    private string AddTokenLocked(PhoneLink link)
    {
        var token = Base64Url.Encode(RandomNumberGenerator.GetBytes(32));
        var lifetime = link.ExpiresAt - DateTimeOffset.UtcNow;
        cache.Set(TokenKey(token), link.Id, (lifetime > TimeSpan.Zero ? lifetime : TimeSpan.Zero) + Grace);
        return token;
    }

    private PhoneLink? Get(string? id) =>
        !string.IsNullOrEmpty(id) && cache.TryGetValue(LinkKey(id), out PhoneLink? link) ? link : null;

    private static PhoneLink? Refresh(PhoneLink? link)
    {
        if (link is not null && link.ExpiresAt <= DateTimeOffset.UtcNow &&
            link.Status is PhoneLinkStatus.Waiting or PhoneLinkStatus.Opened or PhoneLinkStatus.Approved)
            link.Status = PhoneLinkStatus.Expired;
        return link;
    }

    private static bool Move(PhoneLink link, string to, params string[] from)
    {
        if (!from.Contains(link.Status)) return false;
        link.Status = to;
        return true;
    }

    private static bool IsLoopback(string host) =>
        host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
        (IPAddress.TryParse(host.Trim('[', ']'), out var address) && IPAddress.IsLoopback(address));

    private static byte[] Hash(string value) => SHA256.HashData(Encoding.UTF8.GetBytes(value));
    private static string TokenKey(string token) => "bio:phone:token:" + Convert.ToHexString(Hash(token));
    private static string LinkKey(string id) => "bio:phone:link:" + id;
    private static string UserKey(string userId, PhoneLinkKind kind) => "bio:phone:user:" + kind + ":" + userId;

    private static CookieOptions Cookie(TimeSpan? lifetime) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        IsEssential = true,
        Path = PhonePath,
        MaxAge = lifetime
    };
}
