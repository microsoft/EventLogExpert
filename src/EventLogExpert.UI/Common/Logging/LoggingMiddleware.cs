// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Logging;
using EventLogExpert.UI.StatusBar;
using EventLogExpert.UI.Store.EventLog;
using EventLogExpert.UI.Store.EventTable;
using Fluxor;
using System.Text.Json;

namespace EventLogExpert.UI.Common.Logging;

public sealed class LoggingMiddleware(ITraceLogger debugLogger) : Middleware
{
    private readonly ITraceLogger _debugLogger = debugLogger;

    public override void BeforeDispatch(object action)
    {
        switch (action)
        {
            case LoadEventsAction loadEventsAction:
                _debugLogger.Debug($"Action: {action.GetType()} with {loadEventsAction.Events.Count} events.");
                break;
            case LoadEventsPartialAction loadEventsPartialAction:
                _debugLogger.Debug($"Action: {action.GetType()} with {loadEventsPartialAction.Events.Count} events.");
                break;
            case AddEventAction addEventAction:
                _debugLogger.Debug($"Action: {action.GetType()} with {addEventAction.NewEvent.Source} event ID {addEventAction.NewEvent.Id}.");
                break;
            case OpenLogAction openLogAction:
                _debugLogger.Information($"Action: {action.GetType()} with {openLogAction.LogName} log type {openLogAction.LogPathType}.");
                break;
            case SelectEventAction selectEventAction:
                _debugLogger.Debug($"Action: {nameof(SelectEventAction)} selected {selectEventAction.SelectedEvent.Source} event ID {selectEventAction.SelectedEvent.Id}.");
                break;
            case SelectEventsAction selectEventsAction:
                _debugLogger.Debug($"Action: {nameof(SelectEventsAction)} selected {selectEventsAction.SelectedEvents.Count} events.");
                break;
            case SetSelectedEventsAction setSelectedEventsAction:
                _debugLogger.Debug($"Action: {nameof(SetSelectedEventsAction)} set {setSelectedEventsAction.SelectedEvents.Count} events.");
                break;
            case AppendTableEventsAction appendTableEventsAction:
                _debugLogger.Debug($"Action: {action.GetType()} with {appendTableEventsAction.Events.Count} events for log {appendTableEventsAction.LogId}.");
                break;
            case AppendTableEventsBatchAction appendTableEventsBatchAction:
                var totalAppendEvents = appendTableEventsBatchAction.EventsByLog.Values.Sum(eventsForLog => eventsForLog.Count);
                _debugLogger.Debug($"Action: {action.GetType()} with {totalAppendEvents} events across {appendTableEventsBatchAction.EventsByLog.Count} logs.");
                break;
            case SetEventsLoadingAction:
                _debugLogger.Debug($"Action: {action.GetType()} {JsonSerializer.Serialize(action)}");
                break;
            default:
                _debugLogger.Debug($"Action: {action.GetType()}.");
                break;
        }
    }
}
