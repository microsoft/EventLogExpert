// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;
using System.Diagnostics.CodeAnalysis;

namespace EventLogExpert.Runtime.LogTable;

internal sealed class EventColumnView : IEventColumnView
{
    private readonly SortContext _context;
    private readonly int[] _order;
    private readonly int[] _rankByPhysical;
    private readonly IEventColumnReader _reader;

    private Dictionary<ValueKey, int>? _byKey;

    private EventColumnView(IEventColumnReader reader, int[] order, int[] rankByPhysical, SortContext context)
    {
        _reader = reader;
        _order = order;
        _rankByPhysical = rankByPhysical;
        _context = context;
    }

    public int Count => _order.Length;

    internal SortContext Context => _context;

    internal IEventColumnReader Reader => _reader;

    // The filter-surviving physical rows, used by WithContext to re-sort in place; the survivor set is order-independent,
    // so the current display order is a valid re-sort input.
    internal ReadOnlySpan<int> Survivors => _order;

    public void BucketTimeTicksByEventData(
        long minTicks,
        long bucketSpanTicks,
        int bucketCount,
        string fieldName,
        long[] targetCodes,
        int[] slotCounts,
        CancellationToken cancellationToken) =>
        _reader.BucketTimeTicksByEventData(
            _rankByPhysical,
            minTicks,
            bucketSpanTicks,
            bucketCount,
            fieldName,
            targetCodes,
            slotCounts,
            cancellationToken);

    public void BucketTimeTicksByEventDataHResult(
        long minTicks,
        long bucketSpanTicks,
        int bucketCount,
        string fieldName,
        IReadOnlyCollection<string> eligibleProviders,
        long[] targetCodes,
        int[] slotCounts,
        CancellationToken cancellationToken) =>
        _reader.BucketTimeTicksByEventDataHResult(
            _rankByPhysical,
            minTicks,
            bucketSpanTicks,
            bucketCount,
            fieldName,
            eligibleProviders,
            targetCodes,
            slotCounts,
            cancellationToken);

    public void BucketTimeTicksByEventId(
        long minTicks,
        long bucketSpanTicks,
        int bucketCount,
        int[] targetIds,
        int[] slotCounts,
        CancellationToken cancellationToken) =>
        _reader.BucketTimeTicksByEventId(_rankByPhysical,
            minTicks,
            bucketSpanTicks,
            bucketCount,
            targetIds,
            slotCounts,
            cancellationToken);

    public void BucketTimeTicksByField(
        long minTicks,
        long bucketSpanTicks,
        int bucketCount,
        EventFieldId field,
        string[] targetValues,
        int[] slotCounts,
        CancellationToken cancellationToken) =>
        _reader.BucketTimeTicksByField(_rankByPhysical,
            minTicks,
            bucketSpanTicks,
            bucketCount,
            field,
            targetValues,
            slotCounts,
            cancellationToken);

    public void BucketTimeTicksBySeverity(
        long minTicks,
        long bucketSpanTicks,
        int bucketCount,
        int[] slotCounts,
        CancellationToken cancellationToken) =>
        _reader.BucketTimeTicksBySeverity(_rankByPhysical,
            minTicks,
            bucketSpanTicks,
            bucketCount,
            slotCounts,
            cancellationToken);

    public void CountEventDataHResults(
        string fieldName,
        IReadOnlyCollection<string> eligibleProviders,
        IDictionary<long, int> counts,
        CancellationToken cancellationToken) =>
        _reader.CountEventDataHResults(_rankByPhysical, fieldName, eligibleProviders, counts, cancellationToken);

    public void CountEventDataValues(
        string fieldName,
        IDictionary<long, int> counts,
        CancellationToken cancellationToken) =>
        _reader.CountEventDataValues(_rankByPhysical, fieldName, counts, cancellationToken);

    public void CountEventIds(IDictionary<int, int> counts, CancellationToken cancellationToken) =>
        _reader.CountEventIds(_rankByPhysical, counts, cancellationToken);

    public void CountFieldValues(
        EventFieldId field,
        IDictionary<string, int> counts,
        CancellationToken cancellationToken) =>
        _reader.CountFieldValues(_rankByPhysical, field, counts, cancellationToken);

    public IEnumerable<ResolvedEvent> EnumerateDetail()
    {
        foreach (int physical in _order)
        {
            yield return _reader.GetDetail(_reader.LocatorAt(physical));
        }
    }

    public ResolvedEvent GetDetail(EventLocator locator) => _reader.GetDetail(locator);

    public ResolvedEvent GetDetailLean(EventLocator locator) => _reader.GetDetailLean(locator);

    public string GroupKeyAt(EventLocator locator, ColumnName column) =>
        ResolvedEventGroupKey.For(_reader, locator, column);

    public EventLocator LocatorAt(int displayIndex) => _reader.LocatorAt(_order[displayIndex]);

    public int Rank(EventLocator locator) =>
        locator.LogId == _reader.LogId
        && locator.Generation == _reader.Generation
        && locator.Index >= 0
        && locator.Index < _rankByPhysical.Length
            ? _rankByPhysical[locator.Index]
            : -1;

    public EventLocator? ResolveByKey(ValueKey key)
    {
        var byKey = Volatile.Read(ref _byKey);

        if (byKey is null)
        {
            byKey = BuildByKey();

            // First writer wins; a racing reader either sees null (and builds its own discarded copy) or a complete map.
            byKey = Interlocked.CompareExchange(ref _byKey, byKey, null) ?? byKey;
        }

        return byKey.TryGetValue(key, out int physical) ? _reader.LocatorAt(physical) : null;
    }

    public IReadOnlyList<DisplayRow> Slice(int start, int count)
    {
        int clampedStart = Math.Clamp(start, 0, _order.Length);
        int clampedCount = Math.Clamp(count, 0, _order.Length - clampedStart);
        List<DisplayRow> rows = new(clampedCount);

        for (int offset = 0; offset < clampedCount; offset++)
        {
            EventLocator locator = LocatorAt(clampedStart + offset);
            rows.Add(new DisplayRow(locator, _reader.GetDetailLean(locator)));
        }

        return rows;
    }

    public bool TryGetDetail(EventLocator locator, [NotNullWhen(true)] out ResolvedEvent? detail)
    {
        if (locator.LogId == _reader.LogId
            && locator.Generation == _reader.Generation
            && locator.Index >= 0
            && locator.Index < _reader.Count)
        {
            detail = _reader.GetDetail(locator);

            return true;
        }

        detail = null;

        return false;
    }

    public bool TryGetTimeTicks(EventLocator locator, out long ticks)
    {
        if (locator.LogId == _reader.LogId
            && locator.Generation == _reader.Generation
            && locator.Index >= 0
            && locator.Index < _reader.Count)
        {
            ticks = _reader.GetTimeTicks(locator);

            return true;
        }

        ticks = 0;

        return false;
    }

    public bool TryGetTimeTicksRange(out long minTicks, out long maxTicks, CancellationToken cancellationToken) =>
        _reader.TryGetTimeTicksRange(_rankByPhysical, out minTicks, out maxTicks, cancellationToken);

    /// <summary>
    ///     Builds a view over the filter-surviving <paramref name="survivors" />, sorted into display order, with the
    ///     physical-to-display inverse used by <see cref="Rank" />.
    /// </summary>
    internal static EventColumnView Create(
        IEventColumnReader reader,
        ReadOnlySpan<int> survivors,
        ColumnName? orderBy,
        bool isDescending,
        ColumnName? groupBy,
        bool isGroupDescending) =>
        Create(reader, survivors, new SortContext(orderBy, isDescending, groupBy, isGroupDescending));

    /// <summary>
    ///     Live-path overload: sorts under <paramref name="context" /> and retains it so the reducers can detect (
    ///     <see cref="HasContext" />) and repair (<see cref="WithContext" />) a stale sort without re-reading the raw store.
    /// </summary>
    internal static EventColumnView Create(IEventColumnReader reader, ReadOnlySpan<int> survivors, SortContext context)
    {
        ArgumentNullException.ThrowIfNull(reader);

        int[] order = ResolvedEventOrdering.SortColumnDirect(
            reader, survivors, context.OrderBy, context.IsDescending, context.GroupBy, context.IsGroupDescending);

        int[] rankByPhysical = new int[reader.Count];
        Array.Fill(rankByPhysical, -1);

        for (int display = 0; display < order.Length; display++)
        {
            rankByPhysical[order[display]] = display;
        }

        return new EventColumnView(reader, order, rankByPhysical, context);
    }

    internal bool HasContext(SortContext context) => _context.Equals(context);

    // Re-sorts the same survivor set under a new context without re-reading the raw store (the reducers use this when the
    // effective default order flips between single- and multi-log).
    internal EventColumnView WithContext(SortContext context) => Create(_reader, _order, context);

    private Dictionary<ValueKey, int> BuildByKey()
    {
        var map = new Dictionary<ValueKey, int>(_order.Length);

        foreach (int physical in _order)
        {
            // First survivor wins on a duplicate key; null-RecordId rows never produce a key, so they stay unresolvable.
            if (ValueKey.TryCreate(_reader.GetDetailLean(_reader.LocatorAt(physical)), out ValueKey key))
            {
                map.TryAdd(key, physical);
            }
        }

        return map;
    }
}
