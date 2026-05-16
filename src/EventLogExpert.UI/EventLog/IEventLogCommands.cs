// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;

namespace EventLogExpert.UI.EventLog;

public interface IEventLogCommands
{
    /// <summary>Closes the log identified by <paramref name="logId" /> / <paramref name="logName" />.</summary>
    void CloseLog(EventLogId logId, string logName);

    /// <summary>Reads any buffered new events into the active table and clears the buffer counter.</summary>
    void LoadNewEvents();

    /// <summary>Opens <paramref name="logName" /> (channel or file) with an optional cancellation token.</summary>
    void OpenLog(string logName, LogPathType logPathType, CancellationToken token = default);

    /// <summary>Toggles whether new events for the active log auto-append (vs. buffer for manual flush).</summary>
    void SetContinuouslyUpdate(bool continuouslyUpdate);

    /// <summary>
    ///     Replaces the current selection with <paramref name="selectedEvents" /> and the focused row with
    ///     <paramref name="selectedEvent" /> atomically.
    /// </summary>
    void SetSelectedEvents(IReadOnlyCollection<ResolvedEvent> selectedEvents, ResolvedEvent? selectedEvent);
}
