// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Collections.Concurrent;
using System.Security.Principal;

namespace EventLogExpert.Eventing.Resolvers;

public sealed class EventResolverCache : IEventResolverCache
{
    private readonly ConcurrentDictionary<string, string> _descriptionCache = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<IReadOnlyList<string>, IReadOnlyList<string>> _keywordsCache = new(KeywordListComparer.Instance);
    private readonly ConcurrentDictionary<SecurityIdentifier, SecurityIdentifier> _sidCache = new();
    private readonly ConcurrentDictionary<string, string> _valueCache = new(StringComparer.Ordinal);

    public void ClearAll()
    {
        _descriptionCache.Clear();
        _keywordsCache.Clear();
        _sidCache.Clear();
        _valueCache.Clear();
    }

    /// <summary>Returns the description if it exists in the cache, otherwise adds it to the cache and returns it.</summary>
    public string GetOrAddDescription(string description) => _descriptionCache.GetOrAdd(description, static key => key);

    /// <summary>Returns a shared list with the same contents, adding a frozen copy to the cache on first sight.</summary>
    public IReadOnlyList<string> GetOrAddKeywords(IReadOnlyList<string> keywords)
    {
        if (keywords.Count == 0) { return []; }

        if (_keywordsCache.TryGetValue(keywords, out var existing)) { return existing; }

        // Freeze to an array on miss so a caller that later mutates its list can't corrupt the cached key.
        string[] frozen = [.. keywords];

        return _keywordsCache.GetOrAdd(frozen, frozen);
    }

    /// <summary>Returns a shared identifier equal to the input, adding it to the cache on first sight.</summary>
    public SecurityIdentifier? GetOrAddSid(SecurityIdentifier? sid) =>
        sid is null ? null : _sidCache.GetOrAdd(sid, static key => key);

    /// <summary>Returns the value if it exists in the cache, otherwise adds it to the cache and returns it.</summary>
    public string GetOrAddValue(string value) => _valueCache.GetOrAdd(value, static key => key);

    private sealed class KeywordListComparer : IEqualityComparer<IReadOnlyList<string>>
    {
        public static readonly KeywordListComparer Instance = new();

        public bool Equals(IReadOnlyList<string>? x, IReadOnlyList<string>? y)
        {
            if (ReferenceEquals(x, y)) { return true; }

            if (x is null || y is null || x.Count != y.Count) { return false; }

            for (int i = 0; i < x.Count; i++)
            {
                if (!StringComparer.Ordinal.Equals(x[i], y[i])) { return false; }
            }

            return true;
        }

        public int GetHashCode(IReadOnlyList<string> obj)
        {
            var hash = new HashCode();

            for (int i = 0; i < obj.Count; i++)
            {
                hash.Add(obj[i], StringComparer.Ordinal);
            }

            return hash.ToHashCode();
        }
    }
}
