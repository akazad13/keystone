using System.Text.Json;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using StackExchange.Redis;

namespace Keystone.Bff.Services;

/// <summary>The parts of a validated logout token the BFF acts on.</summary>
public sealed record LogoutToken(string? Subject, string? SessionId);

/// <summary>
/// Validates an OIDC back-channel logout token, following section 2.6 of
/// OpenID Connect Back-Channel Logout 1.0.
/// </summary>
public sealed class LogoutTokenValidator
{
    private const string BackChannelLogoutEvent = "http://schemas.openid.net/event/backchannel-logout";
    private const string ReplayKeyPrefix = "keystone:bff:logout-jti:";

    private readonly IOptionsMonitor<OpenIdConnectOptions> _oidcOptions;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<LogoutTokenValidator> _logger;
    private readonly JsonWebTokenHandler _handler = new();

    public LogoutTokenValidator(
        IOptionsMonitor<OpenIdConnectOptions> oidcOptions,
        IConnectionMultiplexer redis,
        ILogger<LogoutTokenValidator> logger)
    {
        _oidcOptions = oidcOptions;
        _redis = redis;
        _logger = logger;
    }

    public async Task<LogoutToken?> ValidateAsync(string logoutToken, CancellationToken cancellationToken)
    {
        // Step 1-3: it is a JWT signed by our identity provider, issued for our client, not expired.
        // The issuer and signing keys come from the same discovery document the login flow uses.
        var options = _oidcOptions.Get(OpenIdConnectDefaults.AuthenticationScheme);
        var metadata = await options.ConfigurationManager!.GetConfigurationAsync(cancellationToken);

        var result = await _handler.ValidateTokenAsync(logoutToken, new TokenValidationParameters
        {
            ValidIssuer = metadata.Issuer,
            ValidAudience = options.ClientId,
            IssuerSigningKeys = metadata.SigningKeys,
            ValidateLifetime = true
        });

        if (!result.IsValid)
        {
            _logger.LogWarning(result.Exception, "Rejected logout token: signature, issuer, audience or lifetime is invalid");
            return null;
        }

        var token = (JsonWebToken)result.SecurityToken;
        using var payload = JsonDocument.Parse(Base64UrlEncoder.Decode(token.EncodedPayload));
        var claims = payload.RootElement;

        // Step 4: the "events" claim must announce a back-channel logout.
        if (!claims.TryGetProperty("events", out var events)
            || events.ValueKind != JsonValueKind.Object
            || !events.TryGetProperty(BackChannelLogoutEvent, out _))
        {
            _logger.LogWarning("Rejected logout token: missing back-channel logout event");
            return null;
        }

        // Step 5: a logout token must never carry a nonce (so an ID token cannot be replayed as one).
        if (claims.TryGetProperty("nonce", out _))
        {
            _logger.LogWarning("Rejected logout token: contains a nonce");
            return null;
        }

        // Step 6: it must say who or which session to log out.
        var subject = GetString(claims, "sub");
        var sessionId = GetString(claims, "sid");
        if (subject is null && sessionId is null)
        {
            _logger.LogWarning("Rejected logout token: neither sub nor sid present");
            return null;
        }

        // Step 7 (recommended): reject a token we have already processed.
        var jti = GetString(claims, "jti");
        if (jti is not null)
        {
            var remember = token.ValidTo - DateTime.UtcNow + TimeSpan.FromMinutes(5);
            var firstTime = await _redis.GetDatabase().StringSetAsync(
                ReplayKeyPrefix + jti, "1", remember > TimeSpan.Zero ? remember : TimeSpan.FromMinutes(5), When.NotExists);

            if (!firstTime)
            {
                _logger.LogWarning("Rejected logout token: jti {Jti} was already used", jti);
                return null;
            }
        }

        return new LogoutToken(subject, sessionId);
    }

    private static string? GetString(JsonElement claims, string name) =>
        claims.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
