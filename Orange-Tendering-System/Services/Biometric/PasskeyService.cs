using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Orange_Tendering_System.Models.Biometric;
using Orange_Tendering_System.Models.Entities;

namespace Orange_Tendering_System.Services.Biometric;

/// <summary>
/// Fingerprint / passkey (WebAuthn), built on the passkey support that ASP.NET Core Identity has since .NET 10.
///
/// Identity does the security work: it creates the options the browser needs, keeps the one-time challenge in a
/// short-lived protected cookie, verifies the device's signed answer (challenge, origin, site id, user verification,
/// signature, sign counter) and stores the credential in the AspNetUserPasskeys table. This class only adds the project's
/// own rules around those calls. The settings are in BiometricServiceCollectionExtensions.cs.
///
/// The private key and the fingerprint never leave the device (a phone, this computer's sensor or a security key);
/// the server keeps only the public key. The device decides how it checks the person, so the site cannot tell
/// whether a fingerprint, face unlock or the device PIN was used.
/// </summary>
public class PasskeyService
{
    private const string Expired = "The request expired. Try again.";

    private readonly UserManager<ApplicationUser> users;
    private readonly SignInManager<ApplicationUser> signInManager;
    private readonly BiometricOptions options;

    public PasskeyService(UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signInManager, IOptions<BiometricOptions> options)
    {
        this.users = users;
        this.signInManager = signInManager;
        this.options = options.Value;
    }

    /// <summary>Registration step 1: the options (JSON) for navigator.credentials.create() in the browser.</summary>
    public async Task<string> CreateRegistrationOptionsAsync(ApplicationUser user)
    {
        string name = user.Email ?? user.UserName ?? user.Id;
        string displayName = name;
        if (!string.IsNullOrWhiteSpace(user.FullName))
        {
            displayName = user.FullName;
        }

        PasskeyUserEntity passkeyUser = new PasskeyUserEntity
        {
            Id = user.Id,
            Name = name,
            DisplayName = displayName
        };
        return await signInManager.MakePasskeyCreationOptionsAsync(passkeyUser);
    }

    /// <summary>Registration step 2: verifies the browser's answer and saves the new passkey on the account.</summary>
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
            // No registration was started in this browser, or it was already answered, or it expired.
            return BiometricResult.Fail(Expired);
        }

        if (!attestation.Succeeded)
        {
            return BiometricResult.Fail(attestation.Failure.Message);
        }

        // The options must have been made for this same account.
        if (attestation.UserEntity.Id != user.Id)
        {
            return BiometricResult.Fail(Expired);
        }

        IList<UserPasskeyInfo> existing = await users.GetPasskeysAsync(user);
        if (existing.Count >= options.MaxPasskeysPerUser)
        {
            return BiometricResult.Fail("You reached the maximum number of registered devices.");
        }

        string name = "My device";
        if (!string.IsNullOrWhiteSpace(label))
        {
            name = label.Trim();
        }
        if (name.Length > 150)
        {
            name = name.Substring(0, 150);
        }
        attestation.Passkey.Name = name;

        IdentityResult saved = await users.AddOrUpdatePasskeyAsync(user, attestation.Passkey);
        if (!saved.Succeeded)
        {
            return BiometricResult.Fail("The passkey could not be saved. Try again.");
        }
        return BiometricResult.Ok(user);
    }

    /// <summary>
    /// Sign-in step 1: the options (JSON) for navigator.credentials.get() in the browser.
    /// Without a user the device offers any passkey it holds for this site. With a user (the identity step after the
    /// email) only that account's passkeys are accepted.
    /// </summary>
    public async Task<string> CreateLoginOptionsAsync(ApplicationUser? user)
    {
        return await signInManager.MakePasskeyRequestOptionsAsync(user);
    }

    /// <summary>
    /// Sign-in step 2: verifies the signed answer. On success the result carries the account the passkey belongs to.
    /// Nobody is signed in here: BiometricSignInService checks the account rules and signs in.
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
            // No sign-in was started in this browser, or it was already answered, or it expired.
            return BiometricResult.Fail(Expired);
        }

        if (!assertion.Succeeded)
        {
            return BiometricResult.Fail(assertion.Failure.Message);
        }

        // Save the new sign counter (Identity refuses a counter that goes backwards: a sign of a cloned device).
        IdentityResult saved = await users.AddOrUpdatePasskeyAsync(assertion.User, assertion.Passkey);
        if (!saved.Succeeded)
        {
            return BiometricResult.Fail("The passkey could not be updated.");
        }
        return BiometricResult.Ok(assertion.User);
    }

    public async Task<IList<UserPasskeyInfo>> ListAsync(ApplicationUser user)
    {
        return await users.GetPasskeysAsync(user);
    }

    /// <summary>Removes one passkey (credential id as base64url text). False when it does not belong to this account.</summary>
    public async Task<bool> RemoveAsync(ApplicationUser user, string? credentialId)
    {
        byte[] id;
        if (!Base64Url.TryDecode(credentialId, out id))
        {
            return false;
        }

        UserPasskeyInfo? passkey = await users.GetPasskeyAsync(user, id);
        if (passkey == null)
        {
            return false;
        }

        IdentityResult removed = await users.RemovePasskeyAsync(user, id);
        return removed.Succeeded;
    }
}
