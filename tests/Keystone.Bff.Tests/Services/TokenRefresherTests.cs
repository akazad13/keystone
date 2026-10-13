using System.Net;
using Keystone.Bff.Services;
using Keystone.Bff.Tests.Fakes;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging.Abstractions;

namespace Keystone.Bff.Tests.Services;

public sealed class TokenRefresherTests
{
    private readonly FakeRedis _redis = new();
    private readonly TestIdentityProvider _idp = new();
    private readonly RedisTicketStore _store;
    private readonly TokenRefresher _refresher;

    public TokenRefresherTests()
    {
        _store = TestSessions.CreateStore(_redis);
        _refresher = new TokenRefresher(_idp.OptionsMonitor, _store, _redis.Connection, NullLogger<TokenRefresher>.Instance);
        _idp.TokenEndpoint.ResponseBody = """
            {"access_token":"new-access-token","expires_in":3600,"refresh_token":"rotated-refresh-token","id_token":"new-id-token"}
            """;
    }

    /// <summary>Stores a session whose access token expires at <paramref name="expiresAt"/>.</summary>
    private async Task<(string SessionId, AuthenticationProperties Properties)> CreateSession(
        DateTimeOffset expiresAt, string? refreshToken = "idp-refresh-token")
    {
        var ticket = TestSessions.CreateTicket(accessTokenExpiresAt: expiresAt, refreshToken: refreshToken);
        var sessionId = await _store.StoreAsync(ticket);
        return (sessionId, ticket.Properties);
    }

    [Fact]
    public async Task Returns_the_current_token_without_refreshing_while_it_is_valid()
    {
        var (sessionId, properties) = await CreateSession(DateTimeOffset.UtcNow.AddMinutes(30));

        var token = await _refresher.GetAccessTokenAsync(sessionId, properties, CancellationToken.None);

        Assert.Equal("idp-access-token", token?.AccessToken);
        Assert.Equal(0, _idp.TokenEndpoint.RequestCount);
        Assert.Equal(0, _redis.Calls("LockTakeAsync"));
    }

    [Fact]
    public async Task Refreshes_a_token_that_is_about_to_expire_and_stores_the_rotated_tokens()
    {
        var (sessionId, properties) = await CreateSession(DateTimeOffset.UtcNow.AddSeconds(30));

        var token = await _refresher.GetAccessTokenAsync(sessionId, properties, CancellationToken.None);

        Assert.Equal("new-access-token", token?.AccessToken);
        Assert.InRange(token!.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(59), DateTimeOffset.UtcNow.AddMinutes(61));

        var form = Assert.Single(_idp.TokenEndpoint.Forms);
        Assert.Equal("refresh_token", form["grant_type"]);
        Assert.Equal("idp-refresh-token", form["refresh_token"]);
        Assert.Equal(TestIdentityProvider.ClientId, form["client_id"]);

        var stored = await _store.RetrieveAsync(sessionId);
        Assert.Equal("new-access-token", stored!.Properties.GetTokenValue("access_token"));
        Assert.Equal("rotated-refresh-token", stored.Properties.GetTokenValue("refresh_token"));
        Assert.Equal("new-id-token", stored.Properties.GetTokenValue("id_token"));
    }

    [Fact]
    public async Task Releases_the_lock_after_refreshing()
    {
        var (sessionId, properties) = await CreateSession(DateTimeOffset.UtcNow.AddSeconds(30));

        await _refresher.GetAccessTokenAsync(sessionId, properties, CancellationToken.None);

        Assert.False(_redis.Contains(RedisTicketStore.SessionDataKey(sessionId, "refresh-lock")));
    }

    [Fact]
    public async Task Uses_the_stored_token_when_another_request_already_refreshed()
    {
        // This request loaded the session before the refresh; Redis already holds the new tokens.
        var (sessionId, staleProperties) = await CreateSession(DateTimeOffset.UtcNow.AddSeconds(30));
        await _refresher.GetAccessTokenAsync(sessionId, staleProperties, CancellationToken.None);

        var token = await _refresher.GetAccessTokenAsync(sessionId, staleProperties, CancellationToken.None);

        Assert.Equal("new-access-token", token?.AccessToken);
        Assert.Equal(1, _idp.TokenEndpoint.RequestCount);
    }

    [Fact]
    public async Task Concurrent_requests_refresh_only_once()
    {
        // Zitadel rotates refresh tokens: a second redemption of the old one would fail.
        var (sessionId, properties) = await CreateSession(DateTimeOffset.UtcNow.AddSeconds(30));
        _idp.TokenEndpoint.Delay = TimeSpan.FromMilliseconds(300);

        var tokens = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ =>
            Task.Run(() => _refresher.GetAccessTokenAsync(sessionId, properties, CancellationToken.None))));

        Assert.Equal(1, _idp.TokenEndpoint.RequestCount);
        Assert.All(tokens, token => Assert.Equal("new-access-token", token?.AccessToken));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task Removes_the_session_when_the_provider_rejects_the_refresh_token(HttpStatusCode status)
    {
        var (sessionId, properties) = await CreateSession(DateTimeOffset.UtcNow.AddSeconds(30));
        _idp.TokenEndpoint.StatusCode = status;
        _idp.TokenEndpoint.ResponseBody = """{"error":"invalid_grant"}""";

        var token = await _refresher.GetAccessTokenAsync(sessionId, properties, CancellationToken.None);

        Assert.Null(token);
        Assert.Null(await _store.RetrieveAsync(sessionId));
    }

    [Fact]
    public async Task Keeps_the_session_when_the_provider_is_unavailable()
    {
        var (sessionId, properties) = await CreateSession(DateTimeOffset.UtcNow.AddSeconds(30));
        _idp.TokenEndpoint.StatusCode = HttpStatusCode.ServiceUnavailable;

        var token = await _refresher.GetAccessTokenAsync(sessionId, properties, CancellationToken.None);

        Assert.Null(token);
        var stored = await _store.RetrieveAsync(sessionId);
        Assert.Equal("idp-refresh-token", stored?.Properties.GetTokenValue("refresh_token"));
    }

    [Fact]
    public async Task Returns_null_without_calling_the_provider_when_there_is_no_refresh_token()
    {
        var (sessionId, properties) = await CreateSession(DateTimeOffset.UtcNow.AddSeconds(30), refreshToken: null);

        Assert.Null(await _refresher.GetAccessTokenAsync(sessionId, properties, CancellationToken.None));
        Assert.Equal(0, _idp.TokenEndpoint.RequestCount);
    }

    [Fact]
    public async Task Returns_null_when_the_session_has_ended()
    {
        var (sessionId, properties) = await CreateSession(DateTimeOffset.UtcNow.AddSeconds(30));
        await _store.RemoveAsync(sessionId);

        Assert.Null(await _refresher.GetAccessTokenAsync(sessionId, properties, CancellationToken.None));
        Assert.Equal(0, _idp.TokenEndpoint.RequestCount);
    }
}
