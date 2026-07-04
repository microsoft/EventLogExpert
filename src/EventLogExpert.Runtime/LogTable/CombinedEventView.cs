// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;
using System.Collections;
using System.Collections.Immutable;
using System.Diagnostics;

namespace EventLogExpert.Runtime.LogTable;

// Cross-log ties are impossible: OwningLog is the comparer's final tiebreak.
internal sealed class CombinedEventView : IReadOnlyList<ResolvedEvent>, IList<ResolvedEvent>
{
    // Offset scratch is stack-allocated per positional read; cap K so a many-log combined view can't overflow
    // the stack (heap fallback above the cap).
    private const int MaxStackOffsets = 256;
    private const int Stride = 64;

    private readonly Dictionary<string, SegmentedSortedList> _byOwningLog;
    private readonly Comparison<ResolvedEvent> _comparer;
    private readonly int _count;
    private readonly Lock _indexGate = new();
    private readonly ImmutableArray<SegmentedSortedList> _lists;

    private int[][]? _index;

    internal CombinedEventView(IEnumerable<SegmentedSortedList> perLogLists, SortContext context)
    {
        _comparer = ResolvedEventOrdering.SelectComparer(
            context.OrderBy, context.IsDescending, context.GroupBy, context.IsGroupDescending);

        var builder = ImmutableArray.CreateBuilder<SegmentedSortedList>();
        _byOwningLog = new Dictionary<string, SegmentedSortedList>(StringComparer.Ordinal);
        int total = 0;

        foreach (var list in perLogLists)
        {
            builder.Add(list);
            total += list.Count;

            if (list.Count > 0 && !_byOwningLog.TryAdd(list[0].OwningLog, list))
            {
                throw new UnreachableException($"Per-log lists share owning log '{list[0].OwningLog}'.");
            }
        }

        _lists = builder.ToImmutable();
        _count = total;
    }

    public int Count => _count;

    public bool IsReadOnly => true;

    internal static CombinedEventView Empty { get; } =
        new([], new SortContext(null, true, null, false));

    public ResolvedEvent this[int index]
    {
        get
        {
            if ((uint)index >= (uint)_count) { throw new ArgumentOutOfRangeException(nameof(index)); }

            int k = _lists.Length;
            Span<int> listOffsets = k <= MaxStackOffsets ? stackalloc int[k] : new int[k];
            int position = SeekTo(index, listOffsets);

            while (position < index)
            {
                listOffsets[PickBest(listOffsets)]++;
                position++;
            }

            int best = PickBest(listOffsets);

            return _lists[best][listOffsets[best]];
        }

        set => throw new NotSupportedException();
    }

    public void Add(ResolvedEvent item) => throw new NotSupportedException();

    public void Clear() => throw new NotSupportedException();

    public bool Contains(ResolvedEvent item) => Rank(item) >= 0;

    public void CopyTo(ResolvedEvent[] array, int arrayIndex)
    {
        ArgumentNullException.ThrowIfNull(array);
        ArgumentOutOfRangeException.ThrowIfNegative(arrayIndex);

        if (array.Length - arrayIndex < _count)
        {
            throw new ArgumentException("Destination array is not long enough.", nameof(array));
        }

        foreach (var resolved in this) { array[arrayIndex++] = resolved; }
    }

    public IEnumerator<ResolvedEvent> GetEnumerator()
    {
        var listOffsets = new int[_lists.Length];

        while (true)
        {
            int best = PickBest(listOffsets);

            if (best < 0) { yield break; }

            yield return _lists[best][listOffsets[best]];

            listOffsets[best]++;
        }
    }

    public int IndexOf(ResolvedEvent item) => Rank(item);

    public void Insert(int index, ResolvedEvent item) => throw new NotSupportedException();

    public bool Remove(ResolvedEvent item) => throw new NotSupportedException();

    public void RemoveAt(int index) => throw new NotSupportedException();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    internal int Rank(ResolvedEvent target)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (!_byOwningLog.TryGetValue(target.OwningLog, out var ownList)) { return -1; }

        int rank = 0;
        int ownOffset = -1;

        foreach (var list in _lists)
        {
            int lowerBound = LowerBound(list, target);

            if (!ReferenceEquals(list, ownList))
            {
                rank += lowerBound;

                continue;
            }

            // Match target in the equal-key run by reference, then RecordId+time+LogName (non-null).
            for (int offset = lowerBound; offset < list.Count && _comparer(list[offset], target) == 0; offset++)
            {
                var current = list[offset];

                if (ReferenceEquals(current, target) ||
                    (current.RecordId is not null && target.RecordId is not null &&
                        current.RecordId == target.RecordId && current.TimeCreated == target.TimeCreated &&
                        string.Equals(current.LogName, target.LogName, StringComparison.Ordinal)))
                {
                    ownOffset = offset;

                    break;
                }
            }

            if (ownOffset < 0) { return -1; }

            rank += ownOffset;
        }

        return rank;
    }

    internal ResolvedEvent? ResolveByKey(ResolvedEvent? candidate)
    {
        if (candidate is null) { return null; }

        if (!_byOwningLog.TryGetValue(candidate.OwningLog, out var list)) { return null; }

        int lowerBound = LowerBound(list, candidate);

        for (int offset = lowerBound; offset < list.Count && _comparer(list[offset], candidate) == 0; offset++)
        {
            var current = list[offset];

            if (ReferenceEquals(current, candidate)) { return current; }

            // Skip null RecordId: null == null would merge distinct error-read events.
            if (current.RecordId is null || candidate.RecordId is null) { continue; }

            if (current.RecordId == candidate.RecordId &&
                current.TimeCreated == candidate.TimeCreated &&
                string.Equals(current.LogName, candidate.LogName, StringComparison.Ordinal))
            {
                return current;
            }
        }

        return null;
    }

    internal IReadOnlyList<ResolvedEvent> Slice(int start, int count)
    {
        if (start < 0) { throw new ArgumentOutOfRangeException(nameof(start)); }

        if (count < 0) { throw new ArgumentOutOfRangeException(nameof(count)); }

        int end = (int)Math.Min((long)start + count, _count);

        if (start >= end) { return []; }

        int k = _lists.Length;
        Span<int> listOffsets = k <= MaxStackOffsets ? stackalloc int[k] : new int[k];
        int position = SeekTo(start, listOffsets);

        Span<int> segment = k <= MaxStackOffsets ? stackalloc int[k] : new int[k];
        Span<int> offset = k <= MaxStackOffsets ? stackalloc int[k] : new int[k];
        Span<int> heap = k <= MaxStackOffsets ? stackalloc int[k] : new int[k];
        var merger = new PerLogMerger(_lists.AsSpan(), _comparer, segment, offset, heap, listOffsets);

        while (position < start)
        {
            merger.AdvanceBest();
            position++;
        }

        var result = new ResolvedEvent[end - start];

        for (int i = 0; position < end; i++, position++)
        {
            result[i] = merger.Current(merger.PeekBest());
            merger.AdvanceBest();
        }

        return result;
    }

    private int[][] BuildIndex()
    {
        var checkpoints = new List<int[]>(_count / Stride);
        var listOffsets = new int[_lists.Length];
        int produced = 0;

        while (true)
        {
            int best = PickBest(listOffsets);

            if (best < 0) { break; }

            listOffsets[best]++;
            produced++;

            if (produced % Stride == 0) { checkpoints.Add((int[])listOffsets.Clone()); }
        }

        return [.. checkpoints];
    }

    private int[][] GetIndex()
    {
        var index = Volatile.Read(ref _index);

        if (index is not null) { return index; }

        lock (_indexGate)
        {
            _index ??= BuildIndex();

            return _index;
        }
    }

    private int LowerBound(SegmentedSortedList list, ResolvedEvent target)
    {
        int low = 0;
        int high = list.Count;

        while (low < high)
        {
            int mid = low + ((high - low) >> 1);

            if (_comparer(list[mid], target) < 0) { low = mid + 1; }
            else { high = mid; }
        }

        return low;
    }

    private int PickBest(ReadOnlySpan<int> listOffsets)
    {
        int best = -1;

        for (int k = 0; k < _lists.Length; k++)
        {
            if (listOffsets[k] >= _lists[k].Count) { continue; }

            if (best < 0 || _comparer(_lists[k][listOffsets[k]], _lists[best][listOffsets[best]]) < 0) { best = k; }
        }

        return best;
    }

    private int SeekTo(int index, Span<int> listOffsets)
    {
        if (index < Stride)
        {
            listOffsets.Clear();

            return 0;
        }

        var checkpoints = GetIndex();
        int checkpoint = index / Stride;
        checkpoints[checkpoint - 1].CopyTo(listOffsets);

        return checkpoint * Stride;
    }

    /// <summary>
    ///     Walks the per-log lists as one globally-sorted stream via per-list positions and a min-heap of the active
    ///     heads, byte-identical to <see cref="PickBest" /> (ties broken by ascending list index). All state is
    ///     caller-provided <c>stackalloc</c> spans, so the merge allocates nothing, and a <see langword="ref" /> struct so it
    ///     cannot escape or be shared. Parity relies on the comparer being a strict weak ordering.
    /// </summary>
    private ref struct PerLogMerger
    {
        private readonly ReadOnlySpan<SegmentedSortedList> _lists;
        private readonly Comparison<ResolvedEvent> _comparer;
        private readonly Span<int> _segment;
        private readonly Span<int> _offset;
        private readonly Span<int> _heap;
        private int _count;

        internal PerLogMerger(
            ReadOnlySpan<SegmentedSortedList> lists,
            Comparison<ResolvedEvent> comparer,
            Span<int> segment,
            Span<int> offset,
            Span<int> heap,
            ReadOnlySpan<int> flatOffsets)
        {
            _lists = lists;
            _comparer = comparer;
            _segment = segment;
            _offset = offset;
            _heap = heap;
            _count = 0;

            for (int list = 0; list < lists.Length; list++)
            {
                if (lists[list].TryGetSegmentOffset(flatOffsets[list], out int seg, out int off))
                {
                    _segment[list] = seg;
                    _offset[list] = off;
                    Push(list);
                }
            }
        }

        internal readonly int PeekBest() => _count > 0 ? _heap[0] : -1;

        internal readonly ResolvedEvent Current(int list) => _lists[list].GetAt(_segment[list], _offset[list]);

        internal void AdvanceBest()
        {
            Debug.Assert(_count > 0, "AdvanceBest called on an empty heap.");

            int best = _heap[0];

            if (_lists[best].TryAdvance(ref _segment[best], ref _offset[best]))
            {
                // The root's key only increased, so a single sift-down from the root restores the heap.
                SiftDown(0);
            }
            else
            {
                _count--;

                if (_count > 0)
                {
                    _heap[0] = _heap[_count];
                    SiftDown(0);
                }
            }
        }

        private void Push(int list)
        {
            int child = _count++;
            _heap[child] = list;

            while (child > 0)
            {
                int parent = (child - 1) >> 1;

                if (!Less(_heap[child], _heap[parent])) { break; }

                (_heap[parent], _heap[child]) = (_heap[child], _heap[parent]);
                child = parent;
            }
        }

        private void SiftDown(int parent)
        {
            while (true)
            {
                int smallest = parent;
                int left = (parent << 1) + 1;
                int right = left + 1;

                if (left < _count && Less(_heap[left], _heap[smallest])) { smallest = left; }

                if (right < _count && Less(_heap[right], _heap[smallest])) { smallest = right; }

                if (smallest == parent) { break; }

                (_heap[parent], _heap[smallest]) = (_heap[smallest], _heap[parent]);
                parent = smallest;
            }
        }

        // Strict total order over list indices: the full comparer on the two heads, ties broken by ascending list
        // index (earliest wins, matching PickBest's strict-< scan).
        private readonly bool Less(int a, int b)
        {
            int comparison = _comparer(Current(a), Current(b));

            return comparison != 0 ? comparison < 0 : a < b;
        }
    }
}
