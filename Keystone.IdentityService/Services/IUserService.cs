using Keystone.IdentityService.Models;

namespace Keystone.IdentityService.Services;

public interface IUserService
{
    /// <summary>
    /// Asks the external identity provider who owns <paramref name="accessToken"/>.
    /// Returns null when the provider rejects the token.
    /// </summary>
    Task<UserInfo?> GetUserInfoAsync(string accessToken, CancellationToken cancellationToken);
}
