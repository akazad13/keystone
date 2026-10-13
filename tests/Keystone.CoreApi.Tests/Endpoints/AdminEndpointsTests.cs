using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Keystone.CoreApi.Endpoints;
using Keystone.CoreApi.Tests.Fakes;
using Microsoft.EntityFrameworkCore;

namespace Keystone.CoreApi.Tests.Endpoints;

public sealed class AdminEndpointsTests : IAsyncLifetime
{
    private CoreApiTestHost _host = null!;

    public async ValueTask InitializeAsync() => _host = await CoreApiTestHost.StartAsync();

    public ValueTask DisposeAsync() => _host.DisposeAsync();

    [Theory]
    [InlineData("/api/admin/users")]
    [InlineData("/api/admin/stats")]
    public async Task Users_without_the_Admin_role_are_forbidden(string url)
    {
        var response = await _host.As("bob", role: "User").GetAsync(url);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_forbidden_call_still_records_the_user_as_seen()
    {
        // Provisioning runs before authorization, so the admin list knows about users who only hit a 403.
        await _host.As("bob", role: "User").GetAsync("/api/admin/users");

        await using var db = _host.Database.CreateContext();
        Assert.True(await db.Users.AnyAsync(u => u.ExternalId == "bob"));
    }

    [Fact]
    public async Task Admins_see_every_user_with_their_project_count()
    {
        var bob = _host.As("bob");
        await bob.PostAsJsonAsync("/api/projects", new { name = "One" });
        await bob.PostAsJsonAsync("/api/projects", new { name = "Two" });

        var users = await _host.As("alice", role: "Admin").GetFromJsonAsync<AdminUserDto[]>("/api/admin/users");

        Assert.Equal(2, users!.Length);
        Assert.Equal(2, users.Single(u => u.DisplayName == "bob").ProjectCount);
        Assert.Equal("Admin", users.Single(u => u.DisplayName == "alice").Role);
    }

    [Fact]
    public async Task Stats_count_users_admins_and_projects()
    {
        var bob = _host.As("bob");
        await bob.PostAsJsonAsync("/api/projects", new { name = "Active one" });
        var done = await (await bob.PostAsJsonAsync("/api/projects", new { name = "Done" })).Content.ReadFromJsonAsync<JsonElement>();
        await bob.PutAsJsonAsync($"/api/projects/{done.GetProperty("id").GetGuid()}", new { name = "Done", status = "Completed" });

        var stats = await _host.As("alice", role: "Admin").GetFromJsonAsync<AdminStatsDto>("/api/admin/stats");

        Assert.Equal(new AdminStatsDto(TotalUsers: 2, ActiveUsers: 2, NewUsersToday: 2, Admins: 1, TotalProjects: 2, ActiveProjects: 1), stats);
    }
}
