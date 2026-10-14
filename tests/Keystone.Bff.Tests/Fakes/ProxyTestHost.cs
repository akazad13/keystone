using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.RateLimiting;
using Keystone.Bff.Middleware;
using Keystone.Bff.Proxy;
using Keystone.Bff.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Yarp.ReverseProxy.Forwarder;

namespace Keystone.Bff.Tests.Fakes;

/// <summary>
/// The BFF's reverse proxy for real: YARP with the routes from Keystone.Bff/appsettings.json, the token
/// transform and the step-up check, on an in-memory test server. Instead of the network, YARP sends to
/// <see cref="CoreApi"/>, which records what the Core API would have received.
/// <para>
/// Callers: <c>X-Test-User</c> names a signed-in user (no header: anonymous), <c>X-Test-Auth-Time</c>
/// says how many minutes ago they entered their password, <c>X-Test-No-Token</c> makes their session
/// unable to produce a Keystone token.
/// </para>
/// </summary>
public sealed class ProxyTestHost : IAsyncDisposable
{
    private readonly WebApplication _app;

    private ProxyTestHost(WebApplication app, RecordingCoreApi coreApi)
    {
        _app = app;
        CoreApi = coreApi;
    }

    public RecordingCoreApi CoreApi { get; }

    public static async Task<ProxyTestHost> StartAsync()
    {
        var coreApi = new RecordingCoreApi();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        // The real route table (copied next to the test assembly, see the .csproj).
        builder.Configuration.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "bff.appsettings.json"));

        builder.Services
            .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddScheme<AuthenticationSchemeOptions, TestUserHandler>(CookieAuthenticationDefaults.AuthenticationScheme, null);
        builder.Services.AddAuthorization();
        // The routes name these rate limiter policies; generous limits so they never interfere.
        builder.Services.AddRateLimiter(options =>
        {
            foreach (var policy in new[] { "keystone-api", "keystone-admin-api" })
            {
                options.AddPolicy(policy, _ => RateLimitPartition.GetNoLimiter("test"));
            }
        });
        builder.Services.AddSingleton<ISessionTokenService, TestSessionTokenService>();

        builder.Services.AddReverseProxy()
            .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
            .AddTransforms(CoreApiTokenTransform.Apply);
        builder.Services.AddSingleton<IForwarderHttpClientFactory>(new RecordingClientFactory(coreApi));

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();
        app.MapReverseProxy(proxy => proxy.RequireRecentAuthenticationForDeletes(TimeSpan.FromMinutes(5)));

        await app.StartAsync();
        return new ProxyTestHost(app, coreApi);
    }

    public HttpClient Anonymous() => _app.GetTestClient();

    /// <summary>A browser signed in as <paramref name="user"/>, who entered their password <paramref name="signedInMinutesAgo"/> minutes ago.</summary>
    public HttpClient SignedIn(string user = "alice", int signedInMinutesAgo = 1, bool sessionHasToken = true)
    {
        var client = _app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-User", user);
        client.DefaultRequestHeaders.Add("X-Test-Auth-Time", signedInMinutesAgo.ToString(CultureInfo.InvariantCulture));
        if (!sessionHasToken)
        {
            client.DefaultRequestHeaders.Add("X-Test-No-Token", "1");
        }
        client.DefaultRequestHeaders.Add("Cookie", "__Host-keystone=session-reference");
        return client;
    }

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();

    /// <summary>Stands in for the Core API: records every request and answers 200.</summary>
    public sealed class RecordingCoreApi : HttpMessageHandler
    {
        private readonly List<HttpRequestMessage> _requests = [];

        public IReadOnlyList<HttpRequestMessage> Requests
        {
            get { lock (_requests) { return [.. _requests]; } }
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (_requests)
            {
                _requests.Add(request);
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") });
        }
    }

    private sealed class RecordingClientFactory(RecordingCoreApi coreApi) : IForwarderHttpClientFactory
    {
        public HttpMessageInvoker CreateClient(ForwarderHttpClientContext context) => new(coreApi, disposeHandler: false);
    }

    /// <summary>The session's Keystone token, as SessionTokenService would return it.</summary>
    private sealed class TestSessionTokenService : ISessionTokenService
    {
        public Task<string?> GetAccessTokenAsync(HttpContext httpContext, CancellationToken cancellationToken)
        {
            var user = httpContext.User.FindFirst("sub")?.Value;
            var hasToken = user is not null && !httpContext.Request.Headers.ContainsKey("X-Test-No-Token");
            return Task.FromResult(hasToken ? $"keystone-token-of-{user}" : null);
        }
    }

    private sealed class TestUserHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var user = Request.Headers["X-Test-User"].ToString();
            if (string.IsNullOrEmpty(user))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var minutesAgo = int.Parse(Request.Headers["X-Test-Auth-Time"].ToString(), CultureInfo.InvariantCulture);
            var authTime = DateTimeOffset.UtcNow.AddMinutes(-minutesAgo).ToUnixTimeSeconds();
            var identity = new ClaimsIdentity(
                [new Claim("sub", user), new Claim("auth_time", authTime.ToString(CultureInfo.InvariantCulture))],
                Scheme.Name, "name", "role");

            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}
