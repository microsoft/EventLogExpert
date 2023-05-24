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

    public record LogSpecifier(string Name, LogType? LogType);

    public enum LogType { Live, File }

    public LogSpecifier ActiveLog { get; init; } = null!;

    public bool ContinuouslyUpdate { get; init; } = false;

    public ReadOnlyCollection<DisplayEventModel> Events { get; init; } = new List<DisplayEventModel>().AsReadOnly();

    public EventBuffer NewEventBuffer { get; init; } = new (new List<DisplayEventModel>().AsReadOnly(), false);

    public DisplayEventModel? SelectedEvent { get; init; }

    public LiveLogWatcher? Watcher { get; init; } = null!;
}
