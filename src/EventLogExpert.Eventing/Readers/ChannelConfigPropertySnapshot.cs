// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Interop;

namespace EventLogExpert.Eventing.Readers;

internal sealed record ChannelConfigPropertySnapshot(bool? Enabled, string? AccessSddl, EvtChannelType? Type);
