using EventLogExpert.Eventing.Helpers;
using EventLogExpert.UI.Store.EventLog;
using EventLogExpert.UI.Store.EventTable;
using EventLogExpert.UI.Store.FilterCache;
using EventLogExpert.UI.Store.FilterColor;
using EventLogExpert.UI.Store.FilterGroup;
using EventLogExpert.UI.Store.FilterPane;
using Fluxor;
using System.Text.Json;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.UI.Store;

public sealed class LoggingMiddleware(ITraceLogger debugLogger) : Middleware
{
    private readonly JsonSerializerOptions _serializerOptions = new();

    private IStore? _store;

    public override void BeforeDispatch(object action)
    {
        switch (action)
        {
            case EventLogAction.LoadEvents loadEventsAction :
                debugLogger.Trace($"Action: {action.GetType()} with {loadEventsAction.Events.Count} events.");
                break;
            case EventLogAction.AddEvent addEventsAction :
                debugLogger.Trace($"Action: {action.GetType()} with {addEventsAction.NewEvent.Source} event ID {addEventsAction.NewEvent.Id}.");

                break;
            case EventTableAction.AddTable :
            case EventTableAction.CloseAll :
            case EventTableAction.CloseLog :
            case EventTableAction.SetActiveTable :
            case EventTableAction.SetOrderBy :
            case EventTableAction.ToggleLoading :
            case EventTableAction.ToggleSorting :
            case EventTableAction.UpdateDisplayedEvents :
            case FilterCacheAction.AddFavoriteFilter :
            case FilterCacheAction.AddFavoriteFilterCompleted :
            case FilterCacheAction.AddRecentFilter :
            case FilterCacheAction.AddRecentFilterCompleted :
            case FilterCacheAction.RemoveFavoriteFilter :
            case FilterCacheAction.RemoveFavoriteFilterCompleted :
            case FilterCacheAction.LoadFiltersCompleted :
            case FilterColorAction.ClearAllFilters :
            case FilterColorAction.RemoveFilter :
            case FilterColorAction.SetFilter :
            case FilterGroupAction.AddFilter :
            case FilterGroupAction.AddGroup :
            case FilterGroupAction.LoadGroups :
            case FilterGroupAction.LoadGroupsSuccess :
            case FilterGroupAction.OpenMenu :
            case FilterGroupAction.RemoveFilter :
            case FilterGroupAction.RemoveGroup :
            case FilterGroupAction.SetFilter :
            case FilterGroupAction.SetGroup :
            case FilterGroupAction.ToggleFilter :
            case FilterGroupAction.ToggleGroup :
            case FilterPaneAction.AddAdvancedFilter :
            case FilterPaneAction.AddBasicFilter :
            case FilterPaneAction.AddCachedFilter :
            case FilterPaneAction.AddSubFilter :
            case FilterPaneAction.ApplyFilterGroup :
            case FilterPaneAction.ClearAllFilters :
            case FilterPaneAction.RemoveAdvancedFilter :
            case FilterPaneAction.RemoveBasicFilter :
            case FilterPaneAction.RemoveCachedFilter :
            case FilterPaneAction.RemoveSubFilter :
            case FilterPaneAction.SaveFilterGroup :
            case FilterPaneAction.SetAdvancedFilter :
            case FilterPaneAction.SetBasicFilter :
            case FilterPaneAction.SetFilterDateRange :
            case FilterPaneAction.ToggleAdvancedFilterEditing :
            case FilterPaneAction.ToggleAdvancedFilterEnabled :
            case FilterPaneAction.ToggleBasicFilterEditing :
            case FilterPaneAction.ToggleBasicFilterEnabled :
            case FilterPaneAction.ToggleCachedFilter :
            case FilterPaneAction.ToggleFilterDate :
            case FilterPaneAction.ToggleIsEnabled :
            case FilterPaneAction.ToggleIsLoading :
            case EventLogAction.AddEventBuffered :
            case EventLogAction.AddEventSuccess :
            case EventLogAction.CloseAll :
            case EventLogAction.CloseLog :
            case EventLogAction.LoadNewEvents :
            case EventLogAction.SetContinouslyUpdate :
            case EventLogAction.SetEventsLoading :
            case EventLogAction.SetFilters :
                debugLogger.Trace($"Action: {action.GetType()}.");
                break;
            case EventLogAction.SelectEvent selectEventAction :
                debugLogger.Trace($"Action: {nameof(EventLogAction.SelectEvent)} selected " +
                    $"{selectEventAction.SelectedEvent?.Source} event ID {selectEventAction.SelectedEvent?.Id}.");

                break;
            default :
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

    public override Task InitializeAsync(IDispatcher dispatcher, IStore store)
    {
        _store = store;
        return Task.CompletedTask;
    }
}
