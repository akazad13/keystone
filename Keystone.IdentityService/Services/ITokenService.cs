using Keystone.IdentityService.Models;

namespace Keystone.IdentityService.Services;

public interface ITokenService
{
    /// <summary>Signs a Keystone access token for <paramref name="user"/>, valid only for <paramref name="audience"/>.</summary>
    TokenResponse IssueAccessToken(UserInfo user, string audience);

    /// <summary>Validates a Keystone access token. Returns its claims, or null when it is not valid.</summary>
    Task<IDictionary<string, object>?> ValidateAccessTokenAsync(string token);

    bool IsKnownAudience(string audience);
}
