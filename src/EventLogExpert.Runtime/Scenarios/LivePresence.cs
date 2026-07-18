// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Scenarios;

/// <summary>
///     A snapshot of the host's channel set. <see cref="Known" /> is false when the channel set could not be read, in
///     which case callers should treat every scenario as launchable rather than falsely reporting it offline.
/// </summary>
public sealed record LivePresence(bool Known, IReadOnlySet<string> Present);
