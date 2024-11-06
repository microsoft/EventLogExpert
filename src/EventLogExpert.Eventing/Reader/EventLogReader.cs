// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Models;
using System.Runtime.InteropServices;

namespace EventLogExpert.Eventing.Reader;

public sealed partial class EventLogReader(string path, PathType pathType) : IDisposable
{
    private readonly object _eventLock = new();
    private readonly EventLogHandle _handle =
        EventMethods.EvtQuery(EventLogSession.GlobalSession.Handle, path, null, pathType);

    ~EventLogReader()
    {
        Dispose(disposing: false);
    }

    public string? LastBookmark { get; private set; }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    public bool TryGetEvents(out EventRecord[] events, int batchSize = 64)
    {
        var buffer = new IntPtr[batchSize];
        int count = 0;

        lock (_eventLock)
        {
            bool success = EventMethods.EvtNext(_handle, batchSize, buffer, 0, 0, ref count);

            if (!success)
            {
                events = [];
                return false;
            }

            LastBookmark = CreateBookmark(new EventLogHandle(buffer[count - 1], false));
        }

        events = new EventRecord[count];

        for (int i = 0; i < count; i++)
        {
            using var eventHandle = new EventLogHandle(buffer[i]);

            try
            {
                events[i] = EventMethods.RenderEvent(eventHandle, EvtRenderFlags.EventValues);
                events[i].Properties = EventMethods.RenderEventProperties(eventHandle);
            }
            catch (Exception ex)
            {
                events[i] = new EventRecord { RecordId = null, Error = ex.Message };
            }
        }

        return true;
    }

    private static string? CreateBookmark(EventLogHandle eventHandle)
    {
        using EventLogHandle handle = EventMethods.EvtCreateBookmark(null);
        int error = Marshal.GetLastWin32Error();

        if (handle.IsInvalid)
        {
            EventMethods.ThrowEventLogException(error);
        }

        bool success = EventMethods.EvtUpdateBookmark(handle, eventHandle);
        error = Marshal.GetLastWin32Error();

        if (!success)
        {
            EventMethods.ThrowEventLogException(error);
        }

        IntPtr buffer = IntPtr.Zero;

        try
        {
            success = EventMethods.EvtRender(
                EventLogHandle.Zero,
                handle,
                EvtRenderFlags.Bookmark,
                0,
                IntPtr.Zero,
                out int bufferUsed,
                out int _);

            error = Marshal.GetLastWin32Error();

            if (!success && error != Interop.ERROR_INSUFFICIENT_BUFFER)
            {
                EventMethods.ThrowEventLogException(error);
            }

            buffer = Marshal.AllocHGlobal(bufferUsed);

            success = EventMethods.EvtRender(
                EventLogHandle.Zero,
                handle,
                EvtRenderFlags.Bookmark,
                bufferUsed,
                buffer,
                out bufferUsed,
                out int _);

            error = Marshal.GetLastWin32Error();

            if (!success)
            {
                EventMethods.ThrowEventLogException(error);
            }

            return Marshal.PtrToStringAuto(buffer);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private void Dispose(bool disposing)
    {
        if (_handle is { IsInvalid: false })
        {
            _handle.Dispose();
        }
    }
}
