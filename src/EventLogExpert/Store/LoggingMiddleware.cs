using EventLogExpert.Library.Helpers;
using EventLogExpert.Store.EventLog;
using EventLogExpert.Store.FilterPane;
using Fluxor;
using System.Text.Json;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.Store;

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
        if (action is EventLogAction.LoadEvents loadEventsAction)
        {
            // We don't want to serialize all the events.
            _debugLogger.Trace($"Action: EventLogAction.LoadEvents with {loadEventsAction.Events.Count} events.");
        }
        else if (action is EventLogAction.AddEvent addEventsAction)
        {
            _debugLogger.Trace($"Action: EventLogAction.AddEvent with {addEventsAction.NewEvent.Source} event ID {addEventsAction.NewEvent.Id}.");
        }
        else if (action is FilterPaneAction.RemoveFilter)
        {
            // We can't serialize a Func.
            _debugLogger.Trace("Action: EventLogAction.FilterEventsAction.");
        }
        else
        {
            _debugLogger.Trace($"Action: {action.GetType()} {JsonSerializer.Serialize(action, _serializerOptions)}");
        }
    }

    public override Task InitializeAsync(IDispatcher dispatcher, IStore store)
    {
        _store = store;
        return Task.CompletedTask;
    }
}
