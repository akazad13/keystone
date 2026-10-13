using System.Globalization;
using System.Net;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Keystone.Bff.Services;

/// <summary>The identity provider's access token for a session, and when it expires.</summary>
public sealed record IdentityProviderAccessToken(string AccessToken, DateTimeOffset ExpiresAt);

/// <summary>
/// Keeps a session's identity-provider access token usable: when it is about to expire, redeems the
/// refresh token at the provider's token endpoint (OAuth 2.0 refresh_token grant) and stores the new
/// tokens back into the session.
/// <para>
/// Zitadel rotates refresh tokens: every refresh returns a new refresh token and the old one stops
/// working. Two requests refreshing at the same moment would therefore make the second one fail, so
/// refreshing is serialized per session with a short Redis lock.
/// </para>
/// </summary>
public sealed class TokenRefresher
{
    private static readonly TimeSpan RenewBeforeExpiry = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan LockRetryDelay = TimeSpan.FromMilliseconds(200);

    private readonly IOptionsMonitor<OpenIdConnectOptions> _oidcOptions;
    private readonly RedisTicketStore _sessions;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<TokenRefresher> _logger;

    public TokenRefresher(
        IOptionsMonitor<OpenIdConnectOptions> oidcOptions,
        RedisTicketStore sessions,
        IConnectionMultiplexer redis,
        ILogger<TokenRefresher> logger)
    {
        _oidcOptions = oidcOptions;
        _sessions = sessions;
        _redis = redis;
        _logger = logger;
    }

    /// <summary>
    /// Returns a usable access token for the session, refreshing it first when needed.
    /// Returns null when the session can no longer get one (no refresh token, refresh rejected, session gone).
    /// </summary>
    public async Task<IdentityProviderAccessToken?> GetAccessTokenAsync(
        string sessionId, AuthenticationProperties properties, CancellationToken cancellationToken)
    {
        var current = Read(properties);
        if (current is not null && !NeedsRefresh(current))
        {
            return current;
        }

        var db = _redis.GetDatabase();
        var lockKey = RedisTicketStore.SessionDataKey(sessionId, "refresh-lock");
        var lockOwner = Guid.NewGuid().ToString("N");
        var giveUpAt = DateTimeOffset.UtcNow + LockTimeout;

        while (!await db.LockTakeAsync(lockKey, lockOwner, LockTimeout))
        {
            if (DateTimeOffset.UtcNow > giveUpAt)
            {
                _logger.LogWarning("Timed out waiting for another refresh of session {SessionId}", sessionId);
                return null;
            }

            await Task.Delay(LockRetryDelay, cancellationToken);
        }

        try
        {
            // Start from the stored session, not the copy loaded with this request: another request
            // may have refreshed while we waited for the lock, or the session may have ended.
            var ticket = await _sessions.RetrieveAsync(sessionId);
            if (ticket is null)
            {
                return null;
            }

            current = Read(ticket.Properties);
            if (current is not null && !NeedsRefresh(current))
            {
                return current;
            }

            var refreshToken = ticket.Properties.GetTokenValue("refresh_token");
            if (string.IsNullOrEmpty(refreshToken))
            {
                _logger.LogInformation("Session {SessionId} has no refresh token; the user must sign in again", sessionId);
                return null;
            }

            var (outcome, response) = await RedeemAsync(refreshToken, cancellationToken);
            if (outcome == RefreshOutcome.Rejected)
            {
                // The provider no longer honours this session's refresh token (expired, revoked, or the
                // SSO session ended). The session is over: delete it so the user gets a clean 401.
                await _sessions.RemoveAsync(sessionId);
                _logger.LogInformation("Refresh token of session {SessionId} was rejected; session removed", sessionId);
                return null;
            }

            if (outcome == RefreshOutcome.Failed)
            {
                return null; // provider unreachable or erroring; keep the session and try again next time
            }

            var expiresAt = DateTimeOffset.UtcNow.AddSeconds(response!.ExpiresIn);
            ticket.Properties.UpdateTokenValue("access_token", response.AccessToken);
            ticket.Properties.UpdateTokenValue("expires_at", expiresAt.ToString("o", CultureInfo.InvariantCulture));
            if (!string.IsNullOrEmpty(response.RefreshToken))
            {
                ticket.Properties.UpdateTokenValue("refresh_token", response.RefreshToken);
            }
            if (!string.IsNullOrEmpty(response.IdToken))
            {
                ticket.Properties.UpdateTokenValue("id_token", response.IdToken);
            }

            // Update the session in place. HttpContext.SignInAsync would create a new session id and
            // break the other requests of this user that are still using the current cookie.
            await _sessions.RenewAsync(sessionId, ticket);

            _logger.LogInformation("Refreshed identity-provider tokens of session {SessionId}; new access token expires {ExpiresAt}",
                sessionId, expiresAt);
            return new IdentityProviderAccessToken(response.AccessToken, expiresAt);
        }
        finally
        {
            await db.LockReleaseAsync(lockKey, lockOwner);
        }
    }

    private async Task<(RefreshOutcome Outcome, RefreshResponse? Response)> RedeemAsync(
        string refreshToken, CancellationToken cancellationToken)
    {
        var options = _oidcOptions.Get(OpenIdConnectDefaults.AuthenticationScheme);
        var metadata = await options.ConfigurationManager!.GetConfigurationAsync(cancellationToken);

        using var response = await options.Backchannel.PostAsync(metadata.TokenEndpoint, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = options.ClientId!,
            ["client_secret"] = options.ClientSecret!
        }), cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return (RefreshOutcome.Refreshed, await response.Content.ReadFromJsonAsync<RefreshResponse>(cancellationToken));
        }

        // RFC 6749 section 5.2: invalid_grant and friends come back as 400 (or 401 for client errors).
        var rejected = response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized;
        _logger.LogWarning("Refresh token request failed: {StatusCode} {Body}",
            response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));

        return (rejected ? RefreshOutcome.Rejected : RefreshOutcome.Failed, null);
    }

    private static IdentityProviderAccessToken? Read(AuthenticationProperties properties)
    {
        var accessToken = properties.GetTokenValue("access_token");
        var expiresAt = properties.GetTokenValue("expires_at");

        if (accessToken is null
            || !DateTimeOffset.TryParse(expiresAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var expires))
        {
            return null;
        }

        return new IdentityProviderAccessToken(accessToken, expires);
    }

    private static bool NeedsRefresh(IdentityProviderAccessToken token) =>
        token.ExpiresAt <= DateTimeOffset.UtcNow.Add(RenewBeforeExpiry);

    private enum RefreshOutcome { Refreshed, Rejected, Failed }

    private sealed record RefreshResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("id_token")] string? IdToken);
}
