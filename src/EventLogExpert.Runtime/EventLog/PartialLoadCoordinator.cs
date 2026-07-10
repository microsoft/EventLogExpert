// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Runtime.LogTable;
using Fluxor;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.Runtime.EventLog;

internal sealed class PartialLoadCoordinator : IDisposable
{
    private static readonly TimeSpan s_flushWindow = TimeSpan.FromMilliseconds(1000);

    private readonly HashSet<EventLogId> _dirty = [];
    private readonly IDispatcher _dispatcher;
    private readonly IState<EventLogState> _eventLogState;
    private readonly HashSet<EventLogId> _finalized = [];
    private readonly Lock _gate = new();
    private readonly IState<LogTableState> _logTableState;
    private readonly IState<RawEventStoreState> _rawEventStore;
    private readonly HashSet<EventLogId> _seen = [];
    private readonly Timer _timer;
    private readonly Dictionary<EventLogId, int> _versions = [];

    private bool _disposed;

    public PartialLoadCoordinator(
        IDispatcher dispatcher,
        IState<RawEventStoreState> rawEventStore,
        IState<EventLogState> eventLogState,
        IState<LogTableState> logTableState)
        : this(dispatcher, rawEventStore, eventLogState, logTableState, s_flushWindow) { }

    internal PartialLoadCoordinator(
        IDispatcher dispatcher,
        IState<RawEventStoreState> rawEventStore,
        IState<EventLogState> eventLogState,
        IState<LogTableState> logTableState,
        TimeSpan flushInterval)
    {
        _dispatcher = dispatcher;
        _rawEventStore = rawEventStore;
        _eventLogState = eventLogState;
        _logTableState = logTableState;
        _timer = new Timer(_ => Flush(), null, flushInterval, flushInterval);
    }

    public void Discard(EventLogId logId)
    {
        lock (_gate)
        {
            _dirty.Remove(logId);
            _versions.Remove(logId);
            _finalized.Remove(logId);
            _seen.Remove(logId);
        }
    }

    public void DiscardAll()
    {
        lock (_gate)
        {
            _dirty.Clear();
            _versions.Clear();
            _finalized.Clear();
            _seen.Clear();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _dirty.Clear();
            _versions.Clear();
            _finalized.Clear();
            _seen.Clear();
        }

        _timer.Dispose();
    }

    public void Enqueue(EventLogId logId, int version)
    {
        lock (_gate)
        {
            // A straggler partial delta can arrive after the final LoadEvents (effects are fire-and-forget); dropping finalized logs prevents rebuilding a view the finalize already published.
            if (_disposed || _finalized.Contains(logId)) { return; }

            _dirty.Add(logId);

            // Use Math.Min so a buffer straddling a filter change adopts the older version, forcing a safe re-sort at finalize.
            _versions[logId] = _versions.TryGetValue(logId, out var existingVersion)
                ? Math.Min(existingVersion, version)
                : version;

            if (_seen.Add(logId)) { FlushLocked(); }
        }
    }

    public void MarkFinalized(EventLogId logId)
    {
        lock (_gate)
        {
            _finalized.Add(logId);
            _dirty.Remove(logId);
            _versions.Remove(logId);
        }
    }

    internal void Flush()
    {
        lock (_gate) { FlushLocked(); }
    }

    private void FlushLocked()
    {
        if (_disposed || _dirty.Count == 0) { return; }

        var raw = _rawEventStore.Value.ByLog;
        var filter = _eventLogState.Value.AppliedFilter;
        var context = _logTableState.Value.SortContext;

        var viewsByLog = new Dictionary<EventLogId, EventColumnView>(_dirty.Count);
        var versionByLog = new Dictionary<EventLogId, int>(_dirty.Count);

        foreach (var logId in _dirty)
        {
            if (!raw.TryGetValue(logId, out var store)) { continue; }

            viewsByLog[logId] = DisplayViewBuilder.Build(store, logId, filter, context);

            if (_versions.TryGetValue(logId, out var version)) { versionByLog[logId] = version; }
        }

        _dirty.Clear();
        _versions.Clear();

        if (viewsByLog.Count == 0) { return; }

        // Dispatch under the lock so batches and the final UpdateTable keep FIFO order in Fluxor's queue.
        _dispatcher.Dispatch(new AppendTableEventsBatchAction { ViewsByLog = viewsByLog, VersionByLog = versionByLog });
    }
}
