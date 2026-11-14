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

        services.AddSingleton<ChallengeStore>();
        services.AddSingleton<BiometricThrottle>();
        services.AddScoped<PasskeyService>();
        services.AddScoped<FaceService>();
        services.AddScoped<BiometricSignInService>();
        return services;
    }
}
