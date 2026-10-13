namespace Keystone.Bff.Services;

public interface ISessionTokenService
{
    /// <summary>
    /// Returns a Keystone access token for the signed-in user, valid for the Core API.
    /// Returns null when there is no session or the identity provider's token can no longer be exchanged.
    /// </summary>
    Task<string?> GetAccessTokenAsync(HttpContext httpContext, CancellationToken cancellationToken);
}
