// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Interop;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace EventLogExpert.Eventing.Readers;

public sealed class EventLogWatcher : IDisposable
{
    private readonly string? _bookmark;
    private readonly ConcurrentDictionary<int, byte> _callbackThreadIds = new();
    private readonly Lock _lifecycleLock = new();
    private readonly AutoResetEvent _newEvents = new(false);
    private readonly string _path;
    private readonly EvtHandle _queryHandle;
    private readonly bool _renderXml;

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

        _queryHandle = NativeMethods.EvtQuery(EventLogSession.GlobalSession.Handle, path, null, LogPathType.Channel);
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

    /// <summary>Raised when new event records arrive; subscriber exceptions are isolated.</summary>
    public event EventHandler<EventRecord>? EventRecordWritten;

    /// <summary>Gets or sets whether the watcher is actively subscribed.</summary>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when set to <c>false</c> from inside an <see cref="EventRecordWritten" /> handler
    ///     while the watcher is still subscribed.
    /// </exception>
    /// <exception cref="ObjectDisposedException">
    ///     Thrown when set to <c>true</c> after the watcher has been disposed.
    /// </exception>
    public bool Enabled
    {
        get
        {
            return Volatile.Read(ref _isSubscribed);
        }
        set
        {
            switch (value)
            {
                case true when !Volatile.Read(ref _isSubscribed):
                    Subscribe();
                    break;
                case false when Volatile.Read(ref _isSubscribed):
                    ThrowIfCurrentCallbackWouldDeadlock();
                    Unsubscribe();
                    break;
            }
        }
    }

    /// <summary>Releases the native subscription and query handles; idempotent.</summary>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when invoked from inside an <see cref="EventRecordWritten" /> handler unless
    ///     the watcher has already been disposed.
    /// </exception>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        // Throw before the CAS so a subsequent Dispose from another thread can still complete.
        if (disposing)
        {
            ThrowIfCurrentCallbackWouldDeadlock();
        }

        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
        {
            return;
        }

        if (disposing)
        {
            Unsubscribe();

            // After Unsubscribe — in-flight callbacks hold _newEvents.SafeWaitHandle.
            _newEvents.Dispose();
        }
        else
        {
            // Finalizer path: release native resources without locks or blocking signals.
            _waitHandle?.Unregister(null);

            if (!_subscriptionHandle.IsClosed)
            {
                _subscriptionHandle.Dispose();
            }
        }

        if (_queryHandle is { IsInvalid: false })
        {
            _queryHandle.Dispose();
        }
    }

    private void ReadAndRaiseEvents(object? state, bool timedOut)
    {
        int threadId = Environment.CurrentManagedThreadId;

        _callbackThreadIds[threadId] = 0;

        try
        {
            bool success;
            var buffer = new IntPtr[64];

            do
            {
                if (Volatile.Read(ref _isSubscribed) is false) { break; }

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
                            // Intentional: per-subscriber isolation.
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

    private void Subscribe()
    {
        lock (_lifecycleLock)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

            if (Volatile.Read(ref _isSubscribed)) { throw new InvalidOperationException("Already subscribed."); }

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

            Volatile.Write(ref _isSubscribed, true);
        }

        // Drain backlog before TP wait registration to prevent concurrent EvtNext.
        ReadAndRaiseEvents(null, false);

        lock (_lifecycleLock)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

            // Concurrent Unsubscribe may have torn down the subscription during the drain.
            if (!Volatile.Read(ref _isSubscribed)) { return; }

            _waitHandle = ThreadPool.RegisterWaitForSingleObject(_newEvents, ReadAndRaiseEvents, null, -1, false);
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

    private void Unsubscribe()
    {
        lock (_lifecycleLock)
        {
            Volatile.Write(ref _isSubscribed, false);

            if (_waitHandle is not null)
            {
                // Signal (not null) — Unregister(null) does NOT wait for in-flight callbacks.
                using var unregisterSignal = new ManualResetEvent(false);

                _waitHandle.Unregister(unregisterSignal);
                unregisterSignal.WaitOne();
                _waitHandle = null;
            }

            // IsClosed flips on Dispose; IsInvalid is value-based and never flips.
            if (!_subscriptionHandle.IsClosed)
            {
                _subscriptionHandle.Dispose();
            }
        }
    }
}
