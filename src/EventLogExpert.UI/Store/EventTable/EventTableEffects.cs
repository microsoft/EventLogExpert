// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Interfaces;
using Fluxor;

namespace EventLogExpert.UI.Store.EventTable;

public sealed class EventTableEffects(IPreferencesProvider preferencesProvider)
{
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
        var columns = new Dictionary<ColumnName, bool>();
        var enabledColumns = _preferencesProvider.EnabledEventTableColumnsPreference;

        foreach (ColumnName column in Enum.GetValues(typeof(ColumnName)))
        {
            columns.Add(column, enabledColumns.Contains(column));
        }

        dispatcher.Dispatch(new EventTableAction.LoadColumnsCompleted(columns));

        return Task.CompletedTask;
    }

    [EffectMethod]
    public Task HandleToggleColumn(EventTableAction.ToggleColumn action, IDispatcher dispatcher)
    {
        var columns = new Dictionary<ColumnName, bool>();
        var enabledColumns = _preferencesProvider.EnabledEventTableColumnsPreference;

        foreach (ColumnName column in Enum.GetValues(typeof(ColumnName)))
        {
            columns.Add(column,
                column.Equals(action.ColumnName) ?
                    !enabledColumns.Contains(column) :
                    enabledColumns.Contains(column));
        }

        _preferencesProvider.EnabledEventTableColumnsPreference = columns.Keys.Where(column => columns[column]).ToList();

        dispatcher.Dispatch(new EventTableAction.LoadColumnsCompleted(columns));

        return Task.CompletedTask;
    }
}
