// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using static EventLogExpert.Runtime.Concurrency.CooperativeCancellation;

namespace EventLogExpert.Runtime.LogTable.OrderedView;

internal sealed record RebuildRequest(
    long Generation,
    Func<EventLocator, IEventColumnReader, bool> Predicate,
    SortContext Context,
    IReadOnlyDictionary<EventLogId, int> RequestedGeneration,
    bool Hold,
    RowCoverage Coverage,
    IReaderResolver BeginResolver,
    FrozenScope Scope,
    long ScopeVersion,
    EventLogId? SingleLog);

internal enum AdoptOutcome
{
    Adopted,
    DroppedStale,
    AbandonedTail
}

internal sealed class OrderedViewState
{
    internal const int DefaultBulkBuildThreshold = 50_000;

    // Conservative weights for the bulk peak-memory estimate; over-estimating only falls back to the delegating path
    // sooner (safe). OrderKey wraps EventLocator (24 B); an SoA lane is bounded by Guid[16]+bool[1]; a pooled-rank
    // entry is int[4]+bool[1].
    private const int BulkOrderKeyBytes = 24;
    private const int BulkPooledRankBytesPerEntry = 5;
    private const int BulkSoaLaneBytesPerRow = 17;

    private readonly Dictionary<EventLogId, int> _activeGeneration = [];
    private readonly Dictionary<LogGeneration, IEventColumnReader> _latestReaders = [];
    private readonly LiveReaderResolver _liveResolver;
    private readonly Dictionary<EventLogId, int> _requestedGeneration = [];
    private readonly OrderedViewScopeState _scopeState = new();

    private SortContext _activeContext = new(null, false, null, false);
    private ImmutableHashSet<LogGeneration> _adoptedInScope = [];
    private FrozenScope _adoptedScope;
    private OrderedViewSnapshot _current = OrderedViewSnapshot.Empty;
    private long _generation;
    private bool _holdIngest;
    private bool _liveIndexInvalidated;
    private ChunkedOrderIndex _index;
    private Func<EventLocator, IEventColumnReader, bool> _predicate = static (_, _) => true;
    private long _publishVersion;
    private SortContext _requestedContext = new(null, false, null, false);
    private Func<EventLocator, IEventColumnReader, bool> _requestedPredicate = static (_, _) => true;

    internal OrderedViewState()
    {
        _liveResolver = new LiveReaderResolver(_latestReaders);
        _index = new ChunkedOrderIndex(OrderKeyComparerFactory.Create(_activeContext, _liveResolver));
        _adoptedScope = _scopeState.Freeze();
    }

    public ImmutableHashSet<LogGeneration> AdoptedInScope => _adoptedInScope;

    public OrderedViewSnapshot Current => Volatile.Read(ref _current);

    public long Generation => Volatile.Read(ref _generation);

    public bool LiveIndexInvalidated => _liveIndexInvalidated;

    public int RowCount => _scopeState.FreezeCoverage().RowCount;

    public long ScopeVersion => _scopeState.ScopeVersion;

    internal int TrackedGenerationCount => _requestedGeneration.Count;

    internal int TrackedReaderCount => _latestReaders.Count;

    public static ChunkedOrderIndex BuildIndex(RebuildRequest request) => BuildIndex(request, CancellationToken.None);

    public static ChunkedOrderIndex BuildIndex(RebuildRequest request, CancellationToken cancellationToken) =>
        BuildIndex(request, cancellationToken, DefaultBulkBuildThreshold, DefaultMemoryBudgetBytes());

    public RebuildRequest BeginRebuild(Func<EventLocator, IEventColumnReader, bool> newPredicate, SortContext newContext, bool? hold = null)
    {
        _requestedPredicate = newPredicate;
        _requestedContext = newContext;

        if (hold == true) { _holdIngest = true; }

        return CaptureRequest();
    }

    public RebuildRequest BeginReset(EventLogId logId, int newGeneration)
    {
        if (!_requestedGeneration.TryGetValue(logId, out int current) || newGeneration > current)
        {
            _requestedGeneration[logId] = newGeneration;
        }

        return CaptureRequest();
    }

    public bool CanRestampAdopted(
        IReadOnlyCollection<EventLogId> scopeLogs,
        IReadOnlyDictionary<EventLogId, IEventColumnReader> scopeReaders)
    {
        if (_holdIngest) { return false; }

        if (!_scopeState.ScopeEquals(scopeLogs)) { return false; }

        foreach ((EventLogId logId, IEventColumnReader reader) in scopeReaders)
        {
            if (!_adoptedScope.Includes(logId)) { return false; }

            if (!_activeGeneration.TryGetValue(logId, out int active) || active != reader.Generation) { return false; }

            if (_scopeState.Coverage(new LogGeneration(logId, reader.Generation)) != reader.Count) { return false; }
        }

        return true;
    }

    public RebuildRequest CaptureScopeReseed() => CaptureRequest();

    public OrderedViewSnapshot Clear()
    {
        Interlocked.Increment(ref _generation);
        _latestReaders.Clear();
        _activeGeneration.Clear();
        _requestedGeneration.Clear();
        _scopeState.Reset();
        _adoptedScope = _scopeState.Freeze();
        _predicate = static (_, _) => true;
        _requestedPredicate = static (_, _) => true;
        _activeContext = new SortContext(null, false, null, false);
        _requestedContext = new SortContext(null, false, null, false);
        _holdIngest = false;
        _liveIndexInvalidated = false;
        _index = new ChunkedOrderIndex(OrderKeyComparerFactory.Create(_activeContext, _liveResolver));

        return PublishWith(FreezeReaders());
    }

    public bool CoversSameGenerations(IReadOnlyDictionary<EventLogId, IEventColumnReader> scopeReaders)
    {
        foreach ((EventLogId logId, IEventColumnReader reader) in scopeReaders)
        {
            if (_requestedGeneration.TryGetValue(logId, out int requested) && reader.Generation != requested)
            {
                return false;
            }
        }

        return true;
    }

    public void NotifyRebuildFailed(RebuildRequest request)
    {
        if (request.Hold && Volatile.Read(ref _generation) == request.Generation) { _holdIngest = false; }
    }

    public OrderedViewSnapshot Publish() => PublishWith(FreezeReaders());

    public bool ReconcileLog(EventLogId logId, IEventColumnReader reader) => ReconcileLog(logId, reader, out _);

    public bool ReconcileLog(EventLogId logId, IEventColumnReader reader, out bool requiresRebuild) =>
        ReconcileLog(logId, reader, isReplace: false, out requiresRebuild);

    public bool ReconcileLog(EventLogId logId, IEventColumnReader reader, bool isReplace, out bool requiresRebuild)
    {
        requiresRebuild = false;

        if (!TryAdmitReader(logId, reader, isReplace, out LogGeneration readerKey, out bool contentReplaced)) { return false; }

        int from = _scopeState.Coverage(readerKey);

        if (contentReplaced)
        {
            _scopeState.SetCoverage(readerKey, reader.Count);
        }
        else
        {
            _scopeState.AdvanceCoverage(readerKey, reader.Count);
        }

        bool activeGenerationMatch =
            _activeGeneration.TryGetValue(logId, out int active) && active == reader.Generation;

        if (contentReplaced && _adoptedScope.Includes(logId) && activeGenerationMatch && reader.Count < from)
        {
            _liveIndexInvalidated = true;
        }

        bool mutated = false;

        if (_adoptedScope.Includes(logId) && !_holdIngest && !_liveIndexInvalidated && activeGenerationMatch)
        {
            for (int index = from; index < reader.Count; index++)
            {
                var locator = new EventLocator(logId, reader.Generation, index);

                if (_predicate(locator, reader))
                {
                    _index.Insert(new OrderKey(locator));
                    mutated = true;
                }
            }
        }

        bool displaysThisGeneration = reader.Count > 0 && activeGenerationMatch && _adoptedScope.Includes(logId);

        requiresRebuild = contentReplaced && activeGenerationMatch &&
            (_adoptedScope.Includes(logId) || _scopeState.Includes(logId));

        return mutated ||
            (displaysThisGeneration && !_adoptedInScope.Contains(readerKey)) ||
            (displaysThisGeneration && contentReplaced);
    }

    public bool ReconcileScopeReaders(IReadOnlyDictionary<EventLogId, IEventColumnReader> scopeReaders)
    {
        bool advanced = false;

        foreach ((EventLogId logId, IEventColumnReader reader) in scopeReaders)
        {
            if (SeedScopeReader(logId, reader)) { advanced = true; }
        }

        return advanced;
    }

    public RebuildRequest RemoveLog(EventLogId logId)
    {
        _scopeState.Remove(logId);
        _requestedGeneration.Remove(logId);

        return CaptureRequest();
    }

    public void RestoreRequestedFromAdopted()
    {
        _requestedContext = _activeContext;
        _requestedPredicate = _predicate;
    }

    public bool SeedScopeReader(EventLogId logId, IEventColumnReader reader)
    {
        bool admitted = TryAdmitReader(logId, reader, isReplace: false, out LogGeneration readerKey, out _);

        if ((admitted || _latestReaders.ContainsKey(readerKey)) &&
            reader.Generation > _requestedGeneration.GetValueOrDefault(logId, int.MinValue))
        {
            _requestedGeneration[logId] = reader.Generation;
        }

        if (!admitted) { return false; }

        int covered = _scopeState.Coverage(readerKey);

        _scopeState.AdvanceCoverage(readerKey, reader.Count);

        return reader.Count > covered;
    }

    public void SupersedeInFlight() => Interlocked.Increment(ref _generation);

    public AdoptOutcome TryAdoptRebuild(RebuildRequest request, ChunkedOrderIndex rebuilt, long tailBudget, bool allowAbandon)
    {
        if (Volatile.Read(ref _generation) != request.Generation) { return AdoptOutcome.DroppedStale; }

        if (allowAbandon && MeasureTail(request) > tailBudget)
        {
            _holdIngest = false;

            return AdoptOutcome.AbandonedTail;
        }

        IReaderResolver commitResolver = FreezeReaders();
        rebuilt.RebindInsertComparer(OrderKeyComparerFactory.Create(request.Context, commitResolver));

        try
        {
            foreach (LogGeneration key in _scopeState.Keys)
            {
                if (!request.Scope.Includes(key.LogId)) { continue; }

                if (!IsCurrent(key, _requestedGeneration)) { continue; }

                if (!commitResolver.TryResolve(new EventLocator(key.LogId, key.Generation, 0), out IEventColumnReader? reader))
                {
                    continue;
                }

                int from = request.Coverage.CoverageOf(key);
                int to = Math.Min(_scopeState.Coverage(key), reader.Count);

                for (int index = from; index < to; index++)
                {
                    var locator = new EventLocator(key.LogId, key.Generation, index);

                    if (request.Predicate(locator, reader))
                    {
                        rebuilt.Insert(new OrderKey(locator));
                    }
                }
            }
        }
        catch
        {
            // Abort leaves live state intact, but must not leave ingest gated: clear the hold so rows resume (safe under
            // pure delegation; the fast-path work owns richer re-key recovery).
            _holdIngest = false;

            throw;
        }

        _activeGeneration.Clear();

        foreach (var entry in _requestedGeneration) { _activeGeneration[entry.Key] = entry.Value; }

        _adoptedScope = request.Scope;
        _index = rebuilt;
        _predicate = request.Predicate;
        _activeContext = request.Context;
        _holdIngest = false;
        _liveIndexInvalidated = false;

        _scopeState.EvictOutOfScope(_adoptedScope, _activeGeneration);
        EvictGenerationsOutOfScope();

        PruneReleasedReaders();
        _index.RebindInsertComparer(OrderKeyComparerFactory.Create(_activeContext, _liveResolver));
        PublishWith(FreezeReaders());

        return AdoptOutcome.Adopted;
    }

    public bool TryAdoptRebuild(RebuildRequest request, ChunkedOrderIndex rebuilt) =>
        TryAdoptRebuild(request, rebuilt, long.MaxValue, allowAbandon: false) == AdoptOutcome.Adopted;

    public bool TrySetActiveScope(IReadOnlyCollection<EventLogId> scopeLogs, long scopeVersion)
    {
        if (!_scopeState.TrySetScope(scopeLogs, scopeVersion)) { return false; }

        Interlocked.Increment(ref _generation);

        return true;
    }

    internal static ChunkedOrderIndex BuildIndex(RebuildRequest request, CancellationToken cancellationToken, int bulkThreshold) =>
        BuildIndex(request, cancellationToken, bulkThreshold, DefaultMemoryBudgetBytes());

    internal static ChunkedOrderIndex BuildIndex(
        RebuildRequest request,
        CancellationToken cancellationToken,
        int bulkThreshold,
        long memoryBudgetBytes)
    {
        if (TryBuildBulk(request, cancellationToken, bulkThreshold, memoryBudgetBytes) is { } bulk) { return bulk; }

        var rebuilt = new ChunkedOrderIndex(OrderKeyComparerFactory.Create(request.Context, request.BeginResolver));
        int examined = 0;

        foreach ((LogGeneration key, int covered) in request.Coverage.Entries)
        {
            if (!request.Scope.Includes(key.LogId)) { continue; }

            if (!IsCurrent(key, request.RequestedGeneration)) { continue; }

            if (!request.BeginResolver.TryResolve(new EventLocator(key.LogId, key.Generation, 0), out IEventColumnReader? reader))
            {
                continue;
            }

            int limit = Math.Min(covered, reader.Count);

            for (int index = 0; index < limit; index++)
            {
                if ((examined++ & CancellationCheckMask) == 0) { cancellationToken.ThrowIfCancellationRequested(); }

                var locator = new EventLocator(key.LogId, key.Generation, index);

                if (request.Predicate(locator, reader))
                {
                    rebuilt.Insert(new OrderKey(locator));
                }
            }
        }

        return rebuilt;
    }

    // Half the currently-free heap headroom: total available minus the bytes currently allocated (which include the
    // live old published index and the raw event store), so the estimate is measured against real remaining room.
    // GC.GetTotalMemory reflects allocations since the last GC, unlike GCMemoryInfo.HeapSizeBytes (a last-GC snapshot).
    internal static long DefaultMemoryBudgetBytes()
    {
        long available = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        long headroom = available - GC.GetTotalMemory(forceFullCollection: false);

        return headroom > 0 ? headroom / 2 : 0;
    }

    private static ChunkedOrderIndex BuildCombinedBulk(
        RebuildRequest request,
        CancellationToken cancellationToken,
        List<(LogGeneration Key, int Covered)> keys,
        IComparer<OrderKey> comparer)
    {
        var runKeys = new List<LogGeneration>(keys.Count);
        var runs = new List<int[]>(keys.Count);
        long totalSurvivors = 0;

        foreach ((LogGeneration key, int covered) in keys)
        {
            IEventColumnReader reader = request.BeginResolver.Resolve(new EventLocator(key.LogId, key.Generation, 0));
            int[] run = SortLogSurvivors(request, cancellationToken, key, covered, reader);

            if (run.Length == 0) { continue; }

            runKeys.Add(key);
            runs.Add(run);
            totalSurvivors += run.Length;
        }

        var merged = new OrderKey[totalSurvivors];
        var cursors = new int[runs.Count];
        var queue = new PriorityQueue<int, OrderKey>(comparer);

        for (int run = 0; run < runs.Count; run++)
        {
            if ((run & CancellationCheckMask) == 0) { cancellationToken.ThrowIfCancellationRequested(); }

            queue.Enqueue(run, HeadKey(runKeys[run], runs[run], 0));
        }

        int emitted = 0;

        while (queue.TryDequeue(out int run, out OrderKey head))
        {
            if ((emitted & CancellationCheckMask) == 0) { cancellationToken.ThrowIfCancellationRequested(); }

            merged[emitted++] = head;
            int next = ++cursors[run];

            if (next < runs[run].Length) { queue.Enqueue(run, HeadKey(runKeys[run], runs[run], next)); }
        }

        return ChunkedOrderIndex.FromSortedRun(merged, comparer, cancellationToken);
    }

    private static ChunkedOrderIndex BuildSingleLogBulk(
        RebuildRequest request, CancellationToken cancellationToken, LogGeneration key, int covered, IComparer<OrderKey> comparer)
    {
        IEventColumnReader reader = request.BeginResolver.Resolve(new EventLocator(key.LogId, key.Generation, 0));
        int[] sortedIndices = SortLogSurvivors(request, cancellationToken, key, covered, reader);

        var sortedOrder = new OrderKey[sortedIndices.Length];

        for (int display = 0; display < sortedIndices.Length; display++)
        {
            if ((display & CancellationCheckMask) == 0) { cancellationToken.ThrowIfCancellationRequested(); }

            sortedOrder[display] = new OrderKey(new EventLocator(key.LogId, key.Generation, sortedIndices[display]));
        }

        return ChunkedOrderIndex.FromSortedRun(sortedOrder, comparer, cancellationToken);
    }

    private static List<(LogGeneration Key, int Covered)> CollectInScopeCurrentKeys(
        RebuildRequest request, CancellationToken cancellationToken)
    {
        var keys = new List<(LogGeneration, int)>();
        int scanned = 0;

        foreach ((LogGeneration key, int covered) in request.Coverage.Entries)
        {
            if ((scanned++ & CancellationCheckMask) == 0) { cancellationToken.ThrowIfCancellationRequested(); }

            if (!request.Scope.Includes(key.LogId)) { continue; }

            if (!IsCurrent(key, request.RequestedGeneration)) { continue; }

            if (!request.BeginResolver.TryResolve(new EventLocator(key.LogId, key.Generation, 0), out IEventColumnReader? reader))
            {
                continue;
            }

            keys.Add((key, Math.Min(covered, reader.Count)));
        }

        return keys;
    }

    // Peak estimate. The SoA columns + the whole-pool rank arrays are materialized one log at a time, so their peak is
    // the largest single log; the two coexisting OrderKey[] sets, the permutation, and the survivor lists all live
    // across the whole merge. (Keyword order/group never reaches here - TryBuildBulk routes it to the delegating path.)
    private static long EstimateBulkPeakBytes(
        RebuildRequest request,
        List<(LogGeneration Key, int Covered)> keys,
        long totalCovered)
    {
        checked
        {
            SortContext context = request.Context;

            int lanes = 2; // RecordId + OwningLog are always materialized.

            if (context.GroupBy is not null || context.OrderBy is null) { lanes++; } // DateAndTime

            if (context.OrderBy is not null) { lanes++; }

            if (context.GroupBy is not null) { lanes++; }

            long soaBytesPerRow = (long)lanes * BulkSoaLaneBytesPerRow;
            long soaPeak = 0;

            foreach ((LogGeneration key, int covered) in keys)
            {
                IEventColumnReader reader = request.BeginResolver.Resolve(new EventLocator(key.LogId, key.Generation, 0));
                long perLog = (covered * soaBytesPerRow) + ((long)reader.Pool.Count * BulkPooledRankBytesPerEntry);

                if (perLog > soaPeak) { soaPeak = perLog; }
            }

            long orderKeyPeak = 2L * BulkOrderKeyBytes * totalCovered;
            long scratch = 2L * sizeof(int) * totalCovered; // the int[] permutation + the survivor int lists

            return soaPeak + orderKeyPeak + scratch;
        }
    }

    private static OrderKey HeadKey(LogGeneration key, int[] run, int cursor) =>
        new(new EventLocator(key.LogId, key.Generation, run[cursor]));

    private static bool IsCurrent(in LogGeneration key, IReadOnlyDictionary<EventLogId, int> generation) =>
        generation.TryGetValue(key.LogId, out int current) && key.Generation == current;

    private static int[] SortLogSurvivors(
        RebuildRequest request, CancellationToken cancellationToken, LogGeneration key, int covered, IEventColumnReader reader)
    {
        var survivors = new List<int>(covered);

        for (int index = 0; index < covered; index++)
        {
            if ((index & CancellationCheckMask) == 0) { cancellationToken.ThrowIfCancellationRequested(); }

            var locator = new EventLocator(key.LogId, key.Generation, index);

            if (request.Predicate(locator, reader)) { survivors.Add(index); }
        }

        return ColumnDirectSort.SortColumnDirect(
            reader,
            CollectionsMarshal.AsSpan(survivors),
            request.Context.OrderBy,
            request.Context.IsDescending,
            request.Context.GroupBy,
            request.Context.IsGroupDescending,
            cancellationToken);
    }

    private static ChunkedOrderIndex? TryBuildBulk(
        RebuildRequest request,
        CancellationToken cancellationToken,
        int bulkThreshold,
        long memoryBudgetBytes)
    {
        List<(LogGeneration Key, int Covered)> keys = CollectInScopeCurrentKeys(request, cancellationToken);

        long totalCovered = 0;
        int summed = 0;

        foreach ((LogGeneration _, int covered) in keys)
        {
            if ((summed++ & CancellationCheckMask) == 0) { cancellationToken.ThrowIfCancellationRequested(); }

            totalCovered += covered;
        }

        if (totalCovered < bulkThreshold) { return null; }

        // Keyword order/group joins arbitrary-length strings per row, so its bake memory cannot be bounded - always
        // delegate. Otherwise fall back when the estimated transient peak would exceed the budget.
        if (request.Context.OrderBy is ColumnName.Keywords || request.Context.GroupBy is ColumnName.Keywords)
        {
            return null;
        }

        if (EstimateBulkPeakBytes(request, keys, totalCovered) > memoryBudgetBytes) { return null; }

        IComparer<OrderKey> comparer = OrderKeyComparerFactory.Create(request.Context, request.BeginResolver);

        return keys.Count switch
        {
            1 => BuildSingleLogBulk(request, cancellationToken, keys[0].Key, keys[0].Covered, comparer),
            >= 2 => BuildCombinedBulk(request, cancellationToken, keys, comparer),
            _ => null
        };
    }

    private ImmutableHashSet<LogGeneration> BuildAdoptedInScope()
    {
        var builder = ImmutableHashSet.CreateBuilder<LogGeneration>();

        foreach ((EventLogId logId, int generation) in _activeGeneration)
        {
            var key = new LogGeneration(logId, generation);

            if (_adoptedScope.Includes(logId) &&
                _latestReaders.TryGetValue(key, out IEventColumnReader? reader) &&
                reader.Count > 0)
            {
                builder.Add(key);
            }
        }

        return builder.ToImmutable();
    }

    private RebuildRequest CaptureRequest()
    {
        long generation = Interlocked.Increment(ref _generation);
        var generationSnapshot = new Dictionary<EventLogId, int>(_requestedGeneration);

        return new RebuildRequest(
            generation,
            _requestedPredicate,
            _requestedContext,
            generationSnapshot,
            _holdIngest,
            _scopeState.FreezeCoverage(),
            FreezeReaders(),
            _scopeState.Freeze(),
            _scopeState.ScopeVersion,
            _scopeState.SingleLog);
    }

    private void EvictGenerationsOutOfScope()
    {
        HashSet<EventLogId>? evicted = null;

        foreach (EventLogId logId in _activeGeneration.Keys)
        {
            if (!_adoptedScope.Includes(logId)) { (evicted ??= []).Add(logId); }
        }

        foreach (EventLogId logId in _requestedGeneration.Keys)
        {
            if (!_adoptedScope.Includes(logId)) { (evicted ??= []).Add(logId); }
        }

        if (evicted is null) { return; }

        foreach (EventLogId logId in evicted)
        {
            _activeGeneration.Remove(logId);
            _requestedGeneration.Remove(logId);
        }
    }

    private FrozenReaderResolver FreezeReaders() =>
        new(new Dictionary<LogGeneration, IEventColumnReader>(_latestReaders));

    private long MeasureTail(RebuildRequest request)
    {
        long tail = 0;

        foreach (LogGeneration key in _scopeState.Keys)
        {
            if (!request.Scope.Includes(key.LogId)) { continue; }

            if (!IsCurrent(key, _requestedGeneration)) { continue; }

            tail += Math.Max(0, _scopeState.Coverage(key) - request.Coverage.CoverageOf(key));
        }

        return tail;
    }

    private void PruneReleasedReaders()
    {
        List<LogGeneration>? released = null;

        foreach (LogGeneration key in _latestReaders.Keys)
        {
            if (!_adoptedScope.Includes(key.LogId) ||
                !_activeGeneration.TryGetValue(key.LogId, out int active) ||
                key.Generation < active)
            {
                (released ??= []).Add(key);
            }
        }

        if (released is null) { return; }

        foreach (LogGeneration key in released)
        {
            _scopeState.RecordGenerationSeen(key.LogId, key.Generation);
            _latestReaders.Remove(key);
        }
    }

    private OrderedViewSnapshot PublishWith(IReaderResolver frozenResolver)
    {
        _adoptedInScope = BuildAdoptedInScope();

        OrderedViewSnapshot snapshot = _index.Publish(OrderKeyComparerFactory.Create(_activeContext, frozenResolver), ++_publishVersion);
        Volatile.Write(ref _current, snapshot);

        return snapshot;
    }

    private bool TryAdmitReader(
        EventLogId logId, IEventColumnReader reader, bool isReplace, out LogGeneration readerKey, out bool contentReplaced)
    {
        readerKey = new LogGeneration(logId, reader.Generation);
        contentReplaced = false;

        if (!_scopeState.Includes(logId)) { return false; }

        bool reestablishing = !_requestedGeneration.ContainsKey(logId) && !_activeGeneration.ContainsKey(logId);

        if (reestablishing && !_scopeState.IsAtOrAboveGenerationFloor(logId, reader.Generation)) { return false; }

        if (_latestReaders.TryGetValue(readerKey, out var existing))
        {
            bool admit = reader.Count > existing.Count ||
                (reader.ContentVersion > existing.ContentVersion &&
                    (reader.Count >= existing.Count || isReplace));

            if (!admit) { return false; }

            contentReplaced = reader.ContentVersion > existing.ContentVersion &&
                (reader.Count <= existing.Count || isReplace);
        }

        _latestReaders[readerKey] = reader;

        if (!_requestedGeneration.ContainsKey(logId)) { _requestedGeneration[logId] = reader.Generation; }

        if (reader.Count > 0 &&
            !_activeGeneration.ContainsKey(logId) &&
            _requestedGeneration.GetValueOrDefault(logId, reader.Generation) == reader.Generation)
        {
            _activeGeneration[logId] = reader.Generation;
        }

        return true;
    }
}
