// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using Fluxor;

namespace EventLogExpert.Runtime.EventLog;

internal sealed class EventLogCommands(IDispatcher dispatcher) : IEventLogCommands
{
    private readonly IDispatcher _dispatcher = dispatcher;

    public void CloseAllLogs() => _dispatcher.Dispatch(new CloseAllLogsAction());

    public void CloseLog(EventLogId logId, string logName)
    {
        _dispatcher.Dispatch(new CloseLogAction(logId, logName, UserInitiated: true));
        _dispatcher.Dispatch(new LogClosedByUserAction(logId, logName));
    }

    public void ConsumeRevealFocus(RevealFocusRequest request) => _dispatcher.Dispatch(new RevealFocusConsumedAction(request));

    public void LoadNewEvents() => _dispatcher.Dispatch(new LoadNewEventsAction());

    public void OpenLog(string logName, LogPathType logPathType, CancellationToken token = default) =>
        _dispatcher.Dispatch(new OpenLogAction(logName, logPathType, token));

    public void RequestRevealFocus(EventLocator target, bool waitForView = true) =>
        _dispatcher.Dispatch(new RequestRevealFocusAction(target, waitForView));

    public void SetContinuouslyUpdate(bool continuouslyUpdate) =>
        _dispatcher.Dispatch(new SetContinuouslyUpdateAction(continuouslyUpdate));

    public void SetSelectedEvents(IReadOnlyCollection<SelectionEntry> selection, SelectionEntry? focus) =>
        _dispatcher.Dispatch(new SetSelectedEventsAction(selection, focus));
}
