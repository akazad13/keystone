using System.Security.Claims;
using System.Text.Encodings.Web;
using FluentValidation;
using Keystone.CoreApi.Authorization;
using Keystone.CoreApi.Data;
using Keystone.CoreApi.Endpoints;
using Keystone.CoreApi.Services;
using Keystone.CoreApi.Validators;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Keystone.CoreApi.Tests.Fakes;

/// <summary>
/// The Core API's endpoints, policies and provisioning middleware on an in-memory test server, with
/// SQLite instead of SQL Server and an in-memory cache instead of Redis.
/// <para>
/// JWT validation is replaced by a test scheme that builds the caller from headers
/// (<c>X-Test-Sub</c>, <c>X-Test-Role</c>): what a validated Keystone access token would contain.
/// Token validation itself is configuration of the JwtBearer handler and is covered end to end.
/// </para>
/// </summary>
public sealed class CoreApiTestHost : IAsyncDisposable
{
    private readonly WebApplication _app;

    private CoreApiTestHost(WebApplication app, TestDatabase database)
    {
        _app = app;
        Database = database;
    }

    public TestDatabase Database { get; }

    public static async Task<CoreApiTestHost> StartAsync()
    {
        var database = new TestDatabase();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();

        builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite(database.Connection));
        builder.Services.AddDistributedMemoryCache();
        builder.Services.AddValidatorsFromAssemblyContaining<CreateProjectValidator>();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
        builder.Services.AddScoped<IProjectService, ProjectService>();
        builder.Services.AddAuthentication(TestTokenHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, TestTokenHandler>(TestTokenHandler.SchemeName, null);
        builder.Services.AddAuthorizationBuilder().AddKeystonePolicies();

        var app = builder.Build();
        app.UseAuthentication();
        app.UseUserProvisioning();
        app.UseAuthorization();
        app.MapProjectEndpoints();
        app.MapWhoAmIEndpoints();
        app.MapAdminEndpoints();

        await app.StartAsync();
        return new CoreApiTestHost(app, database);
    }

    /// <summary>A client calling without a token.</summary>
    public HttpClient Anonymous() => _app.GetTestClient();

    /// <summary>A client calling with a token for <paramref name="sub"/> in <paramref name="role"/>.</summary>
    public HttpClient As(string sub, string role = "User")
    {
        var client = _app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-Sub", sub);
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }

    public async ValueTask DisposeAsync()
    {
        await _app.DisposeAsync();
        Database.Dispose();
    }

    private sealed class TestTokenHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "Test";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var sub = Request.Headers["X-Test-Sub"].ToString();
            if (string.IsNullOrEmpty(sub))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            // Same claim names as a Keystone access token (MapInboundClaims = false; name/role claim types).
            var identity = new ClaimsIdentity(
            [
                new Claim("sub", sub),
                new Claim("email", $"{sub}@example.com"),
                new Claim("name", sub),
                new Claim("role", Request.Headers["X-Test-Role"].ToString())
            ], SchemeName, "name", "role");

            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }
}
