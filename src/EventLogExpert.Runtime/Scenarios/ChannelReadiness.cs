// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Readers;

namespace EventLogExpert.Runtime.Scenarios;

public sealed record ChannelReadiness(string Channel, ChannelPresence Presence, ChannelEnablement Enablement)
{
    public ChannelAccess Access { get; init; } = ChannelAccess.Unknown;
}
