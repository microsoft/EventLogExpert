// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Collections;
using System.Collections.Concurrent;

namespace EventLogExpert.Provider.Resolution;

internal sealed class CompactMessageStore : ILazyMessageSource
{
    private static readonly IReadOnlyList<MessageModel> s_empty = [];

    private readonly ConcurrentDictionary<int, IReadOnlyList<MessageModel>> _byShortIdCache = new();
    private readonly MessageEntry[] _entries;
    private readonly Lazy<Dictionary<long, int>> _firstIndexByRawId;
    private readonly string _providerName;
    private readonly IReadOnlyDictionary<int, MessageModel>? _rareByIndex;
    private readonly ConcurrentDictionary<long, MessageModel?> _rawIdCache = new();
    private readonly int[] _sortedByShortId;

    private CompactMessageStore(
        MessageEntry[] entries,
        int[] sortedByShortId,
        IReadOnlyDictionary<int, MessageModel>? rareByIndex,
        string providerName)
    {
        _entries = entries;
        _sortedByShortId = sortedByShortId;
        _rareByIndex = rareByIndex;
        _providerName = providerName;
        _firstIndexByRawId = new Lazy<Dictionary<long, int>>(BuildRawIdLookup);
    }

    public int Count => _entries.Length;

    public static CompactMessageStore Build(IReadOnlyList<MessageModel> messages)
    {
        int count = messages.Count;
        var entries = new MessageEntry[count];
        Dictionary<int, MessageModel>? rare = null;
        string providerName = count > 0 ? messages[0].ProviderName : string.Empty;

        for (int i = 0; i < count; i++)
        {
            var m = messages[i];
            entries[i] = new MessageEntry(m.ShortId, m.RawId, m.Text);

            // An entry is "rare" when materializing it from (ShortId, RawId, Text) + the shared provider name would
            // not reproduce the original byte-for-byte: any non-null LogLink/Tag/Template, or a differing ProviderName.
            if (m.LogLink is not null ||
                m.Tag is not null ||
                m.Template is not null ||
                !string.Equals(m.ProviderName, providerName, StringComparison.Ordinal))
            {
                (rare ??= [])[i] = m;
            }
        }

        // Stable sort of indices by unsigned ShortId; ties keep ascending original ordinal so the per-ShortId run
        // preserves insertion order (load-bearing for legacy-message disambiguation).
        var sorted = new int[count];
        for (int i = 0; i < count; i++) { sorted[i] = i; }
        Array.Sort(sorted, (a, b) =>
        {
            int ka = (ushort)entries[a].ShortId, kb = (ushort)entries[b].ShortId;
            return ka != kb ? ka.CompareTo(kb) : a.CompareTo(b);
        });

        return new CompactMessageStore(entries, sorted, rare, providerName);
    }

    public IReadOnlyList<MessageModel> AsView() => new MessageView(this);

    public MessageModel? GetByRawIdFirst(long rawId) =>
        _rawIdCache.GetOrAdd(rawId, static (id, store) => store.MaterializeByRawId(id), this);

    public IReadOnlyList<MessageModel> GetByShortId(int shortId) =>
        // The arg is NOT cast to ushort: a >65535 arg must not wrap and false-match (matches the prior
        // Dictionary keyed by (ushort)ShortId and looked up by the raw int id).
        _byShortIdCache.GetOrAdd(shortId, static (id, store) => store.MaterializeByShortId(id), this);

    public IReadOnlyList<MessageModel> MaterializeAll() => [.. new MessageView(this)];

    private Dictionary<long, int> BuildRawIdLookup()
    {
        var lookup = new Dictionary<long, int>(_entries.Length);

        for (int i = 0; i < _entries.Length; i++) { lookup.TryAdd(_entries[i].RawId, i); }

        return lookup;
    }

    private int LowerBound(int key)
    {
        int lo = 0, hi = _sortedByShortId.Length;

        while (lo < hi)
        {
            int mid = lo + ((hi - lo) >> 1);

            if ((ushort)_entries[_sortedByShortId[mid]].ShortId < key) { lo = mid + 1; }
            else { hi = mid; }
        }

        return lo;
    }

    private MessageModel Materialize(int index)
    {
        if (_rareByIndex is not null && _rareByIndex.TryGetValue(index, out var full)) { return full; }

        var entry = _entries[index];

        return new MessageModel
        {
            Text = entry.Text,
            ShortId = entry.ShortId,
            ProviderName = _providerName,
            RawId = entry.RawId
        };
    }

    private MessageModel? MaterializeByRawId(long rawId) =>
        _firstIndexByRawId.Value.TryGetValue(rawId, out int index) ? Materialize(index) : null;

    private IReadOnlyList<MessageModel> MaterializeByShortId(int key)
    {
        // The sorted index groups all entries with a given unsigned ShortId contiguously; binary-search the run.
        int lo = LowerBound(key);

        if (lo == _sortedByShortId.Length || (ushort)_entries[_sortedByShortId[lo]].ShortId != key) { return s_empty; }

        var result = new List<MessageModel>();

        for (int i = lo; i < _sortedByShortId.Length && (ushort)_entries[_sortedByShortId[i]].ShortId == key; i++)
        {
            result.Add(Materialize(_sortedByShortId[i]));
        }

        return result;
    }

    private sealed class MessageView(CompactMessageStore store) : IReadOnlyList<MessageModel>
    {
        public int Count => store._entries.Length;

        public MessageModel this[int index] => store.Materialize(index);

        public IEnumerator<MessageModel> GetEnumerator()
        {
            for (int i = 0; i < store._entries.Length; i++) { yield return store.Materialize(i); }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    internal readonly record struct MessageEntry(short ShortId, long RawId, string Text);
}
