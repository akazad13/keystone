using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Keystone.Bff.Tests.Fakes;

namespace Keystone.Bff.Tests.Endpoints;

public sealed class AuthEndpointsTests : IAsyncLifetime
{
    private BffTestHost _host = null!;

    public async ValueTask InitializeAsync() => _host = await BffTestHost.StartAsync();

    public ValueTask DisposeAsync() => _host.DisposeAsync();

    private static Dictionary<string, string> QueryOf(Uri location) =>
        location.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]));

    [Fact]
    public async Task Me_is_401_without_a_session()
    {
        var response = await _host.AnonymousBrowser().GetAsync("/api/keystone/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_returns_the_signed_in_user()
    {
        var sessionId = await _host.Sessions.StoreAsync(TestSessions.CreateTicket(sub: "alice-sub", name: "Alice"));

        var me = await _host.BrowserWithSession(sessionId).GetFromJsonAsync<JsonElement>("/api/keystone/me");

        Assert.Equal("alice-sub", me.GetProperty("sub").GetString());
        Assert.Equal("Alice", me.GetProperty("name").GetString());
    }

    [Theory]
    [InlineData("/projects", "/projects")]
    [InlineData(null, "/")]
    [InlineData("https://evil.example/phish", "/")]
    [InlineData("//evil.example", "/")]
    public async Task Login_redirects_to_the_identity_provider_and_only_back_to_local_urls(string? returnUrl, string expectedRedirect)
    {
        var url = returnUrl is null ? "/api/keystone/auth/login" : $"/api/keystone/auth/login?returnUrl={Uri.EscapeDataString(returnUrl)}";

        var response = await _host.AnonymousBrowser().GetAsync(url);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("https://idp.test/authorize", response.Headers.Location!.ToString());
        Assert.Equal(expectedRedirect, QueryOf(response.Headers.Location)["redirect_uri"]);
    }

    [Fact]
    public async Task Admin_login_returns_to_the_admin_app_by_default()
    {
        var response = await _host.AnonymousBrowser().GetAsync("/api/keystone-admin/auth/login");

        Assert.Equal("/admin/", QueryOf(response.Headers.Location!)["redirect_uri"]);
    }

    [Theory]
    [InlineData("login", "login")]
    [InlineData("none", "")]
    [InlineData("consent", "")]
    public async Task Login_passes_only_prompt_login_to_the_identity_provider(string prompt, string expected)
    {
        var response = await _host.AnonymousBrowser().GetAsync($"/api/keystone/auth/login?prompt={prompt}");

        Assert.Equal(expected, QueryOf(response.Headers.Location!)["prompt"]);
    }

    [Fact]
    public async Task Login_while_signed_in_ends_the_current_session_first()
    {
        var sessionId = await _host.Sessions.StoreAsync(TestSessions.CreateTicket());

        await _host.BrowserWithSession(sessionId).GetAsync("/api/keystone/auth/login?prompt=login");

        Assert.Null(await _host.Sessions.RetrieveAsync(sessionId));
    }

    [Fact]
    public async Task Logout_deletes_the_session_and_signs_out_at_the_identity_provider()
    {
        var sessionId = await _host.Sessions.StoreAsync(TestSessions.CreateTicket());

        var response = await _host.BrowserWithSession(sessionId).PostAsync("/api/keystone/auth/logout", null);

        Assert.Null(await _host.Sessions.RetrieveAsync(sessionId));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("https://idp.test/end_session", response.Headers.Location!.ToString());
        Assert.Equal("/", QueryOf(response.Headers.Location)["post_logout_redirect_uri"]);
    }
}
