namespace Keystone.IdentityService.Models;

/// <summary>
/// A user as Keystone sees them, independent of which identity provider authenticated them.
/// </summary>
public sealed class UserInfo
{
    public required string Sub { get; init; }
    public string? Name { get; init; }
    public string? Email { get; init; }
    public bool EmailVerified { get; init; }
    public string? PreferredUsername { get; init; }
    public IReadOnlyList<string> Roles { get; init; } = [];
}
