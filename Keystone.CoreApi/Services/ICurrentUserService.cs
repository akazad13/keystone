using Keystone.CoreApi.Data.Entities;

namespace Keystone.CoreApi.Services;

public interface ICurrentUserService
{
    /// <summary>
    /// The caller's <see cref="AppUser"/>, created on first use from the access token's claims
    /// (just-in-time provisioning) and kept in sync with them afterwards.
    /// </summary>
    Task<AppUser> GetOrCreateAsync(CancellationToken ct = default);
}
