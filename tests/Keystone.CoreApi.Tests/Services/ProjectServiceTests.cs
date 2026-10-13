using Keystone.CoreApi.Data.Entities;
using Keystone.CoreApi.Services;
using Keystone.CoreApi.Tests.Fakes;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Keystone.CoreApi.Tests.Services;

public sealed class ProjectServiceTests : IAsyncLifetime, IDisposable
{
    private readonly TestDatabase _database = new();
    private readonly IDistributedCache _cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
    private AppUser _alice = null!;
    private AppUser _bob = null!;

    public async ValueTask InitializeAsync()
    {
        _alice = await _database.AddUserAsync("alice");
        _bob = await _database.AddUserAsync("bob");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public void Dispose() => _database.Dispose();

    /// <summary>A service with its own DbContext, like one per HTTP request.</summary>
    private ProjectService NewService() => new(_database.CreateContext(), _cache, NullLogger<ProjectService>.Instance);

    [Fact]
    public async Task CreateAsync_stores_the_project_for_the_owner()
    {
        var created = await NewService().CreateAsync(_alice.Id, "Website", "Relaunch", new DateOnly(2026, 12, 1));

        var loaded = await NewService().GetByIdAsync(created.Id, _alice.Id);

        Assert.NotNull(loaded);
        Assert.Equal("Website", loaded.Name);
        Assert.Equal("Relaunch", loaded.Description);
        Assert.Equal("Active", loaded.Status);
        Assert.Equal(new DateOnly(2026, 12, 1), loaded.DueDate);
    }

    [Fact]
    public async Task GetForOwnerAsync_returns_only_the_owners_projects_newest_first()
    {
        await using (var db = _database.CreateContext())
        {
            db.Projects.AddRange(
                new Project { Id = Guid.NewGuid(), Name = "Old", OwnerId = _alice.Id, CreatedAt = DateTime.UtcNow.AddDays(-2) },
                new Project { Id = Guid.NewGuid(), Name = "New", OwnerId = _alice.Id, CreatedAt = DateTime.UtcNow },
                new Project { Id = Guid.NewGuid(), Name = "Bob's", OwnerId = _bob.Id, CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        var projects = await NewService().GetForOwnerAsync(_alice.Id);

        Assert.Equal(["New", "Old"], projects.Select(p => p.Name));
    }

    [Fact]
    public async Task Another_owners_project_behaves_as_not_found()
    {
        var alices = await NewService().CreateAsync(_alice.Id, "Secret plan", null, null);

        Assert.Null(await NewService().GetByIdAsync(alices.Id, _bob.Id));
        Assert.Null(await NewService().UpdateAsync(alices.Id, _bob.Id, "Hijacked", null, "Archived", null));
        Assert.False(await NewService().DeleteAsync(alices.Id, _bob.Id));

        var unchanged = await NewService().GetByIdAsync(alices.Id, _alice.Id);
        Assert.Equal("Secret plan", unchanged?.Name);
        Assert.Equal("Active", unchanged?.Status);
    }

    [Fact]
    public async Task UpdateAsync_changes_the_fields_and_sets_UpdatedAt()
    {
        var project = await NewService().CreateAsync(_alice.Id, "Draft", null, null);

        var updated = await NewService().UpdateAsync(project.Id, _alice.Id, "Final", "Done", "Completed", new DateOnly(2027, 1, 31));

        Assert.NotNull(updated);
        Assert.Equal("Final", updated.Name);
        Assert.Equal("Completed", updated.Status);
        Assert.Equal(new DateOnly(2027, 1, 31), updated.DueDate);
        Assert.NotNull(updated.UpdatedAt);
    }

    [Fact]
    public async Task DeleteAsync_removes_the_project()
    {
        var project = await NewService().CreateAsync(_alice.Id, "Temporary", null, null);

        Assert.True(await NewService().DeleteAsync(project.Id, _alice.Id));
        Assert.Null(await NewService().GetByIdAsync(project.Id, _alice.Id));
    }

    [Fact]
    public async Task The_list_is_served_from_the_cache_until_the_owner_changes_something()
    {
        await NewService().CreateAsync(_alice.Id, "First", null, null);
        await NewService().GetForOwnerAsync(_alice.Id); // fills the cache

        // A change that bypasses the service (and so the cache invalidation) is not visible yet...
        await using (var db = _database.CreateContext())
        {
            db.Projects.Add(new Project { Id = Guid.NewGuid(), Name = "Behind the cache", OwnerId = _alice.Id });
            await db.SaveChangesAsync();
        }
        Assert.Single(await NewService().GetForOwnerAsync(_alice.Id));

        // ...until a change through the service invalidates the owner's entry.
        await NewService().CreateAsync(_alice.Id, "Second", null, null);
        Assert.Equal(3, (await NewService().GetForOwnerAsync(_alice.Id)).Count);
    }

    [Fact]
    public async Task The_cache_is_per_owner()
    {
        await NewService().CreateAsync(_alice.Id, "Alice's", null, null);
        await NewService().GetForOwnerAsync(_alice.Id); // cache Alice's list

        var bobs = await NewService().GetForOwnerAsync(_bob.Id);

        Assert.Empty(bobs);
    }

    [Fact]
    public async Task Timestamps_come_back_as_UTC()
    {
        var project = await NewService().CreateAsync(_alice.Id, "Clock", null, null);

        var loaded = await NewService().GetByIdAsync(project.Id, _alice.Id);

        Assert.Equal(DateTimeKind.Utc, loaded!.CreatedAt.Kind);
    }
}
