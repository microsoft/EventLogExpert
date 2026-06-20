// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Readers;
using Microsoft.Win32.SafeHandles;
using System.Buffers;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace EventLogExpert.Eventing.Interop;

internal static partial class NativeMethods
{
    private const string EventLogApi = "wevtapi.dll";
    private const int MaxStackAllocChars = 4096;

    internal static object? ConvertVariant(EvtVariant variant)
    {
        switch (variant.Type)
        {
            case (int)EvtVariantType.Null:
                return null;
            case (int)EvtVariantType.String:
                return Marshal.PtrToStringUni(variant.StringVal);
            case (int)EvtVariantType.AnsiString:
                return Marshal.PtrToStringAnsi(variant.AnsiStringVal);
            case (int)EvtVariantType.SByte:
                return variant.SByteVal;
            case (int)EvtVariantType.Byte:
                return variant.ByteVal;
            case (int)EvtVariantType.Int16:
                return variant.Int16Val;
            case (int)EvtVariantType.UInt16:
                return variant.UInt16Val;
            case (int)EvtVariantType.Int32:
                return variant.Int32Val;
            case (int)EvtVariantType.UInt32:
                return variant.UInt32Val;
            case (int)EvtVariantType.Int64:
                return variant.Int64Val;
            case (int)EvtVariantType.UInt64:
                return variant.UInt64Val;
            case (int)EvtVariantType.Single:
                return variant.SingleVal;
            case (int)EvtVariantType.Double:
                return variant.DoubleVal;
            case (int)EvtVariantType.Boolean:
                return variant.BooleanVal != 0;
            case (int)EvtVariantType.Binary:
                if (variant.Count == 0)
                {
                    return Array.Empty<byte>();
                }

                int byteCount = CheckedCount(variant.Count, EvtVariantType.Binary);

                if (variant.BinaryVal == IntPtr.Zero)
                {
                    throw new InvalidDataException(
                        $"Null reference with non-zero count {variant.Count} for {nameof(EvtVariantType)}.{EvtVariantType.Binary}");
                }

                byte[] bytes = new byte[byteCount];
                Marshal.Copy(variant.BinaryVal, bytes, 0, byteCount);
                return bytes;
            case (int)EvtVariantType.Guid:
                return Marshal.PtrToStructure<Guid>(variant.GuidVal);
            case (int)EvtVariantType.SizeT:
                return variant.SizeTVal;
            case (int)EvtVariantType.FileTime:
                return DateTime.FromFileTimeUtc((long)variant.FileTimeVal);
            case (int)EvtVariantType.SysTime:
                var sysTime = Marshal.PtrToStructure<SystemTime>(variant.SysTimeVal);

                return new DateTime(
                    sysTime.Year,
                    sysTime.Month,
                    sysTime.Day,
                    sysTime.Hour,
                    sysTime.Minute,
                    sysTime.Second,
                    sysTime.Milliseconds,
                    DateTimeKind.Utc);
            case (int)EvtVariantType.Sid:
                return variant.SidVal == IntPtr.Zero ? null : new SecurityIdentifier(variant.SidVal);
            case (int)EvtVariantType.HexInt32:
                return variant.Int32Val;
            case (int)EvtVariantType.HexInt64:
                return variant.UInt64Val;
            case (int)EvtVariantType.Handle:
                return new EvtHandle(variant.EvtHandleVal);
            case (int)EvtVariantType.Xml:
                return Marshal.PtrToStringUni(variant.XmlVal);
            case (int)EvtVariantType.StringArray:
                if (variant.Count == 0)
                {
                    return Array.Empty<string>();
                }

                int stringCount = CheckedCount(variant.Count, EvtVariantType.StringArray);

                if (variant.StringArr == IntPtr.Zero)
                {
                    throw new InvalidDataException(
                        $"Null reference with non-zero count {variant.Count} for {nameof(EvtVariantType)}.{EvtVariantType.StringArray}");
                }

                var stringArray = new string[stringCount];

                for (int i = 0; i < stringCount; i++)
                {
                    IntPtr stringRef = Marshal.ReadIntPtr(variant.StringArr, i * IntPtr.Size);

                    stringArray[i] = Marshal.PtrToStringAuto(stringRef) ?? string.Empty;
                }

                return stringArray;
            case (int)EvtVariantType.ByteArray:
                return ReadBlittableArray<byte>(variant.ByteArr, variant.Count, EvtVariantType.ByteArray);
            case (int)EvtVariantType.UInt16Array:
                return ReadBlittableArray<ushort>(variant.UInt16Arr, variant.Count, EvtVariantType.UInt16Array);
            case (int)EvtVariantType.UInt32Array:
                return ReadBlittableArray<uint>(variant.UInt32Arr, variant.Count, EvtVariantType.UInt32Array);
            case (int)EvtVariantType.HexInt32Array:
                return ReadBlittableArray<int>(variant.Int32Arr, variant.Count, EvtVariantType.HexInt32Array);
            default:
                throw new InvalidDataException($"Invalid {nameof(EvtVariantType)}: {variant.Type}");
        }
    }

    /// <summary>Closes an open handle</summary>
    [LibraryImport(EventLogApi, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EvtClose(IntPtr handle);

    /// <summary>Creates a bookmark that identifies an event in a channel</summary>
    [LibraryImport(EventLogApi, SetLastError = true)]
    internal static partial EvtHandle EvtCreateBookmark([MarshalAs(UnmanagedType.LPWStr)] string? bookmarkXml);

    /// <summary>Creates a context that specifies the information in the event that you want to render</summary>
    [LibraryImport(EventLogApi, SetLastError = true)]
    internal static partial EvtHandle EvtCreateRenderContext(
        int valuePathsCount,
        [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr)][Out] string[]? valuePaths,
        EvtRenderContextFlags flags);

    /// <summary>Formats a message string</summary>
    [LibraryImport(EventLogApi, SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EvtFormatMessage(
        EvtHandle publisherMetadata,
        EvtHandle @event,
        uint messageId,
        int valueCount,
        IntPtr values,
        EvtFormatMessageFlags flags,
        int bufferSize,
        Span<char> buffer,
        out int bufferUsed);

    /// <summary>Gets the specified channel configuration property</summary>
    [LibraryImport(EventLogApi, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EvtGetChannelConfigProperty(
        EvtHandle channelConfig,
        EvtChannelConfigPropertyId propertyId,
        int flags,
        int propertyValueBufferSize,
        IntPtr propertyValueBuffer,
        out int propertyValueBufferUsed);

    /// <summary>Gets the specified event metadata property</summary>
    [LibraryImport(EventLogApi, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EvtGetEventMetadataProperty(
        EvtHandle eventMetadata,
        EvtEventMetadataPropertyId propertyId,
        int flags,
        int eventMetadataPropertyBufferSize,
        IntPtr eventMetadataPropertyBuffer,
        out int eventMetadataPropertyBufferUsed);

    /// <summary>Gets information about a channel or log file</summary>
    [LibraryImport(EventLogApi, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EvtGetLogInfo(
        EvtHandle log,
        EvtLogPropertyId propertyId,
        int propertyValueBufferSize,
        IntPtr propertyValueBuffer,
        out int propertyValueBufferUsed);

    /// <summary>Gets a provider metadata property from the specified object in the array</summary>
    [LibraryImport(EventLogApi, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EvtGetObjectArrayProperty(
        EvtHandle objectArray,
        EvtPublisherMetadataPropertyId propertyId,
        int arrayIndex,
        int flags,
        int propertyValueBufferSize,
        IntPtr propertyValueBuffer,
        out int propertyValueBufferUsed);

    /// <summary>Gets the number of elements in the array of objects</summary>
    [LibraryImport(EventLogApi, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EvtGetObjectArraySize(EvtHandle objectArray, out int objectArraySize);

    /// <summary>Gets the specified provider metadata property</summary>
    [LibraryImport(EventLogApi, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EvtGetPublisherMetadataProperty(
        EvtHandle publisherMetadata,
        EvtPublisherMetadataPropertyId propertyId,
        int flags,
        int publisherMetadataPropertyBufferSize,
        IntPtr publisherMetadataPropertyBuffer,
        out int publisherMetadataPropertyBufferUsed);

    /// <summary>Gets the next event from the query or subscription results</summary>
    [LibraryImport(EventLogApi, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EvtNext(
        EvtHandle resultSet,
        int eventsSize,
        [MarshalAs(UnmanagedType.LPArray)][Out] IntPtr[] events,
        int timeout,
        int flags,
        ref int returned);

    /// <summary>Gets the channel name from the enumerator</summary>
    [LibraryImport(EventLogApi, SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EvtNextChannelPath(
        EvtHandle channelEnum,
        int channelPathBufferSize,
        Span<char> channelPathBuffer,
        out int channelPathBufferUsed);

    /// <summary>Gets an event definition from the enumerator</summary>
    [LibraryImport(EventLogApi, SetLastError = true)]
    internal static partial EvtHandle EvtNextEventMetadata(EvtHandle eventMetadataEnum, int flags);

    /// <summary>Gets the identifier of a provider from the enumerator</summary>
    [LibraryImport(EventLogApi, SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EvtNextPublisherId(
        EvtHandle publisherEnum,
        int publisherIdBufferSize,
        Span<char> publisherIdBuffer,
        out int publisherIdBufferUsed);

    /// <summary>Gets a handle that you use to read or write configuration information for the specified channel</summary>
    [LibraryImport(EventLogApi, SetLastError = true)]
    internal static partial EvtHandle EvtOpenChannelConfig(
        EvtHandle session,
        [MarshalAs(UnmanagedType.LPWStr)] string channelPath,
        int flags);

    /// <summary>Gets a handle that you use to enumerate the list of channels that are registered on the computer</summary>
    [LibraryImport(EventLogApi, SetLastError = true)]
    internal static partial EvtHandle EvtOpenChannelEnum(EvtHandle session, int flags);

    /// <summary>Gets a handle that you use to enumerate the list of events that the provider defines</summary>
    [LibraryImport(EventLogApi, SetLastError = true)]
    internal static partial EvtHandle EvtOpenEventMetadataEnum(EvtHandle publisherMetadata, int flags);

    /// <summary>Gets a handle to a channel or log file that you can then use to get information about the channel or log file</summary>
    [LibraryImport(EventLogApi, SetLastError = true)]
    internal static partial EvtHandle EvtOpenLog(
        EvtHandle session,
        [MarshalAs(UnmanagedType.LPWStr)] string path,
        LogPathType flags);

    /// <summary>Gets a handle that you can use to enumerate the list of registered providers on the computer</summary>
    [LibraryImport(EventLogApi, SetLastError = true)]
    internal static partial EvtHandle EvtOpenPublisherEnum(EvtHandle session, int flags);

    /// <summary>Gets a handle that you use to read the specified provider's metadata</summary>
    [LibraryImport(EventLogApi, SetLastError = true)]
    internal static partial EvtHandle EvtOpenPublisherMetadata(
        EvtHandle session,
        [MarshalAs(UnmanagedType.LPWStr)] string publisherId,
        [MarshalAs(UnmanagedType.LPWStr)] string? logFilePath,
        int locale,
        int flags);

    /// <summary>Runs a query to retrieve events from a channel or log file that match the specified query criteria</summary>
    [LibraryImport(EventLogApi, SetLastError = true)]
    internal static partial EvtHandle EvtQuery(
        EvtHandle session,
        [MarshalAs(UnmanagedType.LPWStr)] string path,
        [MarshalAs(UnmanagedType.LPWStr)] string? query,
        LogPathType flags);

    /// <summary>
    ///     <c>EvtQuery</c> overload that takes the raw combined query-flags <see langword="int" /> (the path-type bits
    ///     ORed with direction flags such as <c>EvtQueryReverseDirection</c> 0x200), so a caller can opt into newest-first
    ///     reads. The typed <see cref="EvtQuery" /> binding remains the path for the default oldest-first reads and its other
    ///     callers (the watcher and the XML resolver).
    /// </summary>
    [LibraryImport(EventLogApi, EntryPoint = "EvtQuery", SetLastError = true)]
    internal static partial EvtHandle EvtQueryWithFlags(
        EvtHandle session,
        [MarshalAs(UnmanagedType.LPWStr)] string path,
        [MarshalAs(UnmanagedType.LPWStr)] string? query,
        int flags);

    /// <summary>Renders an XML fragment base on the render context that you specify</summary>
    [LibraryImport(EventLogApi, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EvtRender(
        EvtHandle context,
        EvtHandle fragment,
        EvtRenderFlags flags,
        int bufferSize,
        IntPtr buffer,
        out int bufferUsed,
        out int propertyCount);

    /// <summary>
    ///     Creates a subscription that will receive current and future events from a channel or log file that match the
    ///     specified query criteria
    /// </summary>
    [LibraryImport(EventLogApi, SetLastError = true)]
    internal static partial EvtHandle EvtSubscribe(
        EvtHandle session,
        SafeWaitHandle signalEvent,
        [MarshalAs(UnmanagedType.LPWStr)] string channelPath,
        [MarshalAs(UnmanagedType.LPWStr)] string? query,
        EvtHandle bookmark,
        IntPtr context,
        IntPtr callback,
        EvtSubscribeFlags flags);

    /// <summary>Updates the bookmark with information that identifies the specified event</summary>
    [LibraryImport(EventLogApi, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EvtUpdateBookmark(EvtHandle bookmark, EvtHandle @event);

    /// <summary>Formats a message string</summary>
    /// <param name="publisherMetadataHandle">Handle returned from <see cref="EvtOpenPublisherMetadata" /></param>
    /// <param name="messageId">The resource identifier of the message string that you want formated</param>
    internal static string FormatMessage(EvtHandle publisherMetadataHandle, uint messageId)
    {
        Span<char> emptyBuffer = ['\0'];

        bool success = EvtFormatMessage(
            publisherMetadataHandle,
            EvtHandle.Zero,
            messageId,
            0,
            IntPtr.Zero,
            EvtFormatMessageFlags.Id,
            0,
            emptyBuffer,
            out int bufferUsed);

        int error = Marshal.GetLastWin32Error();

        if (!success &&
            error != Win32ErrorCodes.ERROR_EVT_UNRESOLVED_VALUE_INSERT &&
            error != Win32ErrorCodes.ERROR_EVT_UNRESOLVED_PARAMETER_INSERT &&
            error != Win32ErrorCodes.ERROR_EVT_MAX_INSERTS_REACHED)
        {
            if (error != Win32ErrorCodes.ERROR_INSUFFICIENT_BUFFER)
            {
                ThrowEventLogException(error);
            }
        }

        int charCount = bufferUsed;
        char[]? rented = null;
        Span<char> buffer = charCount <= MaxStackAllocChars
            ? stackalloc char[charCount]
            : (rented = ArrayPool<char>.Shared.Rent(charCount)).AsSpan(0, charCount);

        try
        {
            success = EvtFormatMessage(
                publisherMetadataHandle,
                EvtHandle.Zero,
                messageId,
                0,
                IntPtr.Zero,
                EvtFormatMessageFlags.Id,
                bufferUsed,
                buffer,
                out bufferUsed);

            error = Marshal.GetLastWin32Error();

            if (!success &&
                error != Win32ErrorCodes.ERROR_EVT_UNRESOLVED_VALUE_INSERT &&
                error != Win32ErrorCodes.ERROR_EVT_UNRESOLVED_PARAMETER_INSERT &&
                error != Win32ErrorCodes.ERROR_EVT_MAX_INSERTS_REACHED)
            {
                ThrowEventLogException(error);
            }

            return bufferUsed - 1 <= 0 ? string.Empty : new string(buffer[..(bufferUsed - 1)]);
        }
        finally
        {
            if (rented is not null) { ArrayPool<char>.Shared.Return(rented, clearArray: true); }
        }
    }

    /// <summary>Converts a buffer that was returned from <see cref="EvtRender" /> to an <see cref="EventRecord" /></summary>
    /// <param name="eventBuffer">
    ///     Pointer to a buffer returned from <see cref="EvtRender" />. Must stay pinned and valid for
    ///     the duration of the call; the variants are read directly through this pointer.
    /// </param>
    /// <param name="propertyCount">Number of properties returned from <see cref="EvtRender" /></param>
    /// <returns></returns>
    internal static unsafe EventRecord GetEventRecord(IntPtr eventBuffer, int propertyCount)
    {
        EventRecord properties = new();

        var variants = (EvtVariant*)eventBuffer;

        for (int i = 0; i < propertyCount; i++)
        {
            ref readonly var variant = ref variants[i];

            // Properties are returned in the order defined in EVT_SYSTEM_PROPERTY_ID enum. Value-type
            // properties are read straight from the typed union field (no boxing); the string, SID and
            // time properties stay on ConvertVariant (no boxing benefit, and it handles the
            // FileTime/SysTime split for TimeCreated). Unmapped indices are intentionally skipped.
            switch (i)
            {
                case (int)EvtSystemPropertyId.ProviderName:
                    properties.ProviderName = (string)ConvertVariant(variant)!;
                    break;
                case (int)EvtSystemPropertyId.EventId:
                    Debug.Assert(variant.Type == (uint)EvtVariantType.UInt16);
                    properties.Id = variant.UInt16Val;
                    break;
                case (int)EvtSystemPropertyId.Qualifiers:
                    properties.Qualifiers = ReadOptionalUInt16(variant);
                    break;
                case (int)EvtSystemPropertyId.Level:
                    properties.Level = ReadOptionalByte(variant);
                    break;
                case (int)EvtSystemPropertyId.Task:
                    properties.Task = ReadOptionalUInt16(variant);
                    break;
                case (int)EvtSystemPropertyId.Keywords:
                    properties.Keywords = ReadOptionalInt64(variant);
                    break;
                case (int)EvtSystemPropertyId.TimeCreated:
                    properties.TimeCreated = (DateTime)ConvertVariant(variant)!;
                    break;
                case (int)EvtSystemPropertyId.EventRecordId:
                    properties.RecordId = ReadOptionalInt64(variant);
                    break;
                case (int)EvtSystemPropertyId.ActivityId:
                    properties.ActivityId = ReadGuidOrNull(variant);
                    break;
                case (int)EvtSystemPropertyId.ProcessID:
                    properties.ProcessId = ReadOptionalInt32(variant);
                    break;
                case (int)EvtSystemPropertyId.ThreadID:
                    properties.ThreadId = ReadOptionalInt32(variant);
                    break;
                case (int)EvtSystemPropertyId.Channel:
                    properties.LogName = (string)ConvertVariant(variant)!;
                    break;
                case (int)EvtSystemPropertyId.Computer:
                    properties.ComputerName = (string)ConvertVariant(variant)!;
                    break;
                case (int)EvtSystemPropertyId.UserID:
                    properties.UserId = (SecurityIdentifier?)ConvertVariant(variant);
                    break;
                case (int)EvtSystemPropertyId.Version:
                    properties.Version = ReadOptionalByte(variant);
                    break;
            }
        }

        return properties;
    }

    internal static object GetObjectArrayProperty(
        EvtHandle array,
        int index,
        EvtPublisherMetadataPropertyId propertyId)
    {
        bool success = EvtGetObjectArrayProperty(array, propertyId, index, 0, 0, IntPtr.Zero, out int bufferUsed);
        int error = Marshal.GetLastWin32Error();

        if (!success && error != Win32ErrorCodes.ERROR_INSUFFICIENT_BUFFER)
        {
            ThrowEventLogException(error);
        }

        int charCount = bufferUsed;
        char[]? rented = null;
        Span<char> buffer = charCount <= MaxStackAllocChars
            ? stackalloc char[charCount]
            : (rented = ArrayPool<char>.Shared.Rent(charCount)).AsSpan(0, charCount);

        try
        {
            unsafe
            {
                fixed (char* bufferPtr = buffer)
                {
                    success = EvtGetObjectArrayProperty(array,
                        propertyId,
                        index,
                        0,
                        bufferUsed,
                        (IntPtr)bufferPtr,
                        out bufferUsed);
                }
            }

            error = Marshal.GetLastWin32Error();

            if (!success)
            {
                ThrowEventLogException(error);
            }

            unsafe
            {
                fixed (char* bufferPtr = buffer)
                {
                    var variant = *(EvtVariant*)bufferPtr;

                    return ConvertVariant(variant) ??
                        throw new InvalidDataException($"Invalid Object Array for Property: {propertyId}");
                }
            }
        }
        finally
        {
            if (rented is not null) { ArrayPool<char>.Shared.Return(rented, clearArray: true); }
        }
    }

    internal static int GetObjectArraySize(EvtHandle array)
    {
        bool success = EvtGetObjectArraySize(array, out int size);
        int error = Marshal.GetLastWin32Error();

        if (!success)
        {
            ThrowEventLogException(error);
        }

        return size;
    }

    internal static EventRecord RenderEvent(EvtHandle eventHandle)
    {
        bool success = EvtRender(
            EventLogSession.GlobalSession.SystemRenderContext,
            eventHandle,
            EvtRenderFlags.EventValues,
            0,
            IntPtr.Zero,
            out int bufferUsed,
            out int propertyCount);

        int error = Marshal.GetLastWin32Error();

        if (!success && error != Win32ErrorCodes.ERROR_INSUFFICIENT_BUFFER)
        {
            ThrowEventLogException(error);
        }

        int charCount = bufferUsed / sizeof(char);
        char[]? rented = null;
        Span<char> buffer = charCount <= MaxStackAllocChars
            ? stackalloc char[charCount]
            : (rented = ArrayPool<char>.Shared.Rent(charCount)).AsSpan(0, charCount);

        try
        {
            unsafe
            {
                fixed (char* bufferPtr = buffer)
                {
                    success = EvtRender(
                        EventLogSession.GlobalSession.SystemRenderContext,
                        eventHandle,
                        EvtRenderFlags.EventValues,
                        bufferUsed,
                        (IntPtr)bufferPtr,
                        out bufferUsed,
                        out propertyCount);
                }
            }

            error = Marshal.GetLastWin32Error();

            if (!success)
            {
                ThrowEventLogException(error);
            }

            unsafe
            {
                fixed (char* bufferPtr = buffer)
                {
                    return GetEventRecord((IntPtr)bufferPtr, propertyCount);
                }
            }
        }
        finally
        {
            if (rented is not null) { ArrayPool<char>.Shared.Return(rented, clearArray: true); }
        }
    }

    internal static IReadOnlyList<object> RenderEventProperties(EvtHandle eventHandle)
    {
        bool success = EvtRender(
            EventLogSession.GlobalSession.UserRenderContext,
            eventHandle,
            EvtRenderFlags.EventValues,
            0,
            IntPtr.Zero,
            out int bufferUsed,
            out int propertyCount);

        int error = Marshal.GetLastWin32Error();

        if (!success && error != Win32ErrorCodes.ERROR_INSUFFICIENT_BUFFER)
        {
            ThrowEventLogException(error);
        }

        int charCount = bufferUsed / sizeof(char);
        char[]? rented = null;
        Span<char> buffer = charCount <= MaxStackAllocChars
            ? stackalloc char[charCount]
            : (rented = ArrayPool<char>.Shared.Rent(charCount)).AsSpan(0, charCount);

        try
        {
            unsafe
            {
                fixed (char* bufferPtr = buffer)
                {
                    success = EvtRender(
                        EventLogSession.GlobalSession.UserRenderContext,
                        eventHandle,
                        EvtRenderFlags.EventValues,
                        bufferUsed,
                        (IntPtr)bufferPtr,
                        out bufferUsed,
                        out propertyCount);
                }
            }

            error = Marshal.GetLastWin32Error();

            if (!success)
            {
                ThrowEventLogException(error);
            }

            if (propertyCount <= 0) { return []; }

            var properties = new object[propertyCount];

            unsafe
            {
                fixed (char* bufferPtr = buffer)
                {
                    var variants = (EvtVariant*)bufferPtr;

                    for (int i = 0; i < propertyCount; i++)
                    {
                        properties[i] = ConvertVariant(variants[i]) ?? throw new InvalidDataException();
                    }
                }
            }

            return properties;
        }
        finally
        {
            if (rented is not null) { ArrayPool<char>.Shared.Return(rented, clearArray: true); }
        }
    }

    internal static string? RenderEventXml(EvtHandle eventHandle)
    {
        bool success = EvtRender(
            EvtHandle.Zero,
            eventHandle,
            EvtRenderFlags.EventXml,
            0,
            IntPtr.Zero,
            out int bufferUsed,
            out int _);

        int error = Marshal.GetLastWin32Error();

        if (!success && error != Win32ErrorCodes.ERROR_INSUFFICIENT_BUFFER)
        {
            ThrowEventLogException(error);
        }

        int charCount = bufferUsed / sizeof(char);
        char[]? rented = null;
        Span<char> buffer = charCount <= MaxStackAllocChars
            ? stackalloc char[charCount]
            : (rented = ArrayPool<char>.Shared.Rent(charCount)).AsSpan(0, charCount);

        try
        {
            unsafe
            {
                fixed (char* bufferPtr = buffer)
                {
                    success = EvtRender(
                        EvtHandle.Zero,
                        eventHandle,
                        EvtRenderFlags.EventXml,
                        bufferUsed,
                        (IntPtr)bufferPtr,
                        out bufferUsed,
                        out int _);
                }
            }

            error = Marshal.GetLastWin32Error();

            if (!success)
            {
                ThrowEventLogException(error);
            }

            return bufferUsed - 1 <= 0 ? null : new string(buffer[..((bufferUsed - 1) / sizeof(char))]);
        }
        finally
        {
            if (rented is not null) { ArrayPool<char>.Shared.Return(rented, clearArray: true); }
        }
    }

    internal static void ThrowEventLogException(int error)
    {
        var message = NativeErrorResolver.GetErrorMessage((uint)HResultConverter.HResultFromWin32(error));

        switch (error)
        {
            case Win32ErrorCodes.ERROR_FILE_NOT_FOUND:
            case Win32ErrorCodes.ERROR_PATH_NOT_FOUND:
            case Win32ErrorCodes.ERROR_EVT_CHANNEL_NOT_FOUND:
            case Win32ErrorCodes.ERROR_EVT_MESSAGE_NOT_FOUND:
            case Win32ErrorCodes.ERROR_EVT_MESSAGE_ID_NOT_FOUND:
            case Win32ErrorCodes.ERROR_EVT_PUBLISHER_METADATA_NOT_FOUND:
                throw new FileNotFoundException(message);
            case Win32ErrorCodes.ERROR_INVALID_DATA:
            case Win32ErrorCodes.ERROR_EVT_INVALID_EVENT_DATA:
                throw new InvalidDataException(message);
            case Win32ErrorCodes.RPC_S_CALL_CANCELED:
            case Win32ErrorCodes.ERROR_CANCELLED:
                throw new OperationCanceledException(message);
            case Win32ErrorCodes.ERROR_ACCESS_DENIED:
            case Win32ErrorCodes.ERROR_INVALID_HANDLE:
                throw new UnauthorizedAccessException(message);
            default:
                throw new Exception(message);
        }
    }

    private static int CheckedCount(uint count, EvtVariantType type)
    {
        try
        {
            return checked((int)count);
        }
        catch (OverflowException ex)
        {
            throw new InvalidDataException(
                $"Invalid {nameof(EvtVariant)} count {count} for {nameof(EvtVariantType)}.{type}", ex);
        }
    }

    private static unsafe T[] ReadBlittableArray<T>(IntPtr reference, uint count, EvtVariantType type) where T : unmanaged
    {
        if (count == 0)
        {
            return [];
        }

        int length = CheckedCount(count, type);

        if (reference == IntPtr.Zero)
        {
            throw new InvalidDataException(
                $"Null reference with non-zero count {count} for {nameof(EvtVariantType)}.{type}");
        }

        var result = new T[length];
        new ReadOnlySpan<T>((void*)reference, length).CopyTo(result);

        return result;
    }

    private static unsafe Guid? ReadGuidOrNull(in EvtVariant variant)
    {
        if (variant.Type == (uint)EvtVariantType.Null) { return null; }

        if (variant.Type != (uint)EvtVariantType.Guid)
        {
            throw new InvalidDataException(
                $"Expected {nameof(EvtVariantType)}.{EvtVariantType.Guid} for the activity id, got type {variant.Type}");
        }

        if (variant.GuidVal == 0)
        {
            throw new InvalidDataException($"Null {nameof(EvtVariantType)}.{EvtVariantType.Guid} pointer for the activity id");
        }

        return *(Guid*)variant.GuidVal;
    }

    private static byte? ReadOptionalByte(in EvtVariant variant)
    {
        if (variant.Type == (uint)EvtVariantType.Null) { return null; }

        Debug.Assert(variant.Type == (uint)EvtVariantType.Byte);

        return variant.ByteVal;
    }

    private static int? ReadOptionalInt32(in EvtVariant variant)
    {
        if (variant.Type == (uint)EvtVariantType.Null) { return null; }

        Debug.Assert(variant.Type == (uint)EvtVariantType.UInt32);

        return unchecked((int)variant.UInt32Val);
    }

    private static long? ReadOptionalInt64(in EvtVariant variant)
    {
        if (variant.Type == (uint)EvtVariantType.Null) { return null; }

        Debug.Assert(variant.Type is (uint)EvtVariantType.UInt64 or (uint)EvtVariantType.HexInt64);

        return unchecked((long)variant.UInt64Val);
    }

    private static ushort? ReadOptionalUInt16(in EvtVariant variant)
    {
        if (variant.Type == (uint)EvtVariantType.Null) { return null; }

        Debug.Assert(variant.Type == (uint)EvtVariantType.UInt16);

        return variant.UInt16Val;
    }
}
