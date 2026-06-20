// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;
using System.Collections;
using System.Collections.Immutable;
using System.Collections.ObjectModel;

namespace EventLogExpert.Runtime.LogTable;

internal sealed class RawEventList : IReadOnlyList<ResolvedEvent>
{
    internal static readonly RawEventList Empty =
        new(ImmutableList<ReadOnlyCollection<ResolvedEvent>>.Empty, 0);

    private readonly ImmutableList<ReadOnlyCollection<ResolvedEvent>> _chunks;
    private readonly int _count;

    // Prefix-sum over chunk lengths (_prefix[i] = events before chunk i; length _chunks.Count + 1), built lazily
    // on first index access. Volatile-published; the instance is immutable so a concurrent recompute is benign.
    private int[]? _prefix;

    private RawEventList(ImmutableList<ReadOnlyCollection<ResolvedEvent>> chunks, int count)
    {
        _chunks = chunks;
        _count = count;
    }

    public int Count => _count;

    internal int ChunkCount => _chunks.Count;

    public ResolvedEvent this[int index]
    {
        get
        {
            if ((uint)index >= (uint)_count) { throw new ArgumentOutOfRangeException(nameof(index)); }

            var prefix = Prefix();
            int chunk = FindChunk(prefix, index);

            return _chunks[chunk][index - prefix[chunk]];
        }
    }

    public IEnumerator<ResolvedEvent> GetEnumerator()
    {
        foreach (var chunk in _chunks)
        {
            foreach (var resolvedEvent in chunk) { yield return resolvedEvent; }
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    internal RawEventList Append(IReadOnlyList<ResolvedEvent> events)
    {
        if (events.Count == 0) { return this; }

        var chunk = Freeze(events);

        return new RawEventList(_chunks.Add(chunk), _count + chunk.Count);
    }

    internal RawEventList Prepend(IReadOnlyList<ResolvedEvent> events)
    {
        if (events.Count == 0) { return this; }

        var chunk = Freeze(events);

        return new RawEventList(_chunks.Insert(0, chunk), _count + chunk.Count);
    }

    private static int FindChunk(int[] prefix, int index)
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

    // Reuse an already-frozen ReadOnlyCollection (the load/partial payloads are AsReadOnly snapshots the caller
    // owns and never mutates); otherwise copy so the stored chunk cannot be mutated by the caller.
    private static ReadOnlyCollection<ResolvedEvent> Freeze(IReadOnlyList<ResolvedEvent> events) =>
        events as ReadOnlyCollection<ResolvedEvent> ?? new ReadOnlyCollection<ResolvedEvent>([.. events]);

    private int[] Prefix()
    {
        var prefix = Volatile.Read(ref _prefix);

        if (prefix is not null) { return prefix; }

        prefix = new int[_chunks.Count + 1];

        for (int i = 0; i < _chunks.Count; i++) { prefix[i + 1] = prefix[i] + _chunks[i].Count; }

        Volatile.Write(ref _prefix, prefix);

        return prefix;
    }
}
