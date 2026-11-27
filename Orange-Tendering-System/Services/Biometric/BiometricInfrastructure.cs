using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Orange_Tendering_System.Models.Biometric;

namespace Orange_Tendering_System.Services.Biometric;

/// <summary>
/// Base64url is the text format WebAuthn uses for binary values (ids, challenges).
/// It is normal Base64 with '-' instead of '+', '_' instead of '/', and no '=' at the end.
/// </summary>
public static class Base64Url
{
    public static string Encode(byte[] data)
    {
        string text = Convert.ToBase64String(data);
        text = text.TrimEnd('=');
        text = text.Replace('+', '-');
        text = text.Replace('/', '_');
        return text;
    }

    public static bool TryDecode(string? text, out byte[] bytes)
    {
        bytes = new byte[0];
        if (string.IsNullOrEmpty(text) || text.Length > 200000)
        {
            return false;
        }

        string base64 = text.Replace('-', '+').Replace('_', '/');
        while (base64.Length % 4 != 0)
        {
            base64 = base64 + "=";
        }

        try
        {
            bytes = Convert.FromBase64String(base64);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

/// <summary>One issued challenge: what it is for, which account it belongs to and when it stops working.</summary>
public class ChallengeEntry
{
    public string Purpose { get; set; } = string.Empty;
    public string? UserId { get; set; }
    public DateTime ExpiresAt { get; set; }
}

/// <summary>
/// Remembers one-time challenges in server memory. A challenge token can be used once and only before it expires,
/// so a captured request cannot be sent again (replayed). With several servers this would need a shared cache.
/// </summary>
public class ChallengeStore
{
    private readonly IMemoryCache cache;
    private readonly object lockObject = new object();

    public ChallengeStore(IMemoryCache cache)
    {
        this.cache = cache;
    }

    /// <summary>Creates a random token for one attempt and returns it.</summary>
    public string Create(string purpose, string? userId, int lifetimeSeconds)
    {
        string token = Base64Url.Encode(RandomNumberGenerator.GetBytes(24));
        TimeSpan lifetime = TimeSpan.FromSeconds(lifetimeSeconds);

        ChallengeEntry entry = new ChallengeEntry();
        entry.Purpose = purpose;
        entry.UserId = userId;
        entry.ExpiresAt = DateTime.UtcNow + lifetime;

        cache.Set(CacheKey(token), entry, lifetime);
        return token;
    }

    /// <summary>Returns the challenge and deletes it (it can never be used again). Null when unknown, used, expired or for another purpose.</summary>
    public ChallengeEntry? Consume(string? token, string purpose)
    {
        // The lock makes "read then delete" one step, so two parallel requests cannot both use the same token.
        lock (lockObject)
        {
            if (string.IsNullOrEmpty(token))
            {
                return null;
            }

            ChallengeEntry? entry;
            if (!cache.TryGetValue(CacheKey(token), out entry) || entry == null)
            {
                return null;
            }

            cache.Remove(CacheKey(token));

            if (entry.Purpose != purpose || entry.ExpiresAt <= DateTime.UtcNow)
            {
                return null;
            }
            return entry;
        }
    }

    /// <summary>Reads a live challenge without deleting it (for a step that spans several requests).</summary>
    public ChallengeEntry? Peek(string? token, string purpose)
    {
        if (string.IsNullOrEmpty(token))
        {
            return null;
        }

        ChallengeEntry? entry;
        if (!cache.TryGetValue(CacheKey(token), out entry) || entry == null)
        {
            return null;
        }

        if (entry.Purpose != purpose || entry.ExpiresAt <= DateTime.UtcNow)
        {
            return null;
        }
        return entry;
    }

    /// <summary>Ends a challenge early. A challenge issued for another purpose is left alone.</summary>
    public void Remove(string? token, string purpose)
    {
        lock (lockObject)
        {
            if (Peek(token, purpose) != null)
            {
                cache.Remove(CacheKey(token!));
            }
        }
    }

    private static string CacheKey(string token)
    {
        return "bio:challenge:" + token;
    }
}

/// <summary>
/// Counts failed attempts per key (an IP address or an account) inside a time window, and blocks a key
/// after too many failures. This slows down anyone who tries many faces, passkeys or codes.
/// </summary>
public class BiometricThrottle
{
    private readonly IMemoryCache cache;
    private readonly BiometricOptions options;
    private readonly object lockObject = new object();

    public BiometricThrottle(IMemoryCache cache, IOptions<BiometricOptions> options)
    {
        this.cache = cache;
        this.options = options.Value;
    }

    /// <summary>True when this key already has the maximum number of failures in the current window.</summary>
    public bool IsBlocked(string key)
    {
        int count;
        if (cache.TryGetValue(CacheKey(key), out count))
        {
            return count >= options.MaxFailuresPerWindow;
        }
        return false;
    }

    /// <summary>Counts one use of a limited action (a new QR code, a setup email). False once the limit is reached.</summary>
    public bool TryUse(string key, int limit)
    {
        lock (lockObject)
        {
            int count;
            if (!cache.TryGetValue(CacheKey(key), out count))
            {
                count = 0;
            }

            if (count >= limit)
            {
                return false;
            }

            cache.Set(CacheKey(key), count + 1, TimeSpan.FromMinutes(options.FailureWindowMinutes));
            return true;
        }
    }

    /// <summary>Adds one failure for this key.</summary>
    public void Failure(string key)
    {
        lock (lockObject)
        {
            int count;
            if (!cache.TryGetValue(CacheKey(key), out count))
            {
                count = 0;
            }

            cache.Set(CacheKey(key), count + 1, TimeSpan.FromMinutes(options.FailureWindowMinutes));
        }
    }

    /// <summary>Forgets the failures of this key (after a successful sign-in).</summary>
    public void Reset(string key)
    {
        cache.Remove(CacheKey(key));
    }

    public static string IpKey(string? ipAddress)
    {
        if (ipAddress == null)
        {
            return "ip:unknown";
        }
        return "ip:" + ipAddress;
    }

    /// <summary>A key for an account name. The email is hashed, so it is not kept in memory as plain text.</summary>
    public static string AccountKey(string? accountName)
    {
        string normalized = (accountName ?? string.Empty).Trim().ToUpperInvariant();
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        string hex = Convert.ToHexString(hash);
        return "acct:" + hex.Substring(0, 16);
    }

    private static string CacheKey(string key)
    {
        return "bio:throttle:" + key;
    }
}
