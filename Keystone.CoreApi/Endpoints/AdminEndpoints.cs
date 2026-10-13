using Microsoft.EntityFrameworkCore;
using Keystone.CoreApi.Data;

namespace Keystone.CoreApi.Endpoints;

public record AdminUserDto(
    Guid Id,
    string Email,
    string DisplayName,
    string Role,
    int ProjectCount,
    DateTime CreatedAt,
    DateTime LastSeenAt);

public record AdminStatsDto(
    int TotalUsers,
    int ActiveUsers,
    int NewUsersToday,
    int Admins,
    int TotalProjects,
    int ActiveProjects);

/// <summary>
/// Read-only views for administrators. The whole group requires the Admin role, which comes from
/// Zitadel: project role "Admin" -> Identity Service -> "role" claim in the Keystone token.
/// Accounts and roles themselves are managed in Zitadel, not here.
/// </summary>
public static class AdminEndpoints
{
    private static readonly TimeSpan ActiveWindow = TimeSpan.FromDays(7);

    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/admin")
            .WithTags("Admin")
            .RequireAuthorization("Admin");

        group.MapGet("/users", async (AppDbContext db, CancellationToken ct) =>
            Results.Ok(await db.Users
                .AsNoTracking()
                .OrderByDescending(u => u.LastSeenAt)
                .Select(u => new AdminUserDto(u.Id, u.Email, u.DisplayName, u.Role, u.Projects.Count, u.CreatedAt, u.LastSeenAt))
                .ToListAsync(ct)))
        .WithName("GetUsers")
        .Produces<IReadOnlyList<AdminUserDto>>()
        .Produces(403);

        group.MapGet("/stats", async (AppDbContext db, CancellationToken ct) =>
        {
            var now = DateTime.UtcNow;
            var activeSince = now - ActiveWindow;
            var today = now.Date;

            return Results.Ok(new AdminStatsDto(
                TotalUsers: await db.Users.CountAsync(ct),
                ActiveUsers: await db.Users.CountAsync(u => u.LastSeenAt >= activeSince, ct),
                NewUsersToday: await db.Users.CountAsync(u => u.CreatedAt >= today, ct),
                Admins: await db.Users.CountAsync(u => u.Role == "Admin", ct),
                TotalProjects: await db.Projects.CountAsync(ct),
                ActiveProjects: await db.Projects.CountAsync(p => p.Status == "Active", ct)));
        })
        .WithName("GetAdminStats")
        .Produces<AdminStatsDto>()
        .Produces(403);

        return routes;
    }
}
