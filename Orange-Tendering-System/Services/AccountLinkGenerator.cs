using Microsoft.AspNetCore.Routing;

namespace Orange_Tendering_System.Services;

// Email links need a stable address that the recipient can open, regardless of
// which host or launch profile the administrator used for the current request.
public sealed class AccountLinkGenerator(LinkGenerator links, IConfiguration configuration)
{
    public string GetUriByPage(HttpContext context, string page, object values)
    {
        var configuredBaseUrl = configuration["AccountLinks:PublicBaseUrl"];
        if (!Uri.TryCreate(configuredBaseUrl, UriKind.Absolute, out var publicBase) ||
            (publicBase.Scheme != Uri.UriSchemeHttp && publicBase.Scheme != Uri.UriSchemeHttps) ||
            publicBase.AbsolutePath != "/" || publicBase.Query.Length != 0 ||
            publicBase.Fragment.Length != 0 || publicBase.UserInfo.Length != 0)
            throw new InvalidOperationException("AccountLinks:PublicBaseUrl must be an HTTP(S) site origin.");

        var url = links.GetUriByPage(context, page, values: values,
            scheme: publicBase.Scheme,
            host: publicBase.IsDefaultPort
                ? new HostString(publicBase.Host)
                : new HostString(publicBase.Host, publicBase.Port));

        return url ?? throw new InvalidOperationException("Could not generate account link.");
    }
}
