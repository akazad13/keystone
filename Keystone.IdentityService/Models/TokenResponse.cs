using System.Text.Json.Serialization;

namespace Keystone.IdentityService.Models;

/// <summary>
/// RFC 8693 token exchange response.
/// </summary>
public sealed class TokenResponse
{
    [JsonPropertyName("access_token")]
    public required string AccessToken { get; init; }

    [JsonPropertyName("issued_token_type")]
    public string IssuedTokenType { get; init; } = TokenExchangeRequest.AccessTokenType;

    [JsonPropertyName("token_type")]
    public string TokenType { get; init; } = "Bearer";

    [JsonPropertyName("expires_in")]
    public required int ExpiresIn { get; init; }
}

/// <summary>
/// OAuth 2.0 error response (RFC 6749 section 5.2).
/// </summary>
public sealed record OAuthError(
    [property: JsonPropertyName("error")] string Error,
    [property: JsonPropertyName("error_description")] string Description);
