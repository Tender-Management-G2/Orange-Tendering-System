using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Orange_Tendering_System.Models.Biometric;
using Orange_Tendering_System.Models.Entities;

namespace Orange_Tendering_System.Services.Biometric;

/// <summary>
/// Fingerprint / passkey sign-in (WebAuthn) on top of ASP.NET Core Identity's built-in passkey support (.NET 10).
/// Identity does the real work: it builds the options the browser needs, keeps the one-time challenge in a short-lived
/// protected cookie, verifies the authenticator's signed answer (challenge, origin, site id, user verification,
/// signature, sign counter) and stores the credential in its AspNetUserPasskeys table. This class only adds the
/// project's own rules around those calls. Settings: AddBiometrics (IdentityPasskeyOptions).
///
/// The private key and the fingerprint never leave the authenticator (a phone, this computer's sensor or a security
/// key); the server keeps the public key. The authenticator decides how it verifies the person, so the site cannot
/// tell whether a fingerprint, face unlock or the device PIN was used.
/// </summary>
public class PasskeyService(
    UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signInManager,
    IOptions<BiometricOptions> options)
{
    private const string Expired = "The request expired. Try again.";

    /// <summary>Options (WebAuthn JSON) for navigator.credentials.create(): a new passkey for this account.</summary>
    public Task<string> CreateRegistrationOptionsAsync(ApplicationUser user)
    {
        var name = user.Email ?? user.UserName ?? user.Id;
        return signInManager.MakePasskeyCreationOptionsAsync(new PasskeyUserEntity
        {
            Id = user.Id,
            Name = name,
            DisplayName = string.IsNullOrWhiteSpace(user.FullName) ? name : user.FullName
        });
    }

    /// <summary>Verifies the browser's answer to the registration options and links the new passkey to the account.</summary>
    public async Task<BiometricResult> RegisterAsync(ApplicationUser user, string? credentialJson, string? label)
    {
        if (string.IsNullOrWhiteSpace(credentialJson))
        {
            return BiometricResult.Fail("The device did not return a credential.");
        }

        PasskeyAttestationResult attestation;
        try
        {
            attestation = await signInManager.PerformPasskeyAttestationAsync(credentialJson);
        }
        catch (InvalidOperationException)
        {
            // No registration was started in this browser, or it was already answered or has expired.
            return BiometricResult.Fail(Expired);
        }

        if (!attestation.Succeeded)
        {
            return BiometricResult.Fail(attestation.Failure.Message);
        }

        // The options must have been issued to this same account.
        if (attestation.UserEntity.Id != user.Id)
        {
            return BiometricResult.Fail(Expired);
        }

        if ((await users.GetPasskeysAsync(user)).Count >= options.Value.MaxPasskeysPerUser)
        {
            return BiometricResult.Fail("You reached the maximum number of registered devices.");
        }

        var name = string.IsNullOrWhiteSpace(label) ? "My device" : label.Trim();
        attestation.Passkey.Name = name.Length > 150 ? name[..150] : name;
        var saved = await users.AddOrUpdatePasskeyAsync(user, attestation.Passkey);
        return saved.Succeeded ? BiometricResult.Ok(user) : BiometricResult.Fail("The passkey could not be saved. Try again.");
    }

    /// <summary>
    /// Options (WebAuthn JSON) for navigator.credentials.get(). Without a user no account name is asked: the
    /// authenticator offers the passkeys it holds for this site, so the login page never reveals whether an account
    /// exists. With a user (the /fingerprint-demo page) only that account's passkeys are accepted.
    /// </summary>
    public Task<string> CreateLoginOptionsAsync(ApplicationUser? user = null) =>
        signInManager.MakePasskeyRequestOptionsAsync(user);

    /// <summary>
    /// Verifies the signed answer to the login options. On success the result carries the account the passkey belongs
    /// to; nobody is signed in here (BiometricSignInService applies the account rules and signs in).
    /// </summary>
    public async Task<BiometricResult> VerifyLoginAsync(string? credentialJson)
    {
        if (string.IsNullOrWhiteSpace(credentialJson))
        {
            return BiometricResult.Fail("The device did not return a credential.");
        }

        PasskeyAssertionResult<ApplicationUser> assertion;
        try
        {
            assertion = await signInManager.PerformPasskeyAssertionAsync(credentialJson);
        }
        catch (InvalidOperationException)
        {
            // No sign-in was started in this browser, or it was already answered or has expired.
            return BiometricResult.Fail(Expired);
        }

        if (!assertion.Succeeded)
        {
            return BiometricResult.Fail(assertion.Failure.Message);
        }

        // Keep the stored sign counter and backup state up to date (a counter that goes backwards is refused).
        var saved = await users.AddOrUpdatePasskeyAsync(assertion.User, assertion.Passkey);
        return saved.Succeeded ? BiometricResult.Ok(assertion.User) : BiometricResult.Fail("The passkey could not be updated.");
    }

    public Task<IList<UserPasskeyInfo>> ListAsync(ApplicationUser user) => users.GetPasskeysAsync(user);

    /// <summary>Removes one passkey of the account (credential id as base64url text); false when it is not theirs.</summary>
    public async Task<bool> RemoveAsync(ApplicationUser user, string? credentialId)
    {
        if (!Base64Url.TryDecode(credentialId, out var id) || await users.GetPasskeyAsync(user, id) is null)
        {
            return false;
        }

        return (await users.RemovePasskeyAsync(user, id)).Succeeded;
    }
}
