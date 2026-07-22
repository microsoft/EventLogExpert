// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Interop;
using EventLogExpert.Eventing.Readers;
using EventLogExpert.Logging.Abstractions;
using System.Runtime.InteropServices;

namespace EventLogExpert.Eventing.Writers;

public sealed class ChannelConfigWriter(ITraceLogger? logger = null) : IChannelConfigWriter
{
    private readonly ITraceLogger? _logger = logger;

    public ChannelEnableResult EnableChannel(string channelName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channelName);

        using var channelConfig = NativeMethods.EvtOpenChannelConfig(
            EventLogSession.GlobalSession.Handle,
            channelName,
            0);

        if (channelConfig.IsInvalid)
        {
            return FromNativeError(Marshal.GetLastWin32Error(), channelName, nameof(NativeMethods.EvtOpenChannelConfig));
        }

        // Read-first, fail-closed: a failed or non-Boolean read must never be treated as "disabled" (that would drive a
        // machine write on an unreadable state). Only a successful Boolean read that is already true short-circuits.
        if (!TryReadEnabled(channelConfig, channelName, out bool alreadyEnabled, out ChannelEnableResult readFailure))
        {
            return readFailure;
        }

        if (alreadyEnabled)
        {
            return new ChannelEnableResult(ChannelEnableOutcome.AlreadyEnabled, Win32ErrorCodes.ERROR_SUCCESS);
        }

        var enabledValue = new EvtVariant(true);

        if (!NativeMethods.EvtSetChannelConfigProperty(
                channelConfig,
                EvtChannelConfigPropertyId.EvtChannelConfigEnabled,
                0,
                in enabledValue))
        {
            return FromNativeError(
                Marshal.GetLastWin32Error(),
                channelName,
                nameof(NativeMethods.EvtSetChannelConfigProperty));
        }

        return !NativeMethods.EvtSaveChannelConfig(channelConfig, 0) ?
            FromNativeError(Marshal.GetLastWin32Error(),
                channelName,
                nameof(NativeMethods.EvtSaveChannelConfig)) :
            // EvtSaveChannelConfig returning true is the persistence guarantee, so the change is committed here.
            new ChannelEnableResult(ChannelEnableOutcome.Enabled, Win32ErrorCodes.ERROR_SUCCESS);
    }

    private ChannelEnableResult FromNativeError(int win32Error, string channelName, string stage)
    {
        _logger?.Debug($"{nameof(EnableChannel)}: {stage} failed for {channelName}. Error: {win32Error}");

        var outcome = win32Error switch
        {
            Win32ErrorCodes.ERROR_ACCESS_DENIED => ChannelEnableOutcome.AccessDenied,
            Win32ErrorCodes.ERROR_EVT_CHANNEL_NOT_FOUND => ChannelEnableOutcome.NotFound,
            _ => ChannelEnableOutcome.Failed
        };

        return new ChannelEnableResult(outcome, win32Error);
    }

    private unsafe bool TryReadEnabled(
        EvtHandle channelConfig,
        string channelName,
        out bool enabled,
        out ChannelEnableResult failure)
    {
        enabled = false;
        failure = null!;
        IntPtr buffer = IntPtr.Zero;

        try
        {
            bool success = NativeMethods.EvtGetChannelConfigProperty(
                channelConfig,
                EvtChannelConfigPropertyId.EvtChannelConfigEnabled,
                0,
                0,
                IntPtr.Zero,
                out int bufferSize);

            int sizeError = Marshal.GetLastWin32Error();

            if (!success && sizeError != Win32ErrorCodes.ERROR_INSUFFICIENT_BUFFER)
            {
                failure = FromNativeError(sizeError, channelName, nameof(NativeMethods.EvtGetChannelConfigProperty));

                return false;
            }

            buffer = Marshal.AllocHGlobal(bufferSize);

            if (!NativeMethods.EvtGetChannelConfigProperty(
                    channelConfig,
                    EvtChannelConfigPropertyId.EvtChannelConfigEnabled,
                    0,
                    bufferSize,
                    buffer,
                    out _))
            {
                failure = FromNativeError(
                    Marshal.GetLastWin32Error(),
                    channelName,
                    nameof(NativeMethods.EvtGetChannelConfigProperty));

                return false;
            }

            var variant = *(EvtVariant*)buffer;

            if (variant.Type != (uint)EvtVariantType.Boolean)
            {
                // The read succeeded but the value is not Boolean, so the last Win32 error is not meaningful; surface a
                // non-zero sentinel (ERROR_INVALID_DATA) rather than 0 so downstream diagnostics stay actionable.
                _logger?.Debug(
                    $"{nameof(EnableChannel)}: {channelName} enabled property was not Boolean (type {variant.Type}).");

                failure = new ChannelEnableResult(ChannelEnableOutcome.Failed, Win32ErrorCodes.ERROR_INVALID_DATA);

                return false;
            }

            enabled = variant.BooleanVal != 0;
            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException
                                       and not StackOverflowException
                                       and not AccessViolationException)
        {
            _logger?.Debug($"{nameof(EnableChannel)}: unexpected failure reading enabled state for {channelName}.\n{ex}");
            failure = new ChannelEnableResult(ChannelEnableOutcome.Failed, Win32ErrorCodes.ERROR_INVALID_DATA);

            return false;
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
