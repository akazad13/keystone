using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Keystone.CoreApi.Tests.Fakes;
using Keystone.IdentityService.Models;
using Keystone.IdentityService.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Keystone.CoreApi.Tests.Authorization;

/// <summary>
/// The Core API's access token validation against the real Identity Service: what it accepts,
/// what it rejects, and how the claims are read.
/// </summary>
public sealed class KeystoneJwtBearerTests : IAsyncLifetime
{
    private JwtTestHost _host = null!;

    private static readonly UserInfo Alice = new() { Sub = "alice-sub", Name = "Alice", Roles = ["Admin"] };
    private static readonly UserInfo Bob = new() { Sub = "bob-sub", Name = "Bob" };

    public async ValueTask InitializeAsync() => _host = await JwtTestHost.StartAsync();

    public ValueTask DisposeAsync() => _host.DisposeAsync();

    private string TokenFor(UserInfo user, string audience = "core-api") => _host.Tokens.IssueAccessToken(user, audience).AccessToken;

    /// <summary>A token like the Identity Service's, signed with its key, with one thing changed.</summary>
    private string HandMadeToken(Action<SecurityTokenDescriptor> change)
    {
        var now = DateTime.UtcNow;
        var descriptor = new SecurityTokenDescriptor
        {
            TokenType = "at+jwt",
            Issuer = JwtTestHost.Issuer,
            Audience = "core-api",
            IssuedAt = now,
            NotBefore = now,
            Expires = now.AddMinutes(15),
            Claims = new Dictionary<string, object> { ["sub"] = "alice-sub", ["role"] = new[] { "Admin", "User" } },
            SigningCredentials = new SigningCredentials(_host.SigningKey, SecurityAlgorithms.RsaSha256)
        };
        change(descriptor);
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    private async Task<HttpStatusCode> WhoAmIStatus(string? token) =>
        (await _host.CoreApiWith(token).GetAsync("/api/whoami")).StatusCode;

    [Fact]
    public async Task Accepts_a_token_from_the_identity_service()
    {
        Assert.Equal(HttpStatusCode.OK, await WhoAmIStatus(TokenFor(Alice)));
    }

    [Fact]
    public async Task Hand_made_baseline_token_is_accepted()
    {
        // Guards the negative tests below: each changes exactly one thing from this valid token.
        Assert.Equal(HttpStatusCode.OK, await WhoAmIStatus(HandMadeToken(_ => { })));
    }

    [Fact]
    public async Task Claims_keep_their_jwt_names()
    {
        var body = await _host.CoreApiWith(TokenFor(Alice)).GetFromJsonAsync<JsonElement>("/api/whoami");

        var types = body.GetProperty("claims").EnumerateArray().Select(c => c.GetProperty("type").GetString()).ToHashSet();
        Assert.Contains("sub", types);
        Assert.Contains("role", types);
        Assert.DoesNotContain("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier", types);
    }

    [Fact]
    public async Task Roles_and_name_come_from_the_role_and_name_claims()
    {
        var response = await _host.CoreApiWith(TokenFor(Alice)).GetAsync("/test/admin-only");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Alice", await response.Content.ReadFromJsonAsync<string>());
    }

    [Fact]
    public async Task A_user_without_the_Admin_role_is_forbidden()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await _host.CoreApiWith(TokenFor(Bob)).GetAsync("/test/admin-only")).StatusCode);
    }

    [Fact]
    public async Task No_token_is_401_with_a_bearer_challenge()
    {
        var response = await _host.CoreApiWith(null).GetAsync("/api/whoami");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Bearer", response.Headers.WwwAuthenticate.Single().Scheme);
    }

    [Fact]
    public async Task Rejects_a_token_for_another_internal_api()
    {
        // Valid, signed by the Identity Service, but issued for "billing-api".
        Assert.Equal(HttpStatusCode.Unauthorized, await WhoAmIStatus(TokenFor(Alice, audience: "billing-api")));
    }

    [Fact]
    public async Task Rejects_a_token_signed_by_another_key()
    {
        using var otherKey = new KeyManager(
            JwtTestHost.IdentityServiceConfiguration(), new TestEnvironment(), NullLogger<KeyManager>.Instance);
        var forged = new TokenService(otherKey, JwtTestHost.IdentityServiceConfiguration()).IssueAccessToken(Alice, "core-api");

        Assert.Equal(HttpStatusCode.Unauthorized, await WhoAmIStatus(forged.AccessToken));
    }

    [Fact]
    public async Task Rejects_a_token_from_another_issuer()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, await WhoAmIStatus(HandMadeToken(d => d.Issuer = "https://evil.example")));
    }

    [Theory]
    [InlineData("JWT")]
    [InlineData("logout+jwt")]
    public async Task Rejects_other_kinds_of_jwt_signed_with_the_same_key(string type)
    {
        // Only access tokens (RFC 9068 "at+jwt") are accepted, not any JWT the key happens to have signed.
        Assert.Equal(HttpStatusCode.Unauthorized, await WhoAmIStatus(HandMadeToken(d => d.TokenType = type)));
    }

    [Fact]
    public async Task Rejects_an_expired_token()
    {
        var token = HandMadeToken(d =>
        {
            var expired = DateTime.UtcNow.AddMinutes(-10); // beyond the default 5 minute clock skew
            d.IssuedAt = d.NotBefore = expired.AddMinutes(-15);
            d.Expires = expired;
        });

        Assert.Equal(HttpStatusCode.Unauthorized, await WhoAmIStatus(token));
    }

    [Fact]
    public async Task Rejects_an_unsigned_token()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, await WhoAmIStatus(HandMadeToken(d => d.SigningCredentials = null)));
    }

    [Fact]
    public async Task Rejects_garbage()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, await WhoAmIStatus("not-a-jwt"));
    }

    [Fact]
    public async Task Fetches_discovery_and_keys_once_then_validates_offline()
    {
        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(HttpStatusCode.OK, await WhoAmIStatus(TokenFor(Alice)));
        }

        Assert.Equal(1, _host.IdentityServiceCalls.To("/.well-known/openid-configuration"));
        Assert.Equal(1, _host.IdentityServiceCalls.To("/.well-known/jwks.json"));
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "test";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
