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

internal enum EvtRenderContextFlags
{
    Values = 0,
    System = 1,
    User = 2
}

internal enum EvtRenderFlags
{
    EventValues = 0,
    EventXml = 1,
    Bookmark = 2
}

internal enum EvtSystemPropertyId
{
    ProviderName = 0,
    ProviderGuid = 1,
    EventId = 2,
    Qualifiers = 3,
    Level = 4,
    Task = 5,
    Opcode = 6,
    Keywords = 7,
    TimeCreated = 8,
    EventRecordId = 9,
    ActivityId = 10,
    RelatedActivityId = 11,
    ProcessID = 12,
    ThreadID = 13,
    Channel = 14,
    Computer = 15,
    UserID = 16,
    Version = 17,
    PropertyIdEND = 18,
}

internal enum EvtVariantType
{
    Null = 0,
    String = 1,
    AnsiString = 2,
    SByte = 3,
    Byte = 4,
    Int16 = 5,
    UInt16 = 6,
    Int32 = 7,
    UInt32 = 8,
    Int64 = 9,
    UInt64 = 10,
    Single = 11,
    Double = 12,
    Boolean = 13,
    Binary = 14,
    Guid = 15,
    SizeT = 16,
    FileTime = 17,
    SysTime = 18,
    Sid = 19,
    HexInt32 = 20,
    HexInt64 = 21,
    Handle = 32,
    Xml = 35,
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
            default:
                return null;
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
        [Out] char[]? channelPathBuffer,
        out int channelPathBufferUsed);

    [LibraryImport(EventLogApi, SetLastError = true)]
    internal static partial EventLogHandle EvtOpenChannelEnum(
        EventLogHandle session,
        int flags);

    [LibraryImport(EventLogApi, SetLastError = true)]
    internal static partial EventLogHandle EvtOpenLog(
        EventLogHandle session,
        [MarshalAs(UnmanagedType.LPWStr)] string path,
        PathType flags);

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

    /// <summary>Converts an event buffer that was returned from EvtRender to an <see cref="EventProperties"/></summary>
    /// <param name="eventBuffer">Pointer to a buffer returned from EvtRender</param>
    /// <param name="propertyCount">Property count returned from EvtRender</param>
    /// <returns></returns>
    internal static EventProperties GetEventProperties(IntPtr eventBuffer, int propertyCount)
    {
        EventProperties properties = new();

        for (int i = 0; i < propertyCount; i++)
        {
            var property = Marshal.PtrToStructure<EvtVariant>(eventBuffer + (i * Marshal.SizeOf<EvtVariant>()));

            switch (i)
            {
                case (int)EvtSystemPropertyId.ActivityId:
                    properties.ActivityId = (Guid?)ConvertVariant(property);
                    break;
                case (int)EvtSystemPropertyId.Computer:
                    properties.ComputerName = (string?)ConvertVariant(property);
                    break;
                case (int)EvtSystemPropertyId.EventId:
                    properties.Id = (ushort?)ConvertVariant(property);
                    break;
                case (int)EvtSystemPropertyId.Keywords:
                    properties.Keywords = (ulong?)ConvertVariant(property);
                    break;
                case (int)EvtSystemPropertyId.Level:
                    properties.Level = (byte?)ConvertVariant(property);
                    break;
                case (int)EvtSystemPropertyId.Channel:
                    properties.LogName = (string?)ConvertVariant(property);
                    break;
                case (int)EvtSystemPropertyId.ProcessID:
                    properties.ProcessId = (uint?)ConvertVariant(property);
                    break;
                case (int)EvtSystemPropertyId.EventRecordId:
                    properties.RecordId = (ulong?)ConvertVariant(property);
                    break;
                case (int)EvtSystemPropertyId.ProviderName:
                    properties.Source = (string?)ConvertVariant(property);
                    break;
                case (int)EvtSystemPropertyId.Task:
                    properties.Task = (ushort?)ConvertVariant(property);
                    break;
                case (int)EvtSystemPropertyId.ThreadID:
                    properties.ThreadId = (uint?)ConvertVariant(property);
                    break;
                case (int)EvtSystemPropertyId.TimeCreated:
                    properties.TimeCreated = (DateTime?)ConvertVariant(property);
                    break;
                case (int)EvtSystemPropertyId.UserID:
                    properties.UserId = (SecurityIdentifier?)ConvertVariant(property);
                    break;
            }
        }

        return properties;
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
