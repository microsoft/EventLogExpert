// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Collections.Frozen;

namespace EventLogExpert.Runtime.Scenarios;

/// <summary>
///     A snapshot of the host's channel set. <see cref="Known" /> is false when the channel set could not be read, in
///     which case callers should treat every scenario as launchable rather than falsely reporting it offline.
/// </summary>
public sealed record LivePresence(bool Known, IReadOnlySet<string> Present)
{
    /// <summary>
    ///     Derives presence from an already-fetched readiness snapshot so callers that need both readiness and presence
    ///     don't probe the host twice. <see cref="Known" /> is false when any channel reads back as
    ///     <see cref="ChannelPresence.Unknown" /> (a global enumeration failure), matching the treat-everything-launchable
    ///     fallback.
    /// </summary>
    public static LivePresence FromReadiness(IReadOnlyCollection<ChannelReadiness> readiness) =>
        readiness.Any(channel => channel.Presence == ChannelPresence.Unknown)
            ? new LivePresence(false, FrozenSet<string>.Empty)
            : new LivePresence(
                true,
                readiness
                    .Where(channel => channel.Presence == ChannelPresence.Present)
                    .Select(channel => channel.Channel)
                    .ToFrozenSet(StringComparer.OrdinalIgnoreCase));
}
