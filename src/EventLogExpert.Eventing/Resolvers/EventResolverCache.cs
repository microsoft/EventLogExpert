// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Security.Principal;

namespace EventLogExpert.Eventing.Resolvers;

public sealed class EventResolverCache : IEventResolverCache
{
    // Bounds the description dedupe table so a high-cardinality log (descriptions with embedded timestamps / PIDs /
    // paths that are unique per event) cannot grow it without limit. At the cap we stop ADDING but keep serving
    // existing hits: this preserves all sharing accumulated so far with no whole-dictionary swap/thrash. The cap is
    // gated on an Interlocked counter rather than ConcurrentDictionary.Count (which locks every stripe on each call),
    // so a pathological all-miss log pays no per-event lock; concurrent inserts can overshoot the cap by at most the
    // number of writer threads.
    private const int DefaultMaxDescriptionCacheSize = 131072;

    private readonly ConcurrentDictionary<string, string> _descriptionCache = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<IReadOnlyList<string>, IReadOnlyList<string>> _keywordsCache = new(KeywordListComparer.Instance);

    private readonly int _maxDescriptionCacheSize;
    private readonly ConcurrentDictionary<SecurityIdentifier, SecurityIdentifier> _sidCache = new();
    private readonly ConcurrentDictionary<string, string> _valueCache = new(StringComparer.Ordinal);

    private int _descriptionCount;

    public EventResolverCache() : this(DefaultMaxDescriptionCacheSize) { }

    // Test seam: lets a unit test exercise the cap without inserting 131072 entries.
    internal EventResolverCache(int maxDescriptionCacheSize) => _maxDescriptionCacheSize = maxDescriptionCacheSize;

    public void ClearAll()
    {
        _descriptionCache.Clear();
        _keywordsCache.Clear();
        _sidCache.Clear();
        _valueCache.Clear();
        Interlocked.Exchange(ref _descriptionCount, 0);
    }

    /// <summary>
    ///     Returns a shared instance equal to <paramref name="description" />, interning it on first sight. Bounded by
    ///     the configured cap: once full, an uncached description is returned as-is (not stored) so the table stays at the
    ///     cap (plus at most a few concurrent inserts) while all prior sharing is retained.
    /// </summary>
    public string GetOrAddDescription(string description)
    {
        if (_descriptionCache.TryGetValue(description, out string? existing)) { return existing; }

        if (Volatile.Read(ref _descriptionCount) >= _maxDescriptionCacheSize) { return description; }

        var shared = _descriptionCache.GetOrAdd(description, static key => key);

        // Count only entries this call inserted: the returned instance is ours iff we won the add race.
        if (ReferenceEquals(shared, description)) { Interlocked.Increment(ref _descriptionCount); }

        return shared;
    }

    /// <summary>Returns a shared read-only list with the same contents, adding a frozen copy to the cache on first sight.</summary>
    public IReadOnlyList<string> GetOrAddKeywords(IReadOnlyList<string> keywords)
    {
        if (keywords.Count == 0) { return []; }

        if (_keywordsCache.TryGetValue(keywords, out var existing)) { return existing; }

        // Copy and wrap on miss so the cached key stays immutable.
        var frozen = new ReadOnlyCollection<string>([.. keywords]);

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
