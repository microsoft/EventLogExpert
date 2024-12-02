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

    public sealed record AddEventBuffered(IEnumerable<DisplayEventModel> UpdatedBuffer, bool IsFull);

    public sealed record AddEventSuccess(ImmutableDictionary<string, EventLogData> ActiveLogs);

    public sealed record CloseAll;

    public sealed record CloseLog(EventLogId LogId, string LogName);

    public sealed record LoadEvents(EventLogData LogData, IEnumerable<DisplayEventModel> Events);

    public sealed record LoadNewEvents;

    public sealed record OpenLog(string LogName, PathType PathType, CancellationToken Token = default);

    public sealed record SelectEvent(
        DisplayEventModel SelectedEvent,
        bool IsMultiSelect = false,
        bool ShouldStaySelected = false);

    public sealed record SelectEvents(IEnumerable<DisplayEventModel> SelectedEvents);

    public sealed record SetContinuouslyUpdate(bool ContinuouslyUpdate);

    public sealed record SetFilters(EventFilter EventFilter);
}
