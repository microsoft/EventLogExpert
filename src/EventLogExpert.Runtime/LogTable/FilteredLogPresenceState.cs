// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using Fluxor;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.LogTable;

public enum FilteredLogPresence
{
    Pending,
    HasSurvivor,
    NoSurvivor
}

[FeatureState]
public sealed record FilteredLogPresenceState
{
    public ImmutableDictionary<EventLogId, FilteredLogPresence> ByLog { get; init; } =
        ImmutableDictionary<EventLogId, FilteredLogPresence>.Empty;

    internal long FilterVersion { get; init; }

    public bool IsKnownEmpty(EventLogId logId) =>
        ByLog.TryGetValue(logId, out var presence) && presence == FilteredLogPresence.NoSurvivor;
}
