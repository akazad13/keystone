using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using StackExchange.Redis;

namespace Keystone.Bff.Services;

/// <summary>
/// Server-side session store for the cookie handler.
/// <para>
/// Without a session store the cookie handler packs the whole authentication ticket (claims plus
/// the identity provider's id, access and refresh tokens) into the encrypted cookie. With this store
/// the browser only holds a random session id; the ticket lives in Redis, encrypted with Data Protection.
/// </para>
/// Sessions are also indexed by the identity provider's session id (<c>sid</c>) and subject (<c>sub</c>)
/// so that an OIDC back-channel logout can find and delete them.
/// </summary>
public sealed class RedisTicketStore : ITicketStore
{
    public const string SessionIdItem = "keystone.session_id";

    private const string KeyPrefix = "keystone:bff:session:";
    private static readonly TimeSpan DefaultLifetime = TimeSpan.FromHours(8);

    private readonly IConnectionMultiplexer _redis;
    private readonly IDataProtector _protector;
    private readonly ILogger<RedisTicketStore> _logger;

    public RedisTicketStore(IConnectionMultiplexer redis, IDataProtectionProvider dataProtection, ILogger<RedisTicketStore> logger)
    {
        _redis = redis;
        _protector = dataProtection.CreateProtector("Keystone.Bff.Sessions");
        _logger = logger;
    }

    public async Task<string> StoreAsync(AuthenticationTicket ticket)
    {
        var sessionId = Guid.NewGuid().ToString("N");
        ticket.Properties.Items[SessionIdItem] = sessionId;

        await RenewAsync(sessionId, ticket);

        var principal = ticket.Principal;
        await IndexAsync(IndexKey("sid", principal.FindFirst("sid")?.Value), sessionId, ticket);
        await IndexAsync(IndexKey("sub", principal.FindFirst("sub")?.Value), sessionId, ticket);

        _logger.LogInformation("Created session {SessionId} for sub {Sub}, IdP sid {Sid}",
            sessionId, principal.FindFirst("sub")?.Value, principal.FindFirst("sid")?.Value);

        return sessionId;
    }

    public Task RenewAsync(string key, AuthenticationTicket ticket)
    {
        var payload = _protector.Protect(TicketSerializer.Default.Serialize(ticket));
        return _redis.GetDatabase().StringSetAsync(SessionKey(key), payload, Lifetime(ticket));
    }

    public async Task<AuthenticationTicket?> RetrieveAsync(string key)
    {
        var payload = (byte[]?)await _redis.GetDatabase().StringGetAsync(SessionKey(key));
        return payload is null ? null : TicketSerializer.Default.Deserialize(_protector.Unprotect(payload));
    }

    public async Task RemoveAsync(string key) => await DeleteSessionAsync(key);

    /// <summary>Deletes every session created from one identity-provider session.</summary>
    public Task<int> RemoveByIdentityProviderSessionAsync(string sid) => RemoveIndexedAsync(IndexKey("sid", sid)!);

    /// <summary>Deletes every session belonging to one user.</summary>
    public Task<int> RemoveBySubjectAsync(string sub) => RemoveIndexedAsync(IndexKey("sub", sub)!);

    private async Task<int> RemoveIndexedAsync(string indexKey)
    {
        var db = _redis.GetDatabase();
        var sessionIds = await db.SetMembersAsync(indexKey);

        var removed = 0;
        foreach (var sessionId in sessionIds)
        {
            if (await DeleteSessionAsync(sessionId!))
            {
                removed++;
            }
        }

        await db.KeyDeleteAsync(indexKey);
        return removed;
    }

    /// <summary>Deletes a session, the data stored alongside it, and its index entries.</summary>
    private async Task<bool> DeleteSessionAsync(string sessionId)
    {
        var db = _redis.GetDatabase();

        var ticket = await RetrieveAsync(sessionId);
        if (ticket is not null)
        {
            foreach (var claim in new[] { "sid", "sub" })
            {
                if (IndexKey(claim, ticket.Principal.FindFirst(claim)?.Value) is { } indexKey)
                {
                    await db.SetRemoveAsync(indexKey, sessionId);
                }
            }
        }

        await db.KeyDeleteAsync(SessionDataKey(sessionId, SessionTokenService.CoreApiTokenName));
        return await db.KeyDeleteAsync(SessionKey(sessionId));
    }

    private Task IndexAsync(string? indexKey, string sessionId, AuthenticationTicket ticket)
    {
        if (indexKey is null)
        {
            return Task.CompletedTask;
        }

        var db = _redis.GetDatabase();
        return Task.WhenAll(
            db.SetAddAsync(indexKey, sessionId),
            db.KeyExpireAsync(indexKey, Lifetime(ticket)));
    }

    private static TimeSpan Lifetime(AuthenticationTicket ticket)
    {
        var remaining = ticket.Properties.ExpiresUtc - DateTimeOffset.UtcNow;
        return remaining > TimeSpan.Zero ? remaining.Value : DefaultLifetime;
    }

    private static string SessionKey(string sessionId) => KeyPrefix + sessionId;

    /// <summary>Key for extra data kept per session; it is deleted together with the session.</summary>
    public static string SessionDataKey(string sessionId, string name) => $"{KeyPrefix}{sessionId}:{name}";

    private static string? IndexKey(string claim, string? value) =>
        string.IsNullOrEmpty(value) ? null : $"{KeyPrefix}{claim}:{value}";
}

/// <summary>Plugs <see cref="RedisTicketStore"/> into the cookie handler once DI is available.</summary>
public sealed class ConfigureCookieSessionStore(RedisTicketStore store)
    : Microsoft.Extensions.Options.IPostConfigureOptions<CookieAuthenticationOptions>
{
    public void PostConfigure(string? name, CookieAuthenticationOptions options) => options.SessionStore = store;
}
