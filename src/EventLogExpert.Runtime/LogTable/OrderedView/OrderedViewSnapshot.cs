// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using System.Diagnostics.CodeAnalysis;

namespace EventLogExpert.Runtime.LogTable.OrderedView;

internal sealed class OrderedViewSnapshot
{
    private readonly OrderKey[][] _chunks;
    private readonly IComparer<OrderKey> _comparer;
    private readonly OrderKey[] _firstOfChunk;
    private readonly int[] _prefix;
    private readonly Dictionary<LogGeneration, ulong[]> _presentByLogGeneration;

    internal OrderedViewSnapshot(
        OrderKey[][] chunks,
        OrderKey[] firstOfChunk,
        int[] prefix,
        Dictionary<LogGeneration, ulong[]> presentByLogGeneration,
        IComparer<OrderKey> comparer,
        long version)
    {
        _chunks = chunks;
        _firstOfChunk = firstOfChunk;
        _prefix = prefix;
        _presentByLogGeneration = presentByLogGeneration;
        _comparer = comparer;
        Version = version;
    }

    public static OrderedViewSnapshot Empty { get; } =
        new([], [], [0], [], OrderKeyComparerFactory.Empty, 0);

    public int Count => _prefix.Length == 0 ? 0 : _prefix[^1];

    public long Version { get; }

    internal int PinnedReaderCount => (_comparer as DelegatingOrderKeyComparer)?.PinnedReaderCount ?? 0;

    public OrderKey At(int displayIndex)
    {
        int chunk = ChunkForOffset(displayIndex);

        return _chunks[chunk][displayIndex - _prefix[chunk]];
    }

    public bool Contains(EventLogId logId, int generation, int index)
    {
        if (index < 0 || !_presentByLogGeneration.TryGetValue(new LogGeneration(logId, generation), out var bits))
        {
            return false;
        }

        int word = index >> 6;

        if ((uint)word >= (uint)bits.Length) { return false; }

        return (bits[word] & (1UL << (index & 63))) != 0;
    }

    public int RankOf(in OrderKey key)
    {
        EventLocator locator = key.Locator;

        if (_chunks.Length == 0 || !Contains(locator.LogId, locator.Generation, locator.Index)) { return -1; }

        int chunk = ChunkForKey(key);
        var keys = _chunks[chunk];
        int within = LowerBound(keys, key);

        return within < keys.Length && _comparer.Compare(keys[within], key) == 0 ? _prefix[chunk] + within : -1;
    }

    public int SliceInto(int offset, int width, OrderKey[] outBuffer)
    {
        int total = Count;

        if (offset < 0 || offset >= total || width <= 0) { return 0; }

        int need = Math.Min(width, total - offset);
        int written = 0;
        int chunk = ChunkForOffset(offset);

        while (written < need && chunk < _chunks.Length)
        {
            int inChunkStart = (offset + written) - _prefix[chunk];
            int available = _chunks[chunk].Length - inChunkStart;

            if (available > 0)
            {
                int take = Math.Min(available, need - written);
                Array.Copy(_chunks[chunk], inChunkStart, outBuffer, written, take);
                written += take;
            }

            chunk++;
        }

        return written;
    }

    internal bool TryGetReader(in EventLocator locator, [NotNullWhen(true)] out IEventColumnReader? reader)
    {
        if (_comparer is DelegatingOrderKeyComparer delegating) { return delegating.Resolver.TryResolve(locator, out reader); }

        reader = null;

        return false;
    }

    internal bool TryGetReaderByLog(
        EventLogId logId,
        int generation,
        [NotNullWhen(true)] out IEventColumnReader? reader)
    {
        if (_comparer is DelegatingOrderKeyComparer delegating)
        {
            return delegating.Resolver.TryResolveByLog(logId, generation, out reader);
        }

        reader = null;

        return false;
    }

    private int ChunkForKey(in OrderKey key)
    {
        int low = 0, high = _firstOfChunk.Length - 1, answer = 0;

        while (low <= high)
        {
            int mid = (low + high) >> 1;

            if (_comparer.Compare(key, _firstOfChunk[mid]) >= 0)
            {
                answer = mid;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        return answer;
    }

    private int ChunkForOffset(int offset)
    {
        int low = 0, high = _prefix.Length - 2, answer = 0;

        while (low <= high)
        {
            int mid = (low + high) >> 1;

            if (_prefix[mid] <= offset)
            {
                answer = mid;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        return answer;
    }

    private int LowerBound(OrderKey[] keys, in OrderKey key)
    {
        int low = 0, high = keys.Length;

        while (low < high)
        {
            int mid = (int)(((uint)low + (uint)high) >> 1);

            if (_comparer.Compare(keys[mid], key) < 0) { low = mid + 1; }
            else { high = mid; }
        }

        return low;
    }
}
