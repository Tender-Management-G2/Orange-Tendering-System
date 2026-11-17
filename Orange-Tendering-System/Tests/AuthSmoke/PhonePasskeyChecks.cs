using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Biometric;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.Services.Biometric;
using static BiometricChecks;

/// <summary>
/// "Use my phone" (Controllers/PhonePasskeyController.cs) over HTTP against SQL Server. The computer and the phone are
/// separate cookie jars on a public-looking HTTPS host (a phone cannot reach localhost), and the phone answers with the
/// same software ES256 authenticator as BiometricChecks, so Identity's real WebAuthn verification runs on every step.
/// </summary>
static class PhonePasskeyChecks
{
    const string Host = "phone-check.ngrok-free.dev";
    const string Site = "https://" + Host;
    const string IPhone = "Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.0 Mobile/15E148 Safari/604.1";
    const string Windows = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36";

    public static async Task Run(Func<string, HttpClient> clientAt, Action<bool, string> check, IServiceProvider services, string suffix, CapturedEmail mail)
    {
        var password = "P!p7" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var (emailA, userA, companyA) = await Supplier(services, check, "phone-a-" + suffix, password, SupplierStatus.Pending);
        var (emailB, userB, _) = await Supplier(services, check, "phone-b-" + suffix, password, SupplierStatus.Approved);

        HttpClient Browser(string userAgent, string site = Site)
        {
            var client = clientAt(site);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
            return client;
        }

        async Task<string> SignInWithPassword(HttpClient client, string email)
        {
            var html = await (await client.GetAsync("/Identity/Account/Login")).Content.ReadAsStringAsync();
            var response = await client.PostAsync("/Identity/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Input.Email"] = email, ["Input.Password"] = password, ["Input.AccountType"] = "supplier", ["__RequestVerificationToken"] = Csrf(html)
            }));
            return response.Headers.Location?.ToString() ?? (await response.Content.ReadAsStringAsync());
        }

        // ---- The registration page offers the phone, without a device name ----
        using var desktop = Browser(Windows);
        check((await SignInWithPassword(desktop, emailA)).Contains("PendingApproval"), "pending supplier signs in with password on the public host");
        var pendingHtml = await (await desktop.GetAsync("/Identity/Account/PendingApproval")).Content.ReadAsStringAsync();
        check(pendingHtml.Contains("id=\"phoneSetup\"") && pendingHtml.Contains("Show QR code for my phone") && pendingHtml.Contains("Send setup link to my email") &&
              pendingHtml.Contains("data-method-choice=\"faceCard\"") && pendingHtml.Contains("data-method-choice=\"passkeyCard\"") &&
              pendingHtml.Contains("id=\"btnFaceEnroll\"") && !pendingHtml.Contains("passkeyLabel") && !pendingHtml.Contains("Device name"),
            "registration step offers Face Recognition or Fingerprint / Phone, with a QR code and no device-name field");
        var csrf = Csrf(pendingHtml);

        // ---- A phone cannot open localhost: a QR code needs the public address ----
        using (var local = Browser(Windows, "https://localhost"))
        {
            await SignInWithPassword(local, emailA);
            var localCsrf = Csrf(await (await local.GetAsync("/Identity/Account/PendingApproval")).Content.ReadAsStringAsync());
            var refused = await Post(local, localCsrf, "/Biometrics/Phone/EnrollStart", new { password });
            check(refused.StatusCode == HttpStatusCode.Conflict && (await refused.Content.ReadAsStringAsync()).Contains("phone cannot reach this address"),
                "without a public address, phone setup on localhost explains that the public HTTPS address is needed");

            // With Biometric:PublicOrigin (the ngrok URL) the computer can stay on localhost: the QR code sends the phone
            // to the public address, and this localhost page sees the result of the same running application.
            var settings = services.GetRequiredService<IOptions<BiometricOptions>>().Value;
            settings.PublicOrigin = Site;
            try
            {
                var localStart = await Post(local, localCsrf, "/Biometrics/Phone/EnrollStart", new { password });
                var localLink = localStart.StatusCode == HttpStatusCode.OK ? (await Json(localStart)).GetProperty("link").GetString()! : "";
                check(localLink.StartsWith(Site + "/Biometrics/Phone#t="), "on localhost the QR code points to the configured public HTTPS address");
                using var publicPhone = Browser(IPhone);
                var publicCsrf = Csrf(await (await publicPhone.GetAsync("/Biometrics/Phone")).Content.ReadAsStringAsync());
                check((await Post(publicPhone, publicCsrf, "/Biometrics/Phone/Info", new { token = Token(localLink) })).StatusCode == HttpStatusCode.OK &&
                      await Status(local) == "opened", "the phone opens it on the public address and the localhost page sees it");
                await Post(local, localCsrf, "/Biometrics/Phone/Cancel", new { });
            }
            finally
            {
                settings.PublicOrigin = null;
            }
        }

        // ---- Start: password re-check, QR code, waiting state ----
        using var stranger = Browser(Windows);
        var anonymousCsrf = Csrf(await (await stranger.GetAsync("/Identity/Account/Login")).Content.ReadAsStringAsync());
        var anonymousStart = await Post(stranger, anonymousCsrf, "/Biometrics/Phone/EnrollStart", new { password });
        check(anonymousStart.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Unauthorized, "anonymous visitor cannot start phone enrollment");
        check((await Post(desktop, csrf, "/Biometrics/Phone/EnrollStart", new { password = "wrong" })).StatusCode == HttpStatusCode.Unauthorized,
            "phone enrollment needs the current password");
        var start = await Json(await Post(desktop, csrf, "/Biometrics/Phone/EnrollStart", new { password }));
        var qrLink = start.GetProperty("link").GetString()!;
        check(start.GetProperty("qr").GetString()!.StartsWith("data:image/png;base64,") && qrLink.StartsWith(Site + "/Biometrics/Phone#t=") &&
              start.GetProperty("expiresIn").GetInt32() is > 590 and <= 600 && start.GetProperty("email").GetString() == "p•••@example.test",
            "QR code opens the phone page on the same public host; token only in the URL fragment; 10-minute expiry");
        check(await Status(desktop) == "waiting", "computer shows the waiting state");
        check(await Status(stranger) == "none", "another browser cannot watch the computer's QR code");

        // ---- The email copy of the link ----
        var before = mail.Messages.Count;
        var emailed = await Post(desktop, csrf, "/Biometrics/Phone/EnrollEmail", new { });
        var message = mail.Messages.Skip(before).LastOrDefault();
        var emailLink = message.Body is null ? null : WebUtility.HtmlDecode(Regex.Match(message.Body, "href=\"([^\"]+)\"").Groups[1].Value);
        check(emailed.StatusCode == HttpStatusCode.OK && message.To == emailA && emailLink?.StartsWith(Site + "/Biometrics/Phone#t=") == true && emailLink != qrLink,
            "\"Send setup link to my email\" delivers a separate single-use link through the configured sender");

        // ---- The phone page ----
        using var phone = Browser(IPhone);
        var phonePage = await phone.GetAsync("/Biometrics/Phone");
        var phoneHtml = await phonePage.Content.ReadAsStringAsync();
        check(phonePage.StatusCode == HttpStatusCode.OK && phonePage.Headers.CacheControl?.NoStore == true &&
              phonePage.Headers.TryGetValues("Referrer-Policy", out var referrer) && referrer.Contains("no-referrer") &&
              phoneHtml.Contains("id=\"phonePage\"") && phoneHtml.Contains("width=device-width"),
            "phone page is mobile, not cached and sends no referrer");
        var phoneCsrf = Csrf(phoneHtml);
        check((await Post(phone, phoneCsrf, "/Biometrics/Phone/Info", new { token = "not-a-real-token" })).StatusCode == HttpStatusCode.Gone,
            "an invented token is refused");
        var token = Token(emailLink!);
        var info = await Json(await Post(phone, phoneCsrf, "/Biometrics/Phone/Info", new { token }));
        check(info.GetProperty("kind").GetString() == "enroll" && info.GetProperty("account").GetString() == "p•••@example.test",
            "phone page shows which account the code registers, with the email masked");
        check(await Status(desktop) == "opened", "computer sees that the phone opened the link");
        check((await Post(phone, phoneCsrf, "/Biometrics/Phone/SignInOptions", new { token, code = "00" })).StatusCode == HttpStatusCode.Gone,
            "an enrollment code cannot be used to approve a sign-in");

        // ---- WebAuthn on the phone: verified by Identity, bound to the link's account ----
        using var phoneKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var phoneId = RandomNumberGenerator.GetBytes(32);
        var options = await Json(await Post(phone, phoneCsrf, "/Biometrics/Phone/EnrollOptions", new { token }));
        check(options.GetProperty("authenticatorSelection").GetProperty("userVerification").GetString() == "required" &&
              options.GetProperty("rp").GetProperty("id").GetString() == Host &&
              options.GetProperty("user").GetProperty("name").GetString() == emailA,
            "registration options demand user verification for the public host and the code's own account");
        var wrongOrigin = Attestation("", options.GetProperty("challenge").GetString()!, phoneId, phoneKey, origin: "https://evil.example", rpId: Host);
        check((await Post(phone, phoneCsrf, "/Biometrics/Phone/EnrollFinish", Merge(wrongOrigin, token))).StatusCode == HttpStatusCode.BadRequest,
            "registration from another origin refused");
        options = await Json(await Post(phone, phoneCsrf, "/Biometrics/Phone/EnrollOptions", new { token }));
        var noUv = Attestation("", options.GetProperty("challenge").GetString()!, phoneId, phoneKey, flags: 0x41, origin: Site, rpId: Host);
        check((await Post(phone, phoneCsrf, "/Biometrics/Phone/EnrollFinish", Merge(noUv, token))).StatusCode == HttpStatusCode.BadRequest,
            "registration without user verification refused");
        options = await Json(await Post(phone, phoneCsrf, "/Biometrics/Phone/EnrollOptions", new { token }));
        var good = Attestation("", options.GetProperty("challenge").GetString()!, phoneId, phoneKey, origin: Site, rpId: Host);
        var finished = await Post(phone, phoneCsrf, "/Biometrics/Phone/EnrollFinish", Merge(good, token));
        check(finished.StatusCode == HttpStatusCode.OK && (await Json(finished)).GetProperty("device").GetString() == "iPhone · Safari",
            "phone registers its passkey; the device is named after the phone automatically");
        using (var scope = services.CreateScope())
        {
            var saved = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().UserPasskeys.AsNoTracking()
                .Where(p => p.UserId == userA).ToListAsync();
            check(saved.Count == 1 && saved[0].CredentialId.SequenceEqual(phoneId) && saved[0].Data.Name == "iPhone · Safari",
                "credential persisted (public key only) against the supplier's Identity user");
        }
        check(await Status(desktop) == "completed", "computer updates by itself once the phone is registered");
        var afterHtml = await (await desktop.GetAsync("/Identity/Account/PendingApproval")).Content.ReadAsStringAsync();
        check(afterHtml.Contains("1 device(s) registered") && afterHtml.Contains("Your phone is linked to your account."),
            "registration page shows the phone as registered");

        // ---- Used, replayed, expired, cancelled and superseded codes ----
        check((await Post(phone, phoneCsrf, "/Biometrics/Phone/EnrollFinish", Merge(good, token))).StatusCode == HttpStatusCode.Gone,
            "a used code cannot register again (replay)");
        using (var secondPhone = Browser(IPhone))
        {
            var otherCsrf = Csrf(await (await secondPhone.GetAsync("/Biometrics/Phone")).Content.ReadAsStringAsync());
            var reused = await Post(secondPhone, otherCsrf, "/Biometrics/Phone/Info", new { token = Token(qrLink) });
            check(reused.StatusCode == HttpStatusCode.Gone && (await reused.Content.ReadAsStringAsync()).Contains("already used"),
                "the QR code of a finished setup is refused on another phone");
        }

        var biometric = services.GetRequiredService<IOptions<BiometricOptions>>().Value;
        var lifetime = biometric.PhoneEnrollSeconds;
        biometric.PhoneEnrollSeconds = 1;
        var shortLived = Token((await Json(await Post(desktop, csrf, "/Biometrics/Phone/EnrollStart", new { password }))).GetProperty("link").GetString()!);
        biometric.PhoneEnrollSeconds = lifetime;
        await Task.Delay(1600);
        var expired = await Post(phone, phoneCsrf, "/Biometrics/Phone/Info", new { token = shortLived });
        check(expired.StatusCode == HttpStatusCode.Gone && (await expired.Content.ReadAsStringAsync()).Contains("expired") && await Status(desktop) == "expired",
            "an expired code is refused and the computer shows it expired");

        var first = Token((await Json(await Post(desktop, csrf, "/Biometrics/Phone/EnrollStart", new { password }))).GetProperty("link").GetString()!);
        var second = Token((await Json(await Post(desktop, csrf, "/Biometrics/Phone/EnrollStart", new { password }))).GetProperty("link").GetString()!);
        check((await Post(phone, phoneCsrf, "/Biometrics/Phone/EnrollOptions", new { token = first })).StatusCode == HttpStatusCode.Gone,
            "a new QR code replaces the previous one");
        await Post(desktop, csrf, "/Biometrics/Phone/Cancel", new { });
        check((await Post(phone, phoneCsrf, "/Biometrics/Phone/EnrollOptions", new { token = second })).StatusCode == HttpStatusCode.Gone,
            "Cancel on the computer ends the code at once");

        // ---- Account rules: a rejected or suspended supplier cannot add a device through an open code ----
        var open = Token((await Json(await Post(desktop, csrf, "/Biometrics/Phone/EnrollStart", new { password }))).GetProperty("link").GetString()!);
        await SetStatus(services, companyA, SupplierStatus.Rejected);
        check((await Post(phone, phoneCsrf, "/Biometrics/Phone/EnrollOptions", new { token = open })).StatusCode == HttpStatusCode.Forbidden,
            "a code opened after the supplier was rejected cannot register a device");
        await SetStatus(services, companyA, SupplierStatus.Pending);

        // ---- Sign-in: a pending supplier has no identity step, so no phone sign-in ----
        using var pendingLogin = Browser(Windows);
        check((await SignInWithPassword(pendingLogin, emailA)).Contains("PendingApproval"), "pending supplier's password goes to Pending Approval");
        var pendingCsrf = Csrf(await (await pendingLogin.GetAsync("/Identity/Account/PendingApproval")).Content.ReadAsStringAsync());
        check((await Post(pendingLogin, pendingCsrf, "/Biometrics/Phone/SignInStart", new { })).StatusCode == HttpStatusCode.Unauthorized,
            "phone sign-in does not bypass administrator approval");
        check((await pendingLogin.GetAsync("/Supplier")).Headers.Location?.ToString().Contains("PendingApproval") == true,
            "registered phone does not open the Supplier workspace before approval");

        await SetStatus(services, companyA, SupplierStatus.Approved);

        // ---- Sign-in on a computer, approved on the phone ----
        // The supplier chose Fingerprint and typed the email (no password): the step for that account opens.
        async Task<(HttpClient Client, string Csrf)> FingerprintStep()
        {
            var client = Browser(Windows);
            var html = await (await client.GetAsync("/Identity/Account/Login")).Content.ReadAsStringAsync();
            var token = Csrf(html);
            var request = new HttpRequestMessage(HttpMethod.Post, "/Identity/Account/Login?handler=IdentityStart")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["Input.Email"] = emailA, ["method"] = "fingerprint", ["__RequestVerificationToken"] = token
                })
            };
            var step = await Json(await client.SendAsync(request));
            check(step.GetProperty("outcome").GetString() == "ready",
                "choosing Fingerprint with an approved supplier's email opens the step with the phone available");
            return (client, token);
        }

        uint counter = 1;
        object Signed(JsonElement opts, ECDsa key, byte[] id, string user, byte flags = 0x05) =>
            SignedAssertion(opts, id, key, user, counter++, Site, Host, flags);

        var (computer, computerCsrf) = await FingerprintStep();
        var signInStart = await Json(await Post(computer, computerCsrf, "/Biometrics/Phone/SignInStart", new { }));
        var code = signInStart.GetProperty("code").GetString()!;
        var signInToken = Token(signInStart.GetProperty("link").GetString()!);
        check(Regex.IsMatch(code, "^[1-9][0-9]$") && signInStart.GetProperty("expiresIn").GetInt32() is > 170 and <= 180,
            "computer shows a QR code and a two-digit number for three minutes");

        using var signInPhone = Browser(IPhone);
        var signInPhoneCsrf = Csrf(await (await signInPhone.GetAsync("/Biometrics/Phone")).Content.ReadAsStringAsync());
        var signInInfo = await Json(await Post(signInPhone, signInPhoneCsrf, "/Biometrics/Phone/Info", new { token = signInToken }));
        check(signInInfo.GetProperty("kind").GetString() == "signIn" && signInInfo.GetProperty("requestedFrom").GetString() == "Windows computer · Chrome",
            "phone shows which computer asked to sign in");
        check(await Status(computer) == "opened", "opening the QR code does not sign the computer in");
        check((await Post(computer, computerCsrf, "/Biometrics/Phone/SignInComplete", new { })).StatusCode == HttpStatusCode.Conflict,
            "the computer cannot finish before the phone approved");
        check((await Post(signInPhone, signInPhoneCsrf, "/Biometrics/Phone/SignInApprove", new { token = signInToken, credentialJson = "{}" })).StatusCode == HttpStatusCode.BadRequest,
            "phone cannot approve before typing the computer's number");
        var loginOptions = await Json(await Post(signInPhone, signInPhoneCsrf, "/Biometrics/Phone/SignInOptions", new { token = signInToken, code }));
        check(loginOptions.GetProperty("userVerification").GetString() == "required" &&
              loginOptions.GetProperty("allowCredentials").EnumerateArray().Select(c => c.GetProperty("id").GetString()).SequenceEqual([Base64Url.Encode(phoneId)]),
            "after the right number the phone is offered only this account's passkey");

        // Another account's passkey (supplier B, also registered on this host) cannot approve A's sign-in.
        using var keyB = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var idB = RandomNumberGenerator.GetBytes(32);
        using (var deviceB = Browser(IPhone))
        {
            check((await SignInWithPassword(deviceB, emailB)).Length > 0, "second supplier signs in with password");
            var bCsrf = Csrf(await (await deviceB.GetAsync("/Identity/Account/Login")).Content.ReadAsStringAsync());
            var bOptions = await Json(await Post(deviceB, bCsrf, "/Biometrics/PasskeyRegisterOptions", new { password }));
            var bRegistered = await Post(deviceB, bCsrf, "/Biometrics/PasskeyRegister", new { credentialJson = CredentialJson(Attestation("", bOptions.GetProperty("challenge").GetString()!, idB, keyB, origin: Site, rpId: Host)) });
            check(bRegistered.StatusCode == HttpStatusCode.OK, "direct smartphone registration works without a QR code or a device name");
            using var scope = services.CreateScope();
            check((await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().UserPasskeys.AsNoTracking().SingleAsync(p => p.UserId == userB)).Data.Name == "iPhone · Safari",
                "direct registration names the device after the phone");
        }
        check((await Post(signInPhone, signInPhoneCsrf, "/Biometrics/Phone/SignInApprove", new { token = signInToken, credentialJson = CredentialJson(Signed(loginOptions, keyB, idB, userB)) })).StatusCode == HttpStatusCode.Unauthorized,
            "another account's passkey cannot approve this sign-in");
        loginOptions = await Json(await Post(signInPhone, signInPhoneCsrf, "/Biometrics/Phone/SignInOptions", new { token = signInToken, code }));
        check((await Post(signInPhone, signInPhoneCsrf, "/Biometrics/Phone/SignInApprove", new { token = signInToken, credentialJson = CredentialJson(Signed(loginOptions, phoneKey, phoneId, userA, flags: 0x01)) })).StatusCode == HttpStatusCode.Unauthorized,
            "approval without user verification refused");
        loginOptions = await Json(await Post(signInPhone, signInPhoneCsrf, "/Biometrics/Phone/SignInOptions", new { token = signInToken, code }));
        var approval = CredentialJson(Signed(loginOptions, phoneKey, phoneId, userA));
        check((await Post(signInPhone, signInPhoneCsrf, "/Biometrics/Phone/SignInApprove", new { token = signInToken, credentialJson = approval })).StatusCode == HttpStatusCode.OK,
            "phone approves with its verified passkey");
        check(await Status(computer) == "approved", "computer sees the approval");

        // Only the computer that made the code, inside its own password step, receives the session.
        check((await Post(stranger, anonymousCsrf, "/Biometrics/Phone/SignInComplete", new { })).StatusCode == HttpStatusCode.Unauthorized &&
              await Status(stranger) == "none", "another browser cannot collect the approved sign-in");
        var (rival, rivalCsrf) = await FingerprintStep();
        check((await Post(rival, rivalCsrf, "/Biometrics/Phone/SignInComplete", new { })).StatusCode == HttpStatusCode.Unauthorized,
            "another browser with its own Fingerprint step for the same account cannot collect it either");
        var completed = await Post(computer, computerCsrf, "/Biometrics/Phone/SignInComplete", new { returnUrl = "/Supplier" });
        check(completed.StatusCode == HttpStatusCode.OK && (await Json(completed)).GetProperty("redirect").GetString() == "/Supplier",
            "the computer that asked is signed in after the phone's approval");
        check((await computer.GetAsync("/Supplier")).StatusCode == HttpStatusCode.OK, "phone-approved session opens the Supplier workspace");
        var again = await Post(computer, computerCsrf, "/Biometrics/Phone/SignInComplete", new { });
        check(!again.IsSuccessStatusCode,
            "an approval signs in once");
        check((await Post(signInPhone, signInPhoneCsrf, "/Biometrics/Phone/SignInApprove", new { token = signInToken, credentialJson = approval })).StatusCode == HttpStatusCode.Gone,
            "a used sign-in code cannot be approved again (replay)");
        using (var scope = services.CreateScope())
        {
            var actions = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().AuditLogs.AsNoTracking()
                .Where(a => a.UserId == userA).Select(a => a.Action + " " + a.NewValues).ToListAsync();
            check(actions.Any(a => a.StartsWith("PhoneSignInApproved")) && actions.Any(a => a.StartsWith("PhoneSignInCompleted")) &&
                  actions.Any(a => a.StartsWith("BiometricEnroll ") && a.Contains("passkey-phone")) &&
                  !actions.Any(a => a.Contains(signInToken) || a.Contains(token)),
                "enrollment and phone sign-in are audited without tokens");
        }

        // ---- "This wasn't me", three wrong numbers, expiry of the password step ----
        var (denied, deniedCsrf) = await FingerprintStep();
        var deniedToken = Token((await Json(await Post(denied, deniedCsrf, "/Biometrics/Phone/SignInStart", new { }))).GetProperty("link").GetString()!);
        await Post(signInPhone, signInPhoneCsrf, "/Biometrics/Phone/Deny", new { token = deniedToken });
        check(await Status(denied) == "denied" && (await Post(denied, deniedCsrf, "/Biometrics/Phone/SignInComplete", new { })).StatusCode == HttpStatusCode.Conflict,
            "a sign-in declined on the phone signs nobody in");

        var (guessed, guessedCsrf) = await FingerprintStep();
        var guessStart = await Json(await Post(guessed, guessedCsrf, "/Biometrics/Phone/SignInStart", new { }));
        var guessToken = Token(guessStart.GetProperty("link").GetString()!);
        var other = guessStart.GetProperty("code").GetString() == "99" ? "98" : "99";
        var firstGuess = await Post(signInPhone, signInPhoneCsrf, "/Biometrics/Phone/SignInOptions", new { token = guessToken, code = other });
        check(firstGuess.StatusCode == HttpStatusCode.BadRequest && (await firstGuess.Content.ReadAsStringAsync()).Contains("does not match"),
            "a wrong number is refused");
        for (var i = 0; i < 2; i++) await Post(signInPhone, signInPhoneCsrf, "/Biometrics/Phone/SignInOptions", new { token = guessToken, code = other });
        check((await Post(signInPhone, signInPhoneCsrf, "/Biometrics/Phone/SignInOptions", new { token = guessToken, code = guessStart.GetProperty("code").GetString() })).StatusCode == HttpStatusCode.Gone &&
              await Status(guessed) == "failed", "three wrong numbers end the code");

        // ---- Suspended supplier: the phone cannot approve and the computer cannot finish ----
        var (suspended, suspendedCsrf) = await FingerprintStep();
        var suspendedStart = await Json(await Post(suspended, suspendedCsrf, "/Biometrics/Phone/SignInStart", new { }));
        var suspendedToken = Token(suspendedStart.GetProperty("link").GetString()!);
        await SetStatus(services, companyA, SupplierStatus.Suspended);
        var suspendedOptions = await Post(signInPhone, signInPhoneCsrf, "/Biometrics/Phone/SignInOptions",
            new { token = suspendedToken, code = suspendedStart.GetProperty("code").GetString() });
        check(suspendedOptions.StatusCode == HttpStatusCode.Unauthorized, "suspended supplier's phone is not offered a sign-in, even with the right number");
        check((await Post(suspended, suspendedCsrf, "/Biometrics/Phone/SignInComplete", new { })).StatusCode is HttpStatusCode.Conflict or HttpStatusCode.Unauthorized,
            "suspended supplier is not signed in");
        await SetStatus(services, companyA, SupplierStatus.Approved);
    }

    static async Task<(string Email, string UserId, int CompanyId)> Supplier(IServiceProvider services, Action<bool, string> check, string name, string password, SupplierStatus status)
    {
        var email = name + "@example.test";
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var company = new Supplier { CompanyName = "Phone check " + name, Email = email, RegistrationNumber = "PHONE-" + name, Status = status };
        db.Suppliers.Add(company);
        await db.SaveChangesAsync();
        var account = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true, IsActive = true, FullName = "Phone supplier " + name, SupplierId = company.Id };
        check((await users.CreateAsync(account, password)).Succeeded && (await users.AddToRoleAsync(account, SystemRoles.Supplier)).Succeeded,
            "verified " + status.ToString().ToLowerInvariant() + " supplier fixture for phone checks created");
        return (email, account.Id, company.Id);
    }

    static async Task SetStatus(IServiceProvider services, int companyId, SupplierStatus status)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Suppliers.Where(s => s.Id == companyId).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, status));
    }

    static async Task<string> Status(HttpClient client) =>
        (await Json(await client.GetAsync("/Biometrics/Phone/Status"))).GetProperty("status").GetString()!;

    static async Task<JsonElement> Json(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    static string Token(string link) => link[(link.IndexOf("#t=", StringComparison.Ordinal) + 3)..];

    // Attestation()/SignedAssertion() return { credentialJson, ... }; the phone endpoints also need the token.
    static object Merge(object body, string token) => new { token, credentialJson = CredentialJson(body) };

    static string CredentialJson(object body) =>
        JsonSerializer.SerializeToElement(body, new JsonSerializerOptions(JsonSerializerDefaults.Web)).GetProperty("credentialJson").GetString()!;
}
