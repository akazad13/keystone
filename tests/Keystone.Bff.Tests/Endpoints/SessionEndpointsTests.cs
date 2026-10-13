using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Keystone.Bff.Tests.Fakes;

namespace Keystone.Bff.Tests.Endpoints;

/// <summary>"Your sessions": list, end one, end all. Nobody can touch another user's sessions.</summary>
public sealed class SessionEndpointsTests : IAsyncLifetime
{
    private BffTestHost _host = null!;
    private string _aliceLaptop = null!;
    private string _alicePhone = null!;
    private string _bobLaptop = null!;

    public async ValueTask InitializeAsync()
    {
        _host = await BffTestHost.StartAsync();
        _aliceLaptop = await _host.Sessions.StoreAsync(TestSessions.CreateTicket(sub: "alice", sid: "sso-a1"));
        _alicePhone = await _host.Sessions.StoreAsync(TestSessions.CreateTicket(sub: "alice", sid: "sso-a2"));
        _bobLaptop = await _host.Sessions.StoreAsync(TestSessions.CreateTicket(sub: "bob", sid: "sso-b1"));
    }

    public ValueTask DisposeAsync() => _host.DisposeAsync();

    [Fact]
    public async Task Requires_a_session()
    {
        var response = await _host.AnonymousBrowser().GetAsync("/api/keystone/sessions");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Lists_the_callers_sessions_and_marks_the_current_one()
    {
        var sessions = await _host.BrowserWithSession(_aliceLaptop).GetFromJsonAsync<JsonElement[]>("/api/keystone/sessions");

        Assert.Equal(
            new[] { _aliceLaptop, _alicePhone }.Order(),
            sessions!.Select(s => s.GetProperty("id").GetString()).Order());
        Assert.True(sessions!.Single(s => s.GetProperty("id").GetString() == _aliceLaptop).GetProperty("current").GetBoolean());
        Assert.False(sessions!.Single(s => s.GetProperty("id").GetString() == _alicePhone).GetProperty("current").GetBoolean());
    }

    [Fact]
    public async Task Revoking_another_device_ends_that_session_only()
    {
        var response = await _host.BrowserWithSession(_aliceLaptop).PostAsync($"/api/keystone/sessions/{_alicePhone}/revoke", null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Null(await _host.Sessions.RetrieveAsync(_alicePhone));
        Assert.NotNull(await _host.Sessions.RetrieveAsync(_aliceLaptop));
        Assert.False(response.Headers.Contains("X-Test-Cookie-Cleared"));
    }

    [Fact]
    public async Task Revoking_the_current_session_also_clears_the_cookie()
    {
        var response = await _host.BrowserWithSession(_aliceLaptop).PostAsync($"/api/keystone/sessions/{_aliceLaptop}/revoke", null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Null(await _host.Sessions.RetrieveAsync(_aliceLaptop));
        Assert.True(response.Headers.Contains("X-Test-Cookie-Cleared"));
    }

    [Fact]
    public async Task Another_users_session_looks_like_an_unknown_one()
    {
        var response = await _host.BrowserWithSession(_aliceLaptop).PostAsync($"/api/keystone/sessions/{_bobLaptop}/revoke", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotNull(await _host.Sessions.RetrieveAsync(_bobLaptop));
    }

    [Fact]
    public async Task Revoke_all_ends_every_session_of_the_caller_and_nobody_elses()
    {
        var response = await _host.BrowserWithSession(_aliceLaptop).PostAsync("/api/keystone/sessions/revoke-all", null);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, body.GetProperty("removed").GetInt32());
        Assert.Null(await _host.Sessions.RetrieveAsync(_aliceLaptop));
        Assert.Null(await _host.Sessions.RetrieveAsync(_alicePhone));
        Assert.NotNull(await _host.Sessions.RetrieveAsync(_bobLaptop));
    }
}
