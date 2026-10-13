using System.Text.Encodings.Web;
using Keystone.Bff.Endpoints;
using Keystone.Bff.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Keystone.Bff.Tests.Fakes;

/// <summary>
/// The BFF's own endpoints (auth, sessions, back-channel logout) on an in-memory test server.
/// <para>
/// The real cookie and OIDC handlers are replaced by small test handlers: the "cookie" is the
/// <c>X-Test-Session</c> header holding a session id, looked up in the real <see cref="RedisTicketStore"/>
/// (over <see cref="FakeRedis"/>); the "identity provider" answers challenges and sign-outs with a
/// redirect that the tests can inspect.
/// </para>
/// </summary>
public sealed class BffTestHost : IAsyncDisposable
{
    public const string SessionHeader = "X-Test-Session";

    private readonly WebApplication _app;

    private BffTestHost(WebApplication app, FakeRedis redis, TestIdentityProvider idp)
    {
        _app = app;
        Redis = redis;
        Idp = idp;
        Sessions = app.Services.GetRequiredService<RedisTicketStore>();
    }

    public FakeRedis Redis { get; }

    public TestIdentityProvider Idp { get; }

    public RedisTicketStore Sessions { get; }

    public static async Task<BffTestHost> StartAsync()
    {
        var redis = new FakeRedis();
        var idp = new TestIdentityProvider();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();

        builder.Services.AddSingleton(TestSessions.CreateStore(redis));
        builder.Services.AddSingleton(new LogoutTokenValidator(idp.OptionsMonitor, redis.Connection, NullLogger<LogoutTokenValidator>.Instance));
        builder.Services
            .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddScheme<AuthenticationSchemeOptions, TestSessionHandler>(CookieAuthenticationDefaults.AuthenticationScheme, null)
            .AddScheme<AuthenticationSchemeOptions, TestIdentityProviderHandler>(OpenIdConnectDefaults.AuthenticationScheme, null);
        builder.Services.AddAuthorization();

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapAuthEndpoints("/api/keystone", appRoot: "/");
        app.MapAuthEndpoints("/api/keystone-admin", appRoot: "/admin/");
        app.MapSessionEndpoints("/api/keystone");
        app.MapBackChannelLogout();

        await app.StartAsync();
        return new BffTestHost(app, redis, idp);
    }

    /// <summary>A browser with no session.</summary>
    public HttpClient AnonymousBrowser() => _app.GetTestClient();

    /// <summary>A browser holding the session cookie for <paramref name="sessionId"/>.</summary>
    public HttpClient BrowserWithSession(string sessionId)
    {
        var client = _app.GetTestClient();
        client.DefaultRequestHeaders.Add(SessionHeader, sessionId);
        return client;
    }

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();

    /// <summary>Stands in for the cookie handler with a server-side session store.</summary>
    private sealed class TestSessionHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, RedisTicketStore sessions)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder), IAuthenticationSignOutHandler
    {
        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var sessionId = Request.Headers[SessionHeader].ToString();
            var ticket = string.IsNullOrEmpty(sessionId) ? null : await sessions.RetrieveAsync(sessionId);

            return ticket is null
                ? AuthenticateResult.NoResult()
                : AuthenticateResult.Success(new AuthenticationTicket(ticket.Principal, ticket.Properties, Scheme.Name));
        }

        public async Task SignOutAsync(AuthenticationProperties? properties)
        {
            // Like the real cookie handler: delete the session from the store and clear the cookie.
            var sessionId = Request.Headers[SessionHeader].ToString();
            if (!string.IsNullOrEmpty(sessionId))
            {
                await sessions.RemoveAsync(sessionId);
            }

            Response.Headers["X-Test-Cookie-Cleared"] = "1";
        }
    }

    /// <summary>Stands in for the OIDC handler: redirects to a fake identity provider with the parameters it would send.</summary>
    private sealed class TestIdentityProviderHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder), IAuthenticationSignOutHandler
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.NoResult());

        protected override Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            properties.Parameters.TryGetValue(OpenIdConnectParameterNames.Prompt, out var prompt);
            Response.Redirect(
                $"https://idp.test/authorize?redirect_uri={Uri.EscapeDataString(properties.RedirectUri ?? "")}&prompt={prompt}");
            return Task.CompletedTask;
        }

        public Task SignOutAsync(AuthenticationProperties? properties)
        {
            Response.Redirect($"https://idp.test/end_session?post_logout_redirect_uri={Uri.EscapeDataString(properties?.RedirectUri ?? "")}");
            return Task.CompletedTask;
        }
    }
}
