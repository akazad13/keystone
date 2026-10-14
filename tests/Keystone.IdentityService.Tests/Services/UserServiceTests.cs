using System.Net;
using Keystone.IdentityService.Services;
using Keystone.IdentityService.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Keystone.IdentityService.Tests.Services;

/// <summary>Asking Zitadel's userinfo endpoint who owns an access token, and mapping Zitadel's role claims.</summary>
public sealed class UserServiceTests
{
    private const string UserInfoEndpoint = "https://idp.test/oidc/v1/userinfo";

    private readonly StubHttpHandler _idp = new();
    private readonly UserService _service;

    public UserServiceTests()
    {
        var discovery = new StaticConfigurationManager<OpenIdConnectConfiguration>(
            new OpenIdConnectConfiguration { UserInfoEndpoint = UserInfoEndpoint });

        _service = new UserService(new StubHttpClientFactory(_idp), discovery, NullLogger<UserService>.Instance);
    }

    [Fact]
    public async Task Calls_the_discovered_userinfo_endpoint_with_the_access_token()
    {
        _idp.ResponseBody = """{"sub":"alice-sub"}""";

        await _service.GetUserInfoAsync("zitadel-access-token", CancellationToken.None);

        var request = Assert.Single(_idp.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(UserInfoEndpoint, request.RequestUri?.ToString());
        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
        Assert.Equal("zitadel-access-token", request.Headers.Authorization?.Parameter);
    }

    [Fact]
    public async Task Maps_the_standard_claims()
    {
        _idp.ResponseBody = """
            {"sub":"alice-sub","name":"Alice","email":"alice@example.com","email_verified":true,"preferred_username":"alice"}
            """;

        var user = await _service.GetUserInfoAsync("token", CancellationToken.None);

        Assert.NotNull(user);
        Assert.Equal("alice-sub", user.Sub);
        Assert.Equal("Alice", user.Name);
        Assert.Equal("alice@example.com", user.Email);
        Assert.True(user.EmailVerified);
        Assert.Equal("alice", user.PreferredUsername);
        Assert.Empty(user.Roles);
    }

    [Fact]
    public async Task Missing_claims_stay_empty()
    {
        _idp.ResponseBody = """{"sub":"minimal","email_verified":"true"}""";

        var user = await _service.GetUserInfoAsync("token", CancellationToken.None);

        Assert.Null(user!.Name);
        Assert.Null(user.Email);
        Assert.False(user.EmailVerified); // only a JSON true counts
    }

    [Fact]
    public async Task Reads_roles_from_the_zitadel_project_roles_claim()
    {
        _idp.ResponseBody = """
            {
              "sub": "alice-sub",
              "urn:zitadel:iam:org:project:roles": {
                "Admin": { "123": "keystone.localhost" },
                "User": { "123": "keystone.localhost" }
              }
            }
            """;

        var user = await _service.GetUserInfoAsync("token", CancellationToken.None);

        Assert.Equal(["Admin", "User"], user!.Roles.Order());
    }

    [Fact]
    public async Task Merges_project_scoped_role_claims_without_duplicates()
    {
        _idp.ResponseBody = """
            {
              "sub": "alice-sub",
              "urn:zitadel:iam:org:project:roles": { "Admin": { "1": "org" } },
              "urn:zitadel:iam:org:project:345678:roles": { "Admin": { "1": "org" }, "Auditor": { "1": "org" } }
            }
            """;

        var user = await _service.GetUserInfoAsync("token", CancellationToken.None);

        Assert.Equal(["Admin", "Auditor"], user!.Roles.Order());
    }

    [Fact]
    public async Task Ignores_other_zitadel_claims()
    {
        _idp.ResponseBody = """
            {
              "sub": "alice-sub",
              "urn:zitadel:iam:org:id": "123",
              "urn:zitadel:iam:org:project:roles": "not-an-object",
              "urn:zitadel:iam:user:metadata": { "Admin": "sneaky" }
            }
            """;

        var user = await _service.GetUserInfoAsync("token", CancellationToken.None);

        Assert.Empty(user!.Roles);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Returns_null_when_the_provider_rejects_the_token(HttpStatusCode status)
    {
        _idp.StatusCode = status;

        Assert.Null(await _service.GetUserInfoAsync("expired-or-forged", CancellationToken.None));
    }

    [Fact]
    public async Task Provider_failures_are_errors_not_rejections()
    {
        // A 500 must not look like "invalid token": that would hide an outage behind a 400 to the BFF.
        _idp.StatusCode = HttpStatusCode.InternalServerError;

        await Assert.ThrowsAsync<HttpRequestException>(() => _service.GetUserInfoAsync("token", CancellationToken.None));
    }
}
