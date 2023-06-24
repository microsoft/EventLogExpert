using EventLogExpert.Eventing.Helpers;
using EventLogExpert.UI.Store.EventLog;
using EventLogExpert.UI.Store.FilterCache;
using EventLogExpert.UI.Store.FilterPane;
using Fluxor;
using Microsoft.Extensions.Logging;
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
                _debugLogger.Trace($"Action: {action.GetType()} with {loadEventsAction.Events.Count} events.", LogLevel.Debug);
                break;
            case EventLogAction.AddEvent addEventsAction:
                _debugLogger.Trace($"Action: {action.GetType()} with {addEventsAction.NewEvent.Source} event ID {addEventsAction.NewEvent.Id}.", LogLevel.Debug);
                break;
            case FilterCacheAction.AddFavoriteFilter:
            case FilterCacheAction.AddFavoriteFilterCompleted:
            case FilterCacheAction.AddRecentFilter:
            case FilterCacheAction.AddRecentFilterCompleted:
            case FilterCacheAction.RemoveFavoriteFilter:
            case FilterCacheAction.RemoveFavoriteFilterCompleted:
            case FilterCacheAction.LoadFiltersCompleted:
            case FilterPaneAction.AddFilter:
            case FilterPaneAction.AddCachedFilter:
            case FilterPaneAction.RemoveCachedFilter:
            case FilterPaneAction.RemoveFilter:
            case FilterPaneAction.SetAdvancedFilter:
            case FilterPaneAction.SetFilter:
            case FilterPaneAction.ToggleCachedFilter:
            case EventLogAction.SetFilters:
                _debugLogger.Trace($"Action: {action.GetType()}.", LogLevel.Debug);
                break;
            case EventLogAction.SelectEvent selectEventAction:
                _debugLogger.Trace($"Action: {nameof(EventLogAction.SelectEvent)} selected {selectEventAction?.SelectedEvent?.Source} event ID {selectEventAction?.SelectedEvent?.Id}.", LogLevel.Debug);
                break;
            default:
                try
                {
                    _debugLogger.Trace($"Action: {action.GetType()} {JsonSerializer.Serialize(action, _serializerOptions)}", LogLevel.Debug);
                }
                catch
                {
                    _debugLogger.Trace($"Action: {action.GetType()}. Could not serialize payload.", LogLevel.Debug);
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
