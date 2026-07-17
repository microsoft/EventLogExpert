// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace EventLogExpert.Runtime.LogTable;

/// <summary>
///     The multi-log display facade: a K-way column-direct merge of several per-log <see cref="EventColumnView" />s
///     into one globally sorted stream, comparing head-to-head off each row's own reader so no event objects are
///     rehydrated to merge. Cross-log ties are impossible (each sub-view is a distinct log and OwningLog is the comparer's
///     final tiebreak).
/// </summary>
internal sealed class CombinedColumnView : IEventColumnView
{
    // Offset scratch is stack-allocated per positional read; cap K so a many-log combined view can't overflow the
    // stack (heap fallback above the cap).
    private const int MaxStackOffsets = 256;
    private const int Stride = 64;

    private readonly Dictionary<EventLogId, EventColumnView> _byLog;
    private readonly ResolvedEventOrdering.CrossComparison _compare;
    private readonly int _count;
    private readonly Lock _indexGate = new();
    private readonly EventColumnView[] _views;

    private int[][]? _index;

    internal CombinedColumnView(IReadOnlyList<EventColumnView> views, SortContext context)
    {
        ArgumentNullException.ThrowIfNull(views);

        _compare = ResolvedEventOrdering.SelectCrossColumnComparer(
            context.OrderBy, context.IsDescending, context.GroupBy, context.IsGroupDescending);

        _views = [.. views];
        _byLog = new Dictionary<EventLogId, EventColumnView>(_views.Length);

        int total = 0;

        foreach (var view in _views)
        {
            total += view.Count;

            if (!_byLog.TryAdd(view.Reader.LogId, view))
            {
                throw new UnreachableException($"Per-log views share log id '{view.Reader.LogId}'.");
            }
        }

        _count = total;
    }

    public int Count => _count;

    internal static CombinedColumnView Empty { get; } = new([], new SortContext(null, true, null, false));

    public void BucketTimeTicksByEventData(
        long minTicks,
        long bucketSpanTicks,
        int bucketCount,
        string fieldName,
        long[] targetCodes,
        int[] slotCounts,
        CancellationToken cancellationToken)
    {
        foreach (var view in _views)
        {
            view.BucketTimeTicksByEventData(minTicks,
                bucketSpanTicks,
                bucketCount,
                fieldName,
                targetCodes,
                slotCounts,
                cancellationToken);
        }
    }

    public void BucketTimeTicksByEventId(
        long minTicks,
        long bucketSpanTicks,
        int bucketCount,
        int[] targetIds,
        int[] slotCounts,
        CancellationToken cancellationToken)
    {
        foreach (var view in _views)
        {
            view.BucketTimeTicksByEventId(minTicks,
                bucketSpanTicks,
                bucketCount,
                targetIds,
                slotCounts,
                cancellationToken);
        }
    }

    public void BucketTimeTicksByField(
        long minTicks,
        long bucketSpanTicks,
        int bucketCount,
        EventFieldId field,
        string[] targetValues,
        int[] slotCounts,
        CancellationToken cancellationToken)
    {
        foreach (var view in _views)
        {
            view.BucketTimeTicksByField(minTicks,
                bucketSpanTicks,
                bucketCount,
                field,
                targetValues,
                slotCounts,
                cancellationToken);
        }
    }

    public void BucketTimeTicksBySeverity(
        long minTicks,
        long bucketSpanTicks,
        int bucketCount,
        int[] slotCounts,
        CancellationToken cancellationToken)
    {
        foreach (var view in _views)
        {
            view.BucketTimeTicksBySeverity(minTicks, bucketSpanTicks, bucketCount, slotCounts, cancellationToken);
        }
    }

    public void CountEventDataValues(string fieldName, IDictionary<long, int> counts, CancellationToken cancellationToken)
    {
        foreach (var view in _views)
        {
            view.CountEventDataValues(fieldName, counts, cancellationToken);
        }
    }

    public void CountEventIds(IDictionary<int, int> counts, CancellationToken cancellationToken)
    {
        foreach (var view in _views)
        {
            view.CountEventIds(counts, cancellationToken);
        }
    }

    public void CountFieldValues(EventFieldId field, IDictionary<string, int> counts, CancellationToken cancellationToken)
    {
        foreach (var view in _views)
        {
            view.CountFieldValues(field, counts, cancellationToken);
        }
    }

    public IEnumerable<ResolvedEvent> EnumerateDetail()
    {
        var offsets = new int[_views.Length];

        while (true)
        {
            int best = PickBest(offsets);

            if (best < 0) { yield break; }

            var view = _views[best];
            var locator = view.LocatorAt(offsets[best]);

            yield return view.GetDetail(locator);

            offsets[best]++;
        }
    }

    public ResolvedEvent GetDetail(EventLocator locator) => Route(locator).GetDetail(locator);

    public ResolvedEvent GetDetailLean(EventLocator locator) => Route(locator).GetDetailLean(locator);

    public string GroupKeyAt(EventLocator locator, ColumnName column) => Route(locator).GroupKeyAt(locator, column);

    public EventLocator LocatorAt(int index)
    {
        if ((uint)index >= (uint)_count) { throw new ArgumentOutOfRangeException(nameof(index)); }

        int k = _views.Length;
        Span<int> offsets = k <= MaxStackOffsets ? stackalloc int[k] : new int[k];
        int position = SeekTo(index, offsets);

        while (position < index)
        {
            offsets[PickBest(offsets)]++;
            position++;
        }

        int best = PickBest(offsets);

        return _views[best].LocatorAt(offsets[best]);
    }

    public int Rank(EventLocator locator)
    {
        if (!_byLog.TryGetValue(locator.LogId, out var ownView)) { return -1; }

        int ownRank = ownView.Rank(locator);

        if (ownRank < 0) { return -1; }

        int rank = ownRank;
        var ownReader = ownView.Reader;

        foreach (var view in _views)
        {
            if (ReferenceEquals(view, ownView)) { continue; }

            rank += LowerBound(view, ownReader, locator);
        }

        return rank;
    }

    public EventLocator? ResolveByKey(ValueKey key)
    {
        foreach (var view in _views)
        {
            var resolved = view.ResolveByKey(key);

            if (resolved is not null) { return resolved; }
        }

        return null;
    }

    public IReadOnlyList<DisplayRow> Slice(int start, int count)
    {
        if (start < 0) { throw new ArgumentOutOfRangeException(nameof(start)); }

        if (count < 0) { throw new ArgumentOutOfRangeException(nameof(count)); }

        int end = (int)Math.Min((long)start + count, _count);

        if (start >= end) { return []; }

        int k = _views.Length;
        Span<int> offsets = k <= MaxStackOffsets ? stackalloc int[k] : new int[k];
        int position = SeekTo(start, offsets);

        while (position < start)
        {
            offsets[PickBest(offsets)]++;
            position++;
        }

        var result = new DisplayRow[end - start];

        for (int i = 0; position < end; i++, position++)
        {
            int best = PickBest(offsets);
            var view = _views[best];
            var locator = view.LocatorAt(offsets[best]);
            result[i] = new DisplayRow(locator, view.GetDetailLean(locator));
            offsets[best]++;
        }

        return result;
    }

    public bool TryGetDetail(EventLocator locator, [NotNullWhen(true)] out ResolvedEvent? detail)
    {
        if (_byLog.TryGetValue(locator.LogId, out var view))
        {
            return view.TryGetDetail(locator, out detail);
        }

        detail = null;

        return false;
    }

    public bool TryGetTimeTicks(EventLocator locator, out long ticks)
    {
        if (_byLog.TryGetValue(locator.LogId, out var view))
        {
            return view.TryGetTimeTicks(locator, out ticks);
        }

        ticks = 0;

        return false;
    }

    public bool TryGetTimeTicksRange(out long minTicks, out long maxTicks, CancellationToken cancellationToken)
    {
        long min = long.MaxValue;
        long max = long.MinValue;
        bool any = false;

        foreach (var view in _views)
        {
            if (!view.TryGetTimeTicksRange(out long viewMin, out long viewMax, cancellationToken)) { continue; }

            if (viewMin < min) { min = viewMin; }

            if (viewMax > max) { max = viewMax; }

            any = true;
        }

        minTicks = any ? min : 0;
        maxTicks = any ? max : 0;

        return any;
    }

    private int[][] BuildIndex()
    {
        var checkpoints = new List<int[]>(_count / Stride);
        var offsets = new int[_views.Length];
        int produced = 0;

        while (true)
        {
            int best = PickBest(offsets);

            if (best < 0) { break; }

            offsets[best]++;
            produced++;

            if (produced % Stride == 0) { checkpoints.Add((int[])offsets.Clone()); }
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

    // Rows of the sub-view that sort strictly before the target; cross-log compares never tie, so this count is exact.
    private int LowerBound(EventColumnView view, IEventColumnReader targetReader, EventLocator target)
    {
        int low = 0;
        int high = view.Count;
        var viewReader = view.Reader;

        while (low < high)
        {
            int mid = low + ((high - low) >> 1);

            if (_compare(viewReader, view.LocatorAt(mid), targetReader, target) < 0) { low = mid + 1; }
            else { high = mid; }
        }

        return low;
    }

    private int PickBest(ReadOnlySpan<int> offsets)
    {
        int best = -1;
        EventLocator bestLocator = default;
        IEventColumnReader? bestReader = null;

        for (int k = 0; k < _views.Length; k++)
        {
            if (offsets[k] >= _views[k].Count) { continue; }

            var locator = _views[k].LocatorAt(offsets[k]);

            if (best < 0 || _compare(_views[k].Reader, locator, bestReader!, bestLocator) < 0)
            {
                best = k;
                bestLocator = locator;
                bestReader = _views[k].Reader;
            }
        }

        return best;
    }

    private EventColumnView Route(EventLocator locator) =>
        _byLog.TryGetValue(locator.LogId, out var view)
            ? view
            : throw new KeyNotFoundException($"Locator log id '{locator.LogId}' is not a member of this combined view.");

    private int SeekTo(int index, Span<int> offsets)
    {
        if (index < Stride)
        {
            offsets.Clear();

            return 0;
        }

        var checkpoints = GetIndex();
        int checkpoint = index / Stride;
        checkpoints[checkpoint - 1].CopyTo(offsets);

        return checkpoint * Stride;
    }
}
