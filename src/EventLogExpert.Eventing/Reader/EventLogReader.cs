// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Models;
using System.Runtime.InteropServices;

namespace EventLogExpert.Eventing.Reader;

public sealed partial class EventLogReader(string path, PathType pathType) : IDisposable
{
    private readonly EventLogHandle _handle =
        EventMethods.EvtQuery(EventLogSession.GlobalSession.Handle, path, null, (int)pathType);
    private readonly SemaphoreSlim _semaphore = new(1);

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    public bool TryGetEvents(out EventRecord[] events, int batchSize = 200)
    {
        var buffer = new IntPtr[batchSize];
        int count = 0;

        _semaphore.Wait();

        try
        {
            bool success = EventMethods.EvtNext(_handle, batchSize, buffer, 0, 0, ref count);

            if (!success)
            {
                events = [];
                return false;
            }
        }
        finally
        {
            _semaphore.Release();
        }

        events = new EventRecord[count];

        for (int i = 0; i < count; i++)
        {
            using var eventHandle = new EventLogHandle(buffer[i]);

            try
            {
                events[i] = RenderEvent(eventHandle, EvtRenderFlags.EventValues);
                events[i].Properties = RenderEventProperties(eventHandle);
            }
            catch
            {
                events[i] = new EventRecord { RecordId = null };
            }
        }

        return true;
    }

    private static EventRecord RenderEvent(EventLogHandle eventHandle, EvtRenderFlags flag)
    {
        IntPtr buffer = IntPtr.Zero;

        try
        {
            bool success = EventMethods.EvtRender(
                EventLogSession.GlobalSession.SystemRenderContext,
                eventHandle,
                flag,
                0,
                IntPtr.Zero,
                out int bufferUsed,
                out int propertyCount);

            int error = Marshal.GetLastWin32Error();

            if (!success && error != Interop.ERROR_INSUFFICIENT_BUFFER)
            {
                EventMethods.ThrowEventLogException(error);
            }

            buffer = Marshal.AllocHGlobal(bufferUsed);

            success = EventMethods.EvtRender(
                EventLogSession.GlobalSession.SystemRenderContext,
                eventHandle,
                flag,
                bufferUsed,
                buffer,
                out bufferUsed,
                out propertyCount);

            error = Marshal.GetLastWin32Error();

            if (!success)
            {
                EventMethods.ThrowEventLogException(error);
            }

            return EventMethods.GetEventRecord(buffer, propertyCount);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static IList<object> RenderEventProperties(EventLogHandle eventHandle)
    {
        IntPtr buffer = IntPtr.Zero;

        try
        {
            bool success = EventMethods.EvtRender(
                EventLogSession.GlobalSession.UserRenderContext,
                eventHandle,
                EvtRenderFlags.EventValues,
                0,
                IntPtr.Zero,
                out int bufferUsed,
                out int propertyCount);

            int error = Marshal.GetLastWin32Error();

            if (!success && error != Interop.ERROR_INSUFFICIENT_BUFFER)
            {
                EventMethods.ThrowEventLogException(error);
            }

            buffer = Marshal.AllocHGlobal(bufferUsed);

            success = EventMethods.EvtRender(
                EventLogSession.GlobalSession.UserRenderContext,
                eventHandle,
                EvtRenderFlags.EventValues,
                bufferUsed,
                buffer,
                out bufferUsed,
                out propertyCount);

            error = Marshal.GetLastWin32Error();

            if (!success)
            {
                EventMethods.ThrowEventLogException(error);
            }

            List<object> properties = [];

            if (propertyCount <= 0) { return properties; }

            for (int i = 0; i < propertyCount; i++)
            {
                var property = Marshal.PtrToStructure<EvtVariant>(buffer + (i * Marshal.SizeOf<EvtVariant>()));

                properties.Add(EventMethods.ConvertVariant(property) ?? throw new InvalidDataException());
            }

            return properties;
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
