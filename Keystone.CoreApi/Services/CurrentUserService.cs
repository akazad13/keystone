using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Keystone.CoreApi.Data;
using Keystone.CoreApi.Data.Entities;

namespace Keystone.CoreApi.Services;

/// <summary>
/// Maps the authenticated caller to a row in the Users table.
/// <para>
/// The identity provider owns the user (passwords, profile, roles); the Core API only keeps the
/// row it needs for relationships such as project ownership. The link between the two is the
/// "sub" claim, stored as <see cref="AppUser.ExternalId"/>. The row is created the first time
/// the user calls the API and its email, name and role are refreshed from later tokens.
/// </para>
/// </summary>
public sealed class CurrentUserService : ICurrentUserService
{
    private readonly AppDbContext _db;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<CurrentUserService> _logger;

    private AppUser? _current;

    public CurrentUserService(AppDbContext db, IHttpContextAccessor httpContextAccessor, ILogger<CurrentUserService> logger)
    {
        _db = db;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public async Task<AppUser> GetOrCreateAsync(CancellationToken ct = default)
    {
        if (_current is not null)
        {
            return _current;
        }

        var principal = _httpContextAccessor.HttpContext?.User
            ?? throw new InvalidOperationException("There is no current request.");

        var sub = principal.FindFirstValue("sub")
            ?? throw new InvalidOperationException("The access token has no 'sub' claim.");
        var email = principal.FindFirstValue("email") ?? string.Empty;
        var displayName = principal.FindFirstValue("name") ?? principal.FindFirstValue("preferred_username") ?? email;
        var role = principal.IsInRole("Admin") ? "Admin" : "User";

        var user = await _db.Users.FirstOrDefaultAsync(u => u.ExternalId == sub, ct);

        if (user is null)
        {
            user = new AppUser
            {
                Id = Guid.NewGuid(),
                ExternalId = sub,
                Email = email,
                DisplayName = displayName,
                Role = role
            };
            _db.Users.Add(user);

            try
            {
                await _db.SaveChangesAsync(ct);
                _logger.LogInformation("Provisioned user {UserId} for sub {Sub}", user.Id, sub);
            }
            catch (DbUpdateException)
            {
                // Two first requests raced; the unique index on ExternalId let only one insert win.
                _db.Entry(user).State = EntityState.Detached;
                user = await _db.Users.SingleAsync(u => u.ExternalId == sub, ct);
            }
        }
        else if (user.Email != email || user.DisplayName != displayName || user.Role != role)
        {
            user.Email = email;
            user.DisplayName = displayName;
            user.Role = role;
            user.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }

        return _current = user;
    }
}
