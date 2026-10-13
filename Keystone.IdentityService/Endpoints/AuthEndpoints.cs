using Keystone.IdentityService.Models;
using Keystone.IdentityService.Services;

namespace Keystone.IdentityService.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/connect/token", HandleTokenExchange).WithTags("Auth");

        return routes;
    }

    /// <summary>
    /// POST /connect/token - RFC 8693 token exchange.
    /// The BFF trades the user's identity-provider access token for a short-lived Keystone
    /// access token scoped to one internal API. Only reachable inside the private network.
    /// </summary>
    private static async Task<IResult> HandleTokenExchange(
        HttpRequest httpRequest,
        IUserService userService,
        ITokenService tokenService,
        CancellationToken cancellationToken)
    {
        if (!httpRequest.HasFormContentType)
        {
            return Error("invalid_request", "Expected application/x-www-form-urlencoded.");
        }

        var request = TokenExchangeRequest.FromForm(await httpRequest.ReadFormAsync(cancellationToken));
        if (request is null)
        {
            return Error("unsupported_grant_type", $"Only {TokenExchangeRequest.GrantType} is supported.");
        }

        if (request.SubjectTokenType != TokenExchangeRequest.AccessTokenType || string.IsNullOrEmpty(request.SubjectToken))
        {
            return Error("invalid_request", "subject_token must be an access token.");
        }

        if (!tokenService.IsKnownAudience(request.Audience))
        {
            return Error("invalid_target", $"Unknown audience '{request.Audience}'.");
        }

        var user = await userService.GetUserInfoAsync(request.SubjectToken, cancellationToken);
        if (user is null)
        {
            return Error("invalid_grant", "The identity provider rejected the subject token.");
        }

        return Results.Ok(tokenService.IssueAccessToken(user, request.Audience));
    }

    private static IResult Error(string error, string description) =>
        Results.BadRequest(new OAuthError(error, description));
}
