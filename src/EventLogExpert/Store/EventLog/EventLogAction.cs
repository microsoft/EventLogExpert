// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Models;
using static EventLogExpert.Store.EventLog.EventLogState;

namespace EventLogExpert.Store.EventLog;

public record EventLogAction
{
    public record AddEvent(DisplayEventModel NewEvent, ITraceLogger TraceLogger) : EventLogAction;

    public record LoadEvents(
        string LogName,
        LogType Type,
        List<DisplayEventModel> Events,
        IEnumerable<int> AllEventIds,
        IEnumerable<string> AllProviderNames,
        IEnumerable<string> AllTaskNames,
        IEnumerable<string> AllKeywords,
        ITraceLogger TraceLogger
    ) : EventLogAction;

    public record LoadNewEvents(ITraceLogger TraceLogger) : EventLogAction;

    public record OpenLog(string LogName, LogType LogType) : EventLogAction;

    public record CloseLog(string LogName) : EventLogAction;

    public record CloseAll : EventLogAction;

    public record SelectEvent(DisplayEventModel? SelectedEvent) : EventLogAction;

    /// <summary>
    /// This action only has meaning for the UI.
    /// </summary>
    /// <param name="LogName"></param>
    public record SelectLog(string? LogName) : EventLogAction;

    public record SetContinouslyUpdate(bool ContinuouslyUpdate, ITraceLogger TraceLogger) : EventLogAction;

    /// <summary>
    /// Used to indicate the progress of event logs being loaded.
    /// </summary>
    /// <param name="ActivityId">
    ///     A unique id that distinguishes this loading activity from others, since log names such as
    ///     Application will be common and many file names will be the same.
    /// </param>
    /// <param name="Count"></param>
    public record SetEventsLoading(Guid ActivityId, int Count) : EventLogAction;

    public record SetFilters(EventFilter EventFilter, ITraceLogger TraceLogger) : EventLogAction;

    public record SetSortDescending(bool SortDescending, ITraceLogger TraceLogger) : EventLogAction;
}
