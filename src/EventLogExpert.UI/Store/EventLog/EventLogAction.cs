// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Models;
using EventLogExpert.UI.Models;
using System.Collections.Immutable;

namespace EventLogExpert.UI.Store.EventLog;

public sealed record EventLogAction
{
    public sealed record AddEvent(DisplayEventModel NewEvent);

    public sealed record AddEventBuffered(IReadOnlyList<DisplayEventModel> UpdatedBuffer, bool IsFull);

    public sealed record AddEventSuccess(ImmutableDictionary<string, EventLogData> ActiveLogs);

    public sealed record CloseAll;

    public sealed record CloseLog(EventLogId LogId, string LogName);

    public sealed record LoadEvents(EventLogData LogData, IReadOnlyList<DisplayEventModel> Events);

    public sealed record LoadEventsPartial(EventLogData LogData, IReadOnlyList<DisplayEventModel> Events);

    public sealed record LoadNewEvents;

    public sealed record OpenLog(string LogName, PathType PathType, CancellationToken Token = default);

    public sealed record SelectEvent(
        DisplayEventModel SelectedEvent,
        bool IsMultiSelect = false,
        bool ShouldStaySelected = false);

    public sealed record SelectEvents(IEnumerable<DisplayEventModel> SelectedEvents);

    /// <summary>
    /// Replaces the entire selection with the supplied events, preserving input order
    /// and de-duplicating by reference identity, and atomically updates the focused
    /// (selected) event. Use this for range selection (Shift+Click, Shift+Arrow),
    /// Select All (Ctrl+A), clear (Escape), and any selection update where the caller
    /// already knows both the new selection and the new focus.
    /// </summary>
    /// <param name="SelectedEvents">The new selection list, in the order that should be
    /// preserved (typically the current table's sort order).</param>
    /// <param name="SelectedEvent">The new focused event, or null to clear focus. Does not
    /// need to be a member of <paramref name="SelectedEvents"/>.</param>
    public sealed record SetSelectedEvents(
        IEnumerable<DisplayEventModel> SelectedEvents,
        DisplayEventModel? SelectedEvent);

    public sealed record SetContinuouslyUpdate(bool ContinuouslyUpdate);

    public sealed record SetFilters(EventFilter EventFilter);
}
