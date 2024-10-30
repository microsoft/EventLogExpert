// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Models;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace EventLogExpert.Eventing.Helpers;

public enum PathType
{
    LogName = 1,
    FilePath = 2
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

internal enum EvtRenderFlags
{
    EventValues,
    EventXml,
    Bookmark
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
    StringArray = 129,
    UInt32Array = 136
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
                return variant.GuidReference == IntPtr.Zero ?
                    Guid.Empty :
                    Marshal.PtrToStructure<Guid>(variant.GuidReference);
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
                return new SecurityIdentifier(variant.SidVal);
            case (int)EvtVariantType.HexInt32:
                return variant.Integer;
            case (int)EvtVariantType.HexInt64:
                return variant.ULong;
            case (int)EvtVariantType.Handle:
                return new EventLogHandle(variant.Handle);
            case (int)EvtVariantType.StringArray:
                if (variant.Count == 0)
                {
                    return Array.Empty<string>();
                }

                var stringArray = new string[variant.Count];

                for (int i = 0; i < variant.Count; i++)
                {
                    stringArray[i] = Marshal.PtrToStringAuto(Marshal.ReadIntPtr(variant.Reference, i * IntPtr.Size));
                }

                return stringArray;
            default:
                throw new InvalidDataException("Invalid EvtVariantType");
        }
    }

    [LibraryImport(EventLogApi, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EvtClose(IntPtr handle);

    [LibraryImport(EventLogApi, SetLastError = true)]
    internal static partial EventLogHandle EvtCreateRenderContext(
        int valuePathsCount,
        [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr)][Out] string[]? valuePaths,
        EvtRenderContextFlags flags);

    [LibraryImport(EventLogApi, SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EvtFormatMessage(
        EventLogHandle publisherMetadata,
        EventLogHandle @event,
        uint messageId,
        int valueCount,
        IntPtr values,
        EvtFormatMessageFlags flags,
        int bufferSize,
        Span<char> buffer,
        out int bufferUsed);

    [LibraryImport(EventLogApi, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EvtGetEventMetadataProperty(
        EventLogHandle eventMetadata,
        EvtEventMetadataPropertyId propertyId,
        int flags,
        int eventMetadataPropertyBufferSize,
        IntPtr eventMetadataPropertyBuffer,
        out int eventMetadataPropertyBufferUsed);

    [LibraryImport(EventLogApi, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EvtGetLogInfo(
        EventLogHandle log,
        EvtLogPropertyId propertyId,
        int propertyValueBufferSize,
        IntPtr propertyValueBuffer,
        out int propertyValueBufferUsed);

    [LibraryImport(EventLogApi, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EvtGetObjectArrayProperty(
        EventLogHandle objectArray,
        int propertyId,
        int arrayIndex,
        int flags,
        int propertyValueBufferSize,
        IntPtr propertyValueBuffer,
        out int propertyValueBufferUsed);

    [LibraryImport(EventLogApi, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EvtGetObjectArraySize(EventLogHandle objectArray, out int objectArraySize);

    [LibraryImport(EventLogApi, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EvtGetPublisherMetadataProperty(
        EventLogHandle publisherMetadata,
        EvtPublisherMetadataPropertyId propertyId,
        int flags,
        int publisherMetadataPropertyBufferSize,
        IntPtr publisherMetadataPropertyBuffer,
        out int publisherMetadataPropertyBufferUsed);

    [LibraryImport(EventLogApi, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EvtNext(
        EventLogHandle resultSet,
        int eventsSize,
        [MarshalAs(UnmanagedType.LPArray)][Out] IntPtr[] events,
        int timeout,
        int flags,
        ref int returned);

    [LibraryImport(EventLogApi, SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EvtNextChannelPath(
        EventLogHandle channelEnum,
        int channelPathBufferSize,
        Span<char> channelPathBuffer,
        out int channelPathBufferUsed);

    [LibraryImport(EventLogApi, SetLastError = true)]
    internal static partial EventLogHandle EvtNextEventMetadata(EventLogHandle eventMetadataEnum, int flags);

    [LibraryImport(EventLogApi, SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EvtNextPublisherId(
        EventLogHandle publisherEnum,
        int publisherIdBufferSize,
        Span<char> publisherIdBuffer,
        out int publisherIdBufferUsed);

    [LibraryImport(EventLogApi, SetLastError = true)]
    internal static partial EventLogHandle EvtOpenChannelEnum(EventLogHandle session, int flags);

    [LibraryImport(EventLogApi, SetLastError = true)]
    internal static partial EventLogHandle EvtOpenEventMetadataEnum(EventLogHandle publisherMetadata, int flags);

    [LibraryImport(EventLogApi, SetLastError = true)]
    internal static partial EventLogHandle EvtOpenLog(
        EventLogHandle session,
        [MarshalAs(UnmanagedType.LPWStr)] string path,
        PathType flags);

    [LibraryImport(EventLogApi, SetLastError = true)]
    internal static partial EventLogHandle EvtOpenPublisherEnum(EventLogHandle session, int flags);

    [LibraryImport(EventLogApi, SetLastError = true)]
    internal static partial EventLogHandle EvtOpenPublisherMetadata(
        EventLogHandle session,
        [MarshalAs(UnmanagedType.LPWStr)] string publisherId,
        [MarshalAs(UnmanagedType.LPWStr)] string? logFilePath,
        int locale,
        int flags);

    [LibraryImport(EventLogApi, SetLastError = true)]
    internal static partial EventLogHandle EvtOpenSession(
        EvtLoginClass loginClass,
        IntPtr login,
        int timeout,
        int flags);

    [LibraryImport(EventLogApi, SetLastError = true)]
    internal static partial EventLogHandle EvtQuery(
        EventLogHandle session,
        [MarshalAs(UnmanagedType.LPWStr)] string path,
        [MarshalAs(UnmanagedType.LPWStr)] string? query,
        int flags);

    [LibraryImport(EventLogApi, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EvtRender(
        EventLogHandle context,
        EventLogHandle fragment,
        EvtRenderFlags flags,
        int bufferSize,
        IntPtr buffer,
        out int bufferUsed,
        out int propertyCount);

    [LibraryImport(EventLogApi, SetLastError = true)]
    internal static partial EventLogHandle EvtSeek(
        EventLogHandle resultSet,
        long position,
        EventLogHandle bookmark,
        int timeout,
        SeekFlags flags);

    internal static string FormatMessage(EventLogHandle handle, uint messageId)
    {
        Span<char> emptyBuffer = ['\0'];

        bool success = EvtFormatMessage(
            handle,
            EventLogHandle.Zero,
            messageId,
            0,
            IntPtr.Zero,
            EvtFormatMessageFlags.Id,
            0,
            emptyBuffer,
            out int bufferUsed);

        int error = Marshal.GetLastWin32Error();

        if (!success &&
            error != 15029 /* ERROR_EVT_UNRESOLVED_VALUE_INSERT */ &&
            error != 15030 /* ERROR_EVT_UNRESOLVED_PARAMETER_INSERT */ &&
            error != 15031 /* ERROR_EVT_MAX_INSERTS_REACHED */)
        {
            if (error != 122 /* ERROR_INSUFFICIENT_BUFFER */)
            {
                ThrowEventLogException(error);
            }
        }

        var buffer = new char[bufferUsed];

        success = EvtFormatMessage(
            handle,
            EventLogHandle.Zero,
            messageId,
            0,
            IntPtr.Zero,
            EvtFormatMessageFlags.Id,
            bufferUsed,
            buffer,
            out bufferUsed);

        error = Marshal.GetLastWin32Error();

        if (!success &&
            error != 15029 /* ERROR_EVT_UNRESOLVED_VALUE_INSERT */ &&
            error != 15030 /* ERROR_EVT_UNRESOLVED_PARAMETER_INSERT */ &&
            error != 15031 /* ERROR_EVT_MAX_INSERTS_REACHED */)
        {
            ThrowEventLogException(error);
        }

        return bufferUsed - 1 <= 0 ? string.Empty : new string(buffer, 0, bufferUsed - 1);
    }

    /// <summary>Converts an event buffer that was returned from EvtRender to an <see cref="EventRecord"/></summary>
    /// <param name="eventBuffer">Pointer to a buffer returned from EvtRender</param>
    /// <param name="propertyCount">Property count returned from EvtRender</param>
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
        EventLogHandle array,
        int index,
        EvtPublisherMetadataPropertyId propertyId)
    {
        IntPtr buffer = IntPtr.Zero;

        try
        {
            bool success = EvtGetObjectArrayProperty(array, (int)propertyId, index, 0, 0, IntPtr.Zero, out int bufferSize);
            int error = Marshal.GetLastWin32Error();

            if (!success && error != 122 /* ERROR_INSUFFICIENT_BUFFER */)
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

            return ConvertVariant(variant);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    internal static int GetObjectArraySize(EventLogHandle array)
    {
        bool success = EvtGetObjectArraySize(array, out int size);
        int error = Marshal.GetLastWin32Error();

        if (!success)
        {
            ThrowEventLogException(error);
        }

        return size;
    }

    internal static void ThrowEventLogException(int error)
    {
        var message = ResolverMethods.GetErrorMessage((uint)Converter.HResultFromWin32(error));

        switch (error)
        {
            case 2 /*ERROR_FILE_NOT_FOUND*/:
            case 3 /*ERROR_PATH_NOT_FOUND*/:
            case 0x3A9f /*ERROR_EVT_CHANNEL_NOT_FOUND*/:
            case 0x3AB3 /*ERROR_EVT_MESSAGE_NOT_FOUND*/:
            case 0x3AB4 /*ERROR_EVT_MESSAGE_ID_NOT_FOUND*/:
            case 0x3A9A /*ERROR_EVT_PUBLISHER_METADATA_NOT_FOUND*/:
                throw new FileNotFoundException(message);
            case 0xD /*ERROR_INVALID_DATA*/:
            case 0x3A9D /*ERROR_EVT_INVALID_EVENT_DATA*/:
                throw new InvalidDataException(message);
            case 0x71A /*RPC_S_CALL_CANCELED*/:
            case 0x4C7 /*ERROR_CANCELLED*/:
                throw new OperationCanceledException(message);
            case 5 /*ERROR_ACCESS_DENIED*/:
            case 6 /*ERROR_INVALID_HANDLE*/:
                throw new UnauthorizedAccessException();
            default:
                throw new Exception(message);
        }
    }
}
