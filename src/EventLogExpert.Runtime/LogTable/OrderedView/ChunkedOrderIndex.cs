// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;

namespace EventLogExpert.Runtime.LogTable.OrderedView;

internal sealed class ChunkedOrderIndex
{
    private const int Capacity = 1024;

    private readonly List<OrderKey[]> _buffers = [];
    private readonly List<int> _counts = [];
    private readonly Dictionary<LogGeneration, ulong[]> _present = [];
    private readonly HashSet<LogGeneration> _presentClonedThisBatch = [];
    private readonly List<OrderKey[]?> _sealed = [];

    private IComparer<OrderKey> _insertComparer;

    internal ChunkedOrderIndex(IComparer<OrderKey> insertComparer) => _insertComparer = insertComparer;

    public int Count
    {
        get
        {
            int total = 0;

            foreach (int count in _counts) { total += count; }

            return total;
        }
    }

    public void Insert(in OrderKey key)
    {
        SetPresent(key.Locator);

        if (_buffers.Count == 0)
        {
            var first = new OrderKey[Capacity];
            first[0] = key;
            _buffers.Add(first);
            _counts.Add(1);
            _sealed.Add(null);
            return;
        }

        int chunk = ChunkForKey(key);

        if (_counts[chunk] == Capacity)
        {
            SplitChunk(chunk);
            chunk = ChunkForKey(key);
        }

        InsertIntoChunk(chunk, key);
    }

    public OrderedViewSnapshot Publish(IComparer<OrderKey> snapshotComparer, long version)
    {
        int chunkCount = _buffers.Count;

        var present = new Dictionary<LogGeneration, ulong[]>(_present);
        _presentClonedThisBatch.Clear();

        if (chunkCount == 0)
        {
            return new OrderedViewSnapshot([], [], [0], present, snapshotComparer, version);
        }

        var chunks = new OrderKey[chunkCount][];
        var firstOfChunk = new OrderKey[chunkCount];
        var prefix = new int[chunkCount + 1];
        int accumulated = 0;

        for (int chunk = 0; chunk < chunkCount; chunk++)
        {
            var frozen = _sealed[chunk];

            if (frozen is null)
            {
                frozen = new OrderKey[_counts[chunk]];
                Array.Copy(_buffers[chunk], 0, frozen, 0, _counts[chunk]);
                _sealed[chunk] = frozen;
            }

            chunks[chunk] = frozen;
            firstOfChunk[chunk] = frozen[0];
            prefix[chunk] = accumulated;
            accumulated += frozen.Length;
        }

        prefix[chunkCount] = accumulated;

        return new OrderedViewSnapshot(chunks, firstOfChunk, prefix, present, snapshotComparer, version);
    }

    internal static ChunkedOrderIndex FromSortedRun(
        OrderKey[] sortedOrder,
        IComparer<OrderKey> insertComparer,
        CancellationToken cancellationToken = default)
    {
        var index = new ChunkedOrderIndex(insertComparer);

        index.BulkFill(sortedOrder,
            cancellationToken);

        return index;
    }

    internal void RebindInsertComparer(IComparer<OrderKey> insertComparer) => _insertComparer = insertComparer;

    private void BulkFill(OrderKey[] sortedOrder, CancellationToken cancellationToken)
    {
        if (sortedOrder.Length == 0) { return; }

        var maxIndexByKey = new Dictionary<LogGeneration, int>();

        for (int position = 0; position < sortedOrder.Length; position++)
        {
            if ((position & 8191) == 0) { cancellationToken.ThrowIfCancellationRequested(); }

            var generationKey = new LogGeneration(sortedOrder[position].Locator.LogId, sortedOrder[position].Locator.Generation);
            int index = sortedOrder[position].Locator.Index;

            if (!maxIndexByKey.TryGetValue(generationKey, out int max) || index > max) { maxIndexByKey[generationKey] = index; }
        }

        foreach ((LogGeneration generationKey, int maxIndex) in maxIndexByKey)
        {
            _present[generationKey] = new ulong[Math.Max((maxIndex >> 6) + 1, 4)];
            _presentClonedThisBatch.Add(generationKey);
        }

        for (int position = 0; position < sortedOrder.Length; position++)
        {
            if ((position & 8191) == 0) { cancellationToken.ThrowIfCancellationRequested(); }

            EventLocator locator = sortedOrder[position].Locator;
            var generationKey = new LogGeneration(locator.LogId, locator.Generation);
            _present[generationKey][locator.Index >> 6] |= 1UL << (locator.Index & 63);
        }

        for (int offset = 0; offset < sortedOrder.Length; offset += Capacity)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int take = Math.Min(Capacity, sortedOrder.Length - offset);
            var buffer = new OrderKey[Capacity];
            Array.Copy(sortedOrder, offset, buffer, 0, take);
            _buffers.Add(buffer);
            _counts.Add(take);
            _sealed.Add(null);
        }
    }

    private int ChunkForKey(in OrderKey key)
    {
        int low = 0, high = _buffers.Count - 1, answer = 0;

        while (low <= high)
        {
            int mid = (low + high) >> 1;

            if (_insertComparer.Compare(key, _buffers[mid][0]) >= 0)
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

    private void InsertIntoChunk(int chunk, in OrderKey key)
    {
        var buffer = _buffers[chunk];
        int count = _counts[chunk];
        int position = LowerBound(buffer, count, key);

        Array.Copy(buffer, position, buffer, position + 1, count - position);
        buffer[position] = key;
        _counts[chunk] = count + 1;
        _sealed[chunk] = null;
    }

    private int LowerBound(OrderKey[] buffer, int count, in OrderKey key)
    {
        int low = 0, high = count;

        while (low < high)
        {
            int mid = (int)(((uint)low + (uint)high) >> 1);

            if (_insertComparer.Compare(buffer[mid], key) < 0) { low = mid + 1; }
            else { high = mid; }
        }

        return low;
    }

    private void SetPresent(in EventLocator locator)
    {
        var key = new LogGeneration(locator.LogId, locator.Generation);
        int index = locator.Index;
        int word = index >> 6;

        if (!_present.TryGetValue(key, out var bits))
        {
            bits = new ulong[Math.Max(word + 1, 4)];
            _present[key] = bits;
            _presentClonedThisBatch.Add(key);
        }
        else
        {
            if (!_presentClonedThisBatch.Contains(key))
            {
                bits = (ulong[])bits.Clone();
                _present[key] = bits;
                _presentClonedThisBatch.Add(key);
            }

            if (word >= bits.Length)
            {
                Array.Resize(ref bits, Math.Max(word + 1, bits.Length * 2));
                _present[key] = bits;
            }
        }

        bits[word] |= 1UL << (index & 63);
    }

    private void SplitChunk(int chunk)
    {
        var buffer = _buffers[chunk];
        int half = Capacity / 2;
        var left = new OrderKey[Capacity];
        var right = new OrderKey[Capacity];

        Array.Copy(buffer, 0, left, 0, half);
        Array.Copy(buffer, half, right, 0, Capacity - half);

        _buffers[chunk] = left;
        _counts[chunk] = half;
        _sealed[chunk] = null;

        _buffers.Insert(chunk + 1, right);
        _counts.Insert(chunk + 1, Capacity - half);
        _sealed.Insert(chunk + 1, null);
    }
}
