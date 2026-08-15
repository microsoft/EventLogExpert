// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Filtering.Evaluation;
using EventLogExpert.Runtime.EventLog;
using Fluxor;
using System.Collections.Immutable;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.Runtime.LogTable;

internal sealed class FilteredLogPresenceCoordinator : IDisposable
{
    private readonly EventLogConcurrencyState _concurrencyState;
    private readonly HashSet<EventLogId> _dirty = [];
    private readonly IDispatcher _dispatcher;
    private readonly IState<EventLogState> _eventLogState;
    private readonly Lock _gate = new();
    private readonly XmlFilterMatchCache _matchCache;
    private readonly IState<FilteredLogPresenceState> _presenceState;
    private readonly IState<RawEventStoreState> _rawEventStore;
    private readonly bool _scanInline;
    private readonly Dictionary<EventLogId, ScanPosition> _scanPositions = [];

    private bool _disposed;
    private long _filterVersion;
    private bool _scanRunning;

    public FilteredLogPresenceCoordinator(
        IDispatcher dispatcher,
        IState<EventLogState> eventLogState,
        IState<RawEventStoreState> rawEventStore,
        IState<FilteredLogPresenceState> presenceState,
        EventLogConcurrencyState concurrencyState,
        XmlFilterMatchCache matchCache)
        : this(dispatcher, eventLogState, rawEventStore, presenceState, concurrencyState, matchCache, scanInline: false) { }

    internal FilteredLogPresenceCoordinator(
        IDispatcher dispatcher,
        IState<EventLogState> eventLogState,
        IState<RawEventStoreState> rawEventStore,
        IState<FilteredLogPresenceState> presenceState,
        EventLogConcurrencyState concurrencyState,
        XmlFilterMatchCache matchCache,
        bool scanInline)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(eventLogState);
        ArgumentNullException.ThrowIfNull(rawEventStore);
        ArgumentNullException.ThrowIfNull(presenceState);
        ArgumentNullException.ThrowIfNull(concurrencyState);
        ArgumentNullException.ThrowIfNull(matchCache);

        _dispatcher = dispatcher;
        _eventLogState = eventLogState;
        _rawEventStore = rawEventStore;
        _presenceState = presenceState;
        _concurrencyState = concurrencyState;
        _matchCache = matchCache;
        _scanInline = scanInline;
    }

    internal Action? OnBatchDrainedForTest { get; set; }

    public void Discard(EventLogId logId)
    {
        lock (_gate)
        {
            _dirty.Remove(logId);
            _scanPositions.Remove(logId);
        }

        ScheduleScan();
    }

    public void DiscardAll()
    {
        lock (_gate)
        {
            _dirty.Clear();
            _scanPositions.Clear();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) { return; }

            _disposed = true;
            _dirty.Clear();
            _scanPositions.Clear();
        }
    }

    public void MarkAppended(IEnumerable<EventLogId> logIds)
    {
        ArgumentNullException.ThrowIfNull(logIds);

        lock (_gate)
        {
            if (_disposed) { return; }

            foreach (var logId in logIds) { _dirty.Add(logId); }
        }

        ScheduleScan();
    }

    public void MarkFilterChanged()
    {
        ImmutableArray<EventLogId> openLogs = [.. _eventLogState.Value.OpenLogs.Values.Select(log => log.Id)];
        long filterVersion;

        lock (_gate)
        {
            if (_disposed) { return; }

            filterVersion = ++_filterVersion;
            _scanPositions.Clear();
            _dirty.Clear();
        }

        _dispatcher.Dispatch(new FilteredPresenceInvalidatedAction(filterVersion, openLogs));

        lock (_gate)
        {
            if (_disposed) { return; }

            foreach (var logId in openLogs) { _dirty.Add(logId); }
        }

        ScheduleScan();
    }

    public void MarkRebuilt(EventLogId logId)
    {
        lock (_gate)
        {
            if (_disposed) { return; }

            _scanPositions.Remove(logId);
            _dirty.Add(logId);
        }

        ScheduleScan();
    }

    /// <summary>Re-attempts a scan that was deferred while on-demand XML match was still being computed.</summary>
    public void RetryScan() => ScheduleScan();

    private static bool ScanFrom(
        EventColumnStore store,
        EventLogId logId,
        int start,
        Func<IEventColumnReader, EventLocator, bool> survives)
    {
        var reader = store.CreateReader(logId);
        int count = reader.Count;

        for (int index = start; index < count; index++)
        {
            if (survives(reader, reader.LocatorAt(index))) { return true; }
        }

        return false;
    }

    private ImmutableArray<KeyValuePair<EventLogId, FilteredLogPresence>> Evaluate(EventLogId[] batch, long filterVersion)
    {
        var eventLogState = _eventLogState.Value;
        Filter filter = eventLogState.AppliedFilter;

        if (XmlDeferralActive(eventLogState))
        {
            lock (_gate)
            {
                if (!_disposed) { foreach (var logId in batch) { _dirty.Add(logId); } }
            }

            return [];
        }

        var openIds = eventLogState.OpenLogs.Values.Select(log => log.Id).ToHashSet();
        var stores = _rawEventStore.Value.ByLog;
        var presenceSnapshot = _presenceState.Value;
        var known = presenceSnapshot.ByLog;
        bool knownReflectsCurrentFilter = presenceSnapshot.FilterVersion == filterVersion;
        var results = ImmutableArray.CreateBuilder<KeyValuePair<EventLogId, FilteredLogPresence>>(batch.Length);

        Func<IEventColumnReader, EventLocator, bool>? survives = null;

        foreach (var logId in batch)
        {
            lock (_gate)
            {
                if (_disposed || filterVersion != _filterVersion) { return []; }
            }

            if (!openIds.Contains(logId) || !stores.TryGetValue(logId, out var store)) { continue; }

            if (store.Count <= 0)
            {
                results.Add(new(logId, FilteredLogPresence.NoSurvivor));

                continue;
            }

            if (!filter.IsFilteringEnabled)
            {
                results.Add(new(logId, FilteredLogPresence.HasSurvivor));

                continue;
            }

            if (knownReflectsCurrentFilter && known.TryGetValue(logId, out var current) && current == FilteredLogPresence.HasSurvivor) { continue; }

            int start = ResumePoint(logId, store);

            survives ??= XmlFilterGate.BuildSurvivorPredicate(filter, _concurrencyState, _matchCache);

            bool found = ScanFrom(store, logId, start, survives);

            results.Add(new(logId, found ? FilteredLogPresence.HasSurvivor : FilteredLogPresence.NoSurvivor));

            lock (_gate)
            {
                if (_disposed || filterVersion != _filterVersion) { return []; }

                if (found) { _scanPositions.Remove(logId); }
                else { _scanPositions[logId] = new ScanPosition(store.Count, store.Generation); }
            }
        }

        return results.ToImmutable();
    }

    private int ResumePoint(EventLogId logId, EventColumnStore store)
    {
        lock (_gate)
        {
            if (!_scanPositions.TryGetValue(logId, out var scanPosition)) { return 0; }

            if (scanPosition.Generation != store.Generation || store.Count < scanPosition.ScannedCount)
            {
                _scanPositions.Remove(logId);

                return 0;
            }

            return scanPosition.ScannedCount;
        }
    }

    private void RunScanLoop()
    {
        while (true)
        {
            EventLogId[] batch;
            long filterVersion;

            lock (_gate)
            {
                if (_disposed || _dirty.Count <= 0)
                {
                    _scanRunning = false;

                    return;
                }

                if (XmlDeferralActive(_eventLogState.Value))
                {
                    _scanRunning = false;

                    return;
                }

                batch = [.. _dirty];
                filterVersion = _filterVersion;
                _dirty.Clear();
            }

            OnBatchDrainedForTest?.Invoke();

            try
            {
                var verdicts = Evaluate(batch, filterVersion);

                if (verdicts.Length > 0) { _dispatcher.Dispatch(new FilteredPresenceUpdatedAction(filterVersion, verdicts)); }
            }
            catch (Exception)
            {
                // A faulty survivor predicate or a dispatch failure must not escape this fire-and-forget scan
                // loop: escaping would leave _scanRunning stuck true and silently wedge every later presence
                // scan. Drop this batch; a later filter change or append re-dirties the logs and retries.
            }
        }
    }

    private void ScheduleScan()
    {
        lock (_gate)
        {
            if (_disposed || _scanRunning || _dirty.Count <= 0) { return; }

            if (XmlDeferralActive(_eventLogState.Value)) { return; }

            _scanRunning = true;
        }

        if (_scanInline)
        {
            RunScanLoop();

            return;
        }

        _ = Task.Run(RunScanLoop);
    }

    private bool XmlDeferralActive(EventLogState eventLogState) =>
        XmlFilterGate.IsDeferred(
            eventLogState.AppliedFilter, eventLogState, _rawEventStore.Value, _concurrencyState, _matchCache);

    private readonly record struct ScanPosition(int ScannedCount, int Generation);
}
