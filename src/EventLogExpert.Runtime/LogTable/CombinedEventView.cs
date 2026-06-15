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

            var cursors = SeekTo(index, out int position);

            while (position < index)
            {
                cursors[PickBest(cursors)]++;
                position++;
            }

            int best = PickBest(cursors);

            return _lists[best][cursors[best]];
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
        var cursors = new int[_lists.Length];

        while (true)
        {
            int best = PickBest(cursors);

            if (best < 0) { yield break; }

            yield return _lists[best][cursors[best]];

            cursors[best]++;
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

        var cursors = SeekTo(start, out int position);

        while (position < start)
        {
            cursors[PickBest(cursors)]++;
            position++;
        }

        var result = new ResolvedEvent[end - start];

        for (int i = 0; position < end; i++, position++)
        {
            int best = PickBest(cursors);
            result[i] = _lists[best][cursors[best]];
            cursors[best]++;
        }

        return result;
    }

    private int[][] BuildIndex()
    {
        var checkpoints = new List<int[]>(_count / Stride);
        var cursors = new int[_lists.Length];
        int produced = 0;

        while (true)
        {
            int best = PickBest(cursors);

            if (best < 0) { break; }

            cursors[best]++;
            produced++;

            if (produced % Stride == 0) { checkpoints.Add((int[])cursors.Clone()); }
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

    private int PickBest(int[] cursors)
    {
        int best = -1;

        for (int k = 0; k < _lists.Length; k++)
        {
            if (cursors[k] >= _lists[k].Count) { continue; }

            if (best < 0 || _comparer(_lists[k][cursors[k]], _lists[best][cursors[best]]) < 0) { best = k; }
        }

        return best;
    }

    private int[] SeekTo(int index, out int position)
    {
        if (index < Stride)
        {
            position = 0;

            return new int[_lists.Length];
        }

        var checkpoints = GetIndex();
        int checkpoint = index / Stride;
        position = checkpoint * Stride;

        return (int[])checkpoints[checkpoint - 1].Clone();
    }
}
