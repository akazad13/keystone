using Keystone.Bff.Services;

namespace Keystone.Bff.Endpoints;

public static class BackChannelLogoutEndpoints
{
    public const string Path = "/bff/backchannel-logout";

    public static IEndpointRouteBuilder MapBackChannelLogout(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(Path, HandleBackChannelLogout)
            .AllowAnonymous()
            .DisableAntiforgery();

        return endpoints;
    }

    /// <summary>
    /// POST /bff/backchannel-logout - OpenID Connect Back-Channel Logout 1.0.
    /// <para>
    /// The identity provider calls this server-to-server (no browser involved) when an SSO session ends:
    /// the user signed out of another application, an admin terminated the session, and so on.
    /// The body is a form with one field, <c>logout_token</c>: a signed JWT naming the session (<c>sid</c>)
    /// and/or user (<c>sub</c>). We delete the matching sessions from Redis, so the user's next request
    /// to the BFF has no session and gets a 401.
    /// </para>
    /// </summary>
    private static async Task<IResult> HandleBackChannelLogout(
        HttpContext httpContext,
        LogoutTokenValidator validator,
        RedisTicketStore sessions,
        ILogger<LogoutTokenValidator> logger)
    {
        // The spec requires responses to not be cached.
        httpContext.Response.Headers.CacheControl = "no-cache, no-store";
        httpContext.Response.Headers.Pragma = "no-cache";

        if (!httpContext.Request.HasFormContentType)
        {
            return Results.BadRequest();
        }

        var form = await httpContext.Request.ReadFormAsync(httpContext.RequestAborted);
        var logoutToken = await validator.ValidateAsync(form["logout_token"].ToString(), httpContext.RequestAborted);
        if (logoutToken is null)
        {
            return Results.BadRequest();
        }

        // Prefer the narrower sid (one SSO session); fall back to sub (every session of the user).
        var removed = logoutToken.SessionId is not null
            ? await sessions.RemoveByIdentityProviderSessionAsync(logoutToken.SessionId)
            : await sessions.RemoveBySubjectAsync(logoutToken.Subject!);

        logger.LogInformation("Back-channel logout for sid {Sid} / sub {Sub} removed {Count} session(s)",
            logoutToken.SessionId, logoutToken.Subject, removed);

        return Results.Ok();
    }
}
