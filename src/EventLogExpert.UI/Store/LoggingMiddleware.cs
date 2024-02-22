using EventLogExpert.Eventing.Helpers;
using EventLogExpert.UI.Store.EventLog;
using EventLogExpert.UI.Store.EventTable;
using EventLogExpert.UI.Store.FilterCache;
using EventLogExpert.UI.Store.FilterGroup;
using EventLogExpert.UI.Store.FilterPane;
using EventLogExpert.UI.Store.Settings;
using EventLogExpert.UI.Store.StatusBar;
using Fluxor;
using Microsoft.Extensions.Logging;
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
            case EventLogAction.OpenLog openLogAction :
                debugLogger.Trace($"Action: {action.GetType()} with {openLogAction.LogName} log type {openLogAction.LogType}.");
                break;
            case EventLogAction.AddEventBuffered :
            case EventLogAction.AddEventSuccess :
            case EventLogAction.SetFilters :
            case EventTableAction.AddTable :
            case EventTableAction.LoadColumnsCompleted :
            case EventTableAction.UpdateDisplayedEvents :
            case FilterCacheAction.AddFavoriteFilterCompleted :
            case FilterCacheAction.AddRecentFilterCompleted :
            case FilterCacheAction.ImportFavorites :
            case FilterCacheAction.LoadFiltersCompleted :
            case FilterCacheAction.RemoveFavoriteFilterCompleted :
            case FilterGroupAction.AddGroup :
            case FilterGroupAction.ImportGroups :
            case FilterGroupAction.LoadGroupsSuccess :
            case FilterGroupAction.SetFilter :
            case FilterGroupAction.SetGroup :
            case FilterGroupAction.UpdateDisplayGroups :
            case FilterPaneAction.AddAdvancedFilter :
            case FilterPaneAction.AddBasicFilter :
            case FilterPaneAction.AddCachedFilter :
            case FilterPaneAction.ApplyFilterGroup :
            case FilterPaneAction.SetAdvancedFilter :
            case FilterPaneAction.SetBasicFilter :
            case FilterPaneAction.SetCachedFilter :
            case FilterPaneAction.SetFilterDateRange :
            case SettingsAction.LoadDatabasesCompleted :
            case SettingsAction.LoadSettingsCompleted :
                debugLogger.Trace($"Action: {action.GetType()}.");
                break;
            case EventLogAction.SelectEvent selectEventAction :
                debugLogger.Trace($"Action: {nameof(EventLogAction.SelectEvent)} selected " +
                    $"{selectEventAction.SelectedEvent?.Source} event ID {selectEventAction.SelectedEvent?.Id}.");

                break;
            case StatusBarAction.SetEventsLoading :
            case StatusBarAction.SetXmlLoading :
                debugLogger.Trace($"Action: {action.GetType()} {JsonSerializer.Serialize(action, _serializerOptions)}", LogLevel.Debug);
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
