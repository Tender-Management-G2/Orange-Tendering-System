using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Orange_Tendering_System.Models.Biometric;
using Orange_Tendering_System.Models.Entities;
using QRCoder;

namespace Orange_Tendering_System.Services.Biometric;

/// <summary>What a phone link is for: registering a phone, or approving a sign-in on the computer.</summary>
public enum PhoneLinkKind { Enroll, SignIn }

/// <summary>The states of a phone link, as the computer page and the phone page see them.</summary>
public static class PhoneLinkStatus
{
    public const string Waiting = "waiting";     // QR code shown, nobody opened it yet
    public const string Opened = "opened";       // the phone page opened it
    public const string Verifying = "verifying"; // the phone's WebAuthn answer is being checked (a moment)
    public const string Completed = "completed"; // enrollment: the phone's passkey is saved on the account
    public const string Approved = "approved";   // sign-in: the phone approved; the computer may finish signing in
    public const string SignedIn = "signedIn";   // sign-in: the computer received its session (used up)
    public const string Denied = "denied";       // the person on the phone said "This wasn't me"
    public const string Cancelled = "cancelled"; // the computer cancelled or replaced the code
    public const string Failed = "failed";       // too many failed attempts
    public const string Expired = "expired";
}

/// <summary>
/// One QR code / email link. It lives only in server memory (like the other one-time challenges):
/// a restart ends every open link, and the person simply creates a new code.
/// </summary>
public class PhoneLink
{
    public string Id { get; set; } = string.Empty;
    public PhoneLinkKind Kind { get; set; }
    public string UserId { get; set; } = string.Empty;

    /// <summary>The account's security stamp when the link was made: a password change or lockout ends the link.</summary>
    public string? SecurityStamp { get; set; }

    /// <summary>SHA-256 of the secret in the computer's HttpOnly cookie. Only that browser can poll the link or finish it.</summary>
    public byte[] DesktopHash { get; set; } = new byte[0];

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>Sign-in only: the two-digit number shown on the computer that the phone must type.</summary>
    public string? Code { get; set; }

    /// <summary>The computer that asked (browser and system, never an address), shown on the phone.</summary>
    public string? RequestedFrom { get; set; }

    public string Status { get; set; } = PhoneLinkStatus.Waiting;
    public bool CodeConfirmed { get; set; }
    public int FailedAttempts { get; set; }

    /// <summary>The phone that opened the link (browser and system), shown on the computer.</summary>
    public string? Device { get; set; }
}

/// <summary>
/// "Use my phone" for passkeys. A single-use, short-lived token goes into a QR code (or an emailed link). A smartphone
/// opens it to register a passkey for the signed-in account, or to approve that account's waiting sign-in on the computer.
///
/// Security:
///   - The token is in the URL fragment (#t=...), so it never reaches server or tunnel request logs. Only its hash is kept.
///   - Opening the link proves nothing. Enrollment needs a verified WebAuthn registration on the phone; sign-in needs the
///     number shown on the computer plus a verified WebAuthn answer from the account's own passkey.
///   - The computer that made the code is bound to it by its own HttpOnly cookie, so another browser cannot poll it or
///     collect the session.
/// </summary>
public class PhoneLinkStore
{
    public const string DesktopCookie = ".Orange.PhoneLink";
    public const string PhonePath = "/Biometrics/Phone";
    private const int MaxFailedAttempts = 3;
    // Links stay in memory 2 minutes after they expire, so a late status request still gets "expired".
    private static readonly TimeSpan Grace = TimeSpan.FromMinutes(2);

    private readonly IMemoryCache cache;
    private readonly BiometricOptions options;
    private readonly object lockObject = new object();

    public PhoneLinkStore(IMemoryCache cache, IOptions<BiometricOptions> options)
    {
        this.cache = cache;
        this.options = options.Value;
    }

    /// <summary>
    /// Starts a new link for this computer. Older live links of the same account and kind, and this computer's previous
    /// link, are cancelled: only the newest code works. Then call AddToken() to get the token for the QR code.
    /// </summary>
    public PhoneLink Create(HttpContext http, PhoneLinkKind kind, ApplicationUser user, string? code)
    {
        int seconds = options.PhoneSignInSeconds;
        if (kind == PhoneLinkKind.Enroll)
        {
            seconds = options.PhoneEnrollSeconds;
        }
        TimeSpan lifetime = TimeSpan.FromSeconds(seconds);

        // The computer keeps this secret in its cookie; the server keeps only the hash.
        string secret = Base64Url.Encode(RandomNumberGenerator.GetBytes(32));
        DateTimeOffset now = DateTimeOffset.UtcNow;

        PhoneLink link = new PhoneLink();
        link.Id = Base64Url.Encode(RandomNumberGenerator.GetBytes(16));
        link.Kind = kind;
        link.UserId = user.Id;
        link.SecurityStamp = user.SecurityStamp;
        link.DesktopHash = Hash(secret);
        link.CreatedAt = now;
        link.ExpiresAt = now + lifetime;
        link.Code = code;
        link.RequestedFrom = DeviceLabel(http.Request.Headers.UserAgent);

        lock (lockObject)
        {
            string? previousId;
            if (cache.TryGetValue(UserKey(user.Id, kind), out previousId))
            {
                PhoneLink? previous = Get(previousId);
                if (previous != null)
                {
                    Move(previous, PhoneLinkStatus.Cancelled, PhoneLinkStatus.Waiting, PhoneLinkStatus.Opened, PhoneLinkStatus.Approved);
                }
            }

            PhoneLink? mine = FromDesktop(http);
            if (mine != null)
            {
                Move(mine, PhoneLinkStatus.Cancelled, PhoneLinkStatus.Waiting, PhoneLinkStatus.Opened, PhoneLinkStatus.Approved);
            }

            cache.Set(LinkKey(link.Id), link, lifetime + Grace);
            cache.Set(UserKey(user.Id, kind), link.Id, lifetime + Grace);
        }

        http.Response.Cookies.Append(DesktopCookie, link.Id + "." + secret, CookieSettings(lifetime + Grace));
        return link;
    }

    /// <summary>A new random token for the link (one for the QR code, another for the email). Either one opens the link.</summary>
    public string AddToken(PhoneLink link)
    {
        string token = Base64Url.Encode(RandomNumberGenerator.GetBytes(32));
        TimeSpan lifetime = link.ExpiresAt - DateTimeOffset.UtcNow;
        if (lifetime < TimeSpan.Zero)
        {
            lifetime = TimeSpan.Zero;
        }

        lock (lockObject)
        {
            cache.Set(TokenKey(token), link.Id, lifetime + Grace);
        }
        return token;
    }

    /// <summary>The link a phone token belongs to (in any state), or null for an unknown token.</summary>
    public PhoneLink? FromToken(string? token)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 100)
        {
            return null;
        }

        string? id;
        if (!cache.TryGetValue(TokenKey(token), out id))
        {
            return null;
        }
        return MarkExpired(Get(id));
    }

    /// <summary>The link this browser started (checked against its HttpOnly cookie), or null.</summary>
    public PhoneLink? FromDesktop(HttpContext http)
    {
        // The cookie value is "linkId.secret".
        string? value = http.Request.Cookies[DesktopCookie];
        if (value == null)
        {
            return null;
        }
        int dot = value.IndexOf('.');
        if (dot <= 0)
        {
            return null;
        }

        PhoneLink? link = Get(value.Substring(0, dot));
        if (link == null)
        {
            return null;
        }

        // FixedTimeEquals takes the same time for any input, so the secret cannot be guessed by measuring time.
        byte[] givenHash = Hash(value.Substring(dot + 1));
        if (!CryptographicOperations.FixedTimeEquals(link.DesktopHash, givenHash))
        {
            return null;
        }
        return MarkExpired(link);
    }

    public void EndDesktop(HttpContext http)
    {
        http.Response.Cookies.Delete(DesktopCookie, CookieSettings(null));
    }

    /// <summary>Moves the link to a new state, but only from one of the expected states. False otherwise.</summary>
    public bool TryMove(PhoneLink link, string to, params string[] from)
    {
        lock (lockObject)
        {
            MarkExpired(link);
            return Move(link, to, from);
        }
    }

    /// <summary>Counts a failed attempt. The third one ends the link (and returns true).</summary>
    public bool Fail(PhoneLink link, string backTo)
    {
        lock (lockObject)
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

    /// <summary>Waiting or Opened: the link can still be used.</summary>
    public static bool IsLive(PhoneLink link)
    {
        return link.Status == PhoneLinkStatus.Waiting || link.Status == PhoneLinkStatus.Opened;
    }

    public static int SecondsLeft(PhoneLink link)
    {
        double seconds = (link.ExpiresAt - DateTimeOffset.UtcNow).TotalSeconds;
        if (seconds <= 0)
        {
            return 0;
        }
        return (int)Math.Ceiling(seconds);
    }

    /// <summary>
    /// The address the phone opens, or null when no phone could reach this site.
    /// A page opened over a public HTTPS address uses its own address. A page opened on this computer (localhost or plain
    /// HTTP) uses Biometric:PublicOrigin, the public HTTPS address of the same running app (for example ngrok).
    /// </summary>
    public string? PhoneUrl(HttpRequest request, string token)
    {
        string? origin = PhoneOrigin(request);
        if (origin == null)
        {
            return null;
        }
        return origin + PhonePath + "#t=" + token;
    }

    public bool PhoneReachable(HttpRequest request)
    {
        return PhoneOrigin(request) != null;
    }

    private string? PhoneOrigin(HttpRequest request)
    {
        if (request.IsHttps && !IsLoopback(request.Host.Host))
        {
            return request.Scheme + "://" + request.Host + request.PathBase;
        }

        Uri? publicOrigin;
        if (Uri.TryCreate(options.PublicOrigin, UriKind.Absolute, out publicOrigin) && publicOrigin.Scheme == Uri.UriSchemeHttps)
        {
            return publicOrigin.GetLeftPart(UriPartial.Authority);
        }
        return null;
    }

    /// <summary>A PNG QR code as a "data:" address that an img tag can show directly.</summary>
    public static string QrDataUri(string text)
    {
        using QRCodeGenerator generator = new QRCodeGenerator();
        using QRCodeData data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        byte[] png = new PngByteQRCode(data).GetGraphic(8);
        return "data:image/png;base64," + Convert.ToBase64String(png);
    }

    /// <summary>A readable device name from the browser's User-Agent text, for example "Android phone · Chrome".</summary>
    public static string DeviceLabel(string? userAgent)
    {
        string ua = userAgent ?? string.Empty;

        string device;
        if (ua.Contains("iPhone")) device = "iPhone";
        else if (ua.Contains("iPad")) device = "iPad";
        else if (ua.Contains("Android") && ua.Contains("Mobile")) device = "Android phone";
        else if (ua.Contains("Android")) device = "Android tablet";
        else if (ua.Contains("Windows")) device = "Windows computer";
        else if (ua.Contains("Macintosh")) device = "Mac";
        else if (ua.Contains("CrOS")) device = "Chromebook";
        else if (ua.Contains("Linux")) device = "Linux computer";
        else device = "Device";

        // The order matters: Edge and Samsung Internet also contain "Chrome/", and Chrome also contains "Safari/".
        string? browser;
        if (ua.Contains("Edg")) browser = "Edge";
        else if (ua.Contains("SamsungBrowser")) browser = "Samsung Internet";
        else if (ua.Contains("CriOS") || ua.Contains("Chrome/")) browser = "Chrome";
        else if (ua.Contains("FxiOS") || ua.Contains("Firefox/")) browser = "Firefox";
        else if (ua.Contains("Safari/")) browser = "Safari";
        else browser = null;

        if (browser == null)
        {
            return device;
        }
        return device + " · " + browser;
    }

    /// <summary>"a•••@gmail.com": enough for the owner to recognise the account on the phone.</summary>
    public static string MaskEmail(string? email)
    {
        if (string.IsNullOrEmpty(email))
        {
            return "your account";
        }
        int at = email.IndexOf('@');
        if (at <= 0)
        {
            return "your account";
        }
        return email[0] + "•••" + email.Substring(at);
    }

    // ---------- internals ----------

    private PhoneLink? Get(string? id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return null;
        }
        PhoneLink? link;
        if (cache.TryGetValue(LinkKey(id), out link))
        {
            return link;
        }
        return null;
    }

    // A link whose time is over becomes Expired (unless it already ended another way).
    private static PhoneLink? MarkExpired(PhoneLink? link)
    {
        if (link == null)
        {
            return null;
        }
        bool stillOpen = link.Status == PhoneLinkStatus.Waiting || link.Status == PhoneLinkStatus.Opened || link.Status == PhoneLinkStatus.Approved;
        if (stillOpen && link.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            link.Status = PhoneLinkStatus.Expired;
        }
        return link;
    }

    // Changes the status only when the current status is one of "from".
    private static bool Move(PhoneLink link, string to, params string[] from)
    {
        if (!from.Contains(link.Status))
        {
            return false;
        }
        link.Status = to;
        return true;
    }

    private static bool IsLoopback(string host)
    {
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        IPAddress? address;
        if (IPAddress.TryParse(host.Trim('[', ']'), out address))
        {
            return IPAddress.IsLoopback(address);
        }
        return false;
    }

    private static byte[] Hash(string value)
    {
        return SHA256.HashData(Encoding.UTF8.GetBytes(value));
    }

    private static string TokenKey(string token)
    {
        return "bio:phone:token:" + Convert.ToHexString(Hash(token));
    }

    private static string LinkKey(string id)
    {
        return "bio:phone:link:" + id;
    }

    private static string UserKey(string userId, PhoneLinkKind kind)
    {
        return "bio:phone:user:" + kind + ":" + userId;
    }

    private static CookieOptions CookieSettings(TimeSpan? lifetime)
    {
        CookieOptions cookie = new CookieOptions();
        cookie.HttpOnly = true;
        cookie.Secure = true;
        cookie.SameSite = SameSiteMode.Strict;
        cookie.IsEssential = true;
        cookie.Path = PhonePath;
        cookie.MaxAge = lifetime;
        return cookie;
    }
}
