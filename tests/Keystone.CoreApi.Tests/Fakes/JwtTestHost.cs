using Keystone.CoreApi.Authorization;
using Keystone.CoreApi.Endpoints;
using Keystone.IdentityService.Endpoints;
using Keystone.IdentityService.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace Keystone.CoreApi.Tests.Fakes;

/// <summary>
/// Two in-memory services wired as in Aspire: the real Identity Service (key manager, token service,
/// discovery and JWKS endpoints) and the Core API's real JwtBearer setup and policies. The Core API's
/// "identity-service" HttpClient talks to the Identity Service's test server instead of the network.
/// </summary>
public sealed class JwtTestHost : IAsyncDisposable
{
    public const string Issuer = "https://identity.keystone.internal";

    private readonly WebApplication _identityService;
    private readonly WebApplication _coreApi;

    private JwtTestHost(WebApplication identityService, WebApplication coreApi, RequestCounter identityServiceCalls)
    {
        _identityService = identityService;
        _coreApi = coreApi;
        IdentityServiceCalls = identityServiceCalls;
    }

    /// <summary>The Identity Service's own token service: tokens exactly as the BFF would get them.</summary>
    public ITokenService Tokens => _identityService.Services.GetRequiredService<ITokenService>();

    /// <summary>The Identity Service's signing key, for hand-made tokens.</summary>
    public SecurityKey SigningKey => _identityService.Services.GetRequiredService<IKeyManager>().GetSigningKey();

    /// <summary>Requests the Core API made to the Identity Service (discovery and JWKS).</summary>
    public RequestCounter IdentityServiceCalls { get; }

    public static IConfiguration IdentityServiceConfiguration(string issuer = Issuer) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = issuer,
            ["Jwt:Audiences:0"] = "core-api",
            ["Jwt:Audiences:1"] = "billing-api", // another internal API, so tokens for it can be issued
            ["Jwt:DefaultRole"] = "User",
            ["Jwt:LifetimeMinutes"] = "15"
        }).Build();

    public static async Task<JwtTestHost> StartAsync()
    {
        // --- Identity Service ---
        var identityBuilder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        identityBuilder.WebHost.UseTestServer();
        identityBuilder.Logging.ClearProviders();
        identityBuilder.Configuration.AddConfiguration(IdentityServiceConfiguration());
        identityBuilder.Services.AddSingleton<IKeyManager, KeyManager>();
        identityBuilder.Services.AddSingleton<ITokenService, TokenService>();

        var identityService = identityBuilder.Build();
        identityService.MapWellKnownEndpoints();
        await identityService.StartAsync();

        // --- Core API ---
        var calls = new RequestCounter();
        var coreBuilder = WebApplication.CreateBuilder();
        coreBuilder.WebHost.UseTestServer();
        coreBuilder.Logging.ClearProviders();
        coreBuilder.Services.AddHttpClient(KeystoneJwtBearer.IdentityServiceClientName)
            .ConfigurePrimaryHttpMessageHandler(() => calls.Wrap(identityService.GetTestServer().CreateHandler()));
        coreBuilder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddKeystoneJwtBearer();
        coreBuilder.Services.AddAuthorizationBuilder().AddKeystonePolicies();

        var coreApi = coreBuilder.Build();
        coreApi.UseAuthentication();
        coreApi.UseAuthorization();
        coreApi.MapWhoAmIEndpoints();
        coreApi.MapGet("/test/admin-only", (HttpContext context) => Results.Ok(context.User.Identity?.Name))
            .RequireAuthorization("Admin");
        await coreApi.StartAsync();

        return new JwtTestHost(identityService, coreApi, calls);
    }

    /// <summary>A Core API client that sends <paramref name="token"/> as a Bearer token (none when null).</summary>
    public HttpClient CoreApiWith(string? token)
    {
        var client = _coreApi.GetTestClient();
        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }
        return client;
    }

    public async ValueTask DisposeAsync()
    {
        await _coreApi.DisposeAsync();
        await _identityService.DisposeAsync();
    }

    /// <summary>Counts requests by path.</summary>
    public sealed class RequestCounter
    {
        private readonly List<string> _paths = [];

        public int To(string path)
        {
            lock (_paths) { return _paths.Count(p => p == path); }
        }

        internal HttpMessageHandler Wrap(HttpMessageHandler inner) => new CountingHandler(this) { InnerHandler = inner };

        private sealed class CountingHandler(RequestCounter counter) : DelegatingHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                lock (counter._paths) { counter._paths.Add(request.RequestUri!.AbsolutePath); }
                return base.SendAsync(request, cancellationToken);
            }
        }
    }
}
