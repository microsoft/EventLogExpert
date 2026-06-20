// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;
using System.Collections;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.LogTable;

internal readonly record struct SortContext
{
    internal SortContext(ColumnName? orderBy, bool isDescending, ColumnName? groupBy, bool isGroupDescending)
    {
        // When grouped, the comparer treats a null order-by as DateAndTime, so normalize so null and DateAndTime
        // are the same context/comparer. Ungrouped null stays RecordId-sorted.
        OrderBy = groupBy is null ? orderBy : orderBy ?? ColumnName.DateAndTime;
        IsDescending = isDescending;
        GroupBy = groupBy;
        IsGroupDescending = isGroupDescending;
    }

    internal ColumnName? OrderBy { get; }

    internal bool IsDescending { get; }

    internal ColumnName? GroupBy { get; }

    internal bool IsGroupDescending { get; }
}

// Immutable sorted segments kept globally non-interleaving (segment i entirely <= segment i+1), so the logical
// sequence is their concatenation and indexing is a prefix-sum binary search. Appends that sit before/after
// existing add a segment (no copy); interleaving appends fall back to a full merge.
internal sealed class SegmentedSortedList : IReadOnlyList<ResolvedEvent>, IList<ResolvedEvent>
{
    private readonly Comparison<ResolvedEvent> _comparer;
    private readonly SortContext _context;
    private readonly int[] _prefix;
    private readonly ImmutableArray<ImmutableArray<ResolvedEvent>> _segments;

    private SegmentedSortedList(
        ImmutableArray<ImmutableArray<ResolvedEvent>> segments,
        SortContext context,
        Comparison<ResolvedEvent> comparer)
    {
        _segments = segments;
        _context = context;
        _comparer = comparer;
        _prefix = BuildPrefix(segments);
    }

    public int Count => _prefix[^1];

    public bool IsReadOnly => true;

    internal SortContext Context => _context;

    internal int SegmentCount => _segments.Length;

    public ResolvedEvent this[int index]
    {
        get
        {
            if ((uint)index >= (uint)Count) { throw new ArgumentOutOfRangeException(nameof(index)); }

            int segment = FindSegment(index);

            return _segments[segment][index - _prefix[segment]];
        }

        set => throw new NotSupportedException();
    }

    public void Add(ResolvedEvent item) => throw new NotSupportedException();

    public void Clear() => throw new NotSupportedException();

    public bool Contains(ResolvedEvent item)
    {
        foreach (var segment in _segments)
        {
            if (segment.Contains(item)) { return true; }
        }

        return false;
    }

    public void CopyTo(ResolvedEvent[] array, int arrayIndex)
    {
        ArgumentNullException.ThrowIfNull(array);
        ArgumentOutOfRangeException.ThrowIfNegative(arrayIndex);

        if (array.Length - arrayIndex < Count)
        {
            throw new ArgumentException("Destination array is not long enough.", nameof(array));
        }

        foreach (var segment in _segments)
        {
            segment.CopyTo(array, arrayIndex);
            arrayIndex += segment.Length;
        }
    }

    public IEnumerator<ResolvedEvent> GetEnumerator()
    {
        foreach (var segment in _segments)
        {
            foreach (var resolved in segment) { yield return resolved; }
        }
    }

    public int IndexOf(ResolvedEvent item)
    {
        int index = 0;

        foreach (var resolved in this)
        {
            if (EqualityComparer<ResolvedEvent>.Default.Equals(resolved, item)) { return index; }

            index++;
        }

        return -1;
    }

    public void Insert(int index, ResolvedEvent item) => throw new NotSupportedException();

    public bool Remove(ResolvedEvent item) => throw new NotSupportedException();

    public void RemoveAt(int index) => throw new NotSupportedException();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    internal static SegmentedSortedList CreateSorted(IEnumerable<ResolvedEvent> events, SortContext context)
    {
        var comparer = ResolvedEventOrdering.SelectComparer(
            context.OrderBy, context.IsDescending, context.GroupBy, context.IsGroupDescending);

        var sorted = new List<ResolvedEvent>(events);
        sorted.Sort(comparer);

        ImmutableArray<ImmutableArray<ResolvedEvent>> segments =
            sorted.Count == 0 ? [] : [[..sorted]];

        return new SegmentedSortedList(segments, context, comparer);
    }

    internal static SegmentedSortedList MergeFrom(
        IReadOnlyList<ResolvedEvent> existing,
        IReadOnlyList<ResolvedEvent> batch,
        SortContext context)
    {
        if (existing.Count == 0) { return CreateSorted(batch, context); }

        var comparer = ResolvedEventOrdering.SelectComparer(
            context.OrderBy, context.IsDescending, context.GroupBy, context.IsGroupDescending);

        var sortedBatch = SortBatch(batch, comparer);
        var merged = Merge(existing, sortedBatch, comparer);

        return new SegmentedSortedList([merged], context, comparer);
    }

    internal SegmentedSortedList Append(IReadOnlyList<ResolvedEvent> batch)
    {
        if (batch.Count == 0) { return this; }

        var sortedBatch = SortBatch(batch, _comparer);

        if (_segments.IsEmpty)
        {
            return new SegmentedSortedList([sortedBatch], _context, _comparer);
        }

        // Strict < for prepend but <= for append: MergeSorted is left-stable (existing wins ties), so a batch
        // that ties with the head must not jump ahead - it falls through to the full merge.
        if (_comparer(sortedBatch[^1], _segments[0][0]) < 0)
        {
            return new SegmentedSortedList(_segments.Insert(0, sortedBatch), _context, _comparer);
        }

        if (_comparer(_segments[^1][^1], sortedBatch[0]) <= 0)
        {
            return new SegmentedSortedList(_segments.Add(sortedBatch), _context, _comparer);
        }

        var merged = MergeWith(sortedBatch);

        return new SegmentedSortedList([merged], _context, _comparer);
    }

    internal bool HasContext(SortContext context) => _context == context;

    internal int Rank(ResolvedEvent target)
    {
        ArgumentNullException.ThrowIfNull(target);

        int low = 0;
        int high = Count;

        while (low < high)
        {
            int mid = low + ((high - low) >> 1);

            if (_comparer(this[mid], target) < 0) { low = mid + 1; }
            else { high = mid; }
        }

        // Scan the comparer-equal window for the target: by reference, then by key (RecordId + time + log) so a
        // reloaded/cloned instance still ranks - matching this type's own ResolveByKey and CombinedEventView.Rank.
        for (int i = low; i < Count && _comparer(this[i], target) == 0; i++)
        {
            var current = this[i];

            if (ReferenceEquals(current, target)) { return i; }

            // Skip null RecordId: null == null would merge distinct error-read events.
            if (current.RecordId is null || target.RecordId is null) { continue; }

            if (current.RecordId == target.RecordId &&
                current.TimeCreated == target.TimeCreated &&
                string.Equals(current.OwningLog, target.OwningLog, StringComparison.Ordinal) &&
                string.Equals(current.LogName, target.LogName, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    internal ResolvedEvent? ResolveByKey(ResolvedEvent candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        int low = 0;
        int high = Count;

        while (low < high)
        {
            int mid = low + ((high - low) >> 1);

            if (_comparer(this[mid], candidate) < 0) { low = mid + 1; }
            else { high = mid; }
        }

        for (int i = low; i < Count && _comparer(this[i], candidate) == 0; i++)
        {
            var current = this[i];

            if (ReferenceEquals(current, candidate)) { return current; }

            // Skip null RecordId: null == null would merge distinct error-read events.
            if (current.RecordId is null || candidate.RecordId is null) { continue; }

            if (current.RecordId == candidate.RecordId &&
                current.TimeCreated == candidate.TimeCreated &&
                string.Equals(current.OwningLog, candidate.OwningLog, StringComparison.Ordinal) &&
                string.Equals(current.LogName, candidate.LogName, StringComparison.Ordinal))
            {
                return current;
            }
        }

        return null;
    }

    internal SegmentedSortedList WhereSegmented(Func<ResolvedEvent, bool> predicate)
    {
        var builder = ImmutableArray.CreateBuilder<ImmutableArray<ResolvedEvent>>(_segments.Length);

        foreach (var segment in _segments)
        {
            var kept = ImmutableArray.CreateBuilder<ResolvedEvent>(segment.Length);

            foreach (var resolved in segment)
            {
                if (predicate(resolved)) { kept.Add(resolved); }
            }

            if (kept.Count > 0) { builder.Add(kept.ToImmutable()); }
        }

        return new SegmentedSortedList(builder.ToImmutable(), _context, _comparer);
    }

    private static int[] BuildPrefix(ImmutableArray<ImmutableArray<ResolvedEvent>> segments)
    {
        var prefix = new int[segments.Length + 1];

        for (int i = 0; i < segments.Length; i++)
        {
            prefix[i + 1] = prefix[i] + segments[i].Length;
        }

        return prefix;
    }

    private static ImmutableArray<ResolvedEvent> Merge(
        IReadOnlyList<ResolvedEvent> existing,
        IReadOnlyList<ResolvedEvent> sortedBatch,
        Comparison<ResolvedEvent> comparer)
    {
        var result = ImmutableArray.CreateBuilder<ResolvedEvent>(existing.Count + sortedBatch.Count);
        int i = 0, j = 0;

        while (i < existing.Count && j < sortedBatch.Count)
        {
            result.Add(comparer(existing[i], sortedBatch[j]) <= 0 ? existing[i++] : sortedBatch[j++]);
        }

        while (i < existing.Count) { result.Add(existing[i++]); }

        while (j < sortedBatch.Count) { result.Add(sortedBatch[j++]); }

        return result.MoveToImmutable();
    }

    private static ImmutableArray<ResolvedEvent> SortBatch(IReadOnlyList<ResolvedEvent> batch, Comparison<ResolvedEvent> comparer)
    {
        var sorted = new List<ResolvedEvent>(batch);
        sorted.Sort(comparer);

        return [..sorted];
    }

    private int FindSegment(int index)
    {
        int low = 0;
        int high = _segments.Length - 1;

        while (low < high)
        {
            int mid = (low + high + 1) >> 1;

            if (_prefix[mid] <= index) { low = mid; }
            else { high = mid - 1; }
        }

        return low;
    }

    private ImmutableArray<ResolvedEvent> MergeWith(ImmutableArray<ResolvedEvent> sortedBatch)
    {
        var result = ImmutableArray.CreateBuilder<ResolvedEvent>(Count + sortedBatch.Length);

        using var existing = GetEnumerator();
        bool hasExisting = existing.MoveNext();
        int j = 0;

        while (hasExisting && j < sortedBatch.Length)
        {
            if (_comparer(existing.Current, sortedBatch[j]) <= 0)
            {
                result.Add(existing.Current);
                hasExisting = existing.MoveNext();
            }
            else
            {
                result.Add(sortedBatch[j++]);
            }
        }

        while (hasExisting)
        {
            result.Add(existing.Current);
            hasExisting = existing.MoveNext();
        }

        while (j < sortedBatch.Length) { result.Add(sortedBatch[j++]); }

        return result.MoveToImmutable();
    }
}
