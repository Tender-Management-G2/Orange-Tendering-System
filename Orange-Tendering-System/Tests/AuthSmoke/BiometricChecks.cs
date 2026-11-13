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
              loginHtml.Contains("id=\"btnFaceLogin\"") && !Regex.IsMatch(loginHtml, "id=\"btnFaceLogin\"[^>]*disabled") && loginHtml.Contains("/js/biometric."),
            "Login page offers passkey and Face Login");
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

        // ---- Face recognition: one-time token + instant descriptor matching (no liveness) ----
        // The evidence below is synthetic (no camera here); it exercises the server rules, not the browser model.
        var face = RandomFace(1);
        var forged = new { email, token = "forged", descriptors = Samples(face) };
        check((await Post(session, csrf, "/Biometrics/FaceEnrollChallenge", new { password = "wrong" })).StatusCode == HttpStatusCode.Unauthorized, "face enrollment requires the current password");
        check((await Post(session, csrf, "/Biometrics/FaceEnroll", forged)).StatusCode == HttpStatusCode.BadRequest, "face enrollment with a forged challenge token is rejected");
        var enrollChallenge = await (await Post(session, csrf, "/Biometrics/FaceEnrollChallenge", new { password })).Content.ReadFromJsonAsync<JsonElement>();
        check(!string.IsNullOrEmpty(enrollChallenge.GetProperty("token").GetString()) && !enrollChallenge.TryGetProperty("actions", out _),
            "face enrollment issues a one-time token with no liveness steps");
        check((await Post(session, csrf, "/Biometrics/FaceEnroll", FaceBody(enrollChallenge, face, null, Samples(face, 1)))).StatusCode == HttpStatusCode.BadRequest,
            "a single face sample is not enough to enroll");
        var twoPeople = Samples(face, 2).Concat(Samples(RandomFace(7), 1)).ToArray();
        check((await Post(session, csrf, "/Biometrics/FaceEnroll", FaceBody(await FaceChallenge(session, csrf, "/Biometrics/FaceEnrollChallenge", new { password }), face, null, twoPeople))).StatusCode == HttpStatusCode.BadRequest,
            "samples from two different faces in one capture are not enrolled");
        check((await Post(session, csrf, "/Biometrics/FaceEnroll", FaceBody(await FaceChallenge(session, csrf, "/Biometrics/FaceEnrollChallenge", new { password }), face, null))).StatusCode == HttpStatusCode.OK,
            "an instantly captured face is enrolled");
        using (var faceAnonymous = newClient())
        {
            var faceCsrf = await token(faceAnonymous, "/Identity/Account/Login");
            check((await Post(faceAnonymous, faceCsrf, "/Biometrics/FaceLogin", forged)).StatusCode == HttpStatusCode.Unauthorized, "forged face login token creates no Identity cookie");
            check((await Post(faceAnonymous, faceCsrf, "/Biometrics/FaceLogin", FaceBody(await FaceChallenge(faceAnonymous, faceCsrf, "/Biometrics/FaceChallenge", new { }), RandomFace(2), email))).StatusCode == HttpStatusCode.Unauthorized,
                "a different face is rejected");
            check((await faceAnonymous.GetAsync("/EvaluationCommittee")).StatusCode == HttpStatusCode.Redirect, "a failed face match never signs the user in");
            var matching = FaceBody(await FaceChallenge(faceAnonymous, faceCsrf, "/Biometrics/FaceChallenge", new { }), face, email);
            var faceLogin = await Post(faceAnonymous, faceCsrf, "/Biometrics/FaceLogin", matching);
            check(faceLogin.StatusCode == HttpStatusCode.OK && (await faceAnonymous.GetAsync("/EvaluationCommittee")).StatusCode == HttpStatusCode.OK, "the enrolled face signs in");
            using var faceReplayer = newClient();
            var replayFaceCsrf = await token(faceReplayer, "/Identity/Account/Login");
            check((await Post(faceReplayer, replayFaceCsrf, "/Biometrics/FaceLogin", matching)).StatusCode == HttpStatusCode.Unauthorized, "a captured face login cannot be replayed");
        }
        await SupplierFaceChecks(newClient, token, check, services, suffix);
        await FaceDemoChecks(newClient, token, check, services);
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

    // Registration step and approval rules for a supplier: enroll while Pending (from Pending Approval),
    // face sign-in only once Approved, and no password sign-in after biometrics are enrolled.
    static async Task SupplierFaceChecks(Func<HttpClient> newClient, Func<HttpClient, string, Task<string>> token,
        Action<bool, string> check, IServiceProvider services, string suffix)
    {
        var email = $"bio-supplier-{suffix}@example.test";
        var password = "F!f6" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        int companyId;
        using (var scope = services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var company = new Supplier { CompanyName = "Face check " + suffix, Email = email, RegistrationNumber = "FACE-" + suffix, Status = SupplierStatus.Pending };
            db.Suppliers.Add(company);
            await db.SaveChangesAsync();
            companyId = company.Id;
            var account = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true, IsActive = true, FullName = "Face supplier", SupplierId = company.Id };
            check((await users.CreateAsync(account, password)).Succeeded && (await users.AddToRoleAsync(account, SystemRoles.Supplier)).Succeeded,
                "verified pending supplier fixture created");
        }

        async Task<HttpResponseMessage> PasswordLogin(HttpClient client) =>
            await client.PostAsync("/Identity/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Input.Email"] = email, ["Input.Password"] = password, ["__RequestVerificationToken"] = await token(client, "/Identity/Account/Login")
            }));

        using var pending = newClient();
        var pendingLogin = await PasswordLogin(pending);
        check(pendingLogin.Headers.Location?.ToString().Contains("PendingApproval") == true, "pending supplier password sign-in lands on Pending Approval");
        var pendingPage = await (await pending.GetAsync("/Identity/Account/PendingApproval")).Content.ReadAsStringAsync();
        check(pendingPage.Contains("id=\"btnFaceEnroll\"") && pendingPage.Contains("<h1>Face Registration</h1>") &&
              pendingPage.Contains("aria-current=\"step\">Face registration"), "after email verification the registration shows the Face Registration step");
        var csrf = Csrf(pendingPage);
        var face = RandomFace(3);
        var enrolled = await Post(pending, csrf, "/Biometrics/FaceEnroll", FaceBody(await FaceChallenge(pending, csrf, "/Biometrics/FaceEnrollChallenge", new { password }), face, null));
        check(enrolled.StatusCode == HttpStatusCode.OK, "pending supplier enrolls a face during registration");
        var completed = await (await pending.GetAsync("/Identity/Account/PendingApproval?faceRegistered=true")).Content.ReadAsStringAsync();
        check(completed.Contains("Face registered successfully") && completed.Contains("<h1>Pending Approval</h1>") && completed.Contains("id=\"btnPasskeyRegister\""),
            "after face registration the supplier reaches Pending Approval");
        check((await pending.GetAsync("/Biometrics/Face")).Headers.Location?.ToString().Contains("PendingApproval") == true,
            "other biometric pages stay closed to a pending supplier");
        check((await pending.GetAsync("/Supplier")).Headers.Location?.ToString().Contains("PendingApproval") == true,
            "face enrollment does not open the Supplier workspace before approval");

        using var visitor = newClient();
        var visitorCsrf = await token(visitor, "/Identity/Account/Login");
        var pendingFace = await Post(visitor, visitorCsrf, "/Biometrics/FaceLogin", FaceBody(await FaceChallenge(visitor, visitorCsrf, "/Biometrics/FaceChallenge", new { }), face, email));
        check(pendingFace.StatusCode == HttpStatusCode.Unauthorized && (await pendingFace.Content.ReadAsStringAsync()).Contains("awaiting administrator approval"),
            "a pending supplier's face is recognised but cannot sign in");

        using (var scope = services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await db.Suppliers.SingleAsync(s => s.Id == companyId)).Status = SupplierStatus.Approved;
            await db.SaveChangesAsync();
        }
        var approvedFace = await Post(visitor, visitorCsrf, "/Biometrics/FaceLogin", FaceBody(await FaceChallenge(visitor, visitorCsrf, "/Biometrics/FaceChallenge", new { }), face, email));
        var approvedBody = await approvedFace.Content.ReadFromJsonAsync<JsonElement>();
        check(approvedFace.StatusCode == HttpStatusCode.OK && approvedBody.GetProperty("redirect").GetString() == "/Supplier" &&
              (await visitor.GetAsync("/Supplier")).StatusCode == HttpStatusCode.OK, "approved supplier signs in with face and opens the Supplier workspace");

        using var passwordOnly = newClient();
        var refused = await PasswordLogin(passwordOnly);
        check(refused.StatusCode == HttpStatusCode.OK && (await refused.Content.ReadAsStringAsync()).Contains("Supplier accounts sign in with face recognition") &&
              (await passwordOnly.GetAsync("/Supplier")).StatusCode != HttpStatusCode.OK, "approved supplier with face enrolled cannot bypass it with the password");
    }

    // Development-only /face-demo: real capture checks + matching, but no account, no sign-in and no stored templates.
    static async Task FaceDemoChecks(Func<HttpClient> newClient, Func<HttpClient, string, Task<string>> token,
        Action<bool, string> check, IServiceProvider services)
    {
        async Task<(int Users, int Templates)> Counts()
        {
            using var scope = services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            return (await db.Users.CountAsync(), await db.FaceTemplates.CountAsync());
        }

        var before = await Counts();
        using var demo = newClient();
        var page = await demo.GetAsync("/face-demo");
        check(page.StatusCode == HttpStatusCode.OK && (await page.Content.ReadAsStringAsync()).Contains("id=\"btnDemoVerify\""),
            "face demo page opens anonymously in Development");
        var csrf = await token(demo, "/face-demo");
        var face = RandomFace(4);
        async Task<JsonElement> Attempt(string path, object body) => await (await Post(demo, csrf, path, body)).Content.ReadFromJsonAsync<JsonElement>();
        async Task<JsonElement> Live(string path, float[] who) => await Attempt(path, FaceBody(await FaceChallenge(demo, csrf, "/face-demo/challenge", new { }), who, null));

        check((await Live("/face-demo/verify", face)).GetProperty("stage").GetString() == "noTestFace", "demo verify before registering asks for a test face");
        var registered = await Live("/face-demo/register", face);
        check(registered.GetProperty("registered").GetBoolean() && registered.GetProperty("samples").GetInt32() > 0, "demo registers a test face");
        var same = await Live("/face-demo/verify", face);
        check(same.GetProperty("stage").GetString() == "match" && same.GetProperty("matched").GetBoolean() &&
              same.GetProperty("averageDistance").GetDouble() <= same.GetProperty("threshold").GetDouble(), "demo matches the same face and reports distance and threshold");
        var other = await Live("/face-demo/verify", RandomFace(5));
        check(other.GetProperty("stage").GetString() == "match" && !other.GetProperty("matched").GetBoolean() &&
              other.GetProperty("averageDistance").GetDouble() > other.GetProperty("threshold").GetDouble(), "demo rejects a different face");
        var mixed = await Attempt("/face-demo/verify", FaceBody(await FaceChallenge(demo, csrf, "/face-demo/challenge", new { }), face, null,
            Samples(face, 2).Concat(Samples(RandomFace(6), 1)).ToArray()));
        check(mixed.GetProperty("stage").GetString() == "rejected", "demo rejects a capture that mixes two faces");
        await Post(demo, csrf, "/face-demo/reset", new { });
        check((await Live("/face-demo/verify", face)).GetProperty("stage").GetString() == "noTestFace", "demo reset clears the test face");

        using var noCookie = newClient();
        var noCookieCsrf = await token(noCookie, "/Identity/Account/Login");
        check((await Post(noCookie, noCookieCsrf, "/face-demo/challenge", new { })).StatusCode == HttpStatusCode.BadRequest, "demo endpoints need the demo page's browser id");
        check(await Counts() == before && (await demo.GetAsync("/Supplier")).StatusCode == HttpStatusCode.Redirect &&
              (await demo.GetAsync("/EvaluationCommittee")).StatusCode == HttpStatusCode.Redirect,
            "face demo creates no account, stores no real template and signs nobody in");
    }

    // ---- Synthetic face evidence (what biometric.js sends after face-api.js has read the camera) ----

    static float[] RandomFace(int seed)
    {
        var random = new Random(seed);
        return Enumerable.Range(0, 128).Select(_ => (float)(random.NextDouble() * 0.4 - 0.2)).ToArray();
    }

    // Instant capture: three samples of one face from consecutive camera frames (~40 ms apart), each with a little noise.
    static object[] Samples(float[] face, int count = 3)
    {
        var random = new Random();
        return Enumerable.Range(0, count).Select(i => (object)new
        {
            t = i * 40,
            v = face.Select(x => x + (float)(random.NextDouble() * 0.01 - 0.005)).ToArray()
        }).ToArray();
    }

    static async Task<JsonElement> FaceChallenge(HttpClient client, string csrf, string path, object body) =>
        await (await Post(client, csrf, path, body)).Content.ReadFromJsonAsync<JsonElement>();

    static object FaceBody(JsonElement challenge, float[] face, string? email, object[]? descriptors = null) =>
        new { token = challenge.GetProperty("token").GetString(), email, replace = true, descriptors = descriptors ?? Samples(face) };

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
