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
///     <see cref="SafeHandle" /> that cleans itself up via its own finalizer. Instances are cached in
///     <see cref="EventResolverBase" /> and are intended to be long-lived.
/// </remarks>
internal sealed class ProviderMetadata
{
    private readonly Lock _providerLock = new();
    private readonly EvtHandle _publisherMetadataHandle;

    private ReadOnlyDictionary<uint, string>? _channels;
    private ReadOnlyDictionary<long, string>? _keywords;
    private ReadOnlyDictionary<int, string>? _opcodes;
    private ReadOnlyDictionary<int, string>? _tasks;

    private ProviderMetadata(string providerName, string? metadataPath = null)
    {
        _publisherMetadataHandle = NativeMethods.EvtOpenPublisherMetadata(EventLogSession.GlobalSession.Handle, providerName, metadataPath, 0, 0);
        int error = Marshal.GetLastWin32Error();

        if (_publisherMetadataHandle.IsInvalid)
        {
            Error = NativeErrorResolver.GetErrorMessage((uint)HResultConverter.HResultFromWin32(error));
        }
    }

    public IDictionary<uint, string> Channels
    {
        get
        {
            if (_channels is not null) { return _channels; }

            _providerLock.Enter();

            try
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

                _channels = channels.AsReadOnly();

                return _channels;
            }
            finally
            {
                _providerLock.Exit();
            }
        }
    }

    public IEnumerable<EventMetadata> Events
    {
        get
        {
            List<EventMetadata> events = [];

            using EvtHandle handle = NativeMethods.EvtOpenEventMetadataEnum(_publisherMetadataHandle, 0);
            int error = Marshal.GetLastWin32Error();

            if (handle.IsInvalid)
            {
                Error = NativeErrorResolver.GetErrorMessage((uint)HResultConverter.HResultFromWin32(error));

                return events.AsReadOnly();
            }

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
                long keywords = (long)(ulong)GetEventMetadataProperty(metadataHandle, EvtEventMetadataPropertyId.Keyword);
                string template = (string)GetEventMetadataProperty(metadataHandle, EvtEventMetadataPropertyId.Template);
                int messageId = (int)(uint)GetEventMetadataProperty(metadataHandle, EvtEventMetadataPropertyId.MessageID);

                string message = messageId == -1 ?
                    string.Empty :
                    NativeMethods.FormatMessage(_publisherMetadataHandle, (uint)messageId);

                events.Add(new EventMetadata(id, version, channelId, level, opcode, task, keywords, template, message, this));
            }

            return events.AsReadOnly();
        }
    }

    public IDictionary<long, string> Keywords
    {
        get
        {
            if (_keywords is not null) { return _keywords; }

            _providerLock.Enter();

            try
            {
                using EvtHandle channelRefHandle =
                    GetPublisherMetadataPropertyHandle(EvtPublisherMetadataPropertyId.Keywords);

                int size = NativeMethods.GetObjectArraySize(channelRefHandle);

                Dictionary<long, string> keywords = new(size);

                for (int i = 0; i < size; i++)
                {
                    string name = (string)NativeMethods.GetObjectArrayProperty(
                        channelRefHandle,
                        i,
                        EvtPublisherMetadataPropertyId.KeywordName);

                    long value = (long)(ulong)NativeMethods.GetObjectArrayProperty(
                        channelRefHandle,
                        i,
                        EvtPublisherMetadataPropertyId.KeywordValue);

                    int messageId = (int)(uint)NativeMethods.GetObjectArrayProperty(
                        channelRefHandle,
                        i,
                        EvtPublisherMetadataPropertyId.KeywordMessageID);

                    string displayName = messageId == -1 ?
                        name :
                        NativeMethods.FormatMessage(_publisherMetadataHandle, (uint)messageId);

                    keywords.TryAdd(value, displayName);
                }

                _keywords = keywords.AsReadOnly();

                return _keywords;
            }
            finally
            {
                _providerLock.Exit();
            }
        }
    }

    public string MessageFilePath => Environment.ExpandEnvironmentVariables(GetPublisherMetadataProperty(EvtPublisherMetadataPropertyId.MessageFilePath));

    public IDictionary<int, string> Opcodes
    {
        get
        {
            if (_opcodes is not null) { return _opcodes; }

            _providerLock.Enter();

            try
            {
                using EvtHandle channelRefHandle =
                    GetPublisherMetadataPropertyHandle(EvtPublisherMetadataPropertyId.Opcodes);

                int size = NativeMethods.GetObjectArraySize(channelRefHandle);

                Dictionary<int, string> opcodes = new(size);

                for (int i = 0; i < size; i++)
                {
                    string name = (string)NativeMethods.GetObjectArrayProperty(
                        channelRefHandle,
                        i,
                        EvtPublisherMetadataPropertyId.OpcodeName);

                    uint value = (uint)NativeMethods.GetObjectArrayProperty(
                        channelRefHandle,
                        i,
                        EvtPublisherMetadataPropertyId.OpcodeValue);

                    int messageId = (int)(uint)NativeMethods.GetObjectArrayProperty(
                        channelRefHandle,
                        i,
                        EvtPublisherMetadataPropertyId.OpcodeMessageID);

                    string displayName = messageId == -1 ?
                        name :
                        NativeMethods.FormatMessage(_publisherMetadataHandle, (uint)messageId);

                    opcodes.TryAdd((int)(value >> 16), displayName);
                }

                _opcodes = opcodes.AsReadOnly();

                return _opcodes;
            }
            finally
            {
                _providerLock.Exit();
            }
        }
    }

    public string ParameterFilePath => Environment.ExpandEnvironmentVariables(GetPublisherMetadataProperty(EvtPublisherMetadataPropertyId.ParameterFilePath));

    public IDictionary<int, string> Tasks
    {
        get
        {
            if (_tasks is not null) { return _tasks; }

            _providerLock.Enter();

            try
            {
                using EvtHandle channelRefHandle =
                    GetPublisherMetadataPropertyHandle(EvtPublisherMetadataPropertyId.Tasks);

                int size = NativeMethods.GetObjectArraySize(channelRefHandle);

                Dictionary<int, string> tasks = new(size);

                for (int i = 0; i < size; i++)
                {
                    string name = (string)NativeMethods.GetObjectArrayProperty(
                        channelRefHandle,
                        i,
                        EvtPublisherMetadataPropertyId.TaskName);

                    int value = (int)(uint)NativeMethods.GetObjectArrayProperty(
                        channelRefHandle,
                        i,
                        EvtPublisherMetadataPropertyId.TaskValue);

                    int messageId = (int)(uint)NativeMethods.GetObjectArrayProperty(
                        channelRefHandle,
                        i,
                        EvtPublisherMetadataPropertyId.TaskMessageID);

                    string displayName = messageId == -1 ?
                        name :
                        NativeMethods.FormatMessage(_publisherMetadataHandle, (uint)messageId);

                    tasks.TryAdd(value, displayName);
                }

                _tasks = tasks.AsReadOnly();

                return _tasks;
            }
            finally
            {
                _providerLock.Exit();
            }
        }
    }

    internal string? Error { get; private set; }

    internal bool IsLocaleMetadata { get; private init; }

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
}
