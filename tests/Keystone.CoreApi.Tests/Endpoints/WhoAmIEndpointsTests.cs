using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Keystone.CoreApi.Tests.Fakes;

namespace Keystone.CoreApi.Tests.Endpoints;

public sealed class WhoAmIEndpointsTests : IAsyncLifetime
{
    private CoreApiTestHost _host = null!;

    public async ValueTask InitializeAsync() => _host = await CoreApiTestHost.StartAsync();

    public ValueTask DisposeAsync() => _host.DisposeAsync();

    [Fact]
    public async Task Requires_a_token()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _host.Anonymous().GetAsync("/api/whoami")).StatusCode);
    }

    [Fact]
    public async Task Returns_the_claims_of_the_access_token()
    {
        var body = await _host.As("alice", role: "Admin").GetFromJsonAsync<JsonElement>("/api/whoami");

        var claims = body.GetProperty("claims").EnumerateArray()
            .ToDictionary(c => c.GetProperty("type").GetString()!, c => c.GetProperty("value").GetString());
        Assert.Equal("alice", claims["sub"]);
        Assert.Equal("Admin", claims["role"]);
    }
}
