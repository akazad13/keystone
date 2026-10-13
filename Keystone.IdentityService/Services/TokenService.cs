using Keystone.IdentityService.Models;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Keystone.IdentityService.Services;

/// <summary>
/// Issues and validates Keystone access tokens: short-lived JWTs (RFC 9068 profile) that internal
/// APIs validate offline against this service's JWKS.
/// </summary>
public sealed class TokenService : ITokenService
{
    private readonly IKeyManager _keyManager;
    private readonly JsonWebTokenHandler _handler = new();
    private readonly string _issuer;
    private readonly string[] _audiences;
    private readonly string _defaultRole;
    private readonly TimeSpan _lifetime;

    public TokenService(IKeyManager keyManager, IConfiguration configuration)
    {
        _keyManager = keyManager;
        _issuer = configuration["Jwt:Issuer"] ?? throw new InvalidOperationException("Jwt:Issuer is not configured.");
        _audiences = configuration.GetSection("Jwt:Audiences").Get<string[]>() ?? [];
        _defaultRole = configuration["Jwt:DefaultRole"] ?? "User";
        _lifetime = TimeSpan.FromMinutes(configuration.GetValue("Jwt:LifetimeMinutes", 15));
    }

    public bool IsKnownAudience(string audience) => _audiences.Contains(audience, StringComparer.Ordinal);

    public TokenResponse IssueAccessToken(UserInfo user, string audience)
    {
        var now = DateTime.UtcNow;

        // Every signed-in user gets the default role; identity-provider roles are added on top.
        var roles = user.Roles.Append(_defaultRole).Distinct(StringComparer.Ordinal).ToArray();

        var claims = new Dictionary<string, object>
        {
            [JwtRegisteredClaimNames.Sub] = user.Sub,
            [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString("N"),
            ["role"] = roles
        };
        AddIfPresent(claims, JwtRegisteredClaimNames.Name, user.Name);
        AddIfPresent(claims, JwtRegisteredClaimNames.Email, user.Email);
        AddIfPresent(claims, "preferred_username", user.PreferredUsername);

        var token = _handler.CreateToken(new SecurityTokenDescriptor
        {
            TokenType = "at+jwt",
            Issuer = _issuer,
            Audience = audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = now.Add(_lifetime),
            Claims = claims,
            SigningCredentials = new SigningCredentials(_keyManager.GetSigningKey(), SecurityAlgorithms.RsaSha256)
        });

        return new TokenResponse
        {
            AccessToken = token,
            ExpiresIn = (int)_lifetime.TotalSeconds
        };
    }

    public async Task<IDictionary<string, object>?> ValidateAccessTokenAsync(string token)
    {
        var result = await _handler.ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = _issuer,
            ValidAudiences = _audiences,
            IssuerSigningKey = _keyManager.GetSigningKey(),
            ClockSkew = TimeSpan.FromSeconds(30)
        });

        return result.IsValid ? result.Claims : null;
    }

    private static void AddIfPresent(Dictionary<string, object> claims, string name, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            claims[name] = value;
        }
    }
}
