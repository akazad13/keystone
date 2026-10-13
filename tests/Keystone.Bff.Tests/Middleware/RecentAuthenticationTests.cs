using System.Security.Claims;
using Keystone.Bff.Middleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Model;
using Yarp.ReverseProxy.Transforms.Builder;
using Yarp.ReverseProxy.Forwarder;

namespace Keystone.Bff.Tests.Middleware;

/// <summary>Step-up for DELETE (RFC 9470): the YARP pipeline step that checks auth_time.</summary>
public sealed class RecentAuthenticationTests
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(5);

    /// <summary>Runs one request through the step and reports whether it reached the proxy.</summary>
    private static async Task<(HttpContext Context, bool Forwarded)> Send(
        string method, DateTimeOffset? authTime, string clusterId = "core-api")
    {
        var forwarded = false;
        var pipeline = new ProxyPipelineBuilder(new ServiceCollection().BuildServiceProvider());
        pipeline.RequireRecentAuthenticationForDeletes(MaxAge);
        pipeline.Use(_ => _ =>
        {
            forwarded = true;
            return Task.CompletedTask;
        });

        var claims = authTime is null ? [] : new[] { new Claim("auth_time", authTime.Value.ToUnixTimeSeconds().ToString()) };
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Cookies"))
        };
        context.Request.Method = method;
        context.Response.Body = new MemoryStream();
        context.Features.Set<IReverseProxyFeature>(new ProxyFeature(clusterId));

        await pipeline.Build()(context);
        return (context, forwarded);
    }

    [Fact]
    public async Task Rejects_a_delete_when_the_last_sign_in_is_too_old()
    {
        var (context, forwarded) = await Send("DELETE", DateTimeOffset.UtcNow.AddMinutes(-30));

        Assert.False(forwarded);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        var challenge = context.Response.Headers.WWWAuthenticate.ToString();
        Assert.Contains("error=\"insufficient_user_authentication\"", challenge);
        Assert.Contains("max_age=300", challenge);
    }

    [Fact]
    public async Task Rejects_a_delete_without_auth_time()
    {
        var (context, forwarded) = await Send("DELETE", authTime: null);

        Assert.False(forwarded);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    [Fact]
    public async Task Forwards_a_delete_after_a_recent_sign_in()
    {
        var (_, forwarded) = await Send("DELETE", DateTimeOffset.UtcNow.AddMinutes(-1));

        Assert.True(forwarded);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    [InlineData("PUT")]
    public async Task Other_methods_do_not_need_a_recent_sign_in(string method)
    {
        var (_, forwarded) = await Send(method, DateTimeOffset.UtcNow.AddHours(-3));

        Assert.True(forwarded);
    }

    [Fact]
    public async Task Only_applies_to_api_routes()
    {
        var (_, forwarded) = await Send("DELETE", DateTimeOffset.UtcNow.AddHours(-3), clusterId: "keystone-spa");

        Assert.True(forwarded);
    }

    /// <summary>A YARP proxy pipeline outside a running proxy.</summary>
    private sealed class ProxyPipelineBuilder(IServiceProvider services) : IReverseProxyApplicationBuilder
    {
        private readonly ApplicationBuilder _inner = new(services);

        public IServiceProvider ApplicationServices
        {
            get => _inner.ApplicationServices;
            set => _inner.ApplicationServices = value;
        }

        public IFeatureCollection ServerFeatures => _inner.ServerFeatures;

        public IDictionary<string, object?> Properties => _inner.Properties;

        public RequestDelegate Build() => _inner.Build();

        public IApplicationBuilder New() => _inner.New();

        public IApplicationBuilder Use(Func<RequestDelegate, RequestDelegate> middleware) => _inner.Use(middleware);
    }

    /// <summary>What YARP attaches to a request once it has matched a route.</summary>
    private sealed class ProxyFeature(string clusterId) : IReverseProxyFeature
    {
        public RouteModel Route { get; } = new(
            new RouteConfig { RouteId = "test", ClusterId = clusterId, Match = new RouteMatch { Path = "/{**rest}" } },
            cluster: null,
            HttpTransformer.Default);

        public ClusterModel Cluster => throw new NotSupportedException();

        public IReadOnlyList<DestinationState> AllDestinations { get; } = [];

        public IReadOnlyList<DestinationState> AvailableDestinations { get; set; } = [];

        public DestinationState? ProxiedDestination { get; set; }
    }
}
