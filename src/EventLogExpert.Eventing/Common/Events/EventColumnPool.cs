// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Collections.Immutable;

namespace EventLogExpert.Eventing.Common.Events;

/// <summary>
///     An immutable, append-only interned string pool that rides inside an <see cref="EventColumnStore" /> snapshot.
///     Distinct values live in chunked <see cref="string" /> segments addressed by a lazily built prefix-sum for O(log
///     &#160;segments) <see cref="Get" />; an ordinal <see cref="ImmutableDictionary{TKey,TValue}" /> map backs
///     append-time interning. Indices are never reused or renumbered, so a published pool stays valid as later snapshots
///     extend it, and no mutable state is shared across snapshots.
/// </summary>
internal sealed class EventColumnPool
{
    internal static readonly EventColumnPool Empty = new(
        ImmutableList<string[]>.Empty,
        ImmutableDictionary.Create<string, int>(StringComparer.Ordinal));
    private readonly ImmutableDictionary<string, int> _map;

    private readonly ImmutableList<string[]> _segments;

    // Prefix-sum over segment lengths (_prefix[i] = values before segment i; length _segments.Count + 1), built lazily
    // on first index access. Volatile-published; the instance is immutable so a concurrent recompute is benign.
    private int[]? _prefix;

    private EventColumnPool(ImmutableList<string[]> segments, ImmutableDictionary<string, int> map)
    {
        _segments = segments;
        _map = map;
    }

    internal int DistinctCount => _map.Count;

    /// <summary>Starts a call-local interning session over this pool; see <see cref="Builder" />.</summary>
    internal Builder CreateBuilder() => new(this);

    /// <summary>Returns the interned value at <paramref name="index" />, or <c>null</c> when <paramref name="index" /> is -1.</summary>
    internal string? Get(int index)
    {
        if (index < 0) { return null; }

        int[] prefix = Prefix();

        if ((uint)index >= (uint)prefix[^1]) { throw new ArgumentOutOfRangeException(nameof(index)); }

        int segment = FindSegment(prefix, index);

        return _segments[segment][index - prefix[segment]];
    }

    /// <summary>The stable index interned for <paramref name="value" />, or <c>false</c> when it was never interned.</summary>
    internal bool TryGetIndex(string value, out int index) => _map.TryGetValue(value, out index);

    private static int FindSegment(int[] prefix, int index)
    {
        int low = 0;
        int high = prefix.Length - 2;

        while (low < high)
        {
            int mid = (low + high + 1) >> 1;

            if (prefix[mid] <= index) { low = mid; }
            else { high = mid - 1; }
        }

        return low;
    }

    private int[] Prefix()
    {
        int[]? prefix = Volatile.Read(ref _prefix);

        if (prefix is not null) { return prefix; }

        prefix = new int[_segments.Count + 1];

        for (int i = 0; i < _segments.Count; i++) { prefix[i + 1] = prefix[i] + _segments[i].Length; }

        Volatile.Write(ref _prefix, prefix);

        return prefix;
    }

    /// <summary>
    ///     A call-local, mutable interning session over a base pool. <see cref="Intern" /> assigns a stable index to each
    ///     distinct new value (a <c>null</c> value yields -1); <see cref="ToPool" /> seals the batch's new distinct values
    ///     into ONE new segment and returns a new immutable pool that structurally shares the base's segments and map. The
    ///     session is discarded when the build returns, so nothing mutable outlives a snapshot.
    /// </summary>
    internal sealed class Builder
    {
        private readonly int _baseCount;
        private readonly EventColumnPool _basePool;
        private readonly ImmutableDictionary<string, int>.Builder _map;
        private readonly List<string> _newValues = [];

        internal Builder(EventColumnPool basePool)
        {
            _basePool = basePool;
            _map = basePool._map.ToBuilder();
            _baseCount = basePool._map.Count;
        }

        internal int Intern(string? value)
        {
            if (value is null) { return -1; }

            if (_map.TryGetValue(value, out int existing)) { return existing; }

            int index = _baseCount + _newValues.Count;
            _map.Add(value, index);
            _newValues.Add(value);

            return index;
        }

        internal EventColumnPool ToPool()
        {
            if (_newValues.Count == 0) { return _basePool; }

            return new EventColumnPool(_basePool._segments.Add([.. _newValues]), _map.ToImmutable());
        }
    }
}
