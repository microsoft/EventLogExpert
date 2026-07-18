// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Scenarios;

internal readonly record struct EvtxChannelReadResult(string? Channel, bool Failed)
{
    /// <summary>A log that opened but had no records, so no channel could be read; not a failure.</summary>
    public static EvtxChannelReadResult Empty { get; } = new(null, false);

    /// <summary>A file that could not be opened or read.</summary>
    public static EvtxChannelReadResult Unreadable { get; } = new(null, true);

    public static EvtxChannelReadResult FromChannel(string channel) => new(channel, false);
}
