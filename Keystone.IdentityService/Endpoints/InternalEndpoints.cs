using Keystone.IdentityService.Services;

namespace Keystone.IdentityService.Endpoints;

public static class InternalEndpoints
{
    public static IEndpointRouteBuilder MapInternalEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/internal/introspect", HandleIntrospect).WithTags("Internal");

        return routes;
    }

    /// <summary>
    /// POST /internal/introspect - RFC 7662 token introspection for Keystone access tokens.
    /// Internal APIs do not need this (they validate JWTs offline); it is a debugging aid that
    /// shows what a token contains and whether it is still valid.
    /// </summary>
    private static async Task<IResult> HandleIntrospect(HttpRequest httpRequest, ITokenService tokenService)
    {
        if (!httpRequest.HasFormContentType)
        {
            return Results.BadRequest();
        }

        var form = await httpRequest.ReadFormAsync();
        var claims = await tokenService.ValidateAccessTokenAsync(form["token"].ToString());

        if (claims is null)
        {
            return Results.Ok(new Dictionary<string, object> { ["active"] = false });
        }

        return Results.Ok(new Dictionary<string, object>(claims) { ["active"] = true });
    }
}
