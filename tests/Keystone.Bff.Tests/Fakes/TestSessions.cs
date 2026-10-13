using System.Globalization;
using System.Security.Claims;
using Keystone.Bff.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Keystone.Bff.Tests.Fakes;

/// <summary>Builds BFF sessions the way the cookie and OIDC handlers would after a login.</summary>
public static class TestSessions
{
    public static RedisTicketStore CreateStore(FakeRedis redis, IDataProtectionProvider? dataProtection = null) =>
        new(redis.Connection, dataProtection ?? new EphemeralDataProtectionProvider(), NullLogger<RedisTicketStore>.Instance);

    /// <summary>
    /// A ticket for <paramref name="sub"/> holding identity-provider tokens. By default the access token
    /// is valid for another hour.
    /// </summary>
    public static AuthenticationTicket CreateTicket(
        string sub = "user-1",
        string? sid = "idp-session-1",
        DateTimeOffset? issuedUtc = null,
        string? accessToken = "idp-access-token",
        DateTimeOffset? accessTokenExpiresAt = null,
        string? refreshToken = "idp-refresh-token",
        DateTimeOffset? authTime = null,
        string? name = "Alice")
    {
        var claims = new List<Claim> { new("sub", sub) };
        if (sid is not null) claims.Add(new Claim("sid", sid));
        if (name is not null) claims.Add(new Claim("name", name));
        if (authTime is not null)
        {
            claims.Add(new Claim("auth_time", authTime.Value.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)));
        }

        var issued = issuedUtc ?? DateTimeOffset.UtcNow;
        var properties = new AuthenticationProperties
        {
            IssuedUtc = issued,
            ExpiresUtc = issued.AddHours(8)
        };

        var tokens = new List<AuthenticationToken>();
        if (accessToken is not null)
        {
            tokens.Add(new AuthenticationToken { Name = "access_token", Value = accessToken });
            tokens.Add(new AuthenticationToken
            {
                Name = "expires_at",
                Value = (accessTokenExpiresAt ?? DateTimeOffset.UtcNow.AddHours(1)).ToString("o", CultureInfo.InvariantCulture)
            });
        }
        if (refreshToken is not null)
        {
            tokens.Add(new AuthenticationToken { Name = "refresh_token", Value = refreshToken });
        }
        tokens.Add(new AuthenticationToken { Name = "id_token", Value = "idp-id-token" });
        properties.StoreTokens(tokens);

        var identity = new ClaimsIdentity(claims, "Cookies", "name", "role");
        return new AuthenticationTicket(new ClaimsPrincipal(identity), properties, "Cookies");
    }

    /// <summary>Redis key of a session's ticket (see ARCHITECTURE.md, Redis keys).</summary>
    public static string SessionKey(string sessionId) => $"keystone:bff:session:{sessionId}";

    public static string SubjectIndexKey(string sub) => $"keystone:bff:session:sub:{sub}";

    public static string SidIndexKey(string sid) => $"keystone:bff:session:sid:{sid}";
}
