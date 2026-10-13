using System.Globalization;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using StackExchange.Redis;

namespace Keystone.Bff.Services;

/// <summary>
/// Turns the session's identity-provider access token into a Keystone access token for the Core API
/// (RFC 8693 token exchange against the Identity Service), and caches the result per session.
/// <para>
/// The Zitadel tokens never leave the BFF, and the Core API never sees a Zitadel token: it only
/// trusts tokens signed by the Identity Service.
/// </para>
/// </summary>
public sealed class SessionTokenService : ISessionTokenService
{
    public const string HttpClientName = "identity-service";

    public const string CoreApiTokenName = "core-api-token";
    private const string Audience = "core-api";
    private static readonly TimeSpan RenewBeforeExpiry = TimeSpan.FromMinutes(1);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConnectionMultiplexer _redis;
    private readonly IDataProtector _protector;
    private readonly ILogger<SessionTokenService> _logger;

    public SessionTokenService(
        IHttpClientFactory httpClientFactory,
        IConnectionMultiplexer redis,
        IDataProtectionProvider dataProtection,
        ILogger<SessionTokenService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _redis = redis;
        _protector = dataProtection.CreateProtector("Keystone.Bff.CoreApiTokens");
        _logger = logger;
    }

    public async Task<string?> GetAccessTokenAsync(HttpContext httpContext, CancellationToken cancellationToken)
    {
        var auth = await httpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        if (!auth.Succeeded || !auth.Properties.Items.TryGetValue(RedisTicketStore.SessionIdItem, out var sessionId) || sessionId is null)
        {
            return null;
        }

        var db = _redis.GetDatabase();
        var cacheKey = RedisTicketStore.SessionDataKey(sessionId, CoreApiTokenName);

        var cached = (byte[]?)await db.StringGetAsync(cacheKey);
        if (cached is not null)
        {
            return _protector.Unprotect(System.Text.Encoding.UTF8.GetString(cached));
        }

        // The identity provider's access token, saved in the session at login (SaveTokens = true).
        var subjectToken = auth.Properties.GetTokenValue("access_token");
        if (subjectToken is null || IsExpired(auth.Properties.GetTokenValue("expires_at")))
        {
            // Refreshing the identity provider's token with the refresh_token is a build-along exercise.
            _logger.LogInformation("Session {SessionId} has no usable identity-provider access token", sessionId);
            return null;
        }

        var exchanged = await ExchangeAsync(subjectToken, cancellationToken);
        if (exchanged is null)
        {
            return null;
        }

        var cacheFor = TimeSpan.FromSeconds(exchanged.ExpiresIn) - RenewBeforeExpiry;
        if (cacheFor > TimeSpan.Zero)
        {
            await db.StringSetAsync(cacheKey, System.Text.Encoding.UTF8.GetBytes(_protector.Protect(exchanged.AccessToken)), cacheFor);
        }

        return exchanged.AccessToken;
    }

    private async Task<TokenExchangeResponse?> ExchangeAsync(string subjectToken, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);

        using var response = await client.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "urn:ietf:params:oauth:grant-type:token-exchange",
            ["subject_token"] = subjectToken,
            ["subject_token_type"] = "urn:ietf:params:oauth:token-type:access_token",
            ["audience"] = Audience
        }), cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Token exchange failed: {StatusCode} {Body}",
                response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
            return null;
        }

        return await response.Content.ReadFromJsonAsync<TokenExchangeResponse>(cancellationToken);
    }

    private static bool IsExpired(string? expiresAt) =>
        expiresAt is not null
        && DateTimeOffset.TryParse(expiresAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var expires)
        && expires <= DateTimeOffset.UtcNow.Add(RenewBeforeExpiry);

    private sealed record TokenExchangeResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}
