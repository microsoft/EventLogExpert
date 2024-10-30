// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Models;
using System.Runtime.InteropServices;

namespace EventLogExpert.Eventing.Reader;

public sealed class EventLogInformation
{
    internal EventLogInformation(EventLogSession session, string logName, PathType pathType)
    {
        using EventLogHandle handle = EventMethods.EvtOpenLog(session.Handle, logName, pathType);

        Attributes = (int?)(uint?)GetLogInfo(handle, EvtLogPropertyId.Attributes);
        CreationTime = (DateTime?)GetLogInfo(handle, EvtLogPropertyId.CreationTime);
        FileSize = (long?)(ulong?)GetLogInfo(handle, EvtLogPropertyId.FileSize);
        IsLogFull = (bool?)GetLogInfo(handle, EvtLogPropertyId.Full);
        LastAccessTime = (DateTime?)GetLogInfo(handle, EvtLogPropertyId.LastAccessTime);
        LastWriteTime = (DateTime?)GetLogInfo(handle, EvtLogPropertyId.LastWriteTime);
        OldestRecordNumber = (long?)(ulong?)GetLogInfo(handle, EvtLogPropertyId.OldestRecordNumber);
        RecordCount = (long?)(ulong?)GetLogInfo(handle, EvtLogPropertyId.NumberOfLogRecords);
    }

    public int? Attributes { get; }

    public DateTime? CreationTime { get; }

    public long? FileSize { get; }

    public bool? IsLogFull { get; }

    public DateTime? LastAccessTime { get; }

    public DateTime? LastWriteTime { get; }

    public long? OldestRecordNumber { get; }

    public long? RecordCount { get; }

    private static object? GetLogInfo(EventLogHandle handle, EvtLogPropertyId property)
    {
        IntPtr buffer = IntPtr.Zero;

        try
        {
            bool success = EventMethods.EvtGetLogInfo(handle, property, 0, IntPtr.Zero, out int bufferSize);
            int error = Marshal.GetLastWin32Error();

            if (!success && error != Interop.ERROR_INSUFFICIENT_BUFFER)
            {
                EventMethods.ThrowEventLogException(error);
            }

            buffer = Marshal.AllocHGlobal(bufferSize);

            success = EventMethods.EvtGetLogInfo(handle, property, bufferSize, buffer, out bufferSize);
            error = Marshal.GetLastWin32Error();

            if (!success)
            {
                EventMethods.ThrowEventLogException(error);
            }

            var variant = Marshal.PtrToStructure<EvtVariant>(buffer);

            return EventMethods.ConvertVariant(variant);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
