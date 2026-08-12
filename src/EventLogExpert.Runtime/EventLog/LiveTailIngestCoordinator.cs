// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.LogTable;
using System.Diagnostics;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.Runtime.EventLog;

internal sealed class LiveTailIngestCoordinator : IDisposable
{
    private const int MaxPendingPerLog = 1000;

    private static readonly TimeSpan s_maxBatchAge = TimeSpan.FromMilliseconds(16);

    private readonly IDispatcher _dispatcher;
    private readonly Lock _emissionGate = new();
    private readonly Lock _gate = new();
    private readonly TimeSpan _maxBatchAge;
    private readonly Dictionary<EventLogId, List<ResolvedEvent>> _pending = [];
    private readonly Timer _timer;

    private bool _disposed;

    private long _lastFlushTimestamp;

    public LiveTailIngestCoordinator(IDispatcher dispatcher, TimeSpan? maxBatchAge = null)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);

        _dispatcher = dispatcher;
        _maxBatchAge = maxBatchAge ?? s_maxBatchAge;

        TimeSpan period = _maxBatchAge == Timeout.InfiniteTimeSpan ? Timeout.InfiniteTimeSpan : _maxBatchAge;

        _timer = new Timer(_ => Flush(), null, period, period);
    }

    public void Discard(EventLogId logId)
    {
        lock (_gate) { _pending.Remove(logId); }
    }

    public void DiscardAll()
    {
        lock (_gate) { _pending.Clear(); }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) { return; }

            _disposed = true;

            _pending.Clear();
        }

        _timer.Dispose();
    }

    public void Enqueue(EventLogId logId, ResolvedEvent newEvent)
    {
        ArgumentNullException.ThrowIfNull(newEvent);

        bool flushNow;

        lock (_gate)
        {
            if (_disposed) { return; }

            if (!_pending.TryGetValue(logId, out List<ResolvedEvent>? batch))
            {
                batch = [];
                _pending[logId] = batch;
            }

            batch.Add(newEvent);

            bool idle = Stopwatch.GetElapsedTime(_lastFlushTimestamp) >= _maxBatchAge;

            flushNow = batch.Count >= MaxPendingPerLog || (idle && _pending.Count == 1 && batch.Count == 1);
        }

        if (flushNow) { Flush(); }
    }

    public void Flush()
    {
        Dictionary<EventLogId, IReadOnlyList<ResolvedEvent>> batches;

        lock (_gate)
        {
            if (_disposed || _pending.Count == 0) { return; }

            batches = new Dictionary<EventLogId, IReadOnlyList<ResolvedEvent>>(_pending.Count);

            foreach ((EventLogId logId, List<ResolvedEvent> batch) in _pending) { batches[logId] = batch.AsReadOnly(); }

            _pending.Clear();
            _lastFlushTimestamp = Stopwatch.GetTimestamp();
        }

        lock (_emissionGate)
        {
            if (Volatile.Read(ref _disposed)) { return; }

            _dispatcher.Dispatch(new IngestRawEventsAction(batches, RawIngestMode.Prepend));
        }
    }
}
