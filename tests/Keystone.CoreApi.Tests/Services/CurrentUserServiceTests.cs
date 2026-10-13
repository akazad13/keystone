using System.Security.Claims;
using Keystone.CoreApi.Data.Entities;
using Keystone.CoreApi.Services;
using Keystone.CoreApi.Tests.Fakes;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace Keystone.CoreApi.Tests.Services;

/// <summary>Just-in-time provisioning: the Users row follows the claims in the Keystone access token.</summary>
public sealed class CurrentUserServiceTests : IDisposable
{
    private readonly TestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    private static IHttpContextAccessor CallerWith(params Claim[] claims) => new HttpContextAccessor
    {
        HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer", "name", "role"))
        }
    };

    private static Claim[] Alice(string role = "User", string email = "alice@example.com", string name = "Alice") =>
        [new("sub", "alice-sub"), new("email", email), new("name", name), new("role", role)];

    private CurrentUserService NewService(IHttpContextAccessor caller, params IInterceptor[] interceptors) =>
        new(_database.CreateContext(interceptors), caller, NullLogger<CurrentUserService>.Instance);

    private async Task<AppUser> LoadAsync(string sub)
    {
        await using var db = _database.CreateContext();
        return await db.Users.SingleAsync(u => u.ExternalId == sub);
    }

    [Fact]
    public async Task Creates_the_user_on_the_first_call()
    {
        var user = await NewService(CallerWith(Alice())).GetOrCreateAsync();

        var stored = await LoadAsync("alice-sub");
        Assert.Equal(user.Id, stored.Id);
        Assert.Equal("alice@example.com", stored.Email);
        Assert.Equal("Alice", stored.DisplayName);
        Assert.Equal("User", stored.Role);
    }

    [Fact]
    public async Task Maps_the_Admin_role()
    {
        var user = await NewService(CallerWith(Alice(role: "Admin"))).GetOrCreateAsync();

        Assert.Equal("Admin", user.Role);
    }

    [Fact]
    public async Task Later_calls_find_the_same_user()
    {
        var first = await NewService(CallerWith(Alice())).GetOrCreateAsync();
        var second = await NewService(CallerWith(Alice())).GetOrCreateAsync();

        Assert.Equal(first.Id, second.Id);
        await using var db = _database.CreateContext();
        Assert.Equal(1, await db.Users.CountAsync());
    }

    [Fact]
    public async Task Refreshes_profile_and_role_from_the_token()
    {
        await NewService(CallerWith(Alice())).GetOrCreateAsync();

        await NewService(CallerWith(Alice(role: "Admin", email: "alice@new.example", name: "Alice A."))).GetOrCreateAsync();

        var stored = await LoadAsync("alice-sub");
        Assert.Equal("alice@new.example", stored.Email);
        Assert.Equal("Alice A.", stored.DisplayName);
        Assert.Equal("Admin", stored.Role);
        Assert.NotNull(stored.UpdatedAt);
    }

    [Fact]
    public async Task Display_name_falls_back_to_preferred_username_then_email()
    {
        var withUsername = await NewService(CallerWith(new("sub", "u1"), new("email", "u1@example.com"), new("preferred_username", "user.one")))
            .GetOrCreateAsync();
        var withEmailOnly = await NewService(CallerWith(new("sub", "u2"), new("email", "u2@example.com")))
            .GetOrCreateAsync();

        Assert.Equal("user.one", withUsername.DisplayName);
        Assert.Equal("u2@example.com", withEmailOnly.DisplayName);
    }

    [Fact]
    public async Task LastSeenAt_is_updated_when_it_is_older_than_five_minutes()
    {
        await _database.AddUserAsync("alice-sub", lastSeenAt: DateTime.UtcNow.AddMinutes(-10));

        await NewService(CallerWith(Alice())).GetOrCreateAsync();

        Assert.InRange((await LoadAsync("alice-sub")).LastSeenAt, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow);
    }

    [Fact]
    public async Task LastSeenAt_is_not_written_on_every_request()
    {
        var lastSeen = DateTime.UtcNow.AddMinutes(-1);
        await _database.AddUserAsync("alice-sub", lastSeenAt: lastSeen);

        await NewService(CallerWith(Alice())).GetOrCreateAsync();

        Assert.Equal(lastSeen, (await LoadAsync("alice-sub")).LastSeenAt, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task Returns_the_same_instance_within_one_request()
    {
        var service = NewService(CallerWith(Alice()));

        Assert.Same(await service.GetOrCreateAsync(), await service.GetOrCreateAsync());
    }

    [Fact]
    public async Task Throws_when_the_token_has_no_sub()
    {
        var service = NewService(CallerWith(new Claim("email", "nobody@example.com")));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetOrCreateAsync());
    }

    [Fact]
    public async Task Two_first_requests_racing_end_up_with_one_user()
    {
        // While this request inserts the user, a parallel request inserts the same sub first.
        var racer = new InsertBeforeSaveInterceptor(_database, "alice-sub");

        var user = await NewService(CallerWith(Alice()), racer).GetOrCreateAsync();

        Assert.Equal(racer.InsertedUserId, user.Id);
        await using var db = _database.CreateContext();
        Assert.Equal(1, await db.Users.CountAsync());
    }

    /// <summary>Inserts a user with the same sub just before the service's own insert is saved.</summary>
    private sealed class InsertBeforeSaveInterceptor(TestDatabase database, string sub) : SaveChangesInterceptor
    {
        public Guid? InsertedUserId { get; private set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (InsertedUserId is null)
            {
                InsertedUserId = (await database.AddUserAsync(sub)).Id;
            }

            return result;
        }
    }
}
