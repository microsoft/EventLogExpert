// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Interop;
using System.Runtime.InteropServices;

namespace EventLogExpert.Eventing.Readers;

public sealed class EventLogInformation
{
    internal EventLogInformation(EventLogSession session, string logName, PathType pathType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logName);

        using EvtHandle handle = NativeMethods.EvtOpenLog(session.Handle, logName, pathType);

        int error = Marshal.GetLastWin32Error();

        // Surface the real EvtOpenLog failure — without this, GetLogInfo on a NULL handle masks it as UAE.
        if (handle.IsInvalid)
        {
            NativeMethods.ThrowEventLogException(error);
        }

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

    private static object? GetLogInfo(EvtHandle handle, EvtLogPropertyId property)
    {
        IntPtr buffer = IntPtr.Zero;

        try
        {
            bool success = NativeMethods.EvtGetLogInfo(handle, property, 0, IntPtr.Zero, out int bufferSize);
            int error = Marshal.GetLastWin32Error();

            if (!success && error != Win32ErrorCodes.ERROR_INSUFFICIENT_BUFFER)
            {
                NativeMethods.ThrowEventLogException(error);
            }

            buffer = Marshal.AllocHGlobal(bufferSize);

            success = NativeMethods.EvtGetLogInfo(handle, property, bufferSize, buffer, out bufferSize);
            error = Marshal.GetLastWin32Error();

            if (!success)
            {
                NativeMethods.ThrowEventLogException(error);
            }

            var variant = Marshal.PtrToStructure<EvtVariant>(buffer);

            return NativeMethods.ConvertVariant(variant);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
