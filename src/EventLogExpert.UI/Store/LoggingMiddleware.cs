// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Logging;
using EventLogExpert.UI.Store.EventLog;
using EventLogExpert.UI.Store.EventTable;
using EventLogExpert.UI.Store.StatusBar;
using Fluxor;
using System.Text.Json;

namespace EventLogExpert.UI.Store;

public sealed class LoggingMiddleware(ITraceLogger debugLogger) : Middleware
{
    private readonly ITraceLogger _debugLogger = debugLogger;

    public override void BeforeDispatch(object action)
    {
        switch (action)
        {
            case EventLogAction.LoadEvents loadEventsAction:
                _debugLogger.Debug($"Action: {action.GetType()} with {loadEventsAction.Events.Count} events.");
                break;
            case EventLogAction.LoadEventsPartial loadEventsPartialAction:
                _debugLogger.Debug($"Action: {action.GetType()} with {loadEventsPartialAction.Events.Count} events.");
                break;
            case EventLogAction.AddEvent addEventAction:
                _debugLogger.Debug($"Action: {action.GetType()} with {addEventAction.NewEvent.Source} event ID {addEventAction.NewEvent.Id}.");
                break;
            case EventLogAction.OpenLog openLogAction:
                _debugLogger.Information($"Action: {action.GetType()} with {openLogAction.LogName} log type {openLogAction.LogPathType}.");
                break;
            case EventLogAction.SelectEvent selectEventAction:
                _debugLogger.Debug($"Action: {nameof(EventLogAction.SelectEvent)} selected {selectEventAction.SelectedEvent.Source} event ID {selectEventAction.SelectedEvent.Id}.");
                break;
            case EventLogAction.SelectEvents selectEventsAction:
                _debugLogger.Debug($"Action: {nameof(EventLogAction.SelectEvents)} selected {selectEventsAction.SelectedEvents.Count} events.");
                break;
            case EventLogAction.SetSelectedEvents setSelectedEventsAction:
                _debugLogger.Debug($"Action: {nameof(EventLogAction.SetSelectedEvents)} set {setSelectedEventsAction.SelectedEvents.Count} events.");
                break;
            case EventTableAction.AppendTableEvents appendTableEventsAction:
                _debugLogger.Debug($"Action: {action.GetType()} with {appendTableEventsAction.Events.Count} events for log {appendTableEventsAction.LogId}.");
                break;
            case EventTableAction.AppendTableEventsBatch appendTableEventsBatchAction:
                var totalAppendEvents = appendTableEventsBatchAction.EventsByLog.Values.Sum(eventsForLog => eventsForLog.Count);
                _debugLogger.Debug($"Action: {action.GetType()} with {totalAppendEvents} events across {appendTableEventsBatchAction.EventsByLog.Count} logs.");
                break;
            case StatusBarAction.SetEventsLoading:
                _debugLogger.Debug($"Action: {action.GetType()} {JsonSerializer.Serialize(action)}");
                break;
            default:
                _debugLogger.Debug($"Action: {action.GetType()}.");
                break;
        }
    }
}
