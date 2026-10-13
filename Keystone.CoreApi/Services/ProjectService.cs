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

    // One cache entry per owner: a shared key would serve one user's projects to another.
    private static string OwnerProjectsCacheKey(Guid ownerId) => $"projects:owner:{ownerId}";

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

    public async Task<IReadOnlyList<ProjectDto>> GetForOwnerAsync(Guid ownerId, CancellationToken ct = default)
    {
        var cacheKey = OwnerProjectsCacheKey(ownerId);
        var cached = await _cache.GetStringAsync(cacheKey, ct);
        if (cached is not null)
        {
            _logger.LogDebug("Cache hit for projects of owner {OwnerId}", ownerId);
            return JsonSerializer.Deserialize<List<ProjectDto>>(cached) ?? [];
        }

        var projects = await _db.Projects
            .AsNoTracking()
            .Where(p => p.OwnerId == ownerId)
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new ProjectDto(p.Id, p.Name, p.Description, p.Status, p.DueDate, p.CreatedAt, p.UpdatedAt))
            .ToListAsync(ct);

        await _cache.SetStringAsync(cacheKey, JsonSerializer.Serialize(projects), CacheOptions, ct);

        return projects;
    }

    public async Task<ProjectDto?> GetByIdAsync(Guid id, Guid ownerId, CancellationToken ct = default)
    {
        var project = await _db.Projects
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id && p.OwnerId == ownerId, ct);

        return project is null ? null : ToDto(project);
    }

    public async Task<ProjectDto> CreateAsync(Guid ownerId, string name, string? description, DateOnly? dueDate, CancellationToken ct = default)
    {
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = description,
            DueDate = dueDate,
            OwnerId = ownerId,
            CreatedAt = DateTime.UtcNow
        };

        _db.Projects.Add(project);
        await _db.SaveChangesAsync(ct);
        await InvalidateCacheAsync(ownerId, ct);

        _logger.LogInformation("Created project {ProjectId} with name {Name} for owner {OwnerId}", project.Id, project.Name, ownerId);
        return ToDto(project);
    }

    public async Task<ProjectDto?> UpdateAsync(Guid id, Guid ownerId, string name, string? description, string status, DateOnly? dueDate, CancellationToken ct = default)
    {
        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == id && p.OwnerId == ownerId, ct);
        if (project is null)
            return null;

        project.Name = name;
        project.Description = description;
        project.Status = status;
        project.DueDate = dueDate;
        project.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        await InvalidateCacheAsync(ownerId, ct);

        _logger.LogInformation("Updated project {ProjectId}", id);
        return ToDto(project);
    }

    public async Task<bool> DeleteAsync(Guid id, Guid ownerId, CancellationToken ct = default)
    {
        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == id && p.OwnerId == ownerId, ct);
        if (project is null)
            return false;

        _db.Projects.Remove(project);
        await _db.SaveChangesAsync(ct);
        await InvalidateCacheAsync(ownerId, ct);

        _logger.LogInformation("Deleted project {ProjectId}", id);
        return true;
    }

    private Task InvalidateCacheAsync(Guid ownerId, CancellationToken ct) =>
        _cache.RemoveAsync(OwnerProjectsCacheKey(ownerId), ct);

    private static ProjectDto ToDto(Project p) =>
        new(p.Id, p.Name, p.Description, p.Status, p.DueDate, p.CreatedAt, p.UpdatedAt);
}
