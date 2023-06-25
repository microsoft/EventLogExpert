using EventLogExpert.Eventing.Helpers;
using EventLogExpert.UI.Store.EventLog;
using EventLogExpert.UI.Store.FilterPane;
using Fluxor;
using System.Text.Json;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.UI.Store;

public class LoggingMiddleware : Middleware
{
    private IStore? _store;
    private ITraceLogger _debugLogger;
    private JsonSerializerOptions _serializerOptions;

    public LoggingMiddleware(ITraceLogger debugLogger)
    {
        _debugLogger = debugLogger;
        _serializerOptions = new JsonSerializerOptions();
    }

    public override void BeforeDispatch(object action)
    {
        switch (action)
        {
            case EventLogAction.LoadEvents loadEventsAction:
                _debugLogger.Trace($"Action: {action.GetType()} with {loadEventsAction.Events.Count} events.");
                break;
            case EventLogAction.AddEvent addEventsAction:
                _debugLogger.Trace($"Action: {action.GetType()} with {addEventsAction.NewEvent.Source} event ID {addEventsAction.NewEvent.Id}.");
                break;
            case FilterPaneAction.SetFilter:
            case EventLogAction.SetFilters:
            case FilterPaneAction.AddCachedFilter:
            case FilterPaneAction.RemoveFilter:
                _debugLogger.Trace($"Action: {action.GetType()}.");
                break;
            case EventLogAction.SelectEvent selectEventAction:
                _debugLogger.Trace($"Action: {nameof(EventLogAction.SelectEvent)} selected {selectEventAction?.SelectedEvent?.Source} event ID {selectEventAction?.SelectedEvent?.Id}.");
                break;
            default:
                try
                {
                    _debugLogger.Trace($"Action: {action.GetType()} {JsonSerializer.Serialize(action, _serializerOptions)}");
                }
                catch
                {
                    _debugLogger.Trace($"Action: {action.GetType()}. Could not serialize payload.");
                }

                break;
        }
    }

    public override Task InitializeAsync(IDispatcher dispatcher, IStore store)
    {
        _store = store;
        return Task.CompletedTask;
    }
}
