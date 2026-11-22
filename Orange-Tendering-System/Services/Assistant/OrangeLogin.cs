using Microsoft.AspNetCore.Authentication.Cookies;

namespace Orange_Tendering_System.Services.Assistant;

// A random id stored in the authentication properties of each new sign-in. Cookie renewals and security-stamp refreshes keep the
// same properties, so the id identifies one login session; the AI welcome is shown once per id. It is not a credential.
public static class OrangeLogin
{
    public const string ItemKey = "orange.login";

    public static Task OnSigningIn(CookieSigningInContext context)
    {
        context.Properties.Items.TryAdd(ItemKey, Guid.NewGuid().ToString("N"));
        return Task.CompletedTask;
    }
}
