// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Models;
using System.Runtime.InteropServices;

namespace EventLogExpert.Eventing.Readers;

/// <summary>
///     Represents a session for querying event logs. This class uses a singleton pattern via
///     <see cref="GlobalSession" />.
/// </summary>
/// <remarks>
///     This class does not implement <see cref="IDisposable" />. The underlying <see cref="EvtHandle" /> resources
///     are <see cref="SafeHandle" /> instances that clean themselves up via their own finalizers. The singleton is
///     intended to live for the application's lifetime.
/// </remarks>
public sealed partial class EventLogSession
{
    private EventLogSession() { }

    public static EventLogSession GlobalSession { get; } = new();

    internal EvtHandle Handle { get; } = EvtHandle.Zero;

    internal EvtHandle SystemRenderContext { get; } = CreateRenderContext(EvtRenderContextFlags.System);

    internal EvtHandle UserRenderContext { get; } = CreateRenderContext(EvtRenderContextFlags.User);

    public EventLogInformation GetLogInformation(string logName, PathType pathType) => new(this, logName, pathType);

    /// <summary>Gets an ordered list of all the log names on the system.</summary>
    public IEnumerable<string> GetLogNames()
    {
        List<string> paths = [];

        using EvtHandle channelHandle = EventMethods.EvtOpenChannelEnum(Handle, 0);
        int error = Marshal.GetLastWin32Error();

        if (channelHandle.IsInvalid)
        {
            EventMethods.ThrowEventLogException(error);
        }

        bool doneReading = false;

        do
        {
            string path = NextChannelPath(channelHandle, ref doneReading);

            if (!doneReading)
            {
                paths.Add(path);
            }
        }
        while (!doneReading);

        return paths.Order();
    }

    public HashSet<string> GetProviderNames()
    {
        HashSet<string> providers = [];

        using EvtHandle providerHandle = EventMethods.EvtOpenPublisherEnum(Handle, 0);
        int error = Marshal.GetLastWin32Error();

        if (providerHandle.IsInvalid)
        {
            EventMethods.ThrowEventLogException(error);
        }

        bool doneReading = false;

        do
        {
            string path = NextPublisherId(providerHandle, ref doneReading);

            if (!doneReading)
            {
                providers.Add(path);
            }
        }
        while (!doneReading);

        return providers;
    }

    private static EvtHandle CreateRenderContext(EvtRenderContextFlags renderContextFlags)
    {
        EvtHandle renderContextHandle = EventMethods.EvtCreateRenderContext(0, null, renderContextFlags);
        int error = Marshal.GetLastWin32Error();

        if (renderContextHandle.IsInvalid)
        {
            renderContextHandle.Dispose();

            EventMethods.ThrowEventLogException(error);
        }

        return renderContextHandle;
    }

    private static string NextChannelPath(EvtHandle channelHandle, ref bool doneReading)
    {
        bool success = EventMethods.EvtNextChannelPath(channelHandle, 0, null, out int bufferSize);
        int error = Marshal.GetLastWin32Error();

        if (!success)
        {
            if (error == Interop.ERROR_NO_MORE_ITEMS)
            {
                doneReading = true;

                return string.Empty;
            }

            if (error != Interop.ERROR_INSUFFICIENT_BUFFER)
            {
                EventMethods.ThrowEventLogException(error);
            }
        }

        Span<char> buffer = stackalloc char[bufferSize];

        success = EventMethods.EvtNextChannelPath(channelHandle, bufferSize, buffer, out bufferSize);
        error = Marshal.GetLastWin32Error();

        if (!success)
        {
            EventMethods.ThrowEventLogException(error);
        }

        return bufferSize - 1 <= 0 ? string.Empty : new string(buffer[..(bufferSize - 1)]);
    }

    private static string NextPublisherId(EvtHandle publisherHandle, ref bool doneReading)
    {
        bool success = EventMethods.EvtNextPublisherId(publisherHandle, 0, null, out int bufferSize);
        int error = Marshal.GetLastWin32Error();

        if (!success)
        {
            if (error == Interop.ERROR_NO_MORE_ITEMS)
            {
                doneReading = true;

                return string.Empty;
            }

            if (error != Interop.ERROR_INSUFFICIENT_BUFFER)
            {
                EventMethods.ThrowEventLogException(error);
            }
        }

        Span<char> buffer = stackalloc char[bufferSize];

        success = EventMethods.EvtNextPublisherId(publisherHandle, bufferSize, buffer, out bufferSize);
        error = Marshal.GetLastWin32Error();

        if (!success)
        {
            EventMethods.ThrowEventLogException(error);
        }

        return bufferSize - 1 <= 0 ? string.Empty : new string(buffer[..(bufferSize - 1)]);
    }
}
