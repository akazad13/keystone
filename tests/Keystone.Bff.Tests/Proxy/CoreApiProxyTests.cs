using System.Net;
using Keystone.Bff.Tests.Fakes;

namespace Keystone.Bff.Tests.Proxy;

/// <summary>What the Core API actually receives when the browser calls the BFF, through real YARP and the real routes.</summary>
public sealed class CoreApiProxyTests : IAsyncLifetime
{
    private ProxyTestHost _host = null!;

    public async ValueTask InitializeAsync() => _host = await ProxyTestHost.StartAsync();

    public ValueTask DisposeAsync() => _host.DisposeAsync();

    [Theory]
    [InlineData("/api/keystone/projects", "http://core-api/api/projects")]
    [InlineData("/api/keystone/projects/42?expand=owner", "http://core-api/api/projects/42?expand=owner")]
    [InlineData("/api/keystone-admin/admin/users", "http://core-api/api/admin/users")]
    public async Task Rewrites_the_frontend_prefix_to_the_core_api_path(string browserPath, string coreApiUrl)
    {
        var response = await _host.SignedIn().GetAsync(browserPath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(coreApiUrl, Assert.Single(_host.CoreApi.Requests).RequestUri?.ToString());
    }

    [Fact]
    public async Task Sends_the_session_token_and_never_the_cookie()
    {
        await _host.SignedIn("alice").GetAsync("/api/keystone/projects");

        var request = Assert.Single(_host.CoreApi.Requests);
        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
        Assert.Equal("keystone-token-of-alice", request.Headers.Authorization?.Parameter);
        Assert.False(request.Headers.Contains("Cookie"));
    }

    [Fact]
    public async Task A_token_sent_by_the_browser_never_reaches_the_api()
    {
        var browser = _host.SignedIn("alice", sessionHasToken: false);
        browser.DefaultRequestHeaders.Add("Authorization", "Bearer token-from-the-browser");

        await browser.GetAsync("/api/keystone/projects");

        Assert.Null(Assert.Single(_host.CoreApi.Requests).Headers.Authorization);
    }

    [Fact]
    public async Task Anonymous_calls_are_rejected_before_reaching_the_api()
    {
        var response = await _host.Anonymous().GetAsync("/api/keystone/projects");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(_host.CoreApi.Requests);
    }

    [Fact]
    public async Task A_delete_after_an_old_sign_in_gets_the_step_up_challenge()
    {
        var response = await _host.SignedIn(signedInMinutesAgo: 30).DeleteAsync("/api/keystone/projects/42");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("insufficient_user_authentication", response.Headers.WwwAuthenticate.ToString());
        Assert.Empty(_host.CoreApi.Requests);
    }

    [Fact]
    public async Task A_delete_after_a_recent_sign_in_is_forwarded()
    {
        var response = await _host.SignedIn(signedInMinutesAgo: 1).DeleteAsync("/api/keystone/projects/42");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpMethod.Delete, Assert.Single(_host.CoreApi.Requests).Method);
    }
}
