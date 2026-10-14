using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Keystone.IdentityService.Models;
using Keystone.IdentityService.Tests.Fakes;

namespace Keystone.IdentityService.Tests.Endpoints;

public sealed class DiscoveryAndIntrospectionTests : IAsyncLifetime
{
    private IdentityServiceTestHost _host = null!;

    public async ValueTask InitializeAsync() => _host = await IdentityServiceTestHost.StartAsync();

    public ValueTask DisposeAsync() => _host.DisposeAsync();

    private async Task<string> IssueToken()
    {
        var response = await _host.Client().PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = TokenExchangeRequest.GrantType,
            ["subject_token"] = FakeUserService.ValidToken,
            ["subject_token_type"] = TokenExchangeRequest.AccessTokenType,
            ["audience"] = "core-api"
        }));
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("access_token").GetString()!;
    }

    private async Task<JsonElement> Introspect(string token)
    {
        var response = await _host.Client().PostAsync("/internal/introspect",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = token }));
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task Discovery_names_the_issuer_and_builds_urls_from_the_request()
    {
        var discovery = await _host.Client().GetFromJsonAsync<JsonElement>("/.well-known/openid-configuration");

        Assert.Equal(TestSetup.Issuer, discovery.GetProperty("issuer").GetString());
        Assert.Equal("http://localhost/.well-known/jwks.json", discovery.GetProperty("jwks_uri").GetString());
        Assert.Equal("http://localhost/connect/token", discovery.GetProperty("token_endpoint").GetString());
        Assert.Equal("http://localhost/internal/introspect", discovery.GetProperty("introspection_endpoint").GetString());
        Assert.Equal(TokenExchangeRequest.GrantType, discovery.GetProperty("grant_types_supported")[0].GetString());
    }

    [Fact]
    public async Task Jwks_contains_only_public_key_material()
    {
        var jwks = await _host.Client().GetFromJsonAsync<JsonElement>("/.well-known/jwks.json");

        var key = Assert.Single(jwks.GetProperty("keys").EnumerateArray());
        Assert.Equal(["alg", "e", "kid", "kty", "n", "use"], key.EnumerateObject().Select(p => p.Name).Order());
    }

    [Fact]
    public async Task Introspection_shows_an_active_token_and_its_claims()
    {
        var result = await Introspect(await IssueToken());

        Assert.True(result.GetProperty("active").GetBoolean());
        Assert.Equal("alice-sub", result.GetProperty("sub").GetString());
        Assert.Equal("core-api", result.GetProperty("aud").GetString());
    }

    [Fact]
    public async Task Introspection_reports_an_invalid_token_as_inactive_and_nothing_else()
    {
        var result = await Introspect("not-a-token");

        Assert.Equal(["active"], result.EnumerateObject().Select(p => p.Name));
        Assert.False(result.GetProperty("active").GetBoolean());
    }

    [Fact]
    public async Task Introspection_requires_a_form()
    {
        var response = await _host.Client().PostAsJsonAsync("/internal/introspect", new { token = "x" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
