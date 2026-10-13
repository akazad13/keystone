using System.Globalization;
using System.Security.Claims;
using Yarp.ReverseProxy.Model;

namespace Keystone.Bff.Middleware;

/// <summary>
/// Step-up authentication for destructive API calls.
/// <para>
/// A session can be hours old, and anyone at an unlocked computer can use it. For DELETE requests the
/// BFF therefore also requires that the user actually entered their credentials recently, judged by
/// the <c>auth_time</c> claim of the ID token. When that is too old the request is refused with the
/// challenge defined by RFC 9470 (OAuth 2.0 Step-Up Authentication Challenge Protocol):
/// <c>401</c> with <c>WWW-Authenticate: Bearer error="insufficient_user_authentication", max_age=...</c>.
/// The SPA then sends the user through login with <c>prompt=login</c>, which makes the identity
/// provider ask for the password even when an SSO session exists, and a fresh <c>auth_time</c> comes back.
/// </para>
/// </summary>
public static class RecentAuthentication
{
    public static IReverseProxyApplicationBuilder RequireRecentAuthenticationForDeletes(
        this IReverseProxyApplicationBuilder proxy, TimeSpan maxAge)
    {
        proxy.Use(async (context, next) =>
        {
            var isApiCall = context.GetReverseProxyFeature().Route.Config.ClusterId == "core-api";

            if (isApiCall && HttpMethods.IsDelete(context.Request.Method) && !AuthenticatedWithin(context.User, maxAge))
            {
                var seconds = (int)maxAge.TotalSeconds;
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.Headers.WWWAuthenticate =
                    $"Bearer error=\"insufficient_user_authentication\", error_description=\"Sign in again to continue\", max_age={seconds}";
                await context.Response.WriteAsJsonAsync(new { error = "insufficient_user_authentication", maxAge = seconds });
                return;
            }

            await next();
        });

        return proxy;
    }

    private static bool AuthenticatedWithin(ClaimsPrincipal user, TimeSpan maxAge)
    {
        // auth_time: when the user last authenticated at the identity provider, in seconds since 1970.
        var authTime = user.FindFirst("auth_time")?.Value;
        if (!long.TryParse(authTime, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
        {
            return false;
        }

        return DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(seconds) <= maxAge;
    }
}
