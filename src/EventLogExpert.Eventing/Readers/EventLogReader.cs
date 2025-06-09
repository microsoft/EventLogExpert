// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Models;
using System.Runtime.InteropServices;

namespace EventLogExpert.Eventing.Readers;

public sealed partial class EventLogReader(string path, PathType pathType, bool renderXml = false) : IDisposable
{
    private readonly Lock _eventLock = new();
    private readonly EvtHandle _handle =
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

    // Pre-Windows 11, a batch being returned can be maximum of (2 MB of data, batchSize count of events).
    // If the requested number of events in batchSize exceeded 2 MB, the call failed.
    // With a maximum event size of 64 KB, the maximum batchSize that won't exceed the maximum buffer
    // size is 30 (32 minus some overhead; refer to MS-EVEN6 for details).
    // Windows 11 and later will stop filling out the buffer when the maximum size is reached, regardless
    // of whether the requested batchSize was reached (but it will not exceed the requested count).
    public bool TryGetEvents(out EventRecord[] events, int batchSize = 30)
    {
        var buffer = new IntPtr[batchSize];
        int count = 0;

        bool success = EventMethods.EvtNext(_handle, batchSize, buffer, 0, 0, ref count);

        if (!success)
        {
            events = [];
            return false;
        }

        using (_eventLock.EnterScope())
        {
            LastBookmark = CreateBookmark(new EvtHandle(buffer[count - 1], false));
        }

        events = new EventRecord[count];

        for (int i = 0; i < count; i++)
        {
            using var eventHandle = new EvtHandle(buffer[i]);

            try
            {
                events[i] = EventMethods.RenderEvent(eventHandle);
                events[i].Properties = EventMethods.RenderEventProperties(eventHandle);

                if (renderXml)
                {
                    events[i].Xml = EventMethods.RenderEventXml(eventHandle);
                }
            }
            catch (Exception ex)
            {
                events[i] = new EventRecord { RecordId = null, Error = ex.Message };
            }
            finally
            {
                events[i].PathName = path;
                events[i].PathType = pathType;
            }
        }

        return true;
    }

    private static string? CreateBookmark(EvtHandle eventHandle)
    {
        using EvtHandle handle = EventMethods.EvtCreateBookmark(null);
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

        success = EventMethods.EvtRender(
            EvtHandle.Zero,
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

        Span<char> buffer = stackalloc char[bufferUsed / sizeof(char)];

        unsafe
        {
            fixed (char* bufferPtr = buffer)
            {
                success = EventMethods.EvtRender(
                    EvtHandle.Zero,
                    handle,
                    EvtRenderFlags.Bookmark,
                    bufferUsed,
                    (IntPtr)bufferPtr,
                    out bufferUsed,
                    out int _);
            }
        }

        error = Marshal.GetLastWin32Error();

        if (!success)
        {
            EventMethods.ThrowEventLogException(error);
        }

        return bufferUsed - 1 <= 0 ? null : new string(buffer[..^1]);
    }

    private void Dispose(bool disposing)
    {
        if (disposing) { return; }

        if (_handle is { IsInvalid: false })
        {
            _handle.Dispose();
        }
    }
}
