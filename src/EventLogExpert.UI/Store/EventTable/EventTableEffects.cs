// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Interfaces;
using EventLogExpert.UI.Services;
using Fluxor;

namespace EventLogExpert.UI.Store.EventTable;

public sealed class EventTableEffects(
    IEventTableColumnProvider columnProvider,
    IState<EventTableState> eventTableState,
    IPreferencesProvider preferencesProvider)
{
    [EffectMethod(typeof(EventTableAction.LoadColumns))]
    public Task HandleLoadColumns(IDispatcher dispatcher)
    {
        var columns = columnProvider.GetColumns();

        dispatcher.Dispatch(new EventTableAction.LoadColumnsCompleted(columns));

        return Task.CompletedTask;
    }

    [EffectMethod]
    public Task HandleToggleColumn(EventTableAction.ToggleColumn action, IDispatcher dispatcher)
    {
        switch (action.ColumnName)
        {
            case ColumnName.Level:
                preferencesProvider.LevelColumnPreference = !preferencesProvider.LevelColumnPreference;
                break;
            case ColumnName.DateAndTime:
                preferencesProvider.DateAndTimeColumnPreference = !preferencesProvider.DateAndTimeColumnPreference;
                break;
            case ColumnName.ActivityId:
                preferencesProvider.ActivityIdColumnPreference = !preferencesProvider.ActivityIdColumnPreference;
                break;
            case ColumnName.LogName:
                preferencesProvider.LogNameColumnPreference = !preferencesProvider.LogNameColumnPreference;
                break;
            case ColumnName.ComputerName:
                preferencesProvider.ComputerNameColumnPreference = !preferencesProvider.ComputerNameColumnPreference;
                break;
            case ColumnName.Source:
                preferencesProvider.SourceColumnPreference = !preferencesProvider.SourceColumnPreference;
                break;
            case ColumnName.EventId:
                preferencesProvider.EventIdColumnPreference = !preferencesProvider.EventIdColumnPreference;
                break;
            case ColumnName.TaskCategory:
                preferencesProvider.TaskCategoryColumnPreference = !preferencesProvider.TaskCategoryColumnPreference;
                break;
        }

        return Task.CompletedTask;
    }

    [EffectMethod(typeof(EventTableAction.UpdateDisplayedEvents))]
    public Task HandleUpdateDisplayedEvents(IDispatcher dispatcher)
    {
        if (eventTableState.Value.EventTables.Any(table => table.IsLoading))
        {
            return Task.CompletedTask;
        }

        dispatcher.Dispatch(new EventTableAction.UpdateCombinedEvents());

        return Task.CompletedTask;
    }
}
