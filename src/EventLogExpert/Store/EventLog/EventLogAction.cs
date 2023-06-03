// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.Models;
using static EventLogExpert.Store.EventLog.EventLogState;

namespace EventLogExpert.Store.EventLog;

public record EventLogAction
{
    public record AddEvent(DisplayEventModel NewEvent) : EventLogAction;

    public record LoadEvents(
        string LogName,
        LogType Type,
        List<DisplayEventModel> Events,
        IEnumerable<int> AllEventIds,
        IEnumerable<string> AllProviderNames,
        IEnumerable<string> AllTaskNames,
        IEnumerable<string> AllKeywords
    ) : EventLogAction;

    public record LoadNewEvents : EventLogAction;

    public record OpenLog(string LogName, LogType LogType) : EventLogAction;

    public record CloseLog(string LogName) : EventLogAction;

    public record CloseAll : EventLogAction;

    public record SelectEvent(DisplayEventModel? SelectedEvent) : EventLogAction;

    public record SetContinouslyUpdate(bool ContinuouslyUpdate) : EventLogAction;

    /// <summary>
    /// Used to indicate the progress of event logs being loaded.
    /// </summary>
    /// <param name="ActivityId">
    ///     A unique id that distinguishes this loading activity from others, since log names such as
    ///     Application will be common and many file names will be the same.
    /// </param>
    /// <param name="Count"></param>
    public record SetEventsLoading(Guid ActivityId, int Count) : EventLogAction;
}
