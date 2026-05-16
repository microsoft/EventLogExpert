// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using Fluxor;

namespace EventLogExpert.UI.EventLog;

internal sealed class EventLogCommands(IDispatcher dispatcher) : IEventLogCommands
{
    private readonly IDispatcher _dispatcher = dispatcher;

    public void CloseLog(EventLogId logId, string logName) => _dispatcher.Dispatch(new CloseLogAction(logId, logName));

    public void LoadNewEvents() => _dispatcher.Dispatch(new LoadNewEventsAction());

    public void OpenLog(string logName, LogPathType logPathType, CancellationToken token = default) =>
        _dispatcher.Dispatch(new OpenLogAction(logName, logPathType, token));

    public void SetContinuouslyUpdate(bool continuouslyUpdate) =>
        _dispatcher.Dispatch(new SetContinuouslyUpdateAction(continuouslyUpdate));

    public void SetSelectedEvents(IReadOnlyCollection<ResolvedEvent> selectedEvents, ResolvedEvent? selectedEvent) =>
        _dispatcher.Dispatch(new SetSelectedEventsAction(selectedEvents, selectedEvent));
}
