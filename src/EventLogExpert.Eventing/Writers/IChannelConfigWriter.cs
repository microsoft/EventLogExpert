// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Eventing.Writers;

public interface IChannelConfigWriter
{
    /// <summary>
    ///     Enables a disabled event log channel by persisting <c>EvtChannelConfigEnabled = true</c>. Requires an elevated
    ///     process; without elevation a native call fails and the result is <see cref="ChannelEnableOutcome.AccessDenied" />.
    /// </summary>
    ChannelEnableResult EnableChannel(string channelName);
}
