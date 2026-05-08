// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Interop;
using EventLogExpert.Eventing.Models;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace EventLogExpert.Eventing.Readers;

public sealed class EventLogWatcher : IDisposable
{
    private readonly string? _bookmark;
    // Tracks ManagedThreadIds currently inside ProcessNewEvents. Used by
    // Dispose() and the Enabled=false path to detect a handler trying to stop
    // the watcher from inside its own callback, which would self-deadlock on
    // Unsubscribe's drain wait. Non-disposable so it stays safe across
    // idempotent Dispose calls and finalizer paths.
    private readonly ConcurrentDictionary<int, byte> _callbackThreadIds = new();
    private readonly AutoResetEvent _newEvents = new(false);
    private readonly string _path;
    private readonly EvtHandle _queryHandle;
    private readonly bool _renderXml;
    // Serializes lifecycle transitions: Subscribe (via Enabled=true), Unsubscribe
    // (via Enabled=false or Dispose), and the disposed-state check that gates
    // Subscribe. Without this, two concurrent threads can both pass the
    // _waitHandle null check in Unsubscribe and both call
    // RegisteredWaitHandle.Unregister; the second call returns false (already
    // unregistered) and does NOT signal its wait object, so WaitOne blocks
    // indefinitely. Beyond that race, Subscribe and Unsubscribe both mutate
    // _subscriptionHandle/_waitHandle/_isSubscribed; without the lock,
    // Enabled=true racing with Enabled=false (or Dispose) can leave
    // _subscriptionHandle disposed but ProcessNewEvents already running over
    // it. The lock also makes the loser's Unsubscribe wait for the winner's
    // drain to complete — required so both callers honor the "no callback
    // fires after Unsubscribe returns" contract.
    private readonly Lock _lifecycleLock = new();

    private int _disposed;
    private bool _isSubscribed;
    private EvtHandle _subscriptionHandle = EvtHandle.Zero;
    private RegisteredWaitHandle? _waitHandle;

    public EventLogWatcher(string path, string? bookmark, bool renderXml = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        _path = path;
        _bookmark = bookmark;
        _renderXml = renderXml;

        // Validate the log exists by attempting to open it
        _queryHandle = NativeMethods.EvtQuery(EventLogSession.GlobalSession.Handle, path, null, PathType.LogName);
        int error = Marshal.GetLastWin32Error();

        if (_queryHandle is { IsInvalid: false }) { return; }

        _queryHandle.Dispose();
        NativeMethods.ThrowEventLogException(error);
    }

    public EventLogWatcher(string path, bool renderXml = false) : this(path, null, renderXml) { }

    ~EventLogWatcher()
    {
        Dispose(false);
    }

    /// <summary>
    ///     Raised when one or more new event records arrive from the underlying Windows EventLog subscription; subscriber
    ///     exceptions are caught and isolated, and synchronously calling <see cref="Dispose" /> or setting
    ///     <see cref="Enabled" /> to <c>false</c> from inside a handler throws <see cref="InvalidOperationException" />
    ///     unless another thread has already begun disposing or unsubscribing the watcher (in which case the call is a
    ///     silent no-op for IDisposable idempotency); schedule the stop on a different thread to avoid the throw entirely.
    /// </summary>
    public event EventHandler<EventRecord>? EventRecordWritten;

    /// <summary>
    ///     Gets or sets whether the watcher is actively subscribed to the log; setting <c>false</c> tears down the native
    ///     subscription and blocks until any in-flight <see cref="EventRecordWritten" /> callbacks complete.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when the setter is invoked with a value of <c>false</c> from inside an
    ///     <see cref="EventRecordWritten" /> handler while the watcher is still subscribed; the unsubscribe path waits
    ///     for in-flight callbacks to complete and would self-deadlock the calling thread. If another thread has already
    ///     unsubscribed or disposed the watcher, the call is a silent no-op.
    /// </exception>
    /// <exception cref="ObjectDisposedException">
    ///     Thrown when the setter is invoked with a value of <c>true</c> after the watcher has been disposed.
    /// </exception>
    public bool Enabled
    {
        get
        {
            return _isSubscribed;
        }
        set
        {
            switch (value)
            {
                case true when !_isSubscribed:
                    Subscribe();
                    break;
                case false when _isSubscribed:
                    ThrowIfCurrentCallbackWouldDeadlock();
                    Unsubscribe();
                    break;
            }
        }
    }

    /// <summary>
    ///     Releases the native subscription and query handles; idempotent and safe to call multiple times from any
    ///     non-callback thread.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when invoked from inside an <see cref="EventRecordWritten" /> handler while the watcher is still live.
    ///     The unsubscribe path waits for in-flight callbacks to complete and would self-deadlock the calling thread.
    ///     If another thread has already begun disposing the watcher, the call is a silent no-op for IDisposable
    ///     idempotency.
    /// </exception>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        // Idempotent fast path: if another thread already disposed (or is
        // disposing), reentrant calls — including ones that originate from
        // inside a callback after the disposing thread has already won the
        // CAS — must be a no-op. This preserves the IDisposable.Dispose
        // idempotency contract that callers historically relied on.
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        // Detect reentrancy BEFORE marking _disposed. A handler invoking
        // Dispose() on its own watcher would otherwise wedge inside Unsubscribe
        // (which blocks on the in-flight callback drain) and leave the watcher
        // unrecoverable. Throwing here keeps _disposed at 0 so a subsequent
        // Dispose call from another thread (e.g. the test cleanup path) can
        // still complete normally.
        if (disposing)
        {
            ThrowIfCurrentCallbackWouldDeadlock();
        }

        // Use Interlocked.CompareExchange for atomic check-and-set.
        // Only one thread will successfully change _disposed from 0 to 1.
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
        {
            return; // Already disposed by another thread
        }

        if (disposing)
        {
            Unsubscribe();
            // Release the AutoResetEvent's kernel handle. Must happen AFTER
            // Unsubscribe completes its drain — in-flight callbacks reference
            // _newEvents.SafeWaitHandle via the ThreadPool wait, so disposing
            // earlier could crash a callback mid-flight.
            _newEvents.Dispose();
        }

        if (_queryHandle is { IsInvalid: false })
        {
            _queryHandle.Dispose();
        }
    }

    private void ProcessNewEvents(object? state, bool timedOut)
    {
        int threadId = Environment.CurrentManagedThreadId;

        _callbackThreadIds[threadId] = 0;

        try
        {
            bool success;
            var buffer = new IntPtr[64];

            do
            {
                if (_isSubscribed is false) { break; }

                int count = 0;

                success = NativeMethods.EvtNext(_subscriptionHandle, buffer.Length, buffer, 0, 0, ref count);

                if (!success) { return; }

                for (int i = 0; i < count; i++)
                {
                    using var eventHandle = new EvtHandle(buffer[i]);
                    EventRecord @event;

                    try
                    {
                        @event = NativeMethods.RenderEvent(eventHandle);
                        @event.Properties = NativeMethods.RenderEventProperties(eventHandle);

                        if (_renderXml)
                        {
                            @event.Xml = NativeMethods.RenderEventXml(eventHandle);
                        }
                    }
                    catch (Exception ex)
                    {
                        @event = new EventRecord { RecordId = null, Error = ex.Message };
                    }

                    @event.PathName = _path;

                    // Subscriber failures are isolated by contract: one handler
                    // must not prevent subsequent handlers in the multicast
                    // chain or future events from being delivered. Without this
                    // per-subscriber try/catch, a single throwing subscriber
                    // would short-circuit the multicast invoke and let the
                    // exception bubble into the ThreadPool worker.
                    var subscribers = EventRecordWritten;

                    if (subscribers is null) { continue; }

                    foreach (Delegate handler in subscribers.GetInvocationList())
                    {
                        try
                        {
                            ((EventHandler<EventRecord>)handler)(this, @event);
                        }
                        catch (Exception)
                        {
                            // Intentional: per-subscriber isolation. No logger
                            // is wired into this layer; if one is added later,
                            // log the exception here.
                        }
                    }
                }
            }
            while (success);
        }
        finally
        {
            _callbackThreadIds.TryRemove(threadId, out _);
        }
    }

    private void ThrowIfCurrentCallbackWouldDeadlock()
    {
        if (_callbackThreadIds.ContainsKey(Environment.CurrentManagedThreadId))
        {
            throw new InvalidOperationException(
                "EventLogWatcher cannot be stopped from within an EventRecordWritten handler.");
        }
    }

    private void Subscribe()
    {
        // Serialized with Unsubscribe so Enabled=true racing with Enabled=false
        // (or Dispose) cannot leave _subscriptionHandle disposed while this
        // method is still mutating it. Reentry from inside a callback is
        // impossible because the Enabled setter calls
        // ThrowIfCurrentCallbackWouldDeadlock before invoking Subscribe; the
        // synchronous ProcessNewEvents call below cannot self-recurse because
        // it runs before _waitHandle is registered.
        lock (_lifecycleLock)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

            if (_isSubscribed) { throw new InvalidOperationException("Already subscribed."); }

            EvtSubscribeFlags flag = string.IsNullOrEmpty(_bookmark) ?
                EvtSubscribeFlags.ToFutureEvents :
                EvtSubscribeFlags.StartAfterBookmark;

            EvtHandle bookmarkHandle = string.IsNullOrEmpty(_bookmark) ?
                EvtHandle.Zero :
                NativeMethods.EvtCreateBookmark(_bookmark);

            if (!string.IsNullOrEmpty(_bookmark) && bookmarkHandle.IsInvalid)
            {
                int error = Marshal.GetLastWin32Error();
                bookmarkHandle.Dispose();
                NativeMethods.ThrowEventLogException(error);
            }

            _subscriptionHandle = NativeMethods.EvtSubscribe(
                EventLogSession.GlobalSession.Handle,
                _newEvents.SafeWaitHandle,
                _path,
                null,
                bookmarkHandle,
                IntPtr.Zero,
                IntPtr.Zero,
                flag);

            if (!string.IsNullOrEmpty(_bookmark))
            {
                bookmarkHandle.Dispose();
            }

            int subscriptionError = Marshal.GetLastWin32Error();

            if (_subscriptionHandle.IsInvalid)
            {
                NativeMethods.ThrowEventLogException(subscriptionError);
            }

            _isSubscribed = true;

            ProcessNewEvents(null, false);

            _waitHandle = ThreadPool.RegisterWaitForSingleObject(_newEvents, ProcessNewEvents, null, -1, false);
        }
    }

    private void Unsubscribe()
    {
        // Serialized with Subscribe (see _lifecycleLock comment) so concurrent
        // callers (Dispose racing with Enabled=false, or two Enabled=false calls)
        // cannot both attempt Unregister on the same _waitHandle, and so a
        // racing Subscribe cannot interleave with the teardown. The lock also
        // guarantees the losing caller observes the winner's drain — required
        // for the "no callback fires after Unsubscribe returns" contract that
        // callers (e.g. DatabaseService delete) rely on. Reentry from inside a
        // callback is impossible because both call sites first invoke
        // ThrowIfCurrentCallbackWouldDeadlock.
        lock (_lifecycleLock)
        {
            _isSubscribed = false;

            if (_waitHandle is not null)
            {
                // Pass a wait object (not null) so Unregister blocks until all in-flight
                // ProcessNewEvents callbacks have completed. Per the .NET docs on
                // RegisteredWaitHandle.Unregister, passing null does NOT wait — and our
                // callback creates per-event resolver scopes that hold pooled SQLite
                // connections, so callers (e.g., DatabaseService delete) need a hard
                // guarantee that no callback can fire after Unregister returns.
                using var unregisterSignal = new ManualResetEvent(false);

                _waitHandle.Unregister(unregisterSignal);
                unregisterSignal.WaitOne();
                _waitHandle = null;
            }

            // Use IsClosed (not IsInvalid) — SafeHandle.IsInvalid is based on
            // the underlying handle value and does NOT flip after Dispose, so
            // checking IsInvalid would let a serialized loser call Dispose a
            // second time. SafeHandle.Dispose is idempotent today, but the
            // explicit IsClosed check makes the intent clear and removes the
            // dependency on that idempotency.
            if (!_subscriptionHandle.IsClosed)
            {
                _subscriptionHandle.Dispose();
            }
        }
    }
}
