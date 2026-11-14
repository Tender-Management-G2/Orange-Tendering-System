using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Orange_Tendering_System.Models.Biometric;
using Orange_Tendering_System.Services.Biometric;

// Same namespace as the framework's own AddXxx methods, so Program.cs needs a single line and no extra using.
namespace Microsoft.Extensions.DependencyInjection;

public static class BiometricServiceCollectionExtensions
{
    /// <summary>Registers biometric sign-in (fingerprint passkeys and face recognition) on top of ASP.NET Core Identity.</summary>
    public static IServiceCollection AddBiometrics(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<BiometricOptions>(configuration.GetSection("Biometric"));
        services.AddMemoryCache();
        services.AddDataProtection();

        // Identity's built-in passkey (WebAuthn) settings, shared by registration and sign-in so both ceremonies use
        // the same site id (RP ID) and origin rule.
        services.AddOptions<IdentityPasskeyOptions>().Configure<IOptions<BiometricOptions>>((passkey, biometric) =>
        {
            var settings = biometric.Value;
            // Site id. null = the host name of the request: "localhost" on every development port, so a passkey
            // registered on https://localhost:7182 also works on https://localhost:7190.
            passkey.ServerDomain = settings.RpId;
            passkey.AuthenticatorTimeout = TimeSpan.FromSeconds(settings.PasskeyChallengeSeconds);
            // The authenticator must verify the person (fingerprint, face unlock or PIN), not just their presence.
            passkey.UserVerificationRequirement = "required";
            // A discoverable passkey: sign-in needs no email, the authenticator offers the account itself.
            passkey.ResidentKeyRequirement = "required";
            passkey.AttestationConveyancePreference = "none";
            // Never force "platform" (this computer's own sensor / Windows Hello): the browser must stay free to
            // offer a phone through its QR code, which is how a laptop without a fingerprint reader signs in.
            passkey.AuthenticatorAttachment = null;
            // The response must come from this site: the request's own origin, or the configured list.
            passkey.ValidateOrigin = context =>
            {
                var request = context.HttpContext.Request;
                var allowed = settings.Origins ?? [$"{request.Scheme}://{request.Host}"];
                return ValueTask.FromResult(!context.CrossOrigin && allowed.Contains(context.Origin, StringComparer.Ordinal));
            };
        });

        services.AddSingleton<ChallengeStore>();
        services.AddSingleton<BiometricThrottle>();
        services.AddScoped<PasskeyService>();
        services.AddScoped<FaceService>();
        services.AddScoped<BiometricSignInService>();
        return services;
    }
}
