using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.Services.Biometric;

/// <summary>
/// Real server-side checks of fingerprint (WebAuthn) and face sign-in over HTTP against SQL Server.
/// The fingerprint tests use a software authenticator that signs with a real ES256 key, exactly as a device would,
/// so signature, challenge, origin, user-verification, replay and sign-counter logic are all genuinely exercised.
/// Face checks verify rejection of all client-supplied evidence; no physical face security is claimed.
/// </summary>
static class BiometricChecks
{
    const string Origin = "https://localhost";
    static readonly JsonSerializerOptions Json = new(JsonSerializerOptions.Web);

    public static async Task Run(Func<HttpClient> newClient, Func<HttpClient, string, Task<string>> token,
        Action<bool, string> check, IServiceProvider services, string suffix)
    {
        var email = $"bio-{suffix}@example.test";
        var password = "E!e5" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        string userId;
        using (var scope = services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true, IsActive = true, FullName = "Biometric check user" };
            check((await users.CreateAsync(user, password)).Succeeded && (await users.AddToRoleAsync(user, SystemRoles.EvaluationCommittee)).Succeeded,
                "biometric fixture user created in isolated database");
            userId = user.Id;
        }

        // ---- Login page wiring ----
        using var anon = newClient();
        var loginHtml = await (await anon.GetAsync("/Identity/Account/Login")).Content.ReadAsStringAsync();
        check(loginHtml.Contains("id=\"btnPasskeyLogin\"") && !Regex.IsMatch(loginHtml, "id=\"btnPasskeyLogin\"[^>]*disabled") &&
              Regex.IsMatch(loginHtml, "id=\"btnFaceLogin\"[^>]*disabled") && loginHtml.Contains("/js/biometric."),
            "Login page keeps passkeys enabled and Face Login disabled");
        check(loginHtml.Contains("id=\"forgot-password\"") && loginHtml.Contains("Input_Password"), "Login page keeps password form and Forgot Password link");

        // ---- Anti-forgery and authorization ----
        var anonToken = await token(anon, "/Identity/Account/Login");
        var noCsrf = await anon.PostAsJsonAsync("/Biometrics/PasskeyLoginOptions", new { });
        check(noCsrf.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.MethodNotAllowed, "biometric POST without anti-forgery token rejected (got " + (int)noCsrf.StatusCode + ")");
        check((await Post(anon, anonToken, "/Biometrics/PasskeyLoginOptions", new { })).StatusCode == HttpStatusCode.OK, "passkey login options issued to anonymous visitor");
        var unauth = await Post(anon, anonToken, "/Biometrics/FaceEnrollChallenge", new { password });
        check(unauth.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden, "enrollment endpoints require sign-in");
        check((await anon.GetAsync("/Biometrics/Fingerprint")).StatusCode == HttpStatusCode.Redirect, "enrollment pages require sign-in");

        // ---- Sign in with the password, then enrol ----
        using var session = newClient();
        var loginToken = await token(session, "/Identity/Account/Login");
        var signedIn = await session.PostAsync("/Identity/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Email"] = email, ["Input.Password"] = password, ["__RequestVerificationToken"] = loginToken
        }));
        check(signedIn.StatusCode == HttpStatusCode.Redirect && signedIn.Headers.Location?.ToString() == "/EvaluationCommittee", "password sign-in still works for fixture user");
        var fpPage = await session.GetAsync("/Biometrics/Fingerprint");
        var facePage = await session.GetAsync("/Biometrics/Face");
        check(fpPage.StatusCode == HttpStatusCode.OK && facePage.StatusCode == HttpStatusCode.OK, "signed-in user opens Fingerprint and Face pages");
        var csrf = Csrf(await fpPage.Content.ReadAsStringAsync());

        var wrongPw = await Post(session, csrf, "/Biometrics/PasskeyRegisterOptions", new { password = "wrong" });
        check(wrongPw.StatusCode == HttpStatusCode.Unauthorized, "passkey enrollment needs the current password");
        var regResponse = await Post(session, csrf, "/Biometrics/PasskeyRegisterOptions", new { password });
        check(regResponse.StatusCode == HttpStatusCode.OK, "passkey registration options issued");
        var reg = await regResponse.Content.ReadFromJsonAsync<JsonElement>();
        check(reg.GetProperty("options").GetProperty("authenticatorSelection").GetProperty("userVerification").GetString() == "required",
            "registration demands user verification (fingerprint/PIN)");

        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var credentialId = RandomNumberGenerator.GetBytes(32);
        var challenge = reg.GetProperty("options").GetProperty("challenge").GetString()!;

        // Authenticator that did not verify the user must be refused.
        var noUv = await Post(session, csrf, "/Biometrics/PasskeyRegister", Attestation(reg.GetProperty("token").GetString()!, challenge, credentialId, key, flags: 0x41));
        check(noUv.StatusCode == HttpStatusCode.BadRequest, "registration without user verification refused");

        var reg2 = await (await Post(session, csrf, "/Biometrics/PasskeyRegisterOptions", new { password })).Content.ReadFromJsonAsync<JsonElement>();
        var token2 = reg2.GetProperty("token").GetString()!;
        var challenge2 = reg2.GetProperty("options").GetProperty("challenge").GetString()!;
        var wrongOrigin = await Post(session, csrf, "/Biometrics/PasskeyRegister", Attestation(token2, challenge2, credentialId, key, origin: "https://evil.example"));
        check(wrongOrigin.StatusCode == HttpStatusCode.BadRequest, "registration from a foreign origin refused");
        var reg3 = await (await Post(session, csrf, "/Biometrics/PasskeyRegisterOptions", new { password })).Content.ReadFromJsonAsync<JsonElement>();
        var good = Attestation(reg3.GetProperty("token").GetString()!, reg3.GetProperty("options").GetProperty("challenge").GetString()!, credentialId, key);
        check((await Post(session, csrf, "/Biometrics/PasskeyRegister", good)).StatusCode == HttpStatusCode.OK, "fingerprint device registered (public key only)");
        check((await Post(session, csrf, "/Biometrics/PasskeyRegister", good)).StatusCode == HttpStatusCode.BadRequest, "registration challenge cannot be replayed");

        using (var scope = services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var row = await db.UserPasskeyCredentials.AsNoTracking().SingleAsync(c => c.UserId == userId);
            check(row.PublicKey.Length > 40 && row.CredentialId == Base64Url.Encode(credentialId), "only the public key and credential id are stored");
        }

        // ---- Fingerprint sign-in ----
        uint counter = 1;
        async Task<HttpResponseMessage> PasskeyAttempt(HttpClient client, Func<string, byte[], (string clientData, byte[] authData, byte[] signature)> mutate, byte[]? id = null)
        {
            var t = await token(client, "/Identity/Account/Login");
            var opts = await (await Post(client, t, "/Biometrics/PasskeyLoginOptions", new { })).Content.ReadFromJsonAsync<JsonElement>();
            var ch = opts.GetProperty("options").GetProperty("challenge").GetString()!;
            var (cd, ad, sig) = mutate(ch, Array.Empty<byte>());
            return await Post(client, t, "/Biometrics/PasskeyLogin", new
            {
                token = opts.GetProperty("token").GetString(), id = Base64Url.Encode(id ?? credentialId),
                clientDataJson = cd, authenticatorData = Base64Url.Encode(ad), signature = Base64Url.Encode(sig), userHandle = Base64Url.Encode(Encoding.UTF8.GetBytes(userId))
            });
        }
        (string, byte[], byte[]) Sign(string ch, byte flags = 0x05, string type = "webauthn.get", string origin = Origin, bool tamper = false)
        {
            var cd = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { type, challenge = ch, origin, crossOrigin = false }));
            var ad = AuthData(flags, counter++);
            var sig = key.SignData(ad.Concat(SHA256.HashData(cd)).ToArray(), HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
            if (tamper) sig[^1] ^= 0xFF;
            return (Base64Url.Encode(cd), ad, sig);
        }

        using var device = newClient();
        var ok = await PasskeyAttempt(device, (ch, _) => Sign(ch));
        check(ok.StatusCode == HttpStatusCode.OK && (await ok.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("redirect").GetString() == "/EvaluationCommittee",
            "fingerprint passkey sign-in succeeds and lands in the user's role workspace");
        check((await device.GetAsync("/EvaluationCommittee")).StatusCode == HttpStatusCode.OK, "fingerprint sign-in issues the normal Identity cookie");
        check((await device.GetAsync("/Admin")).StatusCode != HttpStatusCode.OK, "fingerprint sign-in grants no extra role access");

        using var attacker = newClient();
        check((await PasskeyAttempt(attacker, (ch, _) => Sign(ch, tamper: true))).StatusCode == HttpStatusCode.Unauthorized, "forged signature refused");
        check((await PasskeyAttempt(attacker, (ch, _) => Sign(ch, flags: 0x01))).StatusCode == HttpStatusCode.Unauthorized, "assertion without user verification refused");
        check((await PasskeyAttempt(attacker, (ch, _) => Sign(ch, origin: "https://evil.example"))).StatusCode == HttpStatusCode.Unauthorized, "assertion for another origin refused");
        check((await PasskeyAttempt(attacker, (ch, _) => Sign(ch, type: "webauthn.create"))).StatusCode == HttpStatusCode.Unauthorized, "assertion with wrong type refused");
        check((await PasskeyAttempt(attacker, (ch, _) => Sign(Base64Url.Encode(RandomNumberGenerator.GetBytes(32))))).StatusCode == HttpStatusCode.Unauthorized, "assertion for a different challenge refused");
        check((await PasskeyAttempt(attacker, (ch, _) => Sign(ch), RandomNumberGenerator.GetBytes(32))).StatusCode == HttpStatusCode.Unauthorized, "unknown credential refused");
        check((await attacker.GetAsync("/EvaluationCommittee")).StatusCode == HttpStatusCode.Redirect, "failed biometric attempts leave the visitor anonymous");

        // Replay of a previously valid assertion must fail (challenge is one-time).
        var replayTok = await token(attacker, "/Identity/Account/Login");
        var replayOpts = await (await Post(attacker, replayTok, "/Biometrics/PasskeyLoginOptions", new { })).Content.ReadFromJsonAsync<JsonElement>();
        var (rcd, rad, rsig) = Sign(replayOpts.GetProperty("options").GetProperty("challenge").GetString()!);
        object Body() => new { token = replayOpts.GetProperty("token").GetString(), id = Base64Url.Encode(credentialId), clientDataJson = rcd, authenticatorData = Base64Url.Encode(rad), signature = Base64Url.Encode(rsig) };
        check((await Post(attacker, replayTok, "/Biometrics/PasskeyLogin", Body())).StatusCode == HttpStatusCode.OK, "valid assertion accepted once");
        using var replayer = newClient();
        var replayCsrf = await token(replayer, "/Identity/Account/Login");
        check((await Post(replayer, replayCsrf, "/Biometrics/PasskeyLogin", Body())).StatusCode == HttpStatusCode.Unauthorized, "captured assertion cannot be replayed");

        // Account rules are the same as the password login: a deactivated account cannot use biometrics.
        await SetActive(services, userId, false);
        using var inactive = newClient();
        check((await PasskeyAttempt(inactive, (ch, _) => Sign(ch))).StatusCode == HttpStatusCode.Unauthorized, "deactivated account refused by fingerprint sign-in");
        await SetActive(services, userId, true);

        // Browser-provided face decisions/descriptors are never identity proof.
        var forged = new { email, password, token = "forged", verified = true, live = true,
            descriptors = new[] { new { v = new float[128], t = 2000 } }, frames = new[] { new { t = 2000, ear = 0.3, mar = 0.5, yaw = 0.3 } } };
        check((await Post(session, csrf, "/Biometrics/FaceEnrollChallenge", new { password })).StatusCode == HttpStatusCode.NotImplemented, "face enrollment challenge disabled server-side");
        check((await Post(session, csrf, "/Biometrics/FaceEnroll", forged)).StatusCode == HttpStatusCode.NotImplemented, "forged face enrollment rejected even when authenticated");
        using (var faceAnonymous = newClient())
        {
            var faceCsrf = await token(faceAnonymous, "/Identity/Account/Login");
            check((await Post(faceAnonymous, faceCsrf, "/Biometrics/FaceChallenge", new { })).StatusCode == HttpStatusCode.NotImplemented, "face login challenge disabled");
            check((await Post(faceAnonymous, faceCsrf, "/Biometrics/FaceLogin", forged)).StatusCode == HttpStatusCode.NotImplemented, "forged liveness and descriptors cannot create an Identity cookie");
            check((await faceAnonymous.GetAsync("/Admin")).StatusCode == HttpStatusCode.Redirect, "rejected face evidence grants no authenticated access");
        }
        using (var guardScope = services.CreateScope())
        {
            var store = guardScope.ServiceProvider.GetRequiredService<ChallengeStore>();
            var once = store.Create("parallel-test", RandomNumberGenerator.GetBytes(32));
            var consumed = 0;
            Parallel.For(0, 100, _ => { if (store.Consume(once, "parallel-test") != null) Interlocked.Increment(ref consumed); });
            check(consumed == 1, "parallel challenge consumption is atomic");
            check(!WebAuthnVerifier.IsSupportedKey(new byte[] { 0xa0 }), "malformed COSE map fails closed without an exception");
        }

        var removed = await Post(session, csrf, "/Biometrics/PasskeyRemove?id=" + (await FirstPasskeyId(services, userId)), new { });
        check(removed.StatusCode == HttpStatusCode.OK, "fingerprint device can be removed");
        using var afterRemoval = newClient();
        check((await PasskeyAttempt(afterRemoval, (ch, _) => Sign(ch))).StatusCode == HttpStatusCode.Unauthorized, "removed device no longer signs in");

        using (var scope = services.CreateScope())
        {
            var throttle = scope.ServiceProvider.GetRequiredService<BiometricThrottle>();
            for (var i = 0; i < 200; i++) throttle.Failure("ip:throttle-check-" + suffix);
            check(throttle.IsBlocked("ip:throttle-check-" + suffix), "repeated failures from one source are throttled");
            var audits = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().AuditLogs.AsNoTracking()
                .Where(a => a.UserId == userId && a.Action.StartsWith("Biometric")).Select(a => a.Action).Distinct().ToListAsync();
            var anonymousFailure = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().AuditLogs.AsNoTracking()
                .AnyAsync(a => a.UserId == null && a.Action == "BiometricSignInFailed");
            check(audits.Contains("BiometricSignIn") && audits.Contains("BiometricEnroll") && anonymousFailure,
                "verified biometric actions and anonymous failed assertions are audited without trusting claimed identities");
        }
    }

    // ---------- helpers ----------

    static async Task SetActive(IServiceProvider services, string userId, bool active)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, active));
    }

    static async Task<long> FirstPasskeyId(IServiceProvider services, string userId)
    {
        using var scope = services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().UserPasskeyCredentials.Where(c => c.UserId == userId).Select(c => c.Id).FirstAsync();
    }

    static string Csrf(string html) =>
        WebUtility.HtmlDecode(Regex.Match(html, "name=\\\"__RequestVerificationToken\\\"[^>]*value=\\\"([^\\\"]+)\\\"").Groups[1].Value);

    static Task<HttpResponseMessage> Post(HttpClient client, string csrf, string path, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body, options: Json) };
        request.Headers.Add("RequestVerificationToken", csrf);
        return client.SendAsync(request);
    }

    static object Merge(object a, object? b)
    {
        var dict = JsonSerializer.SerializeToElement(a, Json).EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value);
        if (b is not null) foreach (var p in JsonSerializer.SerializeToElement(b, Json).EnumerateObject()) dict[p.Name] = p.Value;
        return dict;
    }

    static byte[] AuthData(byte flags, uint counter, byte[]? attested = null)
    {
        var data = new List<byte>(SHA256.HashData(Encoding.UTF8.GetBytes("localhost"))) { flags };
        data.AddRange([(byte)(counter >> 24), (byte)(counter >> 16), (byte)(counter >> 8), (byte)counter]);
        if (attested is not null) data.AddRange(attested);
        return data.ToArray();
    }

    static object Attestation(string tokenValue, string challenge, byte[] credentialId, ECDsa key, byte flags = 0x45, string origin = Origin)
    {
        var p = key.ExportParameters(false);
        var cose = new List<byte> { 0xA5, 0x01, 0x02, 0x03, 0x26, 0x20, 0x01, 0x21, 0x58, 0x20 };
        cose.AddRange(p.Q.X!); cose.AddRange([0x22, 0x58, 0x20]); cose.AddRange(p.Q.Y!);
        var attested = new List<byte>(new byte[16]) { (byte)(credentialId.Length >> 8), (byte)credentialId.Length };
        attested.AddRange(credentialId); attested.AddRange(cose);
        var authData = AuthData(flags, 0, attested.ToArray());

        var obj = new List<byte> { 0xA3 };
        obj.AddRange(CborText("fmt")); obj.AddRange(CborText("none"));
        obj.AddRange(CborText("attStmt")); obj.Add(0xA0);
        obj.AddRange(CborText("authData")); obj.AddRange(CborBytes(authData));
        var clientData = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { type = "webauthn.create", challenge, origin, crossOrigin = false }));
        return new
        {
            token = tokenValue, id = Base64Url.Encode(credentialId), clientDataJson = Base64Url.Encode(clientData),
            attestationObject = Base64Url.Encode(obj.ToArray()), label = "Software test authenticator"
        };
    }

    static byte[] CborText(string s) => CborHead(3, Encoding.UTF8.GetBytes(s));
    static byte[] CborBytes(byte[] b) => CborHead(2, b);
    static byte[] CborHead(int major, byte[] payload)
    {
        var head = payload.Length < 24 ? new[] { (byte)(major << 5 | payload.Length) }
            : payload.Length < 256 ? new[] { (byte)(major << 5 | 24), (byte)payload.Length }
            : new[] { (byte)(major << 5 | 25), (byte)(payload.Length >> 8), (byte)payload.Length };
        return head.Concat(payload).ToArray();
    }
}
