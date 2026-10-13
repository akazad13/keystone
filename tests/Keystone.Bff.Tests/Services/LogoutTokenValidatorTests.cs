using System.Security.Cryptography;
using Keystone.Bff.Services;
using Keystone.Bff.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;

namespace Keystone.Bff.Tests.Services;

/// <summary>OpenID Connect Back-Channel Logout 1.0, section 2.6: each validation step has a test.</summary>
public sealed class LogoutTokenValidatorTests
{
    private readonly TestIdentityProvider _idp = new();
    private readonly LogoutTokenValidator _validator;

    public LogoutTokenValidatorTests() =>
        _validator = new LogoutTokenValidator(_idp.OptionsMonitor, new FakeRedis().Connection, NullLogger<LogoutTokenValidator>.Instance);

    private Task<LogoutToken?> Validate(string token) => _validator.ValidateAsync(token, CancellationToken.None);

    [Fact]
    public async Task Accepts_a_valid_token_and_returns_sub_and_sid()
    {
        var result = await Validate(_idp.CreateLogoutToken(sub: "alice", sid: "sso-1"));

        Assert.Equal(new LogoutToken("alice", "sso-1"), result);
    }

    [Fact]
    public async Task Accepts_a_token_with_only_sub()
    {
        var result = await Validate(_idp.CreateLogoutToken(sub: "alice", sid: null));

        Assert.Equal(new LogoutToken("alice", null), result);
    }

    [Fact]
    public async Task Rejects_a_token_signed_by_another_key()
    {
        var otherKey = new RsaSecurityKey(RSA.Create(2048)) { KeyId = _idp.SigningKey.KeyId };

        Assert.Null(await Validate(_idp.CreateLogoutToken(signingKey: otherKey)));
    }

    [Fact]
    public async Task Rejects_a_token_from_another_issuer()
    {
        Assert.Null(await Validate(_idp.CreateLogoutToken(issuer: "https://evil.example")));
    }

    [Fact]
    public async Task Rejects_a_token_for_another_client()
    {
        Assert.Null(await Validate(_idp.CreateLogoutToken(audience: "some-other-app")));
    }

    [Fact]
    public async Task Rejects_an_expired_token()
    {
        Assert.Null(await Validate(_idp.CreateLogoutToken(expires: DateTime.UtcNow.AddMinutes(-10))));
    }

    [Fact]
    public async Task Rejects_a_token_without_the_back_channel_logout_event()
    {
        Assert.Null(await Validate(_idp.CreateLogoutToken(customize: claims => claims.Remove("events"))));
    }

    [Fact]
    public async Task Rejects_a_token_with_a_different_event()
    {
        var token = _idp.CreateLogoutToken(customize: claims =>
            claims["events"] = new Dictionary<string, object> { ["http://example.com/other-event"] = new Dictionary<string, object>() });

        Assert.Null(await Validate(token));
    }

    [Fact]
    public async Task Rejects_a_token_with_a_nonce_so_id_tokens_cannot_be_replayed_as_logout_tokens()
    {
        Assert.Null(await Validate(_idp.CreateLogoutToken(customize: claims => claims["nonce"] = "abc")));
    }

    [Fact]
    public async Task Rejects_a_token_without_sub_and_sid()
    {
        Assert.Null(await Validate(_idp.CreateLogoutToken(sub: null, sid: null)));
    }

    [Fact]
    public async Task Rejects_a_replayed_token()
    {
        var token = _idp.CreateLogoutToken();

        Assert.NotNull(await Validate(token));
        Assert.Null(await Validate(token));
    }

    [Fact]
    public async Task Rejects_garbage()
    {
        Assert.Null(await Validate("not-a-jwt"));
    }
}
