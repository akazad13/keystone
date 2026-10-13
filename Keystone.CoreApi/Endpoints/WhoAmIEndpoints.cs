namespace Keystone.CoreApi.Endpoints;

public static class WhoAmIEndpoints
{
    public static IEndpointRouteBuilder MapWhoAmIEndpoints(this IEndpointRouteBuilder routes)
    {
        // Not "/api/me": the BFF already answers /api/{app}/me itself (from its session),
        // so a Core API endpoint with that name would never be reached.
        routes.MapGet("/api/whoami", (HttpContext httpContext) => Results.Ok(new
        {
            // Every claim of the validated Keystone access token, exactly as the Core API sees it.
            Claims = httpContext.User.Claims.Select(c => new { c.Type, c.Value })
        }))
        .WithTags("WhoAmI")
        .WithName("WhoAmI")
        .RequireAuthorization("User");

        return routes;
    }
}
