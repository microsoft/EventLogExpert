using EventLogExpert.Eventing.Helpers;
using EventLogExpert.UI.Store.EventLog;
using EventLogExpert.UI.Store.EventTable;
using EventLogExpert.UI.Store.FilterCache;
using EventLogExpert.UI.Store.FilterPane;
using EventLogExpert.UI.Store.Settings;
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
                _debugLogger.Trace($"Action: {action.GetType()} with {loadEventsAction.Events.Count} events.");
                break;
            case EventLogAction.AddEvent addEventsAction:
                _debugLogger.Trace($"Action: {action.GetType()} with {addEventsAction.NewEvent.Source} event ID {addEventsAction.NewEvent.Id}.");
                break;
            case EventTableAction.CloseAll:
            case EventTableAction.CloseLog:
            case EventTableAction.NewTable:
            case EventTableAction.SetActiveTable:
            case EventTableAction.SetOrderBy:
            case EventTableAction.ToggleLoading:
            case EventTableAction.ToggleSorting:
            case EventTableAction.UpdateDisplayedEvents:
            case FilterCacheAction.AddFavoriteFilter:
            case FilterCacheAction.AddFavoriteFilterCompleted:
            case FilterCacheAction.AddRecentFilter:
            case FilterCacheAction.AddRecentFilterCompleted:
            case FilterCacheAction.RemoveFavoriteFilter:
            case FilterCacheAction.RemoveFavoriteFilterCompleted:
            case FilterCacheAction.LoadFiltersCompleted:
            case FilterPaneAction.AddCachedFilter:
            case FilterPaneAction.AddFilter:
            case FilterPaneAction.AddSubFilter:
            case FilterPaneAction.ClearAllFilters:
            case FilterPaneAction.RemoveCachedFilter:
            case FilterPaneAction.RemoveFilter:
            case FilterPaneAction.RemoveSubFilter:
            case FilterPaneAction.SetAdvancedFilter:
            case FilterPaneAction.SetFilter:
            case FilterPaneAction.SetFilterDateRange:
            case FilterPaneAction.ToggleAdvancedFilter:
            case FilterPaneAction.ToggleCachedFilter:
            case FilterPaneAction.ToggleEditFilter:
            case FilterPaneAction.ToggleEnableFilter:
            case FilterPaneAction.ToggleFilterDate:
            case FilterPaneAction.ToggleIsLoading:
            case EventLogAction.AddEventBuffered:
            case EventLogAction.AddEventSuccess:
            case EventLogAction.CloseAll:
            case EventLogAction.CloseLog:
            case EventLogAction.LoadNewEvents:
            case EventLogAction.SetContinouslyUpdate:
            case EventLogAction.SetEventsLoading:
            case EventLogAction.SetFilters:
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
