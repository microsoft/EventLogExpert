// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Models;
using System.Runtime.InteropServices;

namespace EventLogExpert.Eventing.Readers;

public sealed partial class EventLogWatcher : IDisposable
{
    private readonly string? _bookmark;
    private readonly AutoResetEvent _newEvents = new(false);
    private readonly string _path;
    private readonly EvtHandle _queryHandle;
    private readonly bool _renderXml;

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
        _queryHandle = EventMethods.EvtQuery(EventLogSession.GlobalSession.Handle, path, null, PathType.LogName);
        int error = Marshal.GetLastWin32Error();

        if (_queryHandle is { IsInvalid: false }) { return; }

        _queryHandle.Dispose();
        EventMethods.ThrowEventLogException(error);
    }

    public EventLogWatcher(string path, bool renderXml = false) : this(path, null, renderXml) { }

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

        if (_queryHandle is { IsInvalid: false })
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
                    @event = EventMethods.RenderEvent(eventHandle);
                    @event.Properties = EventMethods.RenderEventProperties(eventHandle);

                    if (_renderXml)
                    {
                        @event.Xml = EventMethods.RenderEventXml(eventHandle);
                    }
                }
                catch (Exception ex)
                {
                    @event = new EventRecord { RecordId = null, Error = ex.Message };
                }

                @event.PathName = _path;

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

        EvtHandle bookmarkHandle = string.IsNullOrEmpty(_bookmark) ?
            EvtHandle.Zero :
            EventMethods.EvtCreateBookmark(_bookmark);

        if (!string.IsNullOrEmpty(_bookmark) && bookmarkHandle.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            bookmarkHandle.Dispose();
            EventMethods.ThrowEventLogException(error);
        }

        _subscriptionHandle = EventMethods.EvtSubscribe(
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
            EventMethods.ThrowEventLogException(subscriptionError);
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
