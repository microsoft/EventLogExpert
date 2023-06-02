// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.Models;
using Fluxor;
using System.Collections.Immutable;
using System.Collections.ObjectModel;

namespace EventLogExpert.Store.EventLog;

[FeatureState]
public record EventLogState
{
    public record EventBuffer(ReadOnlyCollection<DisplayEventModel> Events, bool IsBufferFull);

    public enum LogType { Live, File }

    public record EventLogData(
        string Name,
        LogType Type,
        ReadOnlyCollection<DisplayEventModel> Events,
        ImmutableHashSet<int> EventIds,
        ImmutableHashSet<string> EventProviderNames,
        ImmutableHashSet<string> TaskNames,
        ImmutableHashSet<string> KeywordNames
        );

    public ImmutableDictionary<string, EventLogData> ActiveLogs { get; init; } = ImmutableDictionary<string, EventLogData>.Empty;

    public bool ContinuouslyUpdate { get; init; } = false;

    public int EventsLoading { get; set; } = 0;

    public ReadOnlyCollection<DisplayEventModel> NewEventBuffer { get; init; } = new List<DisplayEventModel>().AsReadOnly();

    public bool NewEventBufferIsFull { get; set; }

    public DisplayEventModel? SelectedEvent { get; init; }
}
