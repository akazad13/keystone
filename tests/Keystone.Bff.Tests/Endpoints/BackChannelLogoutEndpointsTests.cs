using System.Net;
using Keystone.Bff.Tests.Fakes;

namespace Keystone.Bff.Tests.Endpoints;

/// <summary>The identity provider calls /bff/backchannel-logout server-to-server when an SSO session ends.</summary>
public sealed class BackChannelLogoutEndpointsTests : IAsyncLifetime
{
    private BffTestHost _host = null!;
    private string _aliceLaptop = null!;
    private string _alicePhone = null!;
    private string _bobLaptop = null!;

    public async ValueTask InitializeAsync()
    {
        _host = await BffTestHost.StartAsync();
        _aliceLaptop = await _host.Sessions.StoreAsync(TestSessions.CreateTicket(sub: "alice", sid: "sso-laptop"));
        _alicePhone = await _host.Sessions.StoreAsync(TestSessions.CreateTicket(sub: "alice", sid: "sso-phone"));
        _bobLaptop = await _host.Sessions.StoreAsync(TestSessions.CreateTicket(sub: "bob", sid: "sso-bob"));
    }

    public ValueTask DisposeAsync() => _host.DisposeAsync();

    private Task<HttpResponseMessage> PostLogoutToken(string token) =>
        _host.AnonymousBrowser().PostAsync("/bff/backchannel-logout",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["logout_token"] = token }));

    [Fact]
    public async Task A_token_with_sid_ends_only_that_sso_session()
    {
        var response = await PostLogoutToken(_host.Idp.CreateLogoutToken(sub: "alice", sid: "sso-laptop"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(await _host.Sessions.RetrieveAsync(_aliceLaptop));
        Assert.NotNull(await _host.Sessions.RetrieveAsync(_alicePhone));
        Assert.NotNull(await _host.Sessions.RetrieveAsync(_bobLaptop));
    }

    [Fact]
    public async Task A_token_with_only_sub_ends_every_session_of_the_user()
    {
        var response = await PostLogoutToken(_host.Idp.CreateLogoutToken(sub: "alice", sid: null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(await _host.Sessions.RetrieveAsync(_aliceLaptop));
        Assert.Null(await _host.Sessions.RetrieveAsync(_alicePhone));
        Assert.NotNull(await _host.Sessions.RetrieveAsync(_bobLaptop));
    }

    [Fact]
    public async Task Responses_must_not_be_cached()
    {
        var response = await PostLogoutToken(_host.Idp.CreateLogoutToken());

        Assert.True(response.Headers.CacheControl!.NoStore);
        Assert.True(response.Headers.CacheControl.NoCache);
    }

    [Fact]
    public async Task An_invalid_token_is_rejected_and_ends_nothing()
    {
        var response = await PostLogoutToken(_host.Idp.CreateLogoutToken(sub: "alice", sid: "sso-laptop", audience: "another-app"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(await _host.Sessions.RetrieveAsync(_aliceLaptop));
    }

    [Fact]
    public async Task A_request_that_is_not_a_form_is_rejected()
    {
        var response = await _host.AnonymousBrowser().PostAsync("/bff/backchannel-logout", new StringContent("{}"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
