// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Models;
using EventLogExpert.UI.Models;
using System.Collections.Immutable;
using System.Collections.ObjectModel;

namespace EventLogExpert.UI.Store.EventLog;

public sealed record EventLogAction
{
    public sealed record AddEvent(DisplayEventModel NewEvent);

    public sealed record AddEventBuffered(ReadOnlyCollection<DisplayEventModel> UpdatedBuffer, bool IsFull);

    public sealed record AddEventSuccess(ImmutableDictionary<string, EventLogData> ActiveLogs);

    public sealed record CloseAll;

    public sealed record CloseLog(string LogName);

    public sealed record LoadEvents(
        string LogName,
        LogType Type,
        List<DisplayEventModel> Events,
        IEnumerable<int> AllEventIds,
        IEnumerable<Guid?> AllActivityIds,
        IEnumerable<string> AllProviderNames,
        IEnumerable<string> AllTaskNames,
        IEnumerable<string> AllKeywords);

    public sealed record LoadNewEvents;

    public sealed record OpenLog(string LogName, LogType LogType);

    public sealed record SelectEvent(DisplayEventModel? SelectedEvent);

    public sealed record SetContinouslyUpdate(bool ContinuouslyUpdate);

    /// <summary>Used to indicate the progress of event logs being loaded.</summary>
    /// <param name="ActivityId">
    ///     A unique id that distinguishes this loading activity from others, since log names such as
    ///     Application will be common and many file names will be the same.
    /// </param>
    /// <param name="Count"></param>
    public sealed record SetEventsLoading(Guid ActivityId, int Count);

    public sealed record SetFilters(EventFilter EventFilter);
}
