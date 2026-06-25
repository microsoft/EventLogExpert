// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Interop;
using EventLogExpert.Eventing.Readers;
using EventLogExpert.Logging.Abstractions;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;

namespace EventLogExpert.Eventing.PublisherMetadata;

/// <summary>Provides metadata about an event log provider.</summary>
/// <remarks>
///     This class does not implement <see cref="IDisposable" />. The underlying <see cref="EvtHandle" /> is a
///     <see cref="SafeHandle" /> that cleans itself up via its own finalizer. Instances are short-lived: each one is
///     created for a single provider load, consumed once through <see cref="ToRawContent" />, and then discarded.
/// </remarks>
internal sealed class ProviderMetadata
{
    private readonly EvtHandle _publisherMetadataHandle;

    private ProviderMetadata(string providerName, string? metadataPath = null)
    {
        _publisherMetadataHandle = NativeMethods.EvtOpenPublisherMetadata(EventLogSession.GlobalSession.Handle, providerName, metadataPath, 0, 0);
        int error = Marshal.GetLastWin32Error();

        if (_publisherMetadataHandle.IsInvalid)
        {
            Error = NativeErrorResolver.GetErrorMessage((uint)HResultConverter.HResultFromWin32(error));
        }
    }

    public string MessageFilePath => Environment.ExpandEnvironmentVariables(GetPublisherMetadataProperty(EvtPublisherMetadataPropertyId.MessageFilePath));

    public string ParameterFilePath => Environment.ExpandEnvironmentVariables(GetPublisherMetadataProperty(EvtPublisherMetadataPropertyId.ParameterFilePath));

    internal string? Error { get; private set; }

    internal bool IsLocaleMetadata { get; private init; }

    internal Guid PublisherGuid
    {
        get
        {
            try
            {
                return GetPublisherMetadataObject(EvtPublisherMetadataPropertyId.PublisherGuid) as Guid? ?? Guid.Empty;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException
                                           and not StackOverflowException
                                           and not AccessViolationException)
            {
                return Guid.Empty;
            }
        }
    }

    internal string ResourceFilePath
    {
        get
        {
            try
            {
                return Environment.ExpandEnvironmentVariables(
                    GetPublisherMetadataObject(EvtPublisherMetadataPropertyId.ResourceFilePath) as string ?? string.Empty);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException
                                           and not StackOverflowException
                                           and not AccessViolationException)
            {
                return string.Empty;
            }
        }
    }

    internal static ProviderMetadata? Create(
        string providerName,
        IReadOnlyList<string>? metadataPath = null,
        ITraceLogger? logger = null)
    {
        if (metadataPath is { Count: > 0 })
        {
            foreach (var path in metadataPath)
            {
                ProviderMetadata localeMetadata = new(providerName, path) { IsLocaleMetadata = true };

                if (localeMetadata.Error is not null)
                {
                    localeMetadata._publisherMetadataHandle.Dispose();

                    continue;
                }

                logger?.Debug($"Resolved {providerName} from locale metadata: {path}");

                return localeMetadata;
            }

            logger?.Debug($"Locale metadata did not contain {providerName}.");

            return null;
        }

        ProviderMetadata metadata = new(providerName);

        if (metadata.Error is null)
        {
            return metadata;
        }

        metadata._publisherMetadataHandle.Dispose();
        logger?.Debug($"Failed to create metadata for {providerName} provider: {metadata.Error}");

        return null;
    }

    internal string FormatMessageById(uint messageId) =>
        NativeMethods.FormatMessage(_publisherMetadataHandle, messageId);

    internal RawProviderContent ToRawContent(string providerName, ITraceLogger? logger)
    {
        List<RawNamedValue> keywords;

        try
        {
            keywords = ReadNamedValues(
                EvtPublisherMetadataPropertyId.Keywords,
                EvtPublisherMetadataPropertyId.KeywordName,
                EvtPublisherMetadataPropertyId.KeywordValue,
                EvtPublisherMetadataPropertyId.KeywordMessageID,
                static value => (ulong)value);
        }
        catch (Exception ex)
        {
            logger?.Debug($"Failed to read Keywords for provider {providerName}. Exception:\n{ex}");
            keywords = [];
        }

        List<RawNamedValue> opcodes;

        try
        {
            opcodes = ReadNamedValues(
                EvtPublisherMetadataPropertyId.Opcodes,
                EvtPublisherMetadataPropertyId.OpcodeName,
                EvtPublisherMetadataPropertyId.OpcodeValue,
                EvtPublisherMetadataPropertyId.OpcodeMessageID,
                static value => (uint)value);
        }
        catch (Exception ex)
        {
            logger?.Debug($"Failed to read Opcodes for provider {providerName}. Exception:\n{ex}");
            opcodes = [];
        }

        List<RawNamedValue> tasks;

        try
        {
            tasks = ReadNamedValues(
                EvtPublisherMetadataPropertyId.Tasks,
                EvtPublisherMetadataPropertyId.TaskName,
                EvtPublisherMetadataPropertyId.TaskValue,
                EvtPublisherMetadataPropertyId.TaskMessageID,
                static value => (uint)value);
        }
        catch (Exception ex)
        {
            logger?.Debug($"Failed to read Tasks for provider {providerName}. Exception:\n{ex}");
            tasks = [];
        }

        IReadOnlyDictionary<uint, string> channels;
        IReadOnlyList<RawProviderEvent> events;

        try
        {
            channels = ReadChannelsRaw();
            events = ReadEventsRaw();
        }
        catch (Exception ex)
        {
            logger?.Debug($"Failed to read Events for provider {providerName}. Exception:\n{ex}");
            channels = ReadOnlyDictionary<uint, string>.Empty;
            events = [];
        }

        return new RawProviderContent
        {
            ProviderName = providerName,
            PublisherGuid = PublisherGuid,
            ResourceFilePath = ResourceFilePath,
            ResolveMessage = FormatMessageById,
            Channels = channels,
            Events = events,
            Keywords = keywords,
            Opcodes = opcodes,
            Tasks = tasks
        };
    }

    private static object GetEventMetadataProperty(EvtHandle metadataHandle, EvtEventMetadataPropertyId propertyId)
    {
        IntPtr buffer = IntPtr.Zero;

        try
        {
            bool success = NativeMethods.EvtGetEventMetadataProperty(metadataHandle, propertyId, 0, 0, IntPtr.Zero, out int bufferSize);
            int error = Marshal.GetLastWin32Error();

            if (!success && error != Win32ErrorCodes.ERROR_INSUFFICIENT_BUFFER)
            {
                NativeMethods.ThrowEventLogException(error);
            }

            buffer = Marshal.AllocHGlobal(bufferSize);

            success = NativeMethods.EvtGetEventMetadataProperty(metadataHandle, propertyId, 0, bufferSize, buffer, out bufferSize);
            error = Marshal.GetLastWin32Error();

            if (!success)
            {
                NativeMethods.ThrowEventLogException(error);
            }

            var variant = Marshal.PtrToStructure<EvtVariant>(buffer);

            return NativeMethods.ConvertVariant(variant) ??
                throw new InvalidDataException($"Invalid Metadata for PropertyId: {propertyId}");
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static EvtHandle? NextEventMetadata(EvtHandle metadataHandle, int flags)
    {
        EvtHandle handle = NativeMethods.EvtNextEventMetadata(metadataHandle, flags);
        int error = Marshal.GetLastWin32Error();

        if (!handle.IsInvalid) { return handle; }

        if (error != Win32ErrorCodes.ERROR_NO_MORE_ITEMS)
        {
            NativeMethods.ThrowEventLogException(error);
        }

        return null;
    }

    private object? GetPublisherMetadataObject(EvtPublisherMetadataPropertyId propertyId)
    {
        IntPtr buffer = IntPtr.Zero;

        try
        {
            bool success = NativeMethods.EvtGetPublisherMetadataProperty(
                _publisherMetadataHandle,
                propertyId,
                0,
                0,
                IntPtr.Zero,
                out int bufferUsed);

            int error = Marshal.GetLastWin32Error();

            if (!success && error != Win32ErrorCodes.ERROR_INSUFFICIENT_BUFFER)
            {
                NativeMethods.ThrowEventLogException(error);
            }

            buffer = Marshal.AllocHGlobal(bufferUsed);

            success = NativeMethods.EvtGetPublisherMetadataProperty(
                _publisherMetadataHandle,
                propertyId,
                0,
                bufferUsed,
                buffer,
                out bufferUsed);

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

    private string GetPublisherMetadataProperty(EvtPublisherMetadataPropertyId propertyId)
    {
        IntPtr buffer = IntPtr.Zero;

        try
        {
            bool success = NativeMethods.EvtGetPublisherMetadataProperty(
                _publisherMetadataHandle,
                propertyId,
                0,
                0,
                IntPtr.Zero,
                out int bufferUsed);

            int error = Marshal.GetLastWin32Error();

            if (!success && error != Win32ErrorCodes.ERROR_INSUFFICIENT_BUFFER)
            {
                NativeMethods.ThrowEventLogException(error);
            }

            buffer = Marshal.AllocHGlobal(bufferUsed);

            success = NativeMethods.EvtGetPublisherMetadataProperty(
                _publisherMetadataHandle,
                propertyId,
                0,
                bufferUsed,
                buffer,
                out bufferUsed);

            error = Marshal.GetLastWin32Error();

            if (!success)
            {
                NativeMethods.ThrowEventLogException(error);
            }

            var variant = Marshal.PtrToStructure<EvtVariant>(buffer);

            return (string?)NativeMethods.ConvertVariant(variant) ?? string.Empty;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private EvtHandle GetPublisherMetadataPropertyHandle(EvtPublisherMetadataPropertyId propertyId)
    {
        IntPtr buffer = IntPtr.Zero;

        try
        {
            bool success = NativeMethods.EvtGetPublisherMetadataProperty(
                _publisherMetadataHandle,
                propertyId,
                0,
                0,
                IntPtr.Zero,
                out int bufferUsed);

            int error = Marshal.GetLastWin32Error();

            if (!success && error != Win32ErrorCodes.ERROR_INSUFFICIENT_BUFFER)
            {
                NativeMethods.ThrowEventLogException(error);
            }

            buffer = Marshal.AllocHGlobal(bufferUsed);

            success = NativeMethods.EvtGetPublisherMetadataProperty(
                _publisherMetadataHandle,
                propertyId,
                0,
                bufferUsed,
                buffer,
                out bufferUsed);

            error = Marshal.GetLastWin32Error();

            if (!success)
            {
                NativeMethods.ThrowEventLogException(error);
            }

            var variant = Marshal.PtrToStructure<EvtVariant>(buffer);

            return variant.EvtHandleVal == IntPtr.Zero ?
                EvtHandle.Zero :
                new EvtHandle(variant.EvtHandleVal);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private Dictionary<uint, string> ReadChannelsRaw()
    {
        using EvtHandle channelRefHandle =
            GetPublisherMetadataPropertyHandle(EvtPublisherMetadataPropertyId.ChannelReferences);

        int size = NativeMethods.GetObjectArraySize(channelRefHandle);

        Dictionary<uint, string> channels = new(size);

        for (int i = 0; i < size; i++)
        {
            uint channelId = (uint)NativeMethods.GetObjectArrayProperty(
                channelRefHandle,
                i,
                EvtPublisherMetadataPropertyId.ChannelReferenceID);

            string channelName = (string)NativeMethods.GetObjectArrayProperty(
                channelRefHandle,
                i,
                EvtPublisherMetadataPropertyId.ChannelReferencePath);

            channels.TryAdd(channelId, channelName);
        }

        return channels;
    }

    private List<RawProviderEvent> ReadEventsRaw()
    {
        List<RawProviderEvent> events = [];

        using EvtHandle handle = NativeMethods.EvtOpenEventMetadataEnum(_publisherMetadataHandle, 0);

        if (handle.IsInvalid) { return events; }

        while (true)
        {
            using EvtHandle? metadataHandle = NextEventMetadata(handle, 0);

            if (metadataHandle is null) { break; }

            uint id = (uint)GetEventMetadataProperty(metadataHandle, EvtEventMetadataPropertyId.ID);
            byte version = (byte)(uint)GetEventMetadataProperty(metadataHandle, EvtEventMetadataPropertyId.Version);
            byte channelId = (byte)(uint)GetEventMetadataProperty(metadataHandle, EvtEventMetadataPropertyId.Channel);
            byte level = (byte)(uint)GetEventMetadataProperty(metadataHandle, EvtEventMetadataPropertyId.Level);
            byte opcode = (byte)(uint)GetEventMetadataProperty(metadataHandle, EvtEventMetadataPropertyId.Opcode);
            short task = (short)(uint)GetEventMetadataProperty(metadataHandle, EvtEventMetadataPropertyId.Task);
            ulong keywords = (ulong)GetEventMetadataProperty(metadataHandle, EvtEventMetadataPropertyId.Keyword);
            string template = (string)GetEventMetadataProperty(metadataHandle, EvtEventMetadataPropertyId.Template);
            uint messageId = (uint)GetEventMetadataProperty(metadataHandle, EvtEventMetadataPropertyId.MessageID);

            events.Add(new RawProviderEvent(id, version, channelId, level, opcode, task, keywords, template, messageId));
        }

        return events;
    }

    private List<RawNamedValue> ReadNamedValues(
        EvtPublisherMetadataPropertyId tableId,
        EvtPublisherMetadataPropertyId nameId,
        EvtPublisherMetadataPropertyId valueId,
        EvtPublisherMetadataPropertyId messageIdId,
        Func<object, ulong> unboxValue)
    {
        using EvtHandle handle = GetPublisherMetadataPropertyHandle(tableId);

        int size = NativeMethods.GetObjectArraySize(handle);

        List<RawNamedValue> entries = new(size);

        for (int i = 0; i < size; i++)
        {
            string name = (string)NativeMethods.GetObjectArrayProperty(handle, i, nameId);
            ulong value = unboxValue(NativeMethods.GetObjectArrayProperty(handle, i, valueId));
            uint messageId = (uint)NativeMethods.GetObjectArrayProperty(handle, i, messageIdId);

            entries.Add(new RawNamedValue(value, messageId, name));
        }

        return entries;
    }
}
