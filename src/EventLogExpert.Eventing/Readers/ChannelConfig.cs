// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Interop;

namespace EventLogExpert.Eventing.Readers;

public sealed record ChannelConfig(bool? Enabled, ChannelAccess Access, EvtChannelType? Type)
{
    public static ChannelConfig Unknown { get; } = new(null, ChannelAccess.Unknown, null);
}
