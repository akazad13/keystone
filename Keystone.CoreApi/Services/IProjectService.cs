using Keystone.CoreApi.Data.Entities;

namespace Keystone.CoreApi.Services;

public interface IProjectService
{
    Task<IReadOnlyList<Project>> GetAllAsync(CancellationToken ct = default);

    Task<Project?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<Project> CreateAsync(string name, string? description, Guid ownerId, CancellationToken ct = default);

    Task<Project?> UpdateAsync(Guid id, string name, string? description, string status, CancellationToken ct = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}
