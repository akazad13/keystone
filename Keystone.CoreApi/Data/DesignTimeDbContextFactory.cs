using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Keystone.CoreApi.Data;

/// <summary>
/// Used only by the EF Core tools (`dotnet ef migrations add ...`), which cannot start the app
/// because the connection string is supplied by the Aspire AppHost at run time.
/// Creating a migration does not connect to the database.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=localhost;Database=keystonedb;Integrated Security=true;TrustServerCertificate=true")
            .Options);
}
