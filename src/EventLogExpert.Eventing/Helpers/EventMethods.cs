// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Models;
using EventLogExpert.Eventing.Readers;
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace EventLogExpert.Eventing.Helpers;

public enum PathType
{
    LogName = 1,
    FilePath
}

internal enum EvtEventMetadataPropertyId
{
    ID,
    Version,
    Channel,
    Level,
    Opcode,
    Task,
    Keyword,
    MessageID,
    Template
}

internal enum EvtFormatMessageFlags
{
    Event = 1,
    Level,
    Task,
    Opcode,
    Keyword,
    Channel,
    Provider,
    Id,
    Xml
}

internal enum EvtLoginClass { EvtRpcLogin = 1 }

internal enum EvtLogPropertyId
{
    CreationTime,
    LastAccessTime,
    LastWriteTime,
    FileSize,
    Attributes,
    NumberOfLogRecords,
    OldestRecordNumber,
    Full,
}

internal enum EvtPublisherMetadataPropertyId
{
    PublisherGuid,
    ResourceFilePath,
    ParameterFilePath,
    MessageFilePath,
    HelpLink,
    PublisherMessageID,
    ChannelReferences,
    ChannelReferencePath,
    ChannelReferenceIndex,
    ChannelReferenceID,
    ChannelReferenceFlags,
    ChannelReferenceMessageID,
    Levels,
    LevelName,
    LevelValue,
    LevelMessageID,
    Tasks,
    TaskName,
    TaskEventGuid,
    TaskValue,
    TaskMessageID,
    Opcodes,
    OpcodeName,
    OpcodeValue,
    OpcodeMessageID,
    Keywords,
    KeywordName,
    KeywordValue,
    KeywordMessageID
}

internal enum EvtRenderContextFlags
{
    Values,
    System,
    User
}

/// <summary>Defines the values that specify what to render</summary>
internal enum EvtRenderFlags
{
    /// <summary>Render the <see cref="EvtVariant" /> properties specified in the rendering context</summary>
    EventValues,
    /// <summary>Render the event as an XML string</summary>
    EventXml,
    /// <summary>Render the bookmark as an XML string, so that you can easily persist the bookmark for use later</summary>
    Bookmark
}

[Flags]
internal enum EvtSubscribeFlags
{
    ToFutureEvents = 1,
    StartAtOldestRecord = 2,
    StartAfterBookmark = 3,
#pragma warning disable CA1069 // Enums values should not be duplicated
    OriginMask = 3,
#pragma warning restore CA1069 // Enums values should not be duplicated
    TolerateQueryErrors = 0x1000,
    Strict = 0x10000
}

internal enum EvtSystemPropertyId
{
    ProviderName,
    ProviderGuid,
    EventId,
    Qualifiers,
    Level,
    Task,
    Opcode,
    Keywords,
    TimeCreated,
    EventRecordId,
    ActivityId,
    RelatedActivityID,
    ProcessID,
    ThreadID,
    Channel,
    Computer,
    UserID,
    Version
}

internal enum EvtVariantType
{
    Null,
    String,
    AnsiString,
    SByte,
    Byte,
    Int16,
    UInt16,
    Int32,
    UInt32,
    Int64,
    UInt64,
    Single,
    Double,
    Boolean,
    Binary,
    Guid,
    SizeT,
    FileTime,
    SysTime,
    Sid,
    HexInt32,
    HexInt64,
    Handle,
    Xml,

    // Custom types
    StringArray = 129 // String masked with EVT_VARIANT_TYPE_ARRAY
}

[Flags]
internal enum SeekFlags
{
    RelativeToFirst = 1,
    RelativeToLast = 2,
    RelativeToCurrent = 3,
    RelativeToBookmark = 4,
    OriginMask = 7,
    Strict = 65536
}

internal static partial class EventMethods
{
    private const string EventLogApi = "wevtapi.dll";

    internal static object? ConvertVariant(EvtVariant variant)
    {
        switch (variant.Type)
        {
            case (int)EvtVariantType.Null:
                return null;
            case (int)EvtVariantType.String:
                return Marshal.PtrToStringAuto(variant.StringVal);
            case (int)EvtVariantType.AnsiString:
                return Marshal.PtrToStringAnsi(variant.AnsiString);
            case (int)EvtVariantType.SByte:
                return variant.SByte;
            case (int)EvtVariantType.Byte:
                return variant.UInt8;
            case (int)EvtVariantType.Int16:
                return variant.SByte;
            case (int)EvtVariantType.UInt16:
                return variant.UShort;
            case (int)EvtVariantType.Int32:
                return variant.Integer;
            case (int)EvtVariantType.UInt32:
                return variant.UInteger;
            case (int)EvtVariantType.Int64:
                return variant.Long;
            case (int)EvtVariantType.UInt64:
                return variant.ULong;
            case (int)EvtVariantType.Single:
                return variant.Single;
            case (int)EvtVariantType.Double:
                return variant.Double;
            case (int)EvtVariantType.Boolean:
                return variant.Bool != 0;
            case (int)EvtVariantType.Binary:
                byte[] bytes = new byte[variant.Count];
                Marshal.Copy(variant.Binary, bytes, 0, bytes.Length);
                return bytes;
            case (int)EvtVariantType.Guid:
                return Marshal.PtrToStructure<Guid>(variant.GuidReference);
            case (int)EvtVariantType.SizeT:
                return variant.SizeT;
            case (int)EvtVariantType.FileTime:
                return DateTime.FromFileTime((long)variant.FileTime);
            case (int)EvtVariantType.SysTime:
                var sysTime = Marshal.PtrToStructure<SystemTime>(variant.SystemTime);

                return new DateTime(
                    sysTime.Year,
                    sysTime.Month,
                    sysTime.Day,
                    sysTime.Hour,
                    sysTime.Minute,
                    sysTime.Second,
                    sysTime.Milliseconds);
            case (int)EvtVariantType.Sid:
                return variant.SidVal == IntPtr.Zero ? null : new SecurityIdentifier(variant.SidVal);
            case (int)EvtVariantType.HexInt32:
                return variant.Integer;
            case (int)EvtVariantType.HexInt64:
                return variant.ULong;
            case (int)EvtVariantType.Handle:
                return new EvtHandle(variant.Handle);
            case (int)EvtVariantType.StringArray:
                if (variant.Count == 0)
                {
                    return Array.Empty<string>();
                }

                var stringArray = new string[variant.Count];

                for (int i = 0; i < variant.Count; i++)
                {
                    IntPtr stringRef = Marshal.ReadIntPtr(variant.Reference, i * IntPtr.Size);

                    stringArray[i] = Marshal.PtrToStringAuto(stringRef) ?? string.Empty;
                }

                return stringArray;
            default:
                throw new InvalidDataException($"Invalid {nameof(EvtVariantType)}");
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
        int propertyId,
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
        PathType flags);

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
        PathType flags);

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
            error != Interop.ERROR_EVT_UNRESOLVED_VALUE_INSERT &&
            error != Interop.ERROR_EVT_UNRESOLVED_PARAMETER_INSERT &&
            error != Interop.ERROR_EVT_MAX_INSERTS_REACHED)
        {
            if (error != Interop.ERROR_INSUFFICIENT_BUFFER)
            {
                ThrowEventLogException(error);
            }
        }

        Span<char> buffer = stackalloc char[bufferUsed];

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
            error != Interop.ERROR_EVT_UNRESOLVED_VALUE_INSERT &&
            error != Interop.ERROR_EVT_UNRESOLVED_PARAMETER_INSERT &&
            error != Interop.ERROR_EVT_MAX_INSERTS_REACHED)
        {
            ThrowEventLogException(error);
        }

        return bufferUsed - 1 <= 0 ? string.Empty : new string(buffer[..(bufferUsed - 1)]);
    }

    /// <summary>Converts a buffer that was returned from <see cref="EvtRender" /> to an <see cref="EventRecord" /></summary>
    /// <param name="eventBuffer">Pointer to a buffer returned from <see cref="EvtRender" /></param>
    /// <param name="propertyCount">Number of properties returned from <see cref="EvtRender" /></param>
    /// <returns></returns>
    internal static EventRecord GetEventRecord(IntPtr eventBuffer, int propertyCount)
    {
        EventRecord properties = new();

        for (int i = 0; i < propertyCount; i++)
        {
            var property = Marshal.PtrToStructure<EvtVariant>(eventBuffer + (i * Marshal.SizeOf<EvtVariant>()));
            var variant = ConvertVariant(property);

            // Properties are returned in the order defined in EVT_SYSTEM_PROPERTY_ID enum
            switch (i)
            {
                case (int)EvtSystemPropertyId.ActivityId:
                    properties.ActivityId = (Guid?)variant;
                    break;
                case (int)EvtSystemPropertyId.Computer:
                    properties.ComputerName = (string)variant!;
                    break;
                case (int)EvtSystemPropertyId.EventId:
                    properties.Id = (ushort)variant!;
                    break;
                case (int)EvtSystemPropertyId.Keywords:
                    properties.Keywords = (long?)(ulong?)variant;
                    break;
                case (int)EvtSystemPropertyId.Level:
                    properties.Level = (byte?)variant;
                    break;
                case (int)EvtSystemPropertyId.Channel:
                    properties.LogName = (string)variant!;
                    break;
                case (int)EvtSystemPropertyId.ProcessID:
                    properties.ProcessId = (int?)(uint?)variant;
                    break;
                case (int)EvtSystemPropertyId.EventRecordId:
                    properties.RecordId = (long?)(ulong?)variant;
                    break;
                case (int)EvtSystemPropertyId.ProviderName:
                    properties.ProviderName = (string)variant!;
                    break;
                case (int)EvtSystemPropertyId.Task:
                    properties.Task = (ushort?)variant;
                    break;
                case (int)EvtSystemPropertyId.ThreadID:
                    properties.ThreadId = (int?)(uint?)variant;
                    break;
                case (int)EvtSystemPropertyId.TimeCreated:
                    properties.TimeCreated = (DateTime)variant!;
                    break;
                case (int)EvtSystemPropertyId.UserID:
                    properties.UserId = (SecurityIdentifier?)variant;
                    break;
                case (int)EvtSystemPropertyId.Version:
                    properties.Version = (byte?)variant;
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
        IntPtr buffer = IntPtr.Zero;

        try
        {
            bool success = EvtGetObjectArrayProperty(array, (int)propertyId, index, 0, 0, IntPtr.Zero, out int bufferSize);
            int error = Marshal.GetLastWin32Error();

            if (!success && error != Interop.ERROR_INSUFFICIENT_BUFFER)
            {
                ThrowEventLogException(error);
            }

            buffer = Marshal.AllocHGlobal(bufferSize);

            success = EvtGetObjectArrayProperty(array, (int)propertyId, index, 0, bufferSize, buffer, out bufferSize);
            error = Marshal.GetLastWin32Error();

            if (!success)
            {
                ThrowEventLogException(error);
            }

            var variant = Marshal.PtrToStructure<EvtVariant>(buffer);

            return ConvertVariant(variant) ??
                throw new InvalidDataException($"Invalid Object Array for Property: {propertyId}");
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
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

    internal static EventRecord RenderEvent(EvtHandle eventHandle, EvtRenderFlags flag)
    {
        IntPtr buffer = IntPtr.Zero;

        try
        {
            bool success = EvtRender(
                EventLogSession.GlobalSession.SystemRenderContext,
                eventHandle,
                flag,
                0,
                buffer,
                out int bufferUsed,
                out int propertyCount);

            int error = Marshal.GetLastWin32Error();

            if (!success && error != Interop.ERROR_INSUFFICIENT_BUFFER)
            {
                ThrowEventLogException(error);
            }

            buffer = Marshal.AllocHGlobal(bufferUsed);

            success = EvtRender(
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
                ThrowEventLogException(error);
            }

            return GetEventRecord(buffer, propertyCount);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    internal static IList<object> RenderEventProperties(EvtHandle eventHandle)
    {
        IntPtr buffer = IntPtr.Zero;

        try
        {
            bool success = EvtRender(
                EventLogSession.GlobalSession.UserRenderContext,
                eventHandle,
                EvtRenderFlags.EventValues,
                0,
                buffer,
                out int bufferUsed,
                out int propertyCount);

            int error = Marshal.GetLastWin32Error();

            if (!success && error != Interop.ERROR_INSUFFICIENT_BUFFER)
            {
                ThrowEventLogException(error);
            }

            buffer = Marshal.AllocHGlobal(bufferUsed);

            success = EvtRender(
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
                ThrowEventLogException(error);
            }

            List<object> properties = [];

            if (propertyCount <= 0) { return properties; }

            for (int i = 0; i < propertyCount; i++)
            {
                var property = Marshal.PtrToStructure<EvtVariant>(buffer + (i * Marshal.SizeOf<EvtVariant>()));

                properties.Add(ConvertVariant(property) ?? throw new InvalidDataException());
            }

            return properties;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);   
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

        if (!success && error != Interop.ERROR_INSUFFICIENT_BUFFER)
        {
            ThrowEventLogException(error);
        }

        Span<char> buffer = stackalloc char[bufferUsed / sizeof(char)];

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

    internal static void ThrowEventLogException(int error)
    {
        var message = ResolverMethods.GetErrorMessage((uint)Converter.HResultFromWin32(error));

        switch (error)
        {
            case Interop.ERROR_FILE_NOT_FOUND:
            case Interop.ERROR_PATH_NOT_FOUND:
            case Interop.ERROR_EVT_CHANNEL_NOT_FOUND:
            case Interop.ERROR_EVT_MESSAGE_NOT_FOUND:
            case Interop.ERROR_EVT_MESSAGE_ID_NOT_FOUND:
            case Interop.ERROR_EVT_PUBLISHER_METADATA_NOT_FOUND:
                throw new FileNotFoundException(message);
            case Interop.ERROR_INVALID_DATA:
            case Interop.ERROR_EVT_INVALID_EVENT_DATA:
                throw new InvalidDataException(message);
            case Interop.RPC_S_CALL_CANCELED:
            case Interop.ERROR_CANCELLED:
                throw new OperationCanceledException(message);
            case Interop.ERROR_ACCESS_DENIED:
            case Interop.ERROR_INVALID_HANDLE:
                throw new UnauthorizedAccessException();
            default:
                throw new Exception(message);
        }
    }
}
