// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Models;
using static EventLogExpert.UI.Store.EventLog.EventLogState;

namespace EventLogExpert.UI.Store.EventLog;

public sealed record EventLogAction
{
    public sealed record AddEvent(DisplayEventModel NewEvent, ITraceLogger TraceLogger);

    public sealed record LoadEvents(
        string LogName,
        LogType Type,
        List<DisplayEventModel> Events,
        IEnumerable<int> AllEventIds,
        IEnumerable<Guid?> AllActivityIds,
        IEnumerable<string> AllProviderNames,
        IEnumerable<string> AllTaskNames,
        IEnumerable<string> AllKeywords,
        ITraceLogger TraceLogger
    );

    public sealed record LoadNewEvents(ITraceLogger TraceLogger);

    public sealed record OpenLog(string LogName, LogType LogType);

    public sealed record CloseLog(string LogName);

    public sealed record CloseAll;

    public sealed record SelectEvent(DisplayEventModel? SelectedEvent);

    /// <summary>This action only has meaning for the UI.</summary>
    public sealed record SelectLog(string? LogName);

    public sealed record SetContinouslyUpdate(bool ContinuouslyUpdate, ITraceLogger TraceLogger);

    /// <summary>
    /// Used to indicate the progress of event logs being loaded.
    /// </summary>
    /// <param name="ActivityId">
    ///     A unique id that distinguishes this loading activity from others, since log names such as
    ///     Application will be common and many file names will be the same.
    /// </param>
    /// <param name="Count"></param>
    public sealed record SetEventsLoading(Guid ActivityId, int Count);

    public sealed record SetFilters(EventFilter EventFilter, ITraceLogger TraceLogger);

    public sealed record SetOrderBy(ColumnName? OrderBy, ITraceLogger TraceLogger);

    public sealed record ToggleSorting(ITraceLogger TraceLogger);
}
