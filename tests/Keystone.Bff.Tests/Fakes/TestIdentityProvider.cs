using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Keystone.Bff.Tests.Fakes;

/// <summary>
/// Plays the identity provider (Zitadel) for the BFF's services: a discovery document with a signing
/// key, a token endpoint answered by <see cref="TokenEndpoint"/>, and signed logout tokens.
/// </summary>
public sealed class TestIdentityProvider
{
    public const string Issuer = "https://idp.test";
    public const string ClientId = "keystone-bff";
    public const string BackChannelLogoutEvent = "http://schemas.openid.net/event/backchannel-logout";

    public TestIdentityProvider()
    {
        SigningKey = new RsaSecurityKey(RSA.Create(2048)) { KeyId = "idp-key" };

        var configuration = new OpenIdConnectConfiguration
        {
            Issuer = Issuer,
            TokenEndpoint = $"{Issuer}/oauth/v2/token"
        };
        configuration.SigningKeys.Add(SigningKey);

        Options = new OpenIdConnectOptions
        {
            ClientId = ClientId,
            ClientSecret = "test-secret",
            ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration),
            Backchannel = new HttpClient(TokenEndpoint)
        };
    }

    public RsaSecurityKey SigningKey { get; }

    public StubHttpHandler TokenEndpoint { get; } = new();

    public OpenIdConnectOptions Options { get; }

    public IOptionsMonitor<OpenIdConnectOptions> OptionsMonitor => new StaticOptionsMonitor<OpenIdConnectOptions>(Options);

    /// <summary>
    /// A valid logout token for <paramref name="sub"/> / <paramref name="sid"/> (either may be null).
    /// <paramref name="customize"/> can add or remove claims to make it invalid in specific ways.
    /// </summary>
    public string CreateLogoutToken(
        string? sub = "user-1",
        string? sid = "idp-session-1",
        Action<Dictionary<string, object>>? customize = null,
        string issuer = Issuer,
        string audience = ClientId,
        DateTime? expires = null,
        SecurityKey? signingKey = null)
    {
        var claims = new Dictionary<string, object>
        {
            ["jti"] = Guid.NewGuid().ToString("N"),
            ["events"] = new Dictionary<string, object> { [BackChannelLogoutEvent] = new Dictionary<string, object>() }
        };
        if (sub is not null) claims["sub"] = sub;
        if (sid is not null) claims["sid"] = sid;
        customize?.Invoke(claims);

        var expiresAt = expires ?? DateTime.UtcNow.AddMinutes(2);
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Claims = claims,
            IssuedAt = expiresAt.AddMinutes(-5),
            NotBefore = expiresAt.AddMinutes(-5),
            Expires = expiresAt,
            TokenType = "logout+jwt",
            SigningCredentials = new SigningCredentials(signingKey ?? SigningKey, SecurityAlgorithms.RsaSha256)
        });
    }
}

/// <summary>Returns the same options instance for every name.</summary>
public sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
{
    public T CurrentValue => value;

    public T Get(string? name) => value;

    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
