using System.Text;
using Keystone.Bff.Services;
using Keystone.Bff.Tests.Fakes;
using Microsoft.AspNetCore.Authentication;

namespace Keystone.Bff.Tests.Services;

public sealed class RedisTicketStoreTests
{
    private readonly FakeRedis _redis = new();
    private readonly RedisTicketStore _store;

    public RedisTicketStoreTests() => _store = TestSessions.CreateStore(_redis);

    [Fact]
    public async Task StoreAsync_then_RetrieveAsync_round_trips_the_ticket()
    {
        var sessionId = await _store.StoreAsync(TestSessions.CreateTicket(sub: "alice"));

        var ticket = await _store.RetrieveAsync(sessionId);

        Assert.NotNull(ticket);
        Assert.Equal("alice", ticket.Principal.FindFirst("sub")?.Value);
        Assert.Equal("idp-refresh-token", ticket.Properties.GetTokenValue("refresh_token"));
        Assert.Equal(sessionId, ticket.Properties.Items[RedisTicketStore.SessionIdItem]);
    }

    [Fact]
    public async Task Tokens_are_encrypted_in_Redis()
    {
        var sessionId = await _store.StoreAsync(TestSessions.CreateTicket(refreshToken: "very-secret-refresh-token"));

        var stored = Encoding.UTF8.GetString(_redis.GetBytes(TestSessions.SessionKey(sessionId))!);

        Assert.DoesNotContain("very-secret-refresh-token", stored);
    }

    [Fact]
    public async Task Session_expires_in_Redis_when_the_ticket_expires()
    {
        var sessionId = await _store.StoreAsync(TestSessions.CreateTicket());

        var ttl = _redis.TimeToLive(TestSessions.SessionKey(sessionId));

        Assert.NotNull(ttl);
        Assert.InRange(ttl.Value, TimeSpan.FromHours(7.9), TimeSpan.FromHours(8));
    }

    [Fact]
    public async Task Sessions_are_indexed_by_sub_and_sid()
    {
        var sessionId = await _store.StoreAsync(TestSessions.CreateTicket(sub: "alice", sid: "sso-1"));

        Assert.Contains(sessionId, _redis.SetMembers(TestSessions.SubjectIndexKey("alice")));
        Assert.Contains(sessionId, _redis.SetMembers(TestSessions.SidIndexKey("sso-1")));
    }

    [Fact]
    public async Task RetrieveAsync_returns_null_for_an_unknown_session()
    {
        Assert.Null(await _store.RetrieveAsync("does-not-exist"));
    }

    [Fact]
    public async Task ListBySubjectAsync_returns_only_that_users_sessions_newest_first()
    {
        var older = await _store.StoreAsync(TestSessions.CreateTicket(sub: "alice", issuedUtc: DateTimeOffset.UtcNow.AddHours(-2)));
        var newer = await _store.StoreAsync(TestSessions.CreateTicket(sub: "alice", issuedUtc: DateTimeOffset.UtcNow));
        await _store.StoreAsync(TestSessions.CreateTicket(sub: "bob"));

        var sessions = await _store.ListBySubjectAsync("alice");

        Assert.Equal([newer, older], sessions.Select(s => s.Id));
    }

    [Fact]
    public async Task ListBySubjectAsync_prunes_index_entries_of_expired_sessions()
    {
        var expired = await _store.StoreAsync(TestSessions.CreateTicket(sub: "alice"));
        var live = await _store.StoreAsync(TestSessions.CreateTicket(sub: "alice"));
        _redis.Delete(TestSessions.SessionKey(expired)); // Redis expired the ticket, the index still lists it

        var sessions = await _store.ListBySubjectAsync("alice");

        Assert.Equal([live], sessions.Select(s => s.Id));
        Assert.DoesNotContain(expired, _redis.SetMembers(TestSessions.SubjectIndexKey("alice")));
    }

    [Fact]
    public async Task BelongsToAsync_is_false_for_another_users_session()
    {
        var bobsSession = await _store.StoreAsync(TestSessions.CreateTicket(sub: "bob"));

        Assert.True(await _store.BelongsToAsync(bobsSession, "bob"));
        Assert.False(await _store.BelongsToAsync(bobsSession, "alice"));
    }

    [Fact]
    public async Task RemoveAsync_deletes_the_session_its_cached_api_token_and_index_entries()
    {
        var sessionId = await _store.StoreAsync(TestSessions.CreateTicket(sub: "alice", sid: "sso-1"));
        var tokenKey = RedisTicketStore.SessionDataKey(sessionId, SessionTokenService.CoreApiTokenName);
        await _redis.Database.StringSetAsync(tokenKey, "cached-token");

        await _store.RemoveAsync(sessionId);

        Assert.Null(await _store.RetrieveAsync(sessionId));
        Assert.False(_redis.Contains(tokenKey));
        Assert.DoesNotContain(sessionId, _redis.SetMembers(TestSessions.SubjectIndexKey("alice")));
        Assert.DoesNotContain(sessionId, _redis.SetMembers(TestSessions.SidIndexKey("sso-1")));
    }

    [Fact]
    public async Task RemoveByIdentityProviderSessionAsync_removes_only_sessions_of_that_sso_session()
    {
        var laptop = await _store.StoreAsync(TestSessions.CreateTicket(sub: "alice", sid: "sso-laptop"));
        var phone = await _store.StoreAsync(TestSessions.CreateTicket(sub: "alice", sid: "sso-phone"));

        var removed = await _store.RemoveByIdentityProviderSessionAsync("sso-laptop");

        Assert.Equal(1, removed);
        Assert.Null(await _store.RetrieveAsync(laptop));
        Assert.NotNull(await _store.RetrieveAsync(phone));
        Assert.Equal([phone], _redis.SetMembers(TestSessions.SubjectIndexKey("alice")));
    }

    [Fact]
    public async Task RemoveBySubjectAsync_removes_every_session_of_the_user_and_nobody_elses()
    {
        await _store.StoreAsync(TestSessions.CreateTicket(sub: "alice", sid: "sso-1"));
        await _store.StoreAsync(TestSessions.CreateTicket(sub: "alice", sid: "sso-2"));
        var bobs = await _store.StoreAsync(TestSessions.CreateTicket(sub: "bob", sid: "sso-3"));

        var removed = await _store.RemoveBySubjectAsync("alice");

        Assert.Equal(2, removed);
        Assert.Empty(await _store.ListBySubjectAsync("alice"));
        Assert.NotNull(await _store.RetrieveAsync(bobs));
    }
}
