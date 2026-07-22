// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Eventing.Writers;

public sealed record ChannelEnableResult(ChannelEnableOutcome Outcome, int Win32Error);
