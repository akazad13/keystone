using Keystone.Bff.Services;

namespace Keystone.Bff.Extensions;

public static class HttpContextExtensions
{
    /// <summary>
    /// Returns the Keystone access token for the current session.
    /// Used by the YARP request transform to attach the JWT to proxied requests.
    /// </summary>
    public static Task<string?> GetTokenFromSessionAsync(this HttpContext httpContext) =>
        httpContext.RequestServices
            .GetRequiredService<ISessionTokenService>()
            .GetAccessTokenAsync(httpContext, httpContext.RequestAborted);

    /// <summary>
    /// True when <paramref name="url"/> stays on this site, i.e. is safe to redirect to after login.
    /// Rejects absolute URLs and protocol-relative ones such as "//evil.example".
    /// </summary>
    public static bool IsLocalUrl(string? url) =>
        !string.IsNullOrEmpty(url)
        && url[0] == '/'
        && (url.Length == 1 || (url[1] != '/' && url[1] != '\\'));
}
