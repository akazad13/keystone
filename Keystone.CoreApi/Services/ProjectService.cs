using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Keystone.CoreApi.Data;
using Keystone.CoreApi.Data.Entities;

namespace Keystone.CoreApi.Services;

public class ProjectService : IProjectService
{
    private readonly AppDbContext _db;
    private readonly IDistributedCache _cache;
    private readonly ILogger<ProjectService> _logger;

    private const string AllProjectsCacheKey = "projects:all";
    private static string ProjectCacheKey(Guid id) => $"projects:{id}";

    private static readonly DistributedCacheEntryOptions CacheOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5),
        SlidingExpiration = TimeSpan.FromMinutes(2)
    };

    public ProjectService(AppDbContext db, IDistributedCache cache, ILogger<ProjectService> logger)
    {
        _db = db;
        _cache = cache;
        _logger = logger;
    }

    public async Task<IReadOnlyList<Project>> GetAllAsync(CancellationToken ct = default)
    {
        var cached = await _cache.GetStringAsync(AllProjectsCacheKey, ct);
        if (cached is not null)
        {
            _logger.LogDebug("Cache hit for all projects");
            return JsonSerializer.Deserialize<List<Project>>(cached) ?? [];
        }

        var projects = await _db.Projects
            .AsNoTracking()
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(ct);

        await _cache.SetStringAsync(
            AllProjectsCacheKey,
            JsonSerializer.Serialize(projects),
            CacheOptions,
            ct);

        return projects;
    }

    public async Task<Project?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var cacheKey = ProjectCacheKey(id);
        var cached = await _cache.GetStringAsync(cacheKey, ct);
        if (cached is not null)
        {
            _logger.LogDebug("Cache hit for project {ProjectId}", id);
            return JsonSerializer.Deserialize<Project>(cached);
        }

        var project = await _db.Projects
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, ct);

        if (project is not null)
        {
            await _cache.SetStringAsync(
                cacheKey,
                JsonSerializer.Serialize(project),
                CacheOptions,
                ct);
        }

        return project;
    }

    public async Task<Project> CreateAsync(string name, string? description, Guid ownerId, CancellationToken ct = default)
    {
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = description,
            OwnerId = ownerId,
            CreatedAt = DateTime.UtcNow
        };

        _db.Projects.Add(project);
        await _db.SaveChangesAsync(ct);

        await InvalidateCacheAsync(ct);

        _logger.LogInformation("Created project {ProjectId} with name {Name}", project.Id, project.Name);
        return project;
    }

    public async Task<Project?> UpdateAsync(Guid id, string name, string? description, string status, CancellationToken ct = default)
    {
        var project = await _db.Projects.FindAsync([id], ct);
        if (project is null)
            return null;

        project.Name = name;
        project.Description = description;
        project.Status = status;
        project.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        await InvalidateCacheAsync(ct);

        _logger.LogInformation("Updated project {ProjectId}", id);
        return project;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var project = await _db.Projects.FindAsync([id], ct);
        if (project is null)
            return false;

        _db.Projects.Remove(project);
        await _db.SaveChangesAsync(ct);
        await InvalidateCacheAsync(ct);

        _logger.LogInformation("Deleted project {ProjectId}", id);
        return true;
    }

    private async Task InvalidateCacheAsync(CancellationToken ct)
    {
        await _cache.RemoveAsync(AllProjectsCacheKey, ct);
    }
}
