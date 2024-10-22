// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Models;
using System.Runtime.InteropServices;

namespace EventLogExpert.Eventing.Reader;

public sealed partial class EventLogSession : IDisposable
{
    public static EventLogSession GlobalSession { get; } = new();

    internal EventLogHandle Handle { get; } = EventLogHandle.Zero;

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    public EventLogInformation GetLogInformation(string logName, PathType pathType) => new(this, logName, pathType);

    public IEnumerable<string> GetLogNames()
    {
        List<string> paths = [];

        EventLogHandle channelHandle = EventMethods.EvtOpenChannelEnum(Handle, 0);
        int error = Marshal.GetLastWin32Error();

        if (channelHandle.IsInvalid)
        {
            channelHandle.Dispose();
            EventMethods.ThrowEventLogException(error);
        }

        bool doneReading = false;

        try
        {
            do
            {
                string path = NextChannelPath(channelHandle, ref doneReading);

                if (!doneReading)
                {
                    paths.Add(path);
                }
            }
            while (!doneReading);
        }
        finally
        {
            channelHandle.Dispose();
        }

        return paths.Order();
    }

    private static string NextChannelPath(EventLogHandle handle, ref bool doneReading)
    {
        bool success = EventMethods.EvtNextChannelPath(handle, 0, null, out int bufferSize);
        int error = Marshal.GetLastWin32Error();

        if (!success)
        {
            if (error == 259 /* ERROR_NO_MORE_ITEMS */)
            {
                doneReading = true;

                return string.Empty;
            }

            if (error != 122 /* ERROR_INSUFFICIENT_BUFFER */)
            {
                EventMethods.ThrowEventLogException(error);
            }
        }

        char[] buffer = new char[bufferSize];

        success = EventMethods.EvtNextChannelPath(handle, bufferSize, buffer, out bufferSize);
        error = Marshal.GetLastWin32Error();

        if (!success)
        {
            EventMethods.ThrowEventLogException(error);
        }

        return bufferSize - 1 <= 0 ? string.Empty : new string(buffer, 0, bufferSize - 1);
    }

    private void Dispose(bool disposing)
    {
        if (Handle is { IsInvalid: false })
        {
            Handle.Dispose();
        }
    }
}
