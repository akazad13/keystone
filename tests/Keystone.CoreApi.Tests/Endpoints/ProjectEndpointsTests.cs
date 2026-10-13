using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Keystone.CoreApi.Services;
using Keystone.CoreApi.Tests.Fakes;

namespace Keystone.CoreApi.Tests.Endpoints;

public sealed class ProjectEndpointsTests : IAsyncLifetime
{
    private CoreApiTestHost _host = null!;

    public async ValueTask InitializeAsync() => _host = await CoreApiTestHost.StartAsync();

    public ValueTask DisposeAsync() => _host.DisposeAsync();

    private static async Task<ProjectDto> CreateProject(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/projects", new { name });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProjectDto>())!;
    }

    [Fact]
    public async Task Requires_a_token()
    {
        var response = await _host.Anonymous().GetAsync("/api/projects");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Requires_a_keystone_role()
    {
        var response = await _host.As("guest", role: "Guest").GetAsync("/api/projects");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Create_returns_201_with_the_location_and_the_project()
    {
        var response = await _host.As("alice").PostAsJsonAsync("/api/projects", new { name = "Website", dueDate = "2026-12-01" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var project = await response.Content.ReadFromJsonAsync<ProjectDto>();
        Assert.Equal($"/api/projects/{project!.Id}", response.Headers.Location?.ToString());
        Assert.Equal(new DateOnly(2026, 12, 1), project.DueDate);
    }

    [Fact]
    public async Task Create_rejects_an_invalid_project_with_a_validation_problem()
    {
        var response = await _host.As("alice").PostAsJsonAsync("/api/projects", new { name = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("errors").TryGetProperty("Name", out _));
    }

    [Fact]
    public async Task The_owner_always_comes_from_the_token_never_from_the_body()
    {
        var bob = await _host.As("bob").GetAsync("/api/projects"); // provisions bob
        bob.EnsureSuccessStatusCode();

        await _host.As("alice").PostAsJsonAsync("/api/projects", new { name = "Planted", ownerId = Guid.NewGuid(), owner = "bob" });

        Assert.Empty((await _host.As("bob").GetFromJsonAsync<ProjectDto[]>("/api/projects"))!);
        Assert.Single((await _host.As("alice").GetFromJsonAsync<ProjectDto[]>("/api/projects"))!);
    }

    [Fact]
    public async Task Another_users_project_is_404_for_every_operation()
    {
        var alices = await CreateProject(_host.As("alice"), "Alice's");
        var bob = _host.As("bob");

        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/projects/{alices.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await bob.PutAsJsonAsync($"/api/projects/{alices.Id}", new { name = "Mine now", status = "Active" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.DeleteAsync($"/api/projects/{alices.Id}")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await _host.As("alice").GetAsync($"/api/projects/{alices.Id}")).StatusCode);
    }

    [Fact]
    public async Task Update_changes_the_project()
    {
        var alice = _host.As("alice");
        var project = await CreateProject(alice, "Draft");

        var response = await alice.PutAsJsonAsync($"/api/projects/{project.Id}", new { name = "Final", status = "Completed" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Completed", (await response.Content.ReadFromJsonAsync<ProjectDto>())!.Status);
    }

    [Fact]
    public async Task Update_rejects_an_unknown_status()
    {
        var alice = _host.As("alice");
        var project = await CreateProject(alice, "Draft");

        var response = await alice.PutAsJsonAsync($"/api/projects/{project.Id}", new { name = "Draft", status = "Deleted" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Delete_returns_204_and_the_project_is_gone()
    {
        var alice = _host.As("alice");
        var project = await CreateProject(alice, "Temporary");

        Assert.Equal(HttpStatusCode.NoContent, (await alice.DeleteAsync($"/api/projects/{project.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync($"/api/projects/{project.Id}")).StatusCode);
    }
}
