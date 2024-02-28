// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Interfaces;
using EventLogExpert.UI.Services;
using Fluxor;

namespace EventLogExpert.UI.Store.EventTable;

public sealed class EventTableEffects(
    IEventTableColumnProvider columnProvider,
    IPreferencesProvider preferencesProvider)
{
    private readonly IEventTableColumnProvider _columnProvider = columnProvider;
    private readonly IPreferencesProvider _preferencesProvider = preferencesProvider;

    [EffectMethod(typeof(EventTableAction.UpdateDisplayedEvents))]
    public static Task HandleUpdateDisplayedEvents(IDispatcher dispatcher)
    {
        dispatcher.Dispatch(new EventTableAction.UpdateCombinedEvents());

        return Task.CompletedTask;
    }

    [EffectMethod(typeof(EventTableAction.UpdateTable))]
    public static Task HandleUpdateTable(IDispatcher dispatcher)
    {
        dispatcher.Dispatch(new EventTableAction.UpdateCombinedEvents());

        return Task.CompletedTask;
    }

    [EffectMethod(typeof(EventTableAction.LoadColumns))]
    public Task HandleLoadColumns(IDispatcher dispatcher)
    {
        var columns = _columnProvider.GetColumns();

        dispatcher.Dispatch(new EventTableAction.LoadColumnsCompleted(columns));

        return Task.CompletedTask;
    }

    [EffectMethod]
    public Task HandleToggleColumn(EventTableAction.ToggleColumn action, IDispatcher dispatcher)
    {
        switch (action.ColumnName)
        {
            case ColumnName.Level:
                _preferencesProvider.LevelColumnPreference = !_preferencesProvider.LevelColumnPreference;
                break;
            case ColumnName.DateAndTime:
                _preferencesProvider.DateAndTimeColumnPreference = !_preferencesProvider.DateAndTimeColumnPreference;
                break;
            case ColumnName.ActivityId:
                _preferencesProvider.ActivityIdColumnPreference = !_preferencesProvider.ActivityIdColumnPreference;
                break;
            case ColumnName.LogName:
                _preferencesProvider.LogNameColumnPreference = !_preferencesProvider.LogNameColumnPreference;
                break;
            case ColumnName.ComputerName:
                _preferencesProvider.ComputerNameColumnPreference = !_preferencesProvider.ComputerNameColumnPreference;
                break;
            case ColumnName.Source:
                _preferencesProvider.SourceColumnPreference = !_preferencesProvider.SourceColumnPreference;
                break;
            case ColumnName.EventId:
                _preferencesProvider.EventIdColumnPreference = !_preferencesProvider.EventIdColumnPreference;
                break;
            case ColumnName.TaskCategory:
                _preferencesProvider.TaskCategoryColumnPreference = !_preferencesProvider.TaskCategoryColumnPreference;
                break;
        }

        return Task.CompletedTask;
    }
}
