namespace Keystone.CoreApi.Data.Entities;

public class AppUser
{
    public Guid Id { get; set; }

    public string ExternalId { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string Role { get; set; } = "User";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }

    /// <summary>Last API call by this user, updated at most every few minutes.</summary>
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;

    public ICollection<Project> Projects { get; set; } = [];
}
