using System.Reflection;
using StackExchange.Redis;

namespace Keystone.Bff.Tests.Fakes;

/// <summary>
/// An in-memory stand-in for the few Redis commands the BFF uses (strings, sets, expiry, locks).
/// <para>
/// <see cref="IDatabase"/> has hundreds of members, so instead of implementing it by hand the fake
/// uses <see cref="DispatchProxy"/>: every call lands in <see cref="Handle"/>, which supports the
/// commands the BFF needs and fails loudly on anything else. Expiry is recorded, not enforced;
/// tests simulate an expired key with <see cref="Delete"/>.
/// </para>
/// </summary>
public sealed class FakeRedis
{
    private readonly object _gate = new();
    private readonly Dictionary<string, byte[]> _strings = [];
    private readonly Dictionary<string, HashSet<string>> _sets = [];
    private readonly Dictionary<string, TimeSpan> _ttls = [];
    private readonly Dictionary<string, int> _calls = [];

    public FakeRedis()
    {
        Database = DispatchProxy.Create<IDatabase, Proxy>();
        ((Proxy)(object)Database).Handler = Handle;

        Connection = DispatchProxy.Create<IConnectionMultiplexer, Proxy>();
        ((Proxy)(object)Connection).Handler = (method, _) => method.Name == nameof(IConnectionMultiplexer.GetDatabase)
            ? Database
            : throw new NotSupportedException($"FakeRedis does not support IConnectionMultiplexer.{method.Name}");
    }

    public IConnectionMultiplexer Connection { get; }

    public IDatabase Database { get; }

    public bool Contains(string key)
    {
        lock (_gate)
        {
            return _strings.ContainsKey(key) || _sets.ContainsKey(key);
        }
    }

    public byte[]? GetBytes(string key)
    {
        lock (_gate)
        {
            return _strings.GetValueOrDefault(key);
        }
    }

    public IReadOnlySet<string> SetMembers(string key)
    {
        lock (_gate)
        {
            return _sets.TryGetValue(key, out var set) ? new HashSet<string>(set) : new HashSet<string>();
        }
    }

    public TimeSpan? TimeToLive(string key)
    {
        lock (_gate)
        {
            return _ttls.TryGetValue(key, out var ttl) ? ttl : null;
        }
    }

    public IReadOnlyList<string> Keys()
    {
        lock (_gate)
        {
            return [.. _strings.Keys.Concat(_sets.Keys)];
        }
    }

    /// <summary>Removes a key as if it had expired.</summary>
    public void Delete(string key)
    {
        lock (_gate)
        {
            DeleteKey(key);
        }
    }

    /// <summary>How many times a command (e.g. "LockTakeAsync") was called.</summary>
    public int Calls(string command)
    {
        lock (_gate)
        {
            return _calls.GetValueOrDefault(command);
        }
    }

    private object? Handle(MethodInfo method, object?[] args)
    {
        lock (_gate)
        {
            _calls[method.Name] = _calls.GetValueOrDefault(method.Name) + 1;

            var key = args.Length > 0 && args[0] is RedisKey k ? (string)k! : null;

            switch (method.Name)
            {
                case "StringSetAsync":
                {
                    // Older overloads take a When, newer ones (2.13+) a ValueCondition.
                    var when = args.OfType<When>().Concat(args.OfType<ValueCondition>().Select(c =>
                        c.Equals(ValueCondition.NotExists) ? When.NotExists : c.Equals(ValueCondition.Exists) ? When.Exists : When.Always))
                        .FirstOrDefault();
                    if ((when == When.NotExists && _strings.ContainsKey(key!)) || (when == When.Exists && !_strings.ContainsKey(key!)))
                    {
                        return Task.FromResult(false);
                    }

                    _strings[key!] = ((byte[]?)(RedisValue)args[1]!)!;
                    SetTtl(key!, ExpiryOf(method, args));
                    return Task.FromResult(true);
                }

                case "StringGetAsync":
                    return Task.FromResult(_strings.TryGetValue(key!, out var bytes) ? (RedisValue)bytes : RedisValue.Null);

                case "KeyDeleteAsync":
                    return Task.FromResult(DeleteKey(key!));

                case "KeyExpireAsync":
                    if (!_strings.ContainsKey(key!) && !_sets.ContainsKey(key!))
                    {
                        return Task.FromResult(false);
                    }
                    SetTtl(key!, ExpiryOf(method, args));
                    return Task.FromResult(true);

                case "SetAddAsync":
                {
                    if (!_sets.TryGetValue(key!, out var set))
                    {
                        _sets[key!] = set = [];
                    }
                    return Task.FromResult(set.Add(((RedisValue)args[1]!).ToString()));
                }

                case "SetRemoveAsync":
                {
                    var removed = _sets.TryGetValue(key!, out var set) && set.Remove(((RedisValue)args[1]!).ToString());
                    if (set is { Count: 0 })
                    {
                        _sets.Remove(key!);
                    }
                    return Task.FromResult(removed);
                }

                case "SetMembersAsync":
                    return Task.FromResult(_sets.TryGetValue(key!, out var members)
                        ? members.Select(m => (RedisValue)m).ToArray()
                        : Array.Empty<RedisValue>());

                case "SetContainsAsync":
                    return Task.FromResult(_sets.TryGetValue(key!, out var candidates) && candidates.Contains(((RedisValue)args[1]!).ToString()));

                case "LockTakeAsync":
                    if (_strings.ContainsKey(key!))
                    {
                        return Task.FromResult(false);
                    }
                    _strings[key!] = ((byte[]?)(RedisValue)args[1]!)!;
                    SetTtl(key!, ExpiryOf(method, args));
                    return Task.FromResult(true);

                case "LockReleaseAsync":
                {
                    var owner = ((RedisValue)args[1]!).ToString();
                    var held = _strings.TryGetValue(key!, out var value) && System.Text.Encoding.UTF8.GetString(value) == owner;
                    if (held)
                    {
                        DeleteKey(key!);
                    }
                    return Task.FromResult(held);
                }

                default:
                    throw new NotSupportedException($"FakeRedis does not support IDatabase.{method.Name}");
            }
        }
    }

    private static TimeSpan? ExpiryOf(MethodInfo method, object?[] args)
    {
        foreach (var arg in args.Skip(1))
        {
            switch (arg)
            {
                case TimeSpan timeSpan:
                    return timeSpan;
                case Expiration expiration:
                    return ToTimeSpan(expiration, method);
            }
        }

        return null;
    }

    /// <summary>
    /// Reads a relative <see cref="Expiration"/> (what a TimeSpan converts to in the 2.13+ overloads).
    /// Its state is not public, hence the reflection.
    /// </summary>
    private static TimeSpan? ToTimeSpan(Expiration expiration, MethodInfo method)
    {
        T Read<T>(string property) => (T)typeof(Expiration)
            .GetProperty(property, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(expiration)!;

        if (Read<bool>("IsNone") || Read<bool>("IsKeepTtl") || Read<bool>("IsPersist"))
        {
            return null;
        }

        if (!Read<bool>("IsRelative"))
        {
            throw new NotSupportedException($"FakeRedis: absolute expiry in {method.Name} is not supported");
        }

        var value = Read<long>("Value");
        return Read<bool>("IsMilliseconds") ? TimeSpan.FromMilliseconds(value) : TimeSpan.FromSeconds(value);
    }

    private void SetTtl(string key, TimeSpan? ttl)
    {
        if (ttl is { } value)
        {
            _ttls[key] = value;
        }
        else
        {
            _ttls.Remove(key);
        }
    }

    private bool DeleteKey(string key)
    {
        _ttls.Remove(key);
        return _strings.Remove(key) | _sets.Remove(key);
    }

    /// <summary>Forwards every interface call to a handler. Must be public and non-sealed for DispatchProxy.</summary>
    public class Proxy : DispatchProxy
    {
        internal Func<MethodInfo, object?[], object?> Handler { get; set; } = null!;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Handler(targetMethod!, args ?? []);
    }
}
