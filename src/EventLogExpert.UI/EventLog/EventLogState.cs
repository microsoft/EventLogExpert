// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Filtering.Runtime;
using Fluxor;
using System.Collections.Immutable;

namespace EventLogExpert.UI.EventLog;

[FeatureState]
public sealed record EventLogState
{
    /// <summary>The maximum number of new events we will hold in the state before we turn off the watcher.</summary>
    public static int MaxNewEvents => 1000;

    public ImmutableDictionary<string, EventLogData> ActiveLogs { get; init; } =
        [];

    public Filter AppliedFilter { get; init; } = new(null, []);

    public bool ContinuouslyUpdate { get; init; } = false;

    public IReadOnlyList<ResolvedEvent> NewEventBuffer { get; init; } = [];

    public bool NewEventBufferIsFull { get; init; }

    public ResolvedEvent? SelectedEvent { get; init; }

    public ImmutableList<ResolvedEvent> SelectedEvents { get; init; } = [];
}
