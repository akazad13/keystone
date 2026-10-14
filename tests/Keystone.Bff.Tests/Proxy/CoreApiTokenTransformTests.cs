using System.Net.Http.Headers;
using Keystone.Bff.Proxy;
using Keystone.Bff.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Transforms;
using Yarp.ReverseProxy.Transforms.Builder;

namespace Keystone.Bff.Tests.Proxy;

public sealed class CoreApiTokenTransformTests
{
    [Fact]
    public void Is_registered_on_core_api_routes()
    {
        var context = new TransformBuilderContext { Route = new RouteConfig { RouteId = "keystone-api", ClusterId = "core-api" } };

        CoreApiTokenTransform.Apply(context);

        Assert.Single(context.RequestTransforms);
    }

    [Fact]
    public void Is_not_registered_on_frontend_routes()
    {
        // The dev-time routes to the Vite servers must never carry an access token.
        var context = new TransformBuilderContext { Route = new RouteConfig { RouteId = "keystone-spa", ClusterId = "keystone-spa" } };

        CoreApiTokenTransform.Apply(context);

        Assert.Empty(context.RequestTransforms);
    }

    /// <summary>A proxied request as YARP builds it: the browser's headers already copied over.</summary>
    private static RequestTransformContext ProxiedRequest(string? sessionToken, string? browserAuthorization = null)
    {
        var httpContext = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection()
                .AddSingleton<ISessionTokenService>(new FixedSessionTokenService(sessionToken))
                .BuildServiceProvider()
        };

        var proxyRequest = new HttpRequestMessage(HttpMethod.Get, "http://core-api/api/projects");
        proxyRequest.Headers.TryAddWithoutValidation("Cookie", "__Host-keystone=session-reference");
        if (browserAuthorization is not null)
        {
            proxyRequest.Headers.TryAddWithoutValidation("Authorization", browserAuthorization);
        }

        return new RequestTransformContext { HttpContext = httpContext, ProxyRequest = proxyRequest };
    }

    [Fact]
    public async Task Attaches_the_session_token_as_a_bearer_token()
    {
        var context = ProxiedRequest(sessionToken: "keystone-jwt");

        await CoreApiTokenTransform.AttachSessionTokenAsync(context);

        Assert.Equal(new AuthenticationHeaderValue("Bearer", "keystone-jwt"), context.ProxyRequest.Headers.Authorization);
    }

    [Fact]
    public async Task Removes_the_browser_cookies()
    {
        var context = ProxiedRequest(sessionToken: "keystone-jwt");

        await CoreApiTokenTransform.AttachSessionTokenAsync(context);

        Assert.False(context.ProxyRequest.Headers.Contains("Cookie"));
    }

    [Fact]
    public async Task Replaces_an_authorization_header_sent_by_the_browser()
    {
        var context = ProxiedRequest(sessionToken: "keystone-jwt", browserAuthorization: "Bearer token-from-the-browser");

        await CoreApiTokenTransform.AttachSessionTokenAsync(context);

        Assert.Equal("keystone-jwt", context.ProxyRequest.Headers.Authorization?.Parameter);
    }

    [Fact]
    public async Task Without_a_session_token_no_authorization_header_is_forwarded()
    {
        var context = ProxiedRequest(sessionToken: null, browserAuthorization: "Bearer token-from-the-browser");

        await CoreApiTokenTransform.AttachSessionTokenAsync(context);

        Assert.Null(context.ProxyRequest.Headers.Authorization);
        Assert.False(context.ProxyRequest.Headers.Contains("Cookie"));
    }

    private sealed class FixedSessionTokenService(string? token) : ISessionTokenService
    {
        public Task<string?> GetAccessTokenAsync(HttpContext httpContext, CancellationToken cancellationToken) => Task.FromResult(token);
    }
}
