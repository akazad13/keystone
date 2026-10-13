using System.Net;
using System.Text;
using Keystone.Bff.Services;
using Keystone.Bff.Tests.Fakes;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Keystone.Bff.Tests.Services;

/// <summary>RFC 8693 token exchange at the Identity Service, and the per-session cache of its result.</summary>
public sealed class SessionTokenServiceTests
{
    private readonly FakeRedis _redis = new();
    private readonly TestIdentityProvider _idp = new();
    private readonly StubHttpHandler _identityService = new()
    {
        ResponseBody = """{"access_token":"keystone-jwt","token_type":"Bearer","expires_in":900}"""
    };
    private readonly RedisTicketStore _store;
    private readonly SessionTokenService _service;

    public SessionTokenServiceTests()
    {
        var dataProtection = new EphemeralDataProtectionProvider();
        _store = TestSessions.CreateStore(_redis, dataProtection);
        var refresher = new TokenRefresher(_idp.OptionsMonitor, _store, _redis.Connection, NullLogger<TokenRefresher>.Instance);

        _service = new SessionTokenService(
            new StubHttpClientFactory(_identityService, new Uri("http://identity-service")),
            refresher,
            _redis.Connection,
            dataProtection,
            NullLogger<SessionTokenService>.Instance);
    }

    /// <summary>A request from a browser whose session's Zitadel access token expires at <paramref name="upstreamExpiresAt"/>.</summary>
    private async Task<(HttpContext Context, string SessionId)> SignedInRequest(DateTimeOffset upstreamExpiresAt)
    {
        var ticket = TestSessions.CreateTicket(accessTokenExpiresAt: upstreamExpiresAt);
        var sessionId = await _store.StoreAsync(ticket);
        return (RequestWith(AuthenticateResult.Success(ticket)), sessionId);
    }

    private static HttpContext RequestWith(AuthenticateResult result) => new DefaultHttpContext
    {
        RequestServices = new ServiceCollection()
            .AddSingleton<IAuthenticationService>(new FakeAuthenticationService(result))
            .BuildServiceProvider()
    };

    private string CacheKey(string sessionId) => RedisTicketStore.SessionDataKey(sessionId, SessionTokenService.CoreApiTokenName);

    [Fact]
    public async Task Returns_null_without_a_session()
    {
        var token = await _service.GetAccessTokenAsync(RequestWith(AuthenticateResult.NoResult()), CancellationToken.None);

        Assert.Null(token);
        Assert.Equal(0, _identityService.RequestCount);
    }

    [Fact]
    public async Task Exchanges_the_identity_provider_token_for_a_keystone_token()
    {
        var (context, _) = await SignedInRequest(DateTimeOffset.UtcNow.AddHours(1));

        var token = await _service.GetAccessTokenAsync(context, CancellationToken.None);

        Assert.Equal("keystone-jwt", token);
        var form = Assert.Single(_identityService.Forms);
        Assert.Equal("urn:ietf:params:oauth:grant-type:token-exchange", form["grant_type"]);
        Assert.Equal("idp-access-token", form["subject_token"]);
        Assert.Equal("urn:ietf:params:oauth:token-type:access_token", form["subject_token_type"]);
        Assert.Equal("core-api", form["audience"]);
        Assert.Equal("/connect/token", _identityService.RequestUris.Single()!.AbsolutePath);
    }

    [Fact]
    public async Task Caches_the_keystone_token_per_session()
    {
        var (context, _) = await SignedInRequest(DateTimeOffset.UtcNow.AddHours(1));

        await _service.GetAccessTokenAsync(context, CancellationToken.None);
        var second = await _service.GetAccessTokenAsync(context, CancellationToken.None);

        Assert.Equal("keystone-jwt", second);
        Assert.Equal(1, _identityService.RequestCount);
    }

    [Fact]
    public async Task Cached_token_is_encrypted_in_Redis()
    {
        var (context, sessionId) = await SignedInRequest(DateTimeOffset.UtcNow.AddHours(1));

        await _service.GetAccessTokenAsync(context, CancellationToken.None);

        var stored = Encoding.UTF8.GetString(_redis.GetBytes(CacheKey(sessionId))!);
        Assert.DoesNotContain("keystone-jwt", stored);
    }

    [Fact]
    public async Task Cache_lifetime_is_the_keystone_token_lifetime_minus_a_minute()
    {
        var (context, sessionId) = await SignedInRequest(DateTimeOffset.UtcNow.AddHours(1));

        await _service.GetAccessTokenAsync(context, CancellationToken.None);

        Assert.Equal(TimeSpan.FromMinutes(14), _redis.TimeToLive(CacheKey(sessionId)));
    }

    [Fact]
    public async Task Cached_token_never_outlives_the_identity_provider_token()
    {
        var (context, sessionId) = await SignedInRequest(DateTimeOffset.UtcNow.AddMinutes(5));

        await _service.GetAccessTokenAsync(context, CancellationToken.None);

        Assert.InRange(_redis.TimeToLive(CacheKey(sessionId))!.Value, TimeSpan.FromMinutes(3.9), TimeSpan.FromMinutes(4));
    }

    [Fact]
    public async Task Returns_null_and_caches_nothing_when_the_exchange_fails()
    {
        var (context, sessionId) = await SignedInRequest(DateTimeOffset.UtcNow.AddHours(1));
        _identityService.StatusCode = HttpStatusCode.BadRequest;
        _identityService.ResponseBody = """{"error":"invalid_grant"}""";

        Assert.Null(await _service.GetAccessTokenAsync(context, CancellationToken.None));
        Assert.False(_redis.Contains(CacheKey(sessionId)));
    }
}
