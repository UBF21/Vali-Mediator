namespace Vali_Mediator_Caching.Core.Store;

/// <summary>
/// Group-to-keys index used by <see cref="InMemoryCacheStore"/>. Not thread-safe:
/// the store serializes every access under its own lock.
/// </summary>
internal sealed class GroupIndex
{
    private readonly Dictionary<string, HashSet<string>> _groups
        = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

    public int GroupCount => _groups.Count;

    public int KeyCount
    {
        get
        {
            var total = 0;
            foreach (var bucket in _groups.Values)
                total += bucket.Count;
            return total;
        }
    }

    /// <summary>Adds <paramref name="key"/> to <paramref name="group"/>; false when a limit would be exceeded.</summary>
    public bool TryAdd(string group, string key, int maxGroups, int maxKeysPerGroup)
    {
        if (!_groups.TryGetValue(group, out var bucket))
        {
            if (_groups.Count >= maxGroups)
                return false;
            _groups[group] = bucket = new HashSet<string>(StringComparer.Ordinal);
        }
        else if (bucket.Count >= maxKeysPerGroup && !bucket.Contains(key))
        {
            return false;
        }

        bucket.Add(key);
        return true;
    }

    public void Remove(string group, string key)
    {
        if (_groups.TryGetValue(group, out var bucket) && bucket.Remove(key) && bucket.Count == 0)
            _groups.Remove(group);
    }

    /// <summary>Removes the whole group and returns its keys, or <c>null</c> when it does not exist.</summary>
    public HashSet<string>? Take(string group)
        => _groups.Remove(group, out var keys) ? keys : null;
}
