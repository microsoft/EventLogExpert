using EventLogExpert.Eventing.Helpers;
using EventLogExpert.UI.Store.EventLog;
using EventLogExpert.UI.Store.FilterCache;
using EventLogExpert.UI.Store.FilterPane;
using Fluxor;
using System.Text.Json;

namespace EventLogExpert.UI.Store;

public sealed class LoggingMiddleware(ITraceLogger debugLogger) : Middleware
{
    private readonly JsonSerializerOptions _serializerOptions = new();

    public override void BeforeDispatch(object action)
    {
        switch (action)
        {
            case EventLogAction.LoadEvents loadEventsAction:
                debugLogger.Trace($"Action: {action.GetType()} with {loadEventsAction.Events.Count} events.");
                break;
            case EventLogAction.AddEvent addEventsAction:
                debugLogger.Trace($"Action: {action.GetType()} with {addEventsAction.NewEvent.Source} event ID {addEventsAction.NewEvent.Id}.");
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
                debugLogger.Trace($"Action: {action.GetType()}.");
                break;
            case EventLogAction.SelectEvent selectEventAction:
                debugLogger.Trace($"Action: {nameof(EventLogAction.SelectEvent)} selected {selectEventAction?.SelectedEvent?.Source} event ID {selectEventAction?.SelectedEvent?.Id}.");
                break;
            default:
                try
                {
                    debugLogger.Trace($"Action: {action.GetType()} {JsonSerializer.Serialize(action, _serializerOptions)}");
                }
                catch
                {
                    debugLogger.Trace($"Action: {action.GetType()}. Could not serialize payload.");
                }

                break;
        }
    }
}
