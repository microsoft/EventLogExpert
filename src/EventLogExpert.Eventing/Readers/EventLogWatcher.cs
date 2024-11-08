// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Models;
using System.Runtime.InteropServices;

namespace EventLogExpert.Eventing.Readers;

public sealed partial class EventLogWatcher(string path, string? bookmark, bool renderXml = true) : IDisposable
{
    private readonly string? _bookmark = bookmark;
    private readonly AutoResetEvent _newEvents = new(false);
    private readonly EvtHandle _queryHandle = EventMethods.EvtQuery(EventLogSession.GlobalSession.Handle, path, null, PathType.LogName);

    private bool _isSubscribed;
    private EvtHandle _subscriptionHandle = EvtHandle.Zero;
    private RegisteredWaitHandle? _waitHandle;

    public EventLogWatcher(string path, bool renderXml = true) : this(path, null, renderXml) { }

    ~EventLogWatcher()
    {
        Dispose(disposing: false);
    }

    public event EventHandler<EventRecord>? EventRecordWritten;

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
                    Unsubscribe();
                    break;
            }
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        if (disposing)
        {
            Unsubscribe();
        }

        if (!_queryHandle.IsInvalid)
        {
            _queryHandle.Dispose();
        }
    }

    private void ProcessNewEvents(object? state, bool timedOut)
    {
        bool success;
        var buffer = new IntPtr[64];

        do
        {
            if (_isSubscribed is false) { break; }

            int count = 0;

            success = EventMethods.EvtNext(_subscriptionHandle, buffer.Length, buffer, 0, 0, ref count);

            if (!success) { return; }

            for (int i = 0; i < count; i++)
            {
                using var eventHandle = new EvtHandle(buffer[i]);
                EventRecord @event;

                try
                {
                    @event = EventMethods.RenderEvent(eventHandle, EvtRenderFlags.EventValues);
                    @event.Properties = EventMethods.RenderEventProperties(eventHandle);

                    if (renderXml)
                    {
                        @event.Xml = EventMethods.RenderEventXml(eventHandle);
                    }
                }
                catch (Exception ex)
                {
                    @event = new EventRecord { RecordId = null, Error = ex.Message };
                }

                EventRecordWritten?.Invoke(this, @event);
            }
        } while (success);
    }

    private void Subscribe()
    {
        if (_isSubscribed) { throw new InvalidOperationException("Already subscribed."); }

        EvtSubscribeFlags flag = string.IsNullOrEmpty(_bookmark) ?
            EvtSubscribeFlags.ToFutureEvents :
            EvtSubscribeFlags.StartAfterBookmark;

        using EvtHandle bookmarkHandle = EventMethods.EvtCreateBookmark(_bookmark);
        int error = Marshal.GetLastWin32Error();

        if (bookmarkHandle.IsInvalid)
        {
            EventMethods.ThrowEventLogException(error);
        }

        _subscriptionHandle = EventMethods.EvtSubscribe(
            EventLogSession.GlobalSession.Handle,
            _newEvents.SafeWaitHandle,
            path,
            null,
            bookmarkHandle,
            IntPtr.Zero,
            IntPtr.Zero,
            flag);

        error = Marshal.GetLastWin32Error();

        if (_subscriptionHandle.IsInvalid)
        {
            EventMethods.ThrowEventLogException(error);
        }

        _isSubscribed = true;

        ProcessNewEvents(null, false);

        _waitHandle = ThreadPool.RegisterWaitForSingleObject(_newEvents, ProcessNewEvents, null, -1, false);
    }

    private void Unsubscribe()
    {
        _isSubscribed = false;

        if (_waitHandle is not null)
        {
            _waitHandle.Unregister(null);
            _waitHandle = null;
        }

        if (!_subscriptionHandle.IsInvalid)
        {
            _subscriptionHandle.Dispose();
        }
    }
}
