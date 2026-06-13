// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.LogTable;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.Runtime.EventLog;

internal sealed class PartialLoadCoordinator : IDisposable
{
    private static readonly TimeSpan s_flushWindow = TimeSpan.FromMilliseconds(1000);

    private readonly Dictionary<EventLogId, List<ResolvedEvent>> _buffers = [];
    private readonly IDispatcher _dispatcher;
    private readonly HashSet<EventLogId> _finalized = [];
    private readonly Lock _gate = new();
    private readonly HashSet<EventLogId> _seen = [];
    private readonly Timer _timer;

    private bool _disposed;

    public PartialLoadCoordinator(IDispatcher dispatcher) : this(dispatcher, s_flushWindow) { }

    internal PartialLoadCoordinator(IDispatcher dispatcher, TimeSpan flushInterval)
    {
        _dispatcher = dispatcher;
        _timer = new Timer(_ => Flush(), null, flushInterval, flushInterval);
    }

    public void Discard(EventLogId logId)
    {
        lock (_gate)
        {
            _buffers.Remove(logId);
            _finalized.Remove(logId);
            _seen.Remove(logId);
        }
    }

    public void DiscardAll()
    {
        lock (_gate)
        {
            _buffers.Clear();
            _finalized.Clear();
            _seen.Clear();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _buffers.Clear();
            _finalized.Clear();
            _seen.Clear();
        }

        _timer.Dispose();
    }

    public void Enqueue(EventLogId logId, IReadOnlyList<ResolvedEvent> filteredEvents)
    {
        if (filteredEvents.Count == 0) { return; }

        lock (_gate)
        {
            // A straggler partial delta can arrive after the final LoadEvents (effects are fire-and-forget); dropping finalized logs prevents duplicate rows.
            if (_disposed || _finalized.Contains(logId)) { return; }

            if (!_buffers.TryGetValue(logId, out var buffer))
            {
                buffer = new List<ResolvedEvent>(filteredEvents.Count);
                _buffers[logId] = buffer;
            }

            buffer.AddRange(filteredEvents);

            if (_seen.Add(logId)) { FlushLocked(); }
        }
    }

    public void MarkFinalized(EventLogId logId)
    {
        lock (_gate)
        {
            _finalized.Add(logId);
            _buffers.Remove(logId);
        }
    }

    internal void Flush()
    {
        lock (_gate) { FlushLocked(); }
    }

    private void FlushLocked()
    {
        if (_disposed || _buffers.Count == 0) { return; }

        var batch = new Dictionary<EventLogId, IReadOnlyList<ResolvedEvent>>(_buffers.Count);

        foreach (var (logId, buffer) in _buffers)
        {
            if (buffer.Count > 0) { batch[logId] = buffer.AsReadOnly(); }
        }

        _buffers.Clear();

        if (batch.Count == 0) { return; }

        // Dispatch under the lock so batches and the final UpdateTable keep FIFO order in Fluxor's queue.
        _dispatcher.Dispatch(new AppendTableEventsBatchAction(batch));
    }
}
