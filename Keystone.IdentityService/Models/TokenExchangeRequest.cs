namespace Keystone.IdentityService.Models;

/// <summary>
/// RFC 8693 token exchange request, posted as application/x-www-form-urlencoded to /connect/token.
/// </summary>
public sealed class TokenExchangeRequest
{
    public const string GrantType = "urn:ietf:params:oauth:grant-type:token-exchange";
    public const string AccessTokenType = "urn:ietf:params:oauth:token-type:access_token";

    /// <summary>The access token the caller received from the external identity provider.</summary>
    public required string SubjectToken { get; init; }

    /// <summary>Must be <see cref="AccessTokenType"/>.</summary>
    public required string SubjectTokenType { get; init; }

    /// <summary>The internal API the new token is for, e.g. "core-api".</summary>
    public required string Audience { get; init; }

    public static TokenExchangeRequest? FromForm(IFormCollection form)
    {
        if (form["grant_type"] != GrantType)
        {
            return null;
        }

        return new TokenExchangeRequest
        {
            SubjectToken = form["subject_token"].ToString(),
            SubjectTokenType = form["subject_token_type"].ToString(),
            Audience = form["audience"].ToString()
        };
    }
}

/// <summary>
/// RFC 7662 introspection request, posted as application/x-www-form-urlencoded.
/// </summary>
public sealed class IntrospectionRequest
{
    public required string Token { get; init; }
}
