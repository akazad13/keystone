using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Keystone.IdentityService.Models;
using Keystone.IdentityService.Tests.Fakes;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Keystone.IdentityService.Tests.Endpoints;

/// <summary>POST /connect/token: RFC 8693 token exchange, as the BFF calls it.</summary>
public sealed class TokenExchangeEndpointTests : IAsyncLifetime
{
    private IdentityServiceTestHost _host = null!;

    public async ValueTask InitializeAsync() => _host = await IdentityServiceTestHost.StartAsync();

    public ValueTask DisposeAsync() => _host.DisposeAsync();

    /// <summary>The BFF's request, with any field overridden (null removes it).</summary>
    private Task<HttpResponseMessage> Exchange(params (string Name, string? Value)[] overrides)
    {
        var form = new Dictionary<string, string?>
        {
            ["grant_type"] = TokenExchangeRequest.GrantType,
            ["subject_token"] = FakeUserService.ValidToken,
            ["subject_token_type"] = TokenExchangeRequest.AccessTokenType,
            ["audience"] = "core-api"
        };
        foreach (var (name, value) in overrides)
        {
            form[name] = value;
        }

        var fields = form.Where(f => f.Value is not null).Select(f => new KeyValuePair<string, string>(f.Key, f.Value!));
        return _host.Client().PostAsync("/connect/token", new FormUrlEncodedContent(fields));
    }

    private static async Task<string> ErrorOf(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString()!;
    }

    [Fact]
    public async Task Exchanges_a_valid_identity_provider_token_for_a_keystone_token()
    {
        var response = await Exchange();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Bearer", body.GetProperty("token_type").GetString());
        Assert.Equal(TokenExchangeRequest.AccessTokenType, body.GetProperty("issued_token_type").GetString());
        Assert.Equal(900, body.GetProperty("expires_in").GetInt32());
        Assert.Equal("alice-sub", new JsonWebToken(body.GetProperty("access_token").GetString()).Subject);
    }

    [Fact]
    public async Task The_issued_token_validates_with_only_the_discovery_document_and_jwks()
    {
        // Exactly what the Core API does: read discovery, fetch the JWKS, validate offline.
        var client = _host.Client();
        var token = (await (await Exchange()).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("access_token").GetString();
        var discovery = await client.GetFromJsonAsync<JsonElement>("/.well-known/openid-configuration");
        var jwks = new JsonWebKeySet(await client.GetStringAsync(new Uri(discovery.GetProperty("jwks_uri").GetString()!).AbsolutePath));

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = discovery.GetProperty("issuer").GetString(),
            ValidAudience = "core-api",
            ValidTypes = ["at+jwt"],
            IssuerSigningKeys = jwks.GetSigningKeys()
        });

        Assert.True(result.IsValid, result.Exception?.Message);
    }

    [Fact]
    public async Task Rejects_a_request_that_is_not_a_form()
    {
        var response = await _host.Client().PostAsJsonAsync("/connect/token", new { grant_type = TokenExchangeRequest.GrantType });

        Assert.Equal("invalid_request", await ErrorOf(response));
    }

    [Theory]
    [InlineData("authorization_code")]
    [InlineData("client_credentials")]
    [InlineData(null)]
    public async Task Supports_only_the_token_exchange_grant(string? grantType)
    {
        Assert.Equal("unsupported_grant_type", await ErrorOf(await Exchange(("grant_type", grantType))));
    }

    [Theory]
    [InlineData("urn:ietf:params:oauth:token-type:id_token")]
    [InlineData("urn:ietf:params:oauth:token-type:refresh_token")]
    [InlineData(null)]
    public async Task Accepts_only_access_tokens_as_the_subject(string? subjectTokenType)
    {
        Assert.Equal("invalid_request", await ErrorOf(await Exchange(("subject_token_type", subjectTokenType))));
    }

    [Fact]
    public async Task Requires_a_subject_token()
    {
        Assert.Equal("invalid_request", await ErrorOf(await Exchange(("subject_token", null))));
    }

    [Fact]
    public async Task Rejects_an_unknown_audience_without_asking_the_identity_provider()
    {
        Assert.Equal("invalid_target", await ErrorOf(await Exchange(("audience", "billing-api"))));
        Assert.Equal(0, _host.Users.Calls);
    }

    [Fact]
    public async Task Rejects_a_subject_token_the_identity_provider_does_not_accept()
    {
        Assert.Equal("invalid_grant", await ErrorOf(await Exchange(("subject_token", "expired-or-forged"))));
    }
}
