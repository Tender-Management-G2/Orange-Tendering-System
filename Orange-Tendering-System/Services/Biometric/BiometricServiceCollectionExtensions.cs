using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Orange_Tendering_System.Models.Biometric;
using Orange_Tendering_System.Services.Biometric;

// The same namespace as the framework's own AddXxx methods, so Program.cs needs only "builder.Services.AddBiometrics(...)".
namespace Microsoft.Extensions.DependencyInjection;

public static class BiometricServiceCollectionExtensions
{
    /// <summary>Registers Face ID and Fingerprint (passkey) sign-in on top of ASP.NET Core Identity. Called once from Program.cs.</summary>
    public static IServiceCollection AddBiometrics(this IServiceCollection services, IConfiguration configuration)
    {
        // The "Biometric" section of appsettings.json -> BiometricOptions.
        services.Configure<BiometricOptions>(configuration.GetSection("Biometric"));
        services.AddMemoryCache();

        // A fixed application name for the encryption keys. Without it every copy of the project (another folder or
        // port) used different keys and could not read the faces enrolled by another copy.
        // Changing this name would make every stored face unreadable.
        services.AddDataProtection().SetApplicationName("Orange-Tendering-System");

        // Identity's built-in passkey (WebAuthn) settings, used for both registration and sign-in.
        // The settings are read when they are first needed (so test settings are also respected).
        services.AddOptions<IdentityPasskeyOptions>().Configure<IOptions<BiometricOptions>>(ConfigurePasskeys);

        // One object for the whole application (they keep data in memory).
        services.AddSingleton<ChallengeStore>();
        services.AddSingleton<BiometricThrottle>();
        services.AddSingleton<SupplierPreAuth>();
        services.AddSingleton<PhoneLinkStore>();
        // One object per request (they use the database).
        services.AddScoped<PasskeyService>();
        services.AddScoped<FaceService>();
        services.AddScoped<BiometricSignInService>();
        return services;
    }

    private static void ConfigurePasskeys(IdentityPasskeyOptions passkey, IOptions<BiometricOptions> biometricOptions)
    {
        BiometricOptions settings = biometricOptions.Value;

        // The site id (RP ID). null = the host name of the request ("localhost" on every development port).
        passkey.ServerDomain = settings.RpId;
        passkey.AuthenticatorTimeout = TimeSpan.FromSeconds(settings.PasskeyChallengeSeconds);
        // The device must verify the PERSON (fingerprint, face unlock or PIN), not only that someone touched it.
        passkey.UserVerificationRequirement = "required";
        // A discoverable passkey: the device itself offers the account, so no user name has to be typed.
        passkey.ResidentKeyRequirement = "required";
        passkey.AttestationConveyancePreference = "none";
        // Never force "this computer only": the browser must stay free to offer a phone through its own QR code.
        passkey.AuthenticatorAttachment = null;

        // The answer must come from this site: the request's own origin, or the configured list (Biometric:Origins).
        passkey.ValidateOrigin = context =>
        {
            HttpRequest request = context.HttpContext.Request;
            string[] allowedOrigins = settings.Origins ?? new string[] { request.Scheme + "://" + request.Host };
            bool allowed = !context.CrossOrigin && allowedOrigins.Contains(context.Origin, StringComparer.Ordinal);
            return ValueTask.FromResult(allowed);
        };
    }
}
