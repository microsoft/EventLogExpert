// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.Models;
using Fluxor;
using System.Collections.Immutable;

namespace EventLogExpert.Store.EventLog;

[FeatureState]
public record EventLogState
{
    public enum LogType { Live, File }

    public LogSpecifier ActiveLog { get; init; } = null!;

    public bool ContinuouslyUpdate { get; init; } = false;

    public ImmutableList<DisplayEventModel> Events { get; init; } = ImmutableList<DisplayEventModel>.Empty;

    public record LogSpecifier(string Name, LogType? LogType);

    public ImmutableList<DisplayEventModel> NewEvents { get; init; } = ImmutableList<DisplayEventModel>.Empty;

    public DisplayEventModel? SelectedEvent { get; init; }

    public LiveLogWatcher? Watcher { get; init; } = null!;
}
