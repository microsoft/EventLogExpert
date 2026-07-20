// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Interop;
using EventLogExpert.Logging.Abstractions;
using System.Runtime.InteropServices;

namespace EventLogExpert.Eventing.Readers;

internal sealed class EventLogChannelConfigPropertyReader(ITraceLogger? logger = null) : IChannelConfigPropertyReader
{
    private readonly ITraceLogger? _logger = logger;

    public ChannelConfigPropertySnapshot ReadProperties(string channelName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channelName);

        using var channelConfig = NativeMethods.EvtOpenChannelConfig(
            EventLogSession.GlobalSession.Handle,
            channelName,
            0);

        if (channelConfig.IsInvalid)
        {
            int openError = Marshal.GetLastWin32Error();

            _logger?.Debug(
                $"{nameof(ReadProperties)}: EvtOpenChannelConfig failed for {channelName}. Error: {openError} ({NativeMethods.FormatSystemMessage((uint)openError) ?? "unknown"})");

            return new ChannelConfigPropertySnapshot(null, null, null);
        }

        var type = ReadChannelType(channelConfig, channelName);
        var enabled = ReadEnabled(channelConfig, channelName);
        var accessSddl = type is EvtChannelType.Analytic or EvtChannelType.Debug
            ? null
            : ReadAccessSddl(channelConfig, channelName);

        return new ChannelConfigPropertySnapshot(enabled, accessSddl, type);
    }

    // ConvertVariant boxes the channel type as uint; Enum.IsDefined(typeof(...), boxedUint) throws on this int-backed enum, so match the enum value via the generic overload.
    internal static EvtChannelType? ConvertChannelType(object? value) =>
        value switch
        {
            uint raw when Enum.IsDefined((EvtChannelType)raw) => (EvtChannelType)raw,
            int raw when Enum.IsDefined((EvtChannelType)raw) => (EvtChannelType)raw,
            _ => null
        };

    private string? ReadAccessSddl(EvtHandle channelConfig, string channelName) =>
        ReadProperty(channelConfig, EvtChannelConfigPropertyId.EvtChannelConfigAccess, channelName) as string;

    private EvtChannelType? ReadChannelType(EvtHandle channelConfig, string channelName) =>
        ConvertChannelType(ReadProperty(channelConfig, EvtChannelConfigPropertyId.EvtChannelConfigType, channelName));

    private bool? ReadEnabled(EvtHandle channelConfig, string channelName) =>
        ReadProperty(channelConfig, EvtChannelConfigPropertyId.EvtChannelConfigEnabled, channelName) as bool?;

    private unsafe object? ReadProperty(
        EvtHandle channelConfig,
        EvtChannelConfigPropertyId propertyId,
        string channelName)
    {
        IntPtr buffer = IntPtr.Zero;

        try
        {
            bool success = NativeMethods.EvtGetChannelConfigProperty(
                channelConfig,
                propertyId,
                0,
                0,
                IntPtr.Zero,
                out int bufferSize);

            int sizeError = Marshal.GetLastWin32Error();

            if (!success && sizeError != Win32ErrorCodes.ERROR_INSUFFICIENT_BUFFER)
            {
                _logger?.Debug(
                    $"{nameof(ReadProperty)}: size probe failed for {channelName} {propertyId}. Error: {sizeError}");

                return null;
            }

            buffer = Marshal.AllocHGlobal(bufferSize);

            success = NativeMethods.EvtGetChannelConfigProperty(
                channelConfig,
                propertyId,
                0,
                bufferSize,
                buffer,
                out _);

            if (!success)
            {
                int readError = Marshal.GetLastWin32Error();

                _logger?.Debug(
                    $"{nameof(ReadProperty)}: read failed for {channelName} {propertyId}. Error: {readError}");

                return null;
            }

            var variant = *(EvtVariant*)buffer;
            return NativeMethods.ConvertVariant(variant);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException
                                       and not StackOverflowException
                                       and not AccessViolationException)
        {
            _logger?.Debug(
                $"{nameof(ReadProperty)}: unexpected failure for {channelName} {propertyId}.\n{ex}");

            return null;
        }
        finally
        {
            if (buffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
    }
}
