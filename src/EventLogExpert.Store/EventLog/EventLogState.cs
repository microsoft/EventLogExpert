// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.Models;
using Fluxor;

namespace EventLogExpert.Store.EventLog;

/// <summary>
///     NOTE: Because Virtualize requires an ICollection<T>, we have to use
///     some sort of mutable collection for EventsToDisplay, unfortunately.
///     If that ever changes we should consider making these immutable.
/// </summary>
[FeatureState]
public record EventLogState
{
    public enum LogType { Live, File }

    public LogSpecifier ActiveLog { get; init; } = null!;

    public List<DisplayEventModel> Events { get; init; } = new();

    public List<DisplayEventModel> EventsToDisplay { get; init; } = new();

    public DisplayEventModel? SelectedEvent { get; set; }

    public record LogSpecifier(string Name, LogType? LogType);
}
