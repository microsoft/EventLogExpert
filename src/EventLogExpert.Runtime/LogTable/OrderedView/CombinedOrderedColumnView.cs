// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Filtering.Compilation;
using EventLogExpert.Filtering.Persistence;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace EventLogExpert.Runtime.LogTable.OrderedView;

internal sealed class CombinedOrderedColumnView : IEventColumnView
{
    private readonly int _count;
    private readonly ConditionalWeakTable<byte[], byte[][]> _highlightHandles = [];
    private readonly IEventColumnReader[] _readers;
    private readonly Dictionary<LogGeneration, int> _slotByLogGeneration;
    private readonly OrderedViewSnapshot _snapshot;

    private Dictionary<ValueKey, EventLocator>? _byKey;
    private HighlightCache? _highlightCache;
    private Partition? _partition;

    internal CombinedOrderedColumnView(OrderedViewSnapshot snapshot, IReadOnlyCollection<LogGeneration> exactInScope)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(exactInScope);

        _snapshot = snapshot;
        _readers = new IEventColumnReader[exactInScope.Count];
        _slotByLogGeneration = new Dictionary<LogGeneration, int>(exactInScope.Count);

        int next = 0;

        foreach (LogGeneration logGeneration in exactInScope)
        {
            if (!snapshot.TryGetReaderByLog(logGeneration.LogId,
                logGeneration.Generation,
                out IEventColumnReader? reader))
            {
                throw new ArgumentException(
                    $"The snapshot pins no reader for in-scope member '{logGeneration.LogId}' generation {logGeneration.Generation}.",
                    nameof(exactInScope));
            }

            if (!_slotByLogGeneration.TryAdd(logGeneration, next))
            {
                throw new ArgumentException($"Duplicate in-scope member '{logGeneration}'.", nameof(exactInScope));
            }

            _readers[next] = reader;
            next++;
        }

        _count = snapshot.Count;
    }

    public int Count => _count;

    public void BucketTimeTicksByEventData(
        long minTicks,
        long bucketSpanTicks,
        int bucketCount,
        string fieldName,
        long[] targetCodes,
        int[] slotCounts,
        CancellationToken cancellationToken)
    {
        Partition partition = GetPartition();

        for (int slot = 0; slot < _readers.Length; slot++)
        {
            _readers[slot]
                .BucketTimeTicksByEventData(
                    partition.RankByPhysical[slot],
                    minTicks,
                    bucketSpanTicks,
                    bucketCount,
                    fieldName,
                    targetCodes,
                    slotCounts,
                    cancellationToken);
        }
    }

    public void BucketTimeTicksByEventDataHResult(
        long minTicks,
        long bucketSpanTicks,
        int bucketCount,
        string fieldName,
        IReadOnlyCollection<string> eligibleProviders,
        IReadOnlyList<string> userDataErrorCodePaths,
        long[] targetCodes,
        int[] slotCounts,
        CancellationToken cancellationToken)
    {
        Partition partition = GetPartition();

        for (int slot = 0; slot < _readers.Length; slot++)
        {
            _readers[slot]
                .BucketTimeTicksByEventDataHResult(
                    partition.RankByPhysical[slot],
                    minTicks,
                    bucketSpanTicks,
                    bucketCount,
                    fieldName,
                    eligibleProviders,
                    userDataErrorCodePaths,
                    targetCodes,
                    slotCounts,
                    cancellationToken);
        }
    }

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
        CancellationToken cancellationToken)
    {
        Partition partition = GetPartition();
        byte[][] childWinners = ResolveChildWinners(highlightWinners);

        for (int slot = 0; slot < _readers.Length; slot++)
        {
            _readers[slot]
                .BucketTimeTicksByEventDataHResultWithTie(
                    partition.RankByPhysical[slot],
                    childWinners[slot],
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
        }
    }

    public void BucketTimeTicksByEventDataString(
        long minTicks,
        long bucketSpanTicks,
        int bucketCount,
        string[] candidateFields,
        IReadOnlyDictionary<string, int> rawValueToSlot,
        int slotCount,
        int[] slotCounts,
        CancellationToken cancellationToken)
    {
        Partition partition = GetPartition();

        for (int slot = 0; slot < _readers.Length; slot++)
        {
            _readers[slot]
                .BucketTimeTicksByEventDataString(
                    partition.RankByPhysical[slot],
                    minTicks,
                    bucketSpanTicks,
                    bucketCount,
                    candidateFields,
                    rawValueToSlot,
                    slotCount,
                    slotCounts,
                    cancellationToken);
        }
    }

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
        CancellationToken cancellationToken)
    {
        Partition partition = GetPartition();
        byte[][] childWinners = ResolveChildWinners(highlightWinners);

        for (int slot = 0; slot < _readers.Length; slot++)
        {
            _readers[slot]
                .BucketTimeTicksByEventDataStringWithTie(
                    partition.RankByPhysical[slot],
                    childWinners[slot],
                    slotColorMask,
                    minTicks,
                    bucketSpanTicks,
                    bucketCount,
                    candidateFields,
                    rawValueToSlot,
                    slotCount,
                    slotCounts,
                    cancellationToken);
        }
    }

    public void BucketTimeTicksByEventDataWithTie(
        byte[] highlightWinners,
        uint[] slotColorMask,
        long minTicks,
        long bucketSpanTicks,
        int bucketCount,
        string fieldName,
        long[] targetCodes,
        int[] slotCounts,
        CancellationToken cancellationToken)
    {
        Partition partition = GetPartition();
        byte[][] childWinners = ResolveChildWinners(highlightWinners);

        for (int slot = 0; slot < _readers.Length; slot++)
        {
            _readers[slot]
                .BucketTimeTicksByEventDataWithTie(
                    partition.RankByPhysical[slot],
                    childWinners[slot],
                    slotColorMask,
                    minTicks,
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
        Partition partition = GetPartition();

        for (int slot = 0; slot < _readers.Length; slot++)
        {
            _readers[slot]
                .BucketTimeTicksByEventId(
                    partition.RankByPhysical[slot],
                    minTicks,
                    bucketSpanTicks,
                    bucketCount,
                    targetIds,
                    slotCounts,
                    cancellationToken);
        }
    }

    public void BucketTimeTicksByEventIdWithTie(
        byte[] highlightWinners,
        uint[] slotColorMask,
        long minTicks,
        long bucketSpanTicks,
        int bucketCount,
        int[] targetIds,
        int[] slotCounts,
        CancellationToken cancellationToken)
    {
        Partition partition = GetPartition();
        byte[][] childWinners = ResolveChildWinners(highlightWinners);

        for (int slot = 0; slot < _readers.Length; slot++)
        {
            _readers[slot]
                .BucketTimeTicksByEventIdWithTie(
                    partition.RankByPhysical[slot],
                    childWinners[slot],
                    slotColorMask,
                    minTicks,
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
        Partition partition = GetPartition();

        for (int slot = 0; slot < _readers.Length; slot++)
        {
            _readers[slot]
                .BucketTimeTicksByField(
                    partition.RankByPhysical[slot],
                    minTicks,
                    bucketSpanTicks,
                    bucketCount,
                    field,
                    targetValues,
                    slotCounts,
                    cancellationToken);
        }
    }

    public void BucketTimeTicksByFieldWithTie(
        byte[] highlightWinners,
        uint[] slotColorMask,
        long minTicks,
        long bucketSpanTicks,
        int bucketCount,
        EventFieldId field,
        string[] targetValues,
        int[] slotCounts,
        CancellationToken cancellationToken)
    {
        Partition partition = GetPartition();
        byte[][] childWinners = ResolveChildWinners(highlightWinners);

        for (int slot = 0; slot < _readers.Length; slot++)
        {
            _readers[slot]
                .BucketTimeTicksByFieldWithTie(
                    partition.RankByPhysical[slot],
                    childWinners[slot],
                    slotColorMask,
                    minTicks,
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
        Partition partition = GetPartition();

        for (int slot = 0; slot < _readers.Length; slot++)
        {
            _readers[slot]
                .BucketTimeTicksBySeverity(
                    partition.RankByPhysical[slot],
                    minTicks,
                    bucketSpanTicks,
                    bucketCount,
                    slotCounts,
                    cancellationToken);
        }
    }

    public void BucketTimeTicksBySeverityWithTie(
        byte[] highlightWinners,
        uint[] slotColorMask,
        long minTicks,
        long bucketSpanTicks,
        int bucketCount,
        int[] slotCounts,
        CancellationToken cancellationToken)
    {
        Partition partition = GetPartition();
        byte[][] childWinners = ResolveChildWinners(highlightWinners);

        for (int slot = 0; slot < _readers.Length; slot++)
        {
            _readers[slot]
                .BucketTimeTicksBySeverityWithTie(
                    partition.RankByPhysical[slot],
                    childWinners[slot],
                    slotColorMask,
                    minTicks,
                    bucketSpanTicks,
                    bucketCount,
                    slotCounts,
                    cancellationToken);
        }
    }

    public void CountEventDataHResults(
        string fieldName,
        IReadOnlyCollection<string> eligibleProviders,
        IReadOnlyList<string> userDataErrorCodePaths,
        IDictionary<long, int> counts,
        CancellationToken cancellationToken)
    {
        Partition partition = GetPartition();

        for (int slot = 0; slot < _readers.Length; slot++)
        {
            _readers[slot]
                .CountEventDataHResults(
                    partition.RankByPhysical[slot],
                    fieldName,
                    eligibleProviders,
                    userDataErrorCodePaths,
                    counts,
                    cancellationToken);
        }
    }

    public void CountEventDataStringValues(
        string[] candidateFields,
        IDictionary<string, int> counts,
        CancellationToken cancellationToken)
    {
        Partition partition = GetPartition();

        for (int slot = 0; slot < _readers.Length; slot++)
        {
            _readers[slot]
                .CountEventDataStringValues(partition.RankByPhysical[slot], candidateFields, counts, cancellationToken);
        }
    }

    public void CountEventDataValues(
        string fieldName,
        IDictionary<long, int> counts,
        CancellationToken cancellationToken)
    {
        Partition partition = GetPartition();

        for (int slot = 0; slot < _readers.Length; slot++)
        {
            _readers[slot].CountEventDataValues(partition.RankByPhysical[slot], fieldName, counts, cancellationToken);
        }
    }

    public void CountEventIds(IDictionary<int, int> counts, CancellationToken cancellationToken)
    {
        Partition partition = GetPartition();

        for (int slot = 0; slot < _readers.Length; slot++)
        {
            _readers[slot].CountEventIds(partition.RankByPhysical[slot], counts, cancellationToken);
        }
    }

    public void CountFieldValues(
        EventFieldId field,
        IDictionary<string, int> counts,
        CancellationToken cancellationToken)
    {
        Partition partition = GetPartition();

        for (int slot = 0; slot < _readers.Length; slot++)
        {
            _readers[slot].CountFieldValues(partition.RankByPhysical[slot], field, counts, cancellationToken);
        }
    }

    public void CountResolutionBySource(IDictionary<string, ProviderResolutionCounts> counts, CancellationToken cancellationToken)
    {
        Partition partition = GetPartition();

        for (int slot = 0; slot < _readers.Length; slot++)
        {
            _readers[slot].CountResolutionBySource(partition.RankByPhysical[slot], counts, cancellationToken);
        }
    }

    public void CountResolutionDetailForSource(
        string source,
        IDictionary<int, ProviderResolutionCounts> byId,
        ProviderResolutionCounts[] byLevelSlot,
        CancellationToken cancellationToken)
    {
        Partition partition = GetPartition();

        for (int slot = 0; slot < _readers.Length; slot++)
        {
            _readers[slot].CountResolutionDetailForSource(partition.RankByPhysical[slot], source, byId, byLevelSlot, cancellationToken);
        }
    }

    public void CountSeverity(int[] slotCounts, CancellationToken cancellationToken)
    {
        Partition partition = GetPartition();

        for (int slot = 0; slot < _readers.Length; slot++)
        {
            _readers[slot].CountSeverity(partition.RankByPhysical[slot], slotCounts, cancellationToken);
        }
    }

    public byte[] EnsureHighlightWinners(
        IReadOnlyList<SavedFilter> orderedColoredFilters,
        int planKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(orderedColoredFilters);

        HighlightCache? cache = Volatile.Read(ref _highlightCache);
        byte[][] childWinners;

        if (cache is not null && cache.PlanKey == planKey)
        {
            childWinners = cache.ChildWinners;
        }
        else
        {
            Partition partition = GetPartition();
            childWinners = new byte[_readers.Length][];

            for (int slot = 0; slot < _readers.Length; slot++)
            {
                childWinners[slot] = FilterService.ClassifyHighlightWinners(
                    _readers[slot],
                    partition.SurvivingOrder[slot],
                    orderedColoredFilters,
                    cancellationToken);
            }

            Volatile.Write(ref _highlightCache, new HighlightCache(planKey, childWinners));
        }

        byte[] handle = new byte[1];
        _highlightHandles.Add(handle, childWinners);

        return handle;
    }

    public IEnumerable<ResolvedEvent> EnumerateDetail()
    {
        int count = _snapshot.Count;

        for (int display = 0; display < count; display++)
        {
            EventLocator locator = _snapshot.At(display).Locator;

            yield return Reader(locator).GetDetail(locator);
        }
    }

    public IEnumerable<ResolvedEvent> EnumerateDetailLean()
    {
        int count = _snapshot.Count;

        for (int display = 0; display < count; display++)
        {
            EventLocator locator = _snapshot.At(display).Locator;

            yield return Reader(locator).GetDetailLean(locator);
        }
    }

    public ResolvedEvent GetDetail(EventLocator locator) => Reader(locator).GetDetail(locator);

    public ResolvedEvent GetDetailLean(EventLocator locator) => Reader(locator).GetDetailLean(locator);

    public string GroupKeyAt(EventLocator locator, ColumnName column) =>
        ResolvedEventGroupKey.For(Reader(locator), locator, column);

    public EventLocator LocatorAt(int index) => _snapshot.At(index).Locator;

    public int Rank(EventLocator locator) => _snapshot.RankOf(new OrderKey(locator));

    public EventLocator? ResolveByKey(ValueKey key)
    {
        Dictionary<ValueKey, EventLocator>? byKey = Volatile.Read(ref _byKey);

        if (byKey is null)
        {
            byKey = BuildByKey();

            byKey = Interlocked.CompareExchange(ref _byKey, byKey, null) ?? byKey;
        }

        return byKey.TryGetValue(key, out EventLocator locator) ? locator : null;
    }

    public IReadOnlyList<DisplayRow> Slice(int start, int count)
    {
        if (start < 0) { throw new ArgumentOutOfRangeException(nameof(start)); }

        if (count < 0) { throw new ArgumentOutOfRangeException(nameof(count)); }

        int end = (int)Math.Min((long)start + count, _snapshot.Count);

        if (start >= end) { return []; }

        List<DisplayRow> rows = new(end - start);

        for (int display = start; display < end; display++)
        {
            EventLocator locator = _snapshot.At(display).Locator;
            rows.Add(new DisplayRow(locator, Reader(locator).GetDetailLean(locator)));
        }

        return rows;
    }

    public bool TryGetDetail(EventLocator locator, [NotNullWhen(true)] out ResolvedEvent? detail)
    {
        if (TryGetReader(locator, out IEventColumnReader? reader))
        {
            detail = reader.GetDetail(locator);

            return true;
        }

        detail = null;

        return false;
    }

    public bool TryGetTimeTicks(EventLocator locator, out long ticks)
    {
        if (TryGetReader(locator, out IEventColumnReader? reader))
        {
            ticks = reader.GetTimeTicks(locator);

            return true;
        }

        ticks = 0;

        return false;
    }

    public bool TryGetTimeTicksRange(out long minTicks, out long maxTicks, CancellationToken cancellationToken)
    {
        Partition partition = GetPartition();
        long min = long.MaxValue;
        long max = long.MinValue;
        bool any = false;

        for (int slot = 0; slot < _readers.Length; slot++)
        {
            if (!_readers[slot]
                .TryGetTimeTicksRange(partition.RankByPhysical[slot],
                    out long readerMin,
                    out long readerMax,
                    cancellationToken))
            {
                continue;
            }

            if (readerMin < min) { min = readerMin; }

            if (readerMax > max) { max = readerMax; }

            any = true;
        }

        minTicks = any ? min : 0;
        maxTicks = any ? max : 0;

        return any;
    }

    private Dictionary<ValueKey, EventLocator> BuildByKey()
    {
        int count = _snapshot.Count;
        Dictionary<ValueKey, EventLocator> map = new(count);

        for (int display = 0; display < count; display++)
        {
            EventLocator locator = _snapshot.At(display).Locator;

            if (ValueKey.TryCreate(Reader(locator).GetDetailLean(locator), out ValueKey key))
            {
                map.TryAdd(key, locator);
            }
        }

        return map;
    }

    private Partition GetPartition()
    {
        Partition? partition = Volatile.Read(ref _partition);

        if (partition is not null) { return partition; }

        int[][] rankByPhysical = new int[_readers.Length][];
        List<int>[] surviving = new List<int>[_readers.Length];

        for (int slot = 0; slot < _readers.Length; slot++)
        {
            rankByPhysical[slot] = new int[_readers[slot].Count];
            Array.Fill(rankByPhysical[slot], -1);
            surviving[slot] = [];
        }

        int count = _snapshot.Count;

        for (int display = 0; display < count; display++)
        {
            EventLocator locator = _snapshot.At(display).Locator;

            if (!_slotByLogGeneration.TryGetValue(new LogGeneration(locator.LogId, locator.Generation), out int slot))
            {
                throw new InvalidOperationException(
                    $"The snapshot displays a row for out-of-scope member '{locator.LogId}' generation {locator.Generation}.");
            }

            rankByPhysical[slot][locator.Index] = display;
            surviving[slot].Add(locator.Index);
        }

        int[][] survivingOrder = new int[_readers.Length][];

        for (int slot = 0; slot < _readers.Length; slot++) { survivingOrder[slot] = [.. surviving[slot]]; }

        partition = new Partition(rankByPhysical, survivingOrder);

        return Interlocked.CompareExchange(ref _partition, partition, null) ?? partition;
    }

    private IEventColumnReader Reader(in EventLocator locator)
    {
        if (TryGetReader(locator, out IEventColumnReader? reader)) { return reader; }

        throw new KeyNotFoundException(
            $"Locator '{locator.LogId}' generation {locator.Generation} index {locator.Index} is not in this view's scope.");
    }

    private byte[][] ResolveChildWinners(byte[] highlightWinners)
    {
        ArgumentNullException.ThrowIfNull(highlightWinners);

        return _highlightHandles.TryGetValue(highlightWinners, out byte[][]? childWinners) ? childWinners :
            throw new InvalidOperationException("Combined highlight winners must be captured before tie bucketing.");
    }

    private bool TryGetReader(in EventLocator locator, [NotNullWhen(true)] out IEventColumnReader? reader)
    {
        if (_slotByLogGeneration.TryGetValue(new LogGeneration(locator.LogId, locator.Generation), out int slot))
        {
            IEventColumnReader candidate = _readers[slot];

            if (locator.Index >= 0 && locator.Index < candidate.Count)
            {
                reader = candidate;

                return true;
            }
        }

        reader = null;

        return false;
    }

    private sealed record Partition(int[][] RankByPhysical, int[][] SurvivingOrder);

    private sealed record HighlightCache(int PlanKey, byte[][] ChildWinners);
}
