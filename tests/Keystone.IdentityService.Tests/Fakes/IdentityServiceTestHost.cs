using Keystone.IdentityService.Endpoints;
using Keystone.IdentityService.Models;
using Keystone.IdentityService.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Keystone.IdentityService.Tests.Fakes;

/// <summary>
/// The Identity Service's endpoints with the real key manager and token service on an in-memory test
/// server. Only the call to Zitadel's userinfo endpoint is replaced, by <see cref="FakeUserService"/>.
/// </summary>
public sealed class IdentityServiceTestHost : IAsyncDisposable
{
    private readonly WebApplication _app;

    private IdentityServiceTestHost(WebApplication app, FakeUserService users)
    {
        _app = app;
        Users = users;
    }

    public FakeUserService Users { get; }

    public static async Task<IdentityServiceTestHost> StartAsync()
    {
        var users = new FakeUserService();

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Configuration.AddConfiguration(TestSetup.Configuration());

        builder.Services.AddSingleton<IKeyManager, KeyManager>();
        builder.Services.AddSingleton<ITokenService, TokenService>();
        builder.Services.AddSingleton<IUserService>(users);

        var app = builder.Build();
        app.MapAuthEndpoints();
        app.MapWellKnownEndpoints();
        app.MapInternalEndpoints();

        await app.StartAsync();
        return new IdentityServiceTestHost(app, users);
    }

    public HttpClient Client() => _app.GetTestClient();

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();
}

/// <summary>Plays Zitadel's userinfo endpoint: knows one valid access token.</summary>
public sealed class FakeUserService : IUserService
{
    public const string ValidToken = "valid-zitadel-token";

    public int Calls { get; private set; }

    public Task<UserInfo?> GetUserInfoAsync(string accessToken, CancellationToken cancellationToken)
    {
        Calls++;
        return Task.FromResult(accessToken == ValidToken
            ? new UserInfo { Sub = "alice-sub", Name = "Alice", Email = "alice@example.com", Roles = ["Admin"] }
            : null);
    }
}
