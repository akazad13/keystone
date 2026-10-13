using Keystone.CoreApi.Data;
using Keystone.CoreApi.Data.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Keystone.CoreApi.Tests.Fakes;

/// <summary>
/// The real <see cref="AppDbContext"/> model on an in-memory SQLite database. Unlike EF's InMemory
/// provider, SQLite enforces unique indexes and foreign keys, which the tests rely on.
/// The database lives as long as this object (one open connection).
/// </summary>
public sealed class TestDatabase : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public TestDatabase()
    {
        _connection.Open();
        using var db = CreateContext();
        db.Database.EnsureCreated();
    }

    public SqliteConnection Connection => _connection;

    public AppDbContext CreateContext(params IInterceptor[] interceptors) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(interceptors)
            .Options);

    public async Task<AppUser> AddUserAsync(string sub, string role = "User", DateTime? lastSeenAt = null)
    {
        await using var db = CreateContext();
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            ExternalId = sub,
            Email = $"{sub}@example.com",
            DisplayName = sub,
            Role = role,
            LastSeenAt = lastSeenAt ?? DateTime.UtcNow
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    public void Dispose() => _connection.Dispose();
}
