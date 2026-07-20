// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Interop;
using EventLogExpert.Logging.Abstractions;

namespace EventLogExpert.Eventing.Readers;

public sealed class EventLogChannelConfigReader : IChannelConfigReader, IDisposable
{
    private readonly IChannelAccessEvaluator _accessEvaluator;
    private readonly IChannelAccessNative? _ownedAccessNative;
    private readonly IChannelConfigPropertyReader _propertyReader;

    public EventLogChannelConfigReader(ITraceLogger? logger = null)
    {
        _ownedAccessNative = new Win32ChannelAccessNative();
        _accessEvaluator = new NativeChannelAccessEvaluator(_ownedAccessNative);
        _propertyReader = new EventLogChannelConfigPropertyReader(logger);
    }

    internal EventLogChannelConfigReader(
        IChannelConfigPropertyReader propertyReader,
        IChannelAccessEvaluator accessEvaluator)
    {
        _propertyReader = propertyReader;
        _accessEvaluator = accessEvaluator;
    }

    public void Dispose() => _ownedAccessNative?.Dispose();

    public ChannelConfig ReadConfig(string channelName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channelName);

        var properties = _propertyReader.ReadProperties(channelName);

        if (properties.Type is not { } type)
        {
            return new ChannelConfig(properties.Enabled, ChannelAccess.Unknown, null);
        }

        if (type is EvtChannelType.Analytic or EvtChannelType.Debug)
        {
            return new ChannelConfig(properties.Enabled, ChannelAccess.NotEvaluated, type);
        }

        var access = _accessEvaluator.EvaluateAccess(
            properties.AccessSddl,
            string.Equals(channelName, LogChannelNames.SecurityLog, StringComparison.OrdinalIgnoreCase));

        return new ChannelConfig(properties.Enabled, access, type);
    }
}
