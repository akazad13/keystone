using Keystone.IdentityService.Models;
using Keystone.IdentityService.Services;
using Keystone.IdentityService.Tests.Fakes;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Keystone.IdentityService.Tests.Services;

/// <summary>Keystone access tokens: RFC 9068-style JWTs that internal APIs validate offline.</summary>
public sealed class TokenServiceTests : IDisposable
{
    private readonly KeyManager _keys = TestSetup.DevelopmentKeyManager();
    private readonly TokenService _service;

    private static readonly UserInfo Alice = new()
    {
        Sub = "alice-sub",
        Name = "Alice",
        Email = "alice@example.com",
        PreferredUsername = "alice",
        Roles = ["Admin"]
    };

    public TokenServiceTests() => _service = new TokenService(_keys, TestSetup.Configuration());

    public void Dispose() => _keys.Dispose();

    /// <summary>Validates a token the way the Core API does: against the published JWKS only.</summary>
    private async Task<TokenValidationResult> ValidateLikeTheCoreApi(string token) =>
        await new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = TestSetup.Issuer,
            ValidAudience = "core-api",
            ValidTypes = ["at+jwt"],
            IssuerSigningKeys = _keys.GetJsonWebKeySet().GetSigningKeys()
        });

    [Fact]
    public async Task Issues_a_token_the_core_api_accepts()
    {
        var response = _service.IssueAccessToken(Alice, "core-api");

        var result = await ValidateLikeTheCoreApi(response.AccessToken);

        Assert.True(result.IsValid, result.Exception?.Message);
    }

    [Fact]
    public void The_response_follows_RFC_8693()
    {
        var response = _service.IssueAccessToken(Alice, "core-api");

        Assert.Equal("Bearer", response.TokenType);
        Assert.Equal(TokenExchangeRequest.AccessTokenType, response.IssuedTokenType);
        Assert.Equal(15 * 60, response.ExpiresIn);
    }

    [Fact]
    public void The_token_carries_the_user_and_is_signed_with_the_published_key()
    {
        var token = new JsonWebToken(_service.IssueAccessToken(Alice, "core-api").AccessToken);

        Assert.Equal("at+jwt", token.Typ);
        Assert.Equal(_keys.GetSigningKey().KeyId, token.Kid);
        Assert.Equal(TestSetup.Issuer, token.Issuer);
        Assert.Equal(["core-api"], token.Audiences);
        Assert.Equal("alice-sub", token.Subject);
        Assert.Equal("Alice", token.GetClaim("name").Value);
        Assert.Equal("alice@example.com", token.GetClaim("email").Value);
        Assert.Equal("alice", token.GetClaim("preferred_username").Value);
        Assert.False(string.IsNullOrEmpty(token.Id));
    }

    [Fact]
    public void Every_user_gets_the_default_role_on_top_of_their_provider_roles()
    {
        var token = new JsonWebToken(_service.IssueAccessToken(Alice, "core-api").AccessToken);

        Assert.Equal(["Admin", "User"], token.Claims.Where(c => c.Type == "role").Select(c => c.Value).Order());
    }

    [Fact]
    public void Roles_are_not_duplicated()
    {
        var user = new UserInfo { Sub = "bob", Roles = ["User"] };

        var token = new JsonWebToken(_service.IssueAccessToken(user, "core-api").AccessToken);

        Assert.Equal(["User"], token.Claims.Where(c => c.Type == "role").Select(c => c.Value));
    }

    [Fact]
    public void Missing_profile_claims_are_left_out()
    {
        var token = new JsonWebToken(_service.IssueAccessToken(new UserInfo { Sub = "minimal" }, "core-api").AccessToken);

        Assert.False(token.TryGetClaim("name", out _));
        Assert.False(token.TryGetClaim("email", out _));
        Assert.False(token.TryGetClaim("preferred_username", out _));
    }

    [Fact]
    public void Lifetime_comes_from_configuration()
    {
        var service = new TokenService(_keys, TestSetup.Configuration(("Jwt:LifetimeMinutes", "5")));

        var response = service.IssueAccessToken(Alice, "core-api");
        var token = new JsonWebToken(response.AccessToken);

        Assert.Equal(300, response.ExpiresIn);
        Assert.Equal(TimeSpan.FromMinutes(5), token.ValidTo - token.IssuedAt);
    }

    [Fact]
    public void Each_token_has_a_unique_id()
    {
        var first = new JsonWebToken(_service.IssueAccessToken(Alice, "core-api").AccessToken);
        var second = new JsonWebToken(_service.IssueAccessToken(Alice, "core-api").AccessToken);

        Assert.NotEqual(first.Id, second.Id);
    }

    [Theory]
    [InlineData("core-api", true)]
    [InlineData("Core-Api", false)]
    [InlineData("billing-api", false)]
    [InlineData("", false)]
    public void Only_configured_audiences_are_known(string audience, bool known)
    {
        Assert.Equal(known, _service.IsKnownAudience(audience));
    }

    [Fact]
    public void Requires_an_issuer()
    {
        Assert.Throws<InvalidOperationException>(() => new TokenService(_keys, TestSetup.Configuration(("Jwt:Issuer", null))));
    }

    [Fact]
    public async Task ValidateAccessTokenAsync_returns_the_claims_of_a_valid_token()
    {
        var claims = await _service.ValidateAccessTokenAsync(_service.IssueAccessToken(Alice, "core-api").AccessToken);

        Assert.NotNull(claims);
        Assert.Equal("alice-sub", claims["sub"]);
    }

    [Fact]
    public async Task ValidateAccessTokenAsync_rejects_a_token_from_another_key()
    {
        using var otherKeys = TestSetup.DevelopmentKeyManager();
        var forged = new TokenService(otherKeys, TestSetup.Configuration()).IssueAccessToken(Alice, "core-api");

        Assert.Null(await _service.ValidateAccessTokenAsync(forged.AccessToken));
    }

    [Fact]
    public async Task ValidateAccessTokenAsync_rejects_a_token_for_an_unknown_audience()
    {
        var token = _service.IssueAccessToken(Alice, "billing-api");

        Assert.Null(await _service.ValidateAccessTokenAsync(token.AccessToken));
    }

    [Fact]
    public async Task ValidateAccessTokenAsync_rejects_an_expired_token()
    {
        var expiredAt = DateTime.UtcNow.AddMinutes(-5);
        var expired = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = TestSetup.Issuer,
            Audience = "core-api",
            IssuedAt = expiredAt.AddMinutes(-15),
            NotBefore = expiredAt.AddMinutes(-15),
            Expires = expiredAt,
            Claims = new Dictionary<string, object> { ["sub"] = "alice-sub" },
            SigningCredentials = new SigningCredentials(_keys.GetSigningKey(), SecurityAlgorithms.RsaSha256)
        });

        Assert.Null(await _service.ValidateAccessTokenAsync(expired));
    }

    [Fact]
    public async Task ValidateAccessTokenAsync_rejects_garbage()
    {
        Assert.Null(await _service.ValidateAccessTokenAsync("not-a-token"));
    }
}
