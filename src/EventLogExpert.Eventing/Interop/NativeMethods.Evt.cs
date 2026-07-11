// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Readers;
using Microsoft.Win32.SafeHandles;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace EventLogExpert.Eventing.Interop;

internal static partial class NativeMethods
{
    private const string EventLogApi = "wevtapi.dll";

    // Per-thread, grow-only scratch buffers reused by the wevtapi wrappers below (each family owns its buffer so the
    // "no same-family reentrancy" invariant stays local; the three families never nest on a thread). [ThreadStatic]
    // (not shared) because EventLogWatcher renders on overlapping ThreadPool callback threads; each thread owns its
    // buffer, so no lock is needed. Retained up to MaxRetainedChars (below the ~85 KB LOH threshold); a call larger than
    // that uses a transient array that is not stored back, bounding steady-state per-thread retention to ~64 KB each.
#if DEBUG
    // Debug-settable so a test can force the grow / retained-cap / skip-probe paths deterministically without a
    // multi-KB event (const in Release).
    internal static int InitialRenderChars = 4096;
    internal static int MaxRetainedChars = 32768;
#else
    private const int InitialRenderChars = 4096;
    private const int MaxRetainedChars = 32768;
#endif

    [ThreadStatic]
    private static char[]? t_renderBuffer;

    [ThreadStatic]
    private static char[]? t_formatBuffer;

    [ThreadStatic]
    private static char[]? t_objectArrayBuffer;

#if DEBUG
    // Invoked by the variant-reading processors immediately before the first EVT_VARIANT read, while the buffer is still
    // pinned, so a GC-move regression test can force a compacting collection at the moment of highest vulnerability.
    internal static Action? BeforeVariantReadForTest;

    // Count native P/Invokes on the calling thread, so the "one call = one P/Invoke" gate (probe eliminated) can be
    // asserted without tearing under parallel tests.
    [ThreadStatic]
    internal static int RenderPInvokeCountForTest;

    [ThreadStatic]
    internal static int FormatPInvokeCountForTest;

    [ThreadStatic]
    internal static int ObjectArrayPInvokeCountForTest;

    internal static int? RetainedRenderBufferChars => t_renderBuffer?.Length;

    internal static int? RetainedFormatBufferChars => t_formatBuffer?.Length;

    internal static int? RetainedObjectArrayBufferChars => t_objectArrayBuffer?.Length;

    // Resets the calling thread's scratch buffers + P/Invoke counters so a test can deterministically drive the
    // first-touch / grow / retained-cap paths (pair with lowering InitialRenderChars / MaxRetainedChars).
    internal static void ResetRenderScratchForTest()
    {
        t_renderBuffer = null;
        t_formatBuffer = null;
        t_objectArrayBuffer = null;
        RenderPInvokeCountForTest = 0;
        FormatPInvokeCountForTest = 0;
        ObjectArrayPInvokeCountForTest = 0;
    }
#endif

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
                return ReadSysTime(variant);
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

    internal static EventProperty ConvertVariantToProperty(EvtVariant variant)
    {
        switch (variant.Type)
        {
            case (int)EvtVariantType.SByte:
                return variant.SByteVal;
            case (int)EvtVariantType.Byte:
                return variant.ByteVal;
            case (int)EvtVariantType.Int16:
                return variant.Int16Val;
            case (int)EvtVariantType.UInt16:
                return variant.UInt16Val;
            case (int)EvtVariantType.Int32:
            case (int)EvtVariantType.HexInt32:
                return variant.Int32Val;
            case (int)EvtVariantType.UInt32:
                return variant.UInt32Val;
            case (int)EvtVariantType.Int64:
                return variant.Int64Val;
            case (int)EvtVariantType.UInt64:
            case (int)EvtVariantType.HexInt64:
                return variant.UInt64Val;
            case (int)EvtVariantType.Single:
                return variant.SingleVal;
            case (int)EvtVariantType.Double:
                return variant.DoubleVal;
            case (int)EvtVariantType.Boolean:
                return variant.BooleanVal != 0;
            case (int)EvtVariantType.SizeT:
                return variant.SizeTVal;
            case (int)EvtVariantType.FileTime:
                return DateTime.FromFileTimeUtc((long)variant.FileTimeVal);
            case (int)EvtVariantType.SysTime:
                return ReadSysTime(variant);
            default:
                // Reference shapes reuse the boxing converter (reference types add no allocation; the rare boxed
                // Guid is acceptable). Null / unsupported types throw, preserving the boxed path's ?? throw contract.
                return EventProperty.FromReference(ConvertVariant(variant) ?? throw new InvalidDataException());
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
        [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr)][In] string[]? valuePaths,
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
    /// <param name="messageId">The resource identifier of the message string that you want formatted</param>
    internal static string FormatMessage(EvtHandle publisherMetadataHandle, uint messageId)
    {
        char[] buffer = t_formatBuffer ??= new char[InitialRenderChars];

        // Common path: 1 P/Invoke. Copy out only when the message actually fit the buffer.
        if (TryFormat(buffer, out int usedChars, out int error))
        {
            return Materialize(buffer, usedChars);
        }

        if (error != Win32ErrorCodes.ERROR_INSUFFICIENT_BUFFER)
        {
            ThrowEventLogException(error);
        }

        // usedChars carried the required char count; grow to exactly that and retry once.
        buffer = GrowScratch(ref t_formatBuffer, usedChars);

        if (!TryFormat(buffer, out usedChars, out error))
        {
            ThrowEventLogException(error);
        }

        return Materialize(buffer, usedChars);

        // EvtFormatMessage sizes in CHARACTERS (not bytes). An unresolved-insert code means the message was still
        // written best-effort - tolerate + copy out, but ONLY when it fit; a too-large tolerated insert is reported as
        // INSUFFICIENT so the caller grows. Every other failure (e.g. MESSAGE_ID_NOT_FOUND) is thrown, as before.
        bool TryFormat(char[] target, out int chars, out int lastError)
        {
#if DEBUG
            FormatPInvokeCountForTest++;
#endif
            if (EvtFormatMessage(publisherMetadataHandle, EvtHandle.Zero, messageId, 0, IntPtr.Zero,
                EvtFormatMessageFlags.Id, target.Length, target, out chars))
            {
                lastError = 0;
                return true;
            }

            lastError = Marshal.GetLastWin32Error();

            bool unresolvedInsert =
                lastError == Win32ErrorCodes.ERROR_EVT_UNRESOLVED_VALUE_INSERT ||
                lastError == Win32ErrorCodes.ERROR_EVT_UNRESOLVED_PARAMETER_INSERT ||
                lastError == Win32ErrorCodes.ERROR_EVT_MAX_INSERTS_REACHED;

            if (unresolvedInsert && chars <= target.Length)
            {
                lastError = 0;
                return true;
            }

            if (unresolvedInsert)
            {
                lastError = Win32ErrorCodes.ERROR_INSUFFICIENT_BUFFER;
            }

            return false;
        }

        static string Materialize(char[] target, int chars) =>
            chars - 1 <= 0 ? string.Empty : new string(target, 0, chars - 1);
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
                    if (variant.Type != (uint)EvtVariantType.UInt16)
                    {
                        throw new InvalidDataException($"Expected EVT_VARIANT type UInt16 for EventId, got {variant.Type}.");
                    }

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
                case (int)EvtSystemPropertyId.Opcode:
                    properties.Opcode = ReadOptionalByte(variant);
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
                case (int)EvtSystemPropertyId.RelatedActivityID:
                    properties.RelatedActivityId = ReadGuidOrNull(variant);
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

    internal static unsafe object GetObjectArrayProperty(
        EvtHandle array,
        int index,
        EvtPublisherMetadataPropertyId propertyId)
    {
        char[] buffer = t_objectArrayBuffer ??= new char[InitialRenderChars];
        int neededBytes;

        fixed (char* pinned = buffer)
        {
#if DEBUG
            ObjectArrayPInvokeCountForTest++;
#endif
            if (EvtGetObjectArrayProperty(array, propertyId, index, 0, buffer.Length * sizeof(char), (IntPtr)pinned, out int usedBytes))
            {
                return ConvertObjectArrayVariant(pinned, propertyId);
            }

            int error = Marshal.GetLastWin32Error();

            if (error != Win32ErrorCodes.ERROR_INSUFFICIENT_BUFFER)
            {
                ThrowEventLogException(error);
            }

            neededBytes = usedBytes;
        }

        buffer = GrowScratch(ref t_objectArrayBuffer, CharsForBytes(neededBytes));

        fixed (char* pinned = buffer)
        {
#if DEBUG
            ObjectArrayPInvokeCountForTest++;
#endif
            if (!EvtGetObjectArrayProperty(array, propertyId, index, 0, buffer.Length * sizeof(char), (IntPtr)pinned, out int _))
            {
                ThrowEventLogException(Marshal.GetLastWin32Error());
            }

            return ConvertObjectArrayVariant(pinned, propertyId);
        }
    }

    // Reads the single EVT_VARIANT written by EvtGetObjectArrayProperty while the buffer is still pinned (the variant's
    // string/SID fields point into it). The GC-move hook fires immediately before the read, shared with the render path.
    private static unsafe object ConvertObjectArrayVariant(char* pinned, EvtPublisherMetadataPropertyId propertyId)
    {
#if DEBUG
        BeforeVariantReadForTest?.Invoke();
#endif
        return ConvertVariant(*(EvtVariant*)pinned) ??
            throw new InvalidDataException($"Invalid Object Array for Property: {propertyId}");
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

    // Overflow-safe byte->char ceil for the render + object-array paths (EvtRender / EvtGetObjectArrayProperty size in
    // bytes). Bounding neededBytes first keeps (neededBytes + sizeof(char) - 1) from overflowing int.
    private static int CharsForBytes(int neededBytes)
    {
        // Reject only the values where the ceil's `+ (sizeof(char) - 1)` would overflow int, so the bound matches the
        // expression below exactly (int.MaxValue - 1 is still in range and allowed).
        if (neededBytes is < 0 or > int.MaxValue - (sizeof(char) - 1))
        {
            ThrowEventLogException(Win32ErrorCodes.ERROR_INSUFFICIENT_BUFFER);
        }

        return (neededBytes + sizeof(char) - 1) / sizeof(char);
    }

    // Grows a [ThreadStatic] scratch slot to at least neededChars, grow-only: reuses the retained buffer when it is
    // already large enough (no shrink, no redundant allocation), grows + retains when within the LOH-bounded cap, or
    // returns a transient array (not stored back) so a rare huge call cannot permanently bloat the thread.
    private static char[] GrowScratch(ref char[]? retained, int neededChars)
    {
        if (retained is not null && retained.Length >= neededChars) { return retained; }

        return neededChars <= MaxRetainedChars ? (retained = new char[neededChars]) : new char[neededChars];
    }

    // Renders <paramref name="fragment"/> into the per-thread grow-only buffer and invokes <paramref name="process"/>
    // WHILE THE BUFFER IS STILL PINNED. The continuous pin is load-bearing: for EvtRenderContextValues the rendered
    // EVT_VARIANTs hold absolute pointers into the buffer, so a GC compaction between render and variant read would
    // leave them stale. The size-probe pass is skipped - on ERROR_INSUFFICIENT_BUFFER, EvtRender reports the required
    // size, so the buffer is grown once and retried. INVARIANT: a processor must NOT trigger another render on the
    // current thread - t_renderBuffer is live and pinned for the duration.
    private static unsafe T RenderWhilePinned<T>(
        EvtHandle context,
        EvtHandle fragment,
        EvtRenderFlags flags,
        delegate*<IntPtr, int, int, T> process)
    {
        char[] buffer = t_renderBuffer ??= new char[InitialRenderChars];
        int neededBytes;

        fixed (char* pinned = buffer)
        {
#if DEBUG
            RenderPInvokeCountForTest++;
#endif
            if (EvtRender(context, fragment, flags, buffer.Length * sizeof(char), (IntPtr)pinned, out int usedBytes, out int propertyCount))
            {
                return process((IntPtr)pinned, usedBytes, propertyCount);
            }

            int error = Marshal.GetLastWin32Error();

            if (error != Win32ErrorCodes.ERROR_INSUFFICIENT_BUFFER)
            {
                ThrowEventLogException(error);
            }

            neededBytes = usedBytes;
        }

        buffer = GrowScratch(ref t_renderBuffer, CharsForBytes(neededBytes));

        fixed (char* pinned = buffer)
        {
#if DEBUG
            RenderPInvokeCountForTest++;
#endif
            if (!EvtRender(context, fragment, flags, buffer.Length * sizeof(char), (IntPtr)pinned, out int usedBytes, out int propertyCount))
            {
                ThrowEventLogException(Marshal.GetLastWin32Error());
            }

            return process((IntPtr)pinned, usedBytes, propertyCount);
        }
    }

    private static EventRecord ProcessRenderedEventRecord(IntPtr buffer, int usedBytes, int propertyCount)
    {
#if DEBUG
        BeforeVariantReadForTest?.Invoke();
#endif
        return GetEventRecord(buffer, propertyCount);
    }

    private static unsafe ImmutableArray<EventProperty> ProcessRenderedEventProperties(IntPtr buffer, int usedBytes, int propertyCount)
    {
        if (propertyCount <= 0) { return ImmutableArray<EventProperty>.Empty; }

#if DEBUG
        BeforeVariantReadForTest?.Invoke();
#endif

        var properties = new EventProperty[propertyCount];
        var variants = (EvtVariant*)buffer;

        for (int i = 0; i < propertyCount; i++)
        {
            properties[i] = ConvertVariantToProperty(variants[i]);
        }

        return ImmutableCollectionsMarshal.AsImmutableArray(properties);
    }

    private static unsafe string? ProcessRenderedEventXml(IntPtr buffer, int usedBytes, int propertyCount) =>
        usedBytes - 1 <= 0 ? null : new string((char*)buffer, 0, (usedBytes - 1) / sizeof(char));

    // A value-path that is absent on the event renders as Null; map it to a null-reference property (surfaced as
    // EventFieldValueKind.Null) instead of letting ConvertVariantToProperty throw on it.
    private static unsafe ImmutableArray<EventProperty> ProcessRenderedValuePaths(IntPtr buffer, int usedBytes, int propertyCount)
    {
        if (propertyCount <= 0) { return ImmutableArray<EventProperty>.Empty; }

#if DEBUG
        BeforeVariantReadForTest?.Invoke();
#endif

        var properties = new EventProperty[propertyCount];
        var variants = (EvtVariant*)buffer;

        for (int i = 0; i < propertyCount; i++)
        {
            properties[i] = variants[i].Type == (int)EvtVariantType.Null
                ? EventProperty.FromReference(null)
                : ConvertVariantToProperty(variants[i]);
        }

        return ImmutableCollectionsMarshal.AsImmutableArray(properties);
    }

    internal static unsafe EventRecord RenderEvent(EvtHandle eventHandle) =>
        RenderWhilePinned(EventLogSession.GlobalSession.SystemRenderContext,
            eventHandle,
            EvtRenderFlags.EventValues,
            &ProcessRenderedEventRecord);

    internal static unsafe ImmutableArray<EventProperty> RenderEventProperties(EvtHandle eventHandle) =>
        RenderWhilePinned(EventLogSession.GlobalSession.UserRenderContext,
            eventHandle,
            EvtRenderFlags.EventValues,
            &ProcessRenderedEventProperties);

    internal static unsafe ImmutableArray<EventProperty> RenderEventValues(
        EvtHandle valuePathsContext,
        EvtHandle eventHandle) =>
        RenderWhilePinned(valuePathsContext, eventHandle, EvtRenderFlags.EventValues, &ProcessRenderedValuePaths);

    internal static unsafe string? RenderEventXml(EvtHandle eventHandle) =>
        RenderWhilePinned(EvtHandle.Zero, eventHandle, EvtRenderFlags.EventXml, &ProcessRenderedEventXml);

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

        return variant.Type != (uint)EvtVariantType.Byte ?
            throw new InvalidDataException($"Expected EVT_VARIANT type Byte, got {variant.Type}.") :
            variant.ByteVal;
    }

    private static int? ReadOptionalInt32(in EvtVariant variant)
    {
        if (variant.Type == (uint)EvtVariantType.Null) { return null; }

        if (variant.Type != (uint)EvtVariantType.UInt32)
        {
            throw new InvalidDataException($"Expected EVT_VARIANT type UInt32, got {variant.Type}.");
        }

        return unchecked((int)variant.UInt32Val);
    }

    private static long? ReadOptionalInt64(in EvtVariant variant)
    {
        if (variant.Type == (uint)EvtVariantType.Null) { return null; }

        if (variant.Type is not ((uint)EvtVariantType.UInt64 or (uint)EvtVariantType.HexInt64))
        {
            throw new InvalidDataException($"Expected EVT_VARIANT type UInt64 or HexInt64, got {variant.Type}.");
        }

        return unchecked((long)variant.UInt64Val);
    }

    private static ushort? ReadOptionalUInt16(in EvtVariant variant)
    {
        if (variant.Type == (uint)EvtVariantType.Null) { return null; }

        return variant.Type != (uint)EvtVariantType.UInt16 ?
            throw new InvalidDataException($"Expected EVT_VARIANT type UInt16, got {variant.Type}.") :
            variant.UInt16Val;
    }

    private static DateTime ReadSysTime(EvtVariant variant)
    {
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
    }
}
