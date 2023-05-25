// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.Models;
using Fluxor;
using System.Collections.ObjectModel;

namespace EventLogExpert.Store.EventLog;

[FeatureState]
public record EventLogState
{
    public record EventBuffer(ReadOnlyCollection<DisplayEventModel> Events, bool IsBufferFull);

    public record LogSpecifier(string Name, LogType? LogType);

    public enum LogType { Live, File }

    public ReadOnlyCollection<LogSpecifier> ActiveLogs { get; init; } = new List<LogSpecifier>().AsReadOnly();

    public bool ContinuouslyUpdate { get; init; } = false;

    public ReadOnlyCollection<DisplayEventModel> Events { get; init; } = new List<DisplayEventModel>().AsReadOnly();

    public int EventsLoading { get; set; } = 0;

    public ReadOnlyCollection<DisplayEventModel> NewEventBuffer { get; init; } = new List<DisplayEventModel>().AsReadOnly();

    public bool NewEventBufferIsFull { get; set; }

    public DisplayEventModel? SelectedEvent { get; init; }
}
