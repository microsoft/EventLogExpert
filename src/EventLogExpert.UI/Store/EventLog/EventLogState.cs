// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Models;
using EventLogExpert.UI.Models;
using Fluxor;
using System.Collections.Immutable;
using System.Collections.ObjectModel;

namespace EventLogExpert.UI.Store.EventLog;

[FeatureState]
public sealed record EventLogState
{
    /// <summary>The maximum number of new events we will hold in the state before we turn off the watcher.</summary>
    public int MaxNewEvents { get; } = 1000;

    public ImmutableDictionary<string, EventLogData> ActiveLogs { get; init; } =
        ImmutableDictionary<string, EventLogData>.Empty;

    public EventFilter AppliedFilter { get; init; } = new(null, [], [], []);

    public bool ContinuouslyUpdate { get; init; } = false;

    public ImmutableDictionary<Guid, int> EventsLoading { get; init; } = ImmutableDictionary<Guid, int>.Empty;

    public ReadOnlyCollection<DisplayEventModel> NewEventBuffer { get; init; } =
        new List<DisplayEventModel>().AsReadOnly();

    public bool NewEventBufferIsFull { get; init; }

    public DisplayEventModel? SelectedEvent { get; init; }
}
