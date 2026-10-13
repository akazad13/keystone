using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Keystone.Bff.Services;

namespace Keystone.Bff.Endpoints;

/// <summary>
/// "Your sessions": every browser or device where the user is signed in to Keystone, with the
/// option to end one or all of them.
/// <para>
/// These end <b>Keystone</b> sessions only. The user's Zitadel (SSO) session in each browser stays
/// alive, so signing in again there is silent. Ending Zitadel sessions too needs Zitadel's session
/// API and a service account; that is a deliberate next step, not done here.
/// </para>
/// </summary>
public static class SessionEndpoints
{
    public static IEndpointRouteBuilder MapSessionEndpoints(this IEndpointRouteBuilder endpoints, string prefix)
    {
        var group = endpoints.MapGroup($"{prefix}/sessions").RequireAuthorization();

        group.MapGet("/", HandleList);
        group.MapPost("/{id}/revoke", HandleRevoke);
        group.MapPost("/revoke-all", HandleRevokeAll);

        return endpoints;
    }

    /// <summary>GET {prefix}/sessions - the caller's sessions; "current" marks this browser.</summary>
    private static async Task<IResult> HandleList(HttpContext httpContext, RedisTicketStore sessions)
    {
        var (sub, currentSessionId) = await GetCallerAsync(httpContext);
        if (sub is null)
        {
            return Results.Unauthorized();
        }

        var list = await sessions.ListBySubjectAsync(sub);
        return Results.Ok(list.Select(s => new
        {
            s.Id,
            SignedInAt = s.IssuedUtc,
            ExpiresAt = s.ExpiresUtc,
            s.UserAgent,
            Current = s.Id == currentSessionId
        }));
    }

    /// <summary>
    /// POST {prefix}/sessions/{id}/revoke - ends one session. Only the caller's own sessions can be
    /// ended; anyone else's id behaves like an unknown one.
    /// </summary>
    private static async Task<IResult> HandleRevoke(string id, HttpContext httpContext, RedisTicketStore sessions)
    {
        var (sub, currentSessionId) = await GetCallerAsync(httpContext);
        if (sub is null)
        {
            return Results.Unauthorized();
        }

        if (!await sessions.BelongsToAsync(id, sub))
        {
            return Results.NotFound();
        }

        if (id == currentSessionId)
        {
            // Also clears this browser's cookie.
            await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }
        else
        {
            await sessions.RemoveAsync(id);
        }

        return Results.NoContent();
    }

    /// <summary>POST {prefix}/sessions/revoke-all - ends every session of the caller, including this one.</summary>
    private static async Task<IResult> HandleRevokeAll(HttpContext httpContext, RedisTicketStore sessions)
    {
        var (sub, _) = await GetCallerAsync(httpContext);
        if (sub is null)
        {
            return Results.Unauthorized();
        }

        var removed = await sessions.RemoveBySubjectAsync(sub);
        await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        return Results.Ok(new { Removed = removed });
    }

    private static async Task<(string? Sub, string? SessionId)> GetCallerAsync(HttpContext httpContext)
    {
        var auth = await httpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        if (!auth.Succeeded)
        {
            return (null, null);
        }

        auth.Properties.Items.TryGetValue(RedisTicketStore.SessionIdItem, out var sessionId);
        return (auth.Principal.FindFirst("sub")?.Value, sessionId);
    }
}
