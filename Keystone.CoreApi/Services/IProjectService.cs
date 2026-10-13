namespace Keystone.CoreApi.Services;

/// <summary>What the API returns for a project. Never the EF entity, so internals like OwnerId stay private.</summary>
public record ProjectDto(
    Guid Id,
    string Name,
    string? Description,
    string Status,
    DateOnly? DueDate,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

/// <summary>
/// Projects of one owner. Every method takes the owner's id, so a caller can only ever
/// see or change their own projects; asking for someone else's project behaves as "not found".
/// </summary>
public interface IProjectService
{
    Task<IReadOnlyList<ProjectDto>> GetForOwnerAsync(Guid ownerId, CancellationToken ct = default);

    Task<ProjectDto?> GetByIdAsync(Guid id, Guid ownerId, CancellationToken ct = default);

    Task<ProjectDto> CreateAsync(Guid ownerId, string name, string? description, DateOnly? dueDate, CancellationToken ct = default);

    Task<ProjectDto?> UpdateAsync(Guid id, Guid ownerId, string name, string? description, string status, DateOnly? dueDate, CancellationToken ct = default);

    Task<bool> DeleteAsync(Guid id, Guid ownerId, CancellationToken ct = default);
}
