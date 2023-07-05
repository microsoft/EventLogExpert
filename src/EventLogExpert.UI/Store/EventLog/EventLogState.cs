// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Models;
using EventLogExpert.UI.Models;
using Fluxor;
using System.Collections.Immutable;
using System.Collections.ObjectModel;

namespace EventLogExpert.UI.Store.EventLog;

[FeatureState]
public record EventLogState
{
    public record EventBuffer(ReadOnlyCollection<DisplayEventModel> Events, bool IsBufferFull);

    public record EventFilter(
        string? AdvancedFilter,
        FilterDateModel? DateFilter,
        ImmutableList<FilterCacheModel> CachedFilters,
        ImmutableList<ImmutableList<Func<DisplayEventModel, bool>>> Filters);

    public enum LogType { Live, File }

    public record EventLogData(
        string Name,
        LogType Type,
        ReadOnlyCollection<DisplayEventModel> Events,
        ReadOnlyCollection<DisplayEventModel> FilteredEvents,
        ImmutableHashSet<int> EventIds,
        ImmutableHashSet<string> EventProviderNames,
        ImmutableHashSet<string> TaskNames,
        ImmutableHashSet<string> KeywordNames
    );

    public ImmutableDictionary<string, EventLogData> ActiveLogs { get; init; } =
        ImmutableDictionary<string, EventLogData>.Empty;

    public EventFilter AppliedFilter { get; init; } = new(
        string.Empty,
        null,
        ImmutableList<FilterCacheModel>.Empty,
        ImmutableList<ImmutableList<Func<DisplayEventModel, bool>>>.Empty);

    public bool ContinuouslyUpdate { get; init; } = false;

    public ReadOnlyCollection<DisplayEventModel> CombinedEvents { get; init; } =
        new List<DisplayEventModel>().AsReadOnly();

    public ImmutableDictionary<Guid, int> EventsLoading { get; init; } = ImmutableDictionary<Guid, int>.Empty;

    public ReadOnlyCollection<DisplayEventModel> NewEventBuffer { get; init; } =
        new List<DisplayEventModel>().AsReadOnly();

    public bool NewEventBufferIsFull { get; init; }

    public DisplayEventModel? SelectedEvent { get; init; }

    public string? SelectedLogName { get; init; } = null;

    public bool SortDescending { get; init; } = true;
}
