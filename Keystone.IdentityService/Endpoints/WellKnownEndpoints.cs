using Keystone.IdentityService.Models;
using Keystone.IdentityService.Services;

namespace Keystone.IdentityService.Endpoints;

public static class WellKnownEndpoints
{
    public static IEndpointRouteBuilder MapWellKnownEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/.well-known/jwks.json", HandleJwks).WithTags("Discovery");
        routes.MapGet("/.well-known/openid-configuration", HandleDiscovery).WithTags("Discovery");

        return routes;
    }

    private static IResult HandleJwks(IKeyManager keyManager)
    {
        var keys = keyManager.GetJsonWebKeySet().Keys.Select(k => new
        {
            kty = k.Kty,
            use = k.Use,
            kid = k.Kid,
            alg = k.Alg,
            n = k.N,
            e = k.E
        });

        return Results.Json(new { keys });
    }

    /// <summary>
    /// Discovery document for internal consumers. The Core API's JWT handler reads it to find the
    /// issuer name and the signing keys. Endpoint URLs are built from the incoming request, so they
    /// are reachable from wherever the caller is (localhost in development, the internal
    /// Container Apps hostname in Azure).
    /// </summary>
    private static IResult HandleDiscovery(HttpRequest request, IConfiguration config)
    {
        var baseUrl = $"{request.Scheme}://{request.Host}{request.PathBase}";

        return Results.Json(new
        {
            issuer = config["Jwt:Issuer"],
            jwks_uri = $"{baseUrl}/.well-known/jwks.json",
            token_endpoint = $"{baseUrl}/connect/token",
            introspection_endpoint = $"{baseUrl}/internal/introspect",
            grant_types_supported = new[] { TokenExchangeRequest.GrantType },
            token_endpoint_auth_methods_supported = new[] { "none" }
        });
    }
}
