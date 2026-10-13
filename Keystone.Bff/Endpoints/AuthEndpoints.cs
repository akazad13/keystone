using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Keystone.Bff.Extensions;

namespace Keystone.Bff.Endpoints;

public static class AuthEndpoints
{
    /// <summary>
    /// Maps the auth endpoints under a frontend's API prefix (e.g. /api/keystone),
    /// so each frontend reaches them through the same path it uses for proxied API calls.
    /// </summary>
    /// <param name="prefix">The frontend's API prefix.</param>
    /// <param name="appRoot">Where the frontend lives; the default destination after login and logout.</param>
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints, string prefix, string appRoot)
    {
        var group = endpoints.MapGroup(prefix);

        group.MapGet("/auth/login", (HttpContext httpContext, string? returnUrl, string? prompt) =>
            HandleLogin(httpContext, returnUrl, prompt, appRoot));
        group.MapPost("/auth/logout", () => HandleLogout(appRoot)).DisableAntiforgery();
        // Cast so the IResult is written to the response (a bare Func<HttpContext, Task> would be
        // treated as a RequestDelegate and its result discarded).
        group.MapGet("/me", (Delegate)HandleMe);

        return endpoints;
    }

    /// <summary>
    /// GET {prefix}/auth/login - Starts the OIDC authorization code flow (with PKCE).
    /// The OIDC handler redirects the browser to the identity provider; the provider redirects back to
    /// /signin-oidc, the handler redeems the code, creates the session and finally redirects to returnUrl.
    /// <para>
    /// <c>?prompt=login</c> forces the identity provider to ask for credentials again even when the
    /// user has an SSO session (step-up before sensitive actions). Other prompt values are ignored.
    /// </para>
    /// </summary>
    private static async Task<IResult> HandleLogin(HttpContext httpContext, string? returnUrl, string? prompt, string appRoot)
    {
        // Signing in again while signed in (switching account, re-authenticating): end the current
        // session first. The new login creates a new session, and the old one would otherwise linger
        // in Redis with nobody holding its cookie.
        if (httpContext.User.Identity?.IsAuthenticated == true)
        {
            await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }

        var properties = new OpenIdConnectChallengeProperties
        {
            // Only allow local URLs, otherwise the login endpoint becomes an open redirect.
            RedirectUri = HttpContextExtensions.IsLocalUrl(returnUrl) ? returnUrl : appRoot,
            Prompt = prompt == "login" ? "login" : null
        };

        return Results.Challenge(properties, [OpenIdConnectDefaults.AuthenticationScheme]);
    }

    /// <summary>
    /// POST {prefix}/auth/logout - RP-initiated logout.
    /// Deletes the local session, then redirects to the identity provider's end_session_endpoint with
    /// id_token_hint so the provider ends its SSO session too. The provider then sends back-channel
    /// logout requests to every other application that shared that SSO session.
    /// </summary>
    private static IResult HandleLogout(string appRoot) =>
        Results.SignOut(
            new AuthenticationProperties { RedirectUri = appRoot },
            [CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme]);

    /// <summary>
    /// GET {prefix}/me - The signed-in user, from the session. 401 when there is no session.
    /// Frontends use this to decide whether to show the app or a sign-in button.
    /// </summary>
    private static async Task<IResult> HandleMe(HttpContext httpContext)
    {
        var auth = await httpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        if (!auth.Succeeded)
        {
            return Results.Unauthorized();
        }

        var user = auth.Principal;
        return Results.Ok(new
        {
            Sub = user.FindFirst("sub")?.Value,
            Name = user.FindFirst("name")?.Value ?? user.FindFirst("preferred_username")?.Value,
            Email = user.FindFirst("email")?.Value,
            SessionExpiresAt = auth.Properties.ExpiresUtc,
            Claims = user.Claims.Select(c => new { c.Type, c.Value })
        });
    }
}
