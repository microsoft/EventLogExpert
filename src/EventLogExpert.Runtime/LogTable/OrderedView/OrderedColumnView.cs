// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Filtering.Compilation;
using EventLogExpert.Filtering.Persistence;
using System.Diagnostics.CodeAnalysis;

namespace EventLogExpert.Runtime.LogTable.OrderedView;

internal sealed class OrderedColumnView : IEventColumnView
{
    private readonly IEventColumnReader _reader;
    private readonly OrderedViewSnapshot _snapshot;

    private Dictionary<ValueKey, int>? _byKey;
    private HighlightWinnerCache? _highlightCache;
    private PhysicalProjection? _projection;

    internal OrderedColumnView(OrderedViewSnapshot snapshot, IEventColumnReader reader)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(reader);

        _snapshot = snapshot;
        _reader = reader;
    }

    public int Count => _snapshot.Count;

    public void BucketTimeTicksByEventData(
        long minTicks,
        long bucketSpanTicks,
        int bucketCount,
        string fieldName,
        long[] targetCodes,
        int[] slotCounts,
        CancellationToken cancellationToken) =>
        _reader.BucketTimeTicksByEventData(
            RankByPhysical(),
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
        IReadOnlyList<string> userDataErrorCodePaths,
        long[] targetCodes,
        int[] slotCounts,
        CancellationToken cancellationToken) =>
        _reader.BucketTimeTicksByEventDataHResult(
            RankByPhysical(),
            minTicks,
            bucketSpanTicks,
            bucketCount,
            fieldName,
            eligibleProviders,
            userDataErrorCodePaths,
            targetCodes,
            slotCounts,
            cancellationToken);

    public void BucketTimeTicksByEventDataHResultWithTie(
        byte[] highlightWinners,
        uint[] slotColorMask,
        long minTicks,
        long bucketSpanTicks,
        int bucketCount,
        string fieldName,
        IReadOnlyCollection<string> eligibleProviders,
        IReadOnlyList<string> userDataErrorCodePaths,
        long[] targetCodes,
        int[] slotCounts,
        CancellationToken cancellationToken) =>
        _reader.BucketTimeTicksByEventDataHResultWithTie(
            RankByPhysical(),
            highlightWinners,
            slotColorMask,
            minTicks,
            bucketSpanTicks,
            bucketCount,
            fieldName,
            eligibleProviders,
            userDataErrorCodePaths,
            targetCodes,
            slotCounts,
            cancellationToken);

    public void BucketTimeTicksByEventDataString(
        long minTicks,
        long bucketSpanTicks,
        int bucketCount,
        string[] candidateFields,
        IReadOnlyDictionary<string, int> rawValueToSlot,
        int slotCount,
        int[] slotCounts,
        CancellationToken cancellationToken) =>
        _reader.BucketTimeTicksByEventDataString(
            RankByPhysical(),
            minTicks,
            bucketSpanTicks,
            bucketCount,
            candidateFields,
            rawValueToSlot,
            slotCount,
            slotCounts,
            cancellationToken);

    public void BucketTimeTicksByEventDataStringWithTie(
        byte[] highlightWinners,
        uint[] slotColorMask,
        long minTicks,
        long bucketSpanTicks,
        int bucketCount,
        string[] candidateFields,
        IReadOnlyDictionary<string, int> rawValueToSlot,
        int slotCount,
        int[] slotCounts,
        CancellationToken cancellationToken) =>
        _reader.BucketTimeTicksByEventDataStringWithTie(
            RankByPhysical(),
            highlightWinners,
            slotColorMask,
            minTicks,
            bucketSpanTicks,
            bucketCount,
            candidateFields,
            rawValueToSlot,
            slotCount,
            slotCounts,
            cancellationToken);

    public void BucketTimeTicksByEventDataWithTie(
        byte[] highlightWinners,
        uint[] slotColorMask,
        long minTicks,
        long bucketSpanTicks,
        int bucketCount,
        string fieldName,
        long[] targetCodes,
        int[] slotCounts,
        CancellationToken cancellationToken) =>
        _reader.BucketTimeTicksByEventDataWithTie(
            RankByPhysical(),
            highlightWinners,
            slotColorMask,
            minTicks,
            bucketSpanTicks,
            bucketCount,
            fieldName,
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
        _reader.BucketTimeTicksByEventId(
            RankByPhysical(),
            minTicks,
            bucketSpanTicks,
            bucketCount,
            targetIds,
            slotCounts,
            cancellationToken);

    public void BucketTimeTicksByEventIdWithTie(
        byte[] highlightWinners,
        uint[] slotColorMask,
        long minTicks,
        long bucketSpanTicks,
        int bucketCount,
        int[] targetIds,
        int[] slotCounts,
        CancellationToken cancellationToken) =>
        _reader.BucketTimeTicksByEventIdWithTie(
            RankByPhysical(),
            highlightWinners,
            slotColorMask,
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
        _reader.BucketTimeTicksByField(
            RankByPhysical(),
            minTicks,
            bucketSpanTicks,
            bucketCount,
            field,
            targetValues,
            slotCounts,
            cancellationToken);

    public void BucketTimeTicksByFieldWithTie(
        byte[] highlightWinners,
        uint[] slotColorMask,
        long minTicks,
        long bucketSpanTicks,
        int bucketCount,
        EventFieldId field,
        string[] targetValues,
        int[] slotCounts,
        CancellationToken cancellationToken) =>
        _reader.BucketTimeTicksByFieldWithTie(
            RankByPhysical(),
            highlightWinners,
            slotColorMask,
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
        _reader.BucketTimeTicksBySeverity(
            RankByPhysical(),
            minTicks,
            bucketSpanTicks,
            bucketCount,
            slotCounts,
            cancellationToken);

    public void BucketTimeTicksBySeverityWithTie(
        byte[] highlightWinners,
        uint[] slotColorMask,
        long minTicks,
        long bucketSpanTicks,
        int bucketCount,
        int[] slotCounts,
        CancellationToken cancellationToken) =>
        _reader.BucketTimeTicksBySeverityWithTie(
            RankByPhysical(),
            highlightWinners,
            slotColorMask,
            minTicks,
            bucketSpanTicks,
            bucketCount,
            slotCounts,
            cancellationToken);

    public void CountEventDataHResults(
        string fieldName,
        IReadOnlyCollection<string> eligibleProviders,
        IReadOnlyList<string> userDataErrorCodePaths,
        IDictionary<long, int> counts,
        CancellationToken cancellationToken) =>
        _reader.CountEventDataHResults(RankByPhysical(), fieldName, eligibleProviders, userDataErrorCodePaths, counts, cancellationToken);

    public void CountEventDataStringValues(string[] candidateFields, IDictionary<string, int> counts, CancellationToken cancellationToken) =>
        _reader.CountEventDataStringValues(RankByPhysical(), candidateFields, counts, cancellationToken);

    public void CountEventDataValues(string fieldName, IDictionary<long, int> counts, CancellationToken cancellationToken) =>
        _reader.CountEventDataValues(RankByPhysical(), fieldName, counts, cancellationToken);

    public void CountEventIds(IDictionary<int, int> counts, CancellationToken cancellationToken) =>
        _reader.CountEventIds(RankByPhysical(), counts, cancellationToken);

    public void CountFieldValues(EventFieldId field, IDictionary<string, int> counts, CancellationToken cancellationToken) =>
        _reader.CountFieldValues(RankByPhysical(), field, counts, cancellationToken);

    public byte[] EnsureHighlightWinners(
        IReadOnlyList<SavedFilter> orderedColoredFilters,
        int planKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(orderedColoredFilters);

        HighlightWinnerCache? cache = Volatile.Read(ref _highlightCache);

        if (cache is { PlanKey: var key, Winners: var winners } && key == planKey && winners.Length == _reader.Count)
        {
            return winners;
        }

        byte[] fresh = FilterService.ClassifyHighlightWinners(_reader, Projection().Order, orderedColoredFilters, cancellationToken);
        Volatile.Write(ref _highlightCache, new HighlightWinnerCache(planKey, fresh));

        return fresh;
    }

    public IEnumerable<ResolvedEvent> EnumerateDetail()
    {
        int count = _snapshot.Count;

        for (int display = 0; display < count; display++)
        {
            yield return _reader.GetDetail(_snapshot.At(display).Locator);
        }
    }

    public IEnumerable<ResolvedEvent> EnumerateDetailLean()
    {
        int count = _snapshot.Count;

        for (int display = 0; display < count; display++)
        {
            yield return _reader.GetDetailLean(_snapshot.At(display).Locator);
        }
    }

    public ResolvedEvent GetDetail(EventLocator locator) => _reader.GetDetail(locator);

    public ResolvedEvent GetDetailLean(EventLocator locator) => _reader.GetDetailLean(locator);

    public string GroupKeyAt(EventLocator locator, ColumnName column) => ResolvedEventGroupKey.For(_reader, locator, column);

    public EventLocator LocatorAt(int index) => _snapshot.At(index).Locator;

    public int Rank(EventLocator locator) => _snapshot.RankOf(new OrderKey(locator));

    public EventLocator? ResolveByKey(ValueKey key)
    {
        var byKey = Volatile.Read(ref _byKey);

        if (byKey is null)
        {
            byKey = BuildByKey();

            byKey = Interlocked.CompareExchange(ref _byKey, byKey, null) ?? byKey;
        }

        return byKey.TryGetValue(key, out int physical) ? _reader.LocatorAt(physical) : null;
    }

    public IReadOnlyList<DisplayRow> Slice(int start, int count)
    {
        int total = _snapshot.Count;
        int clampedStart = Math.Clamp(start, 0, total);
        int clampedCount = Math.Clamp(count, 0, total - clampedStart);
        List<DisplayRow> rows = new(clampedCount);

        for (int offset = 0; offset < clampedCount; offset++)
        {
            EventLocator locator = _snapshot.At(clampedStart + offset).Locator;
            rows.Add(new DisplayRow(locator, _reader.GetDetailLean(locator)));
        }

        return rows;
    }

    public bool TryGetDetail(EventLocator locator, [NotNullWhen(true)] out ResolvedEvent? detail)
    {
        if (AddressesReader(locator))
        {
            detail = _reader.GetDetail(locator);

            return true;
        }

        detail = null;

        return false;
    }

    public bool TryGetTimeTicks(EventLocator locator, out long ticks)
    {
        if (AddressesReader(locator))
        {
            ticks = _reader.GetTimeTicks(locator);

            return true;
        }

        ticks = 0;

        return false;
    }

    public bool TryGetTimeTicksRange(out long minTicks, out long maxTicks, CancellationToken cancellationToken) =>
        _reader.TryGetTimeTicksRange(RankByPhysical(), out minTicks, out maxTicks, cancellationToken);

    private bool AddressesReader(in EventLocator locator) =>
        locator.LogId == _reader.LogId
        && locator.Generation == _reader.Generation
        && locator.Index >= 0
        && locator.Index < _reader.Count;

    private Dictionary<ValueKey, int> BuildByKey()
    {
        int count = _snapshot.Count;
        var map = new Dictionary<ValueKey, int>(count);

        for (int display = 0; display < count; display++)
        {
            EventLocator locator = _snapshot.At(display).Locator;

            if (ValueKey.TryCreate(_reader.GetDetailLean(locator), out ValueKey key))
            {
                map.TryAdd(key, locator.Index);
            }
        }

        return map;
    }

    private PhysicalProjection Projection()
    {
        var projection = Volatile.Read(ref _projection);

        if (projection is not null) { return projection; }

        int displayCount = _snapshot.Count;
        int[] order = new int[displayCount];
        int[] rankByPhysical = new int[_reader.Count];
        Array.Fill(rankByPhysical, -1);

        for (int display = 0; display < displayCount; display++)
        {
            int physical = _snapshot.At(display).Locator.Index;
            order[display] = physical;
            rankByPhysical[physical] = display;
        }

        projection = new PhysicalProjection(order, rankByPhysical);

        return Interlocked.CompareExchange(ref _projection, projection, null) ?? projection;
    }

    private int[] RankByPhysical() => Projection().RankByPhysical;

    private sealed record PhysicalProjection(int[] Order, int[] RankByPhysical);

    private sealed record HighlightWinnerCache(int PlanKey, byte[] Winners);
}
