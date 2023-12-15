// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Models;
using EventLogExpert.UI.Models;
using Fluxor;

namespace EventLogExpert.UI.Store.EventTable;

public sealed class EventTableReducers
{
    [ReducerMethod(typeof(EventTableAction.CloseAll))]
    public static EventTableState ReduceCloseAll(EventTableState state) => state with { EventTables = [] };

    [ReducerMethod]
    public static EventTableState ReduceNewTable(EventTableState state, EventTableAction.NewTable action)
    {
        var newTable = new EventTableModel
        {
            FileName = action.FileName,
            LogName = action.LogName,
            LogType = action.LogType,
            IsLoading = true
        };

        if (state.EventTables.IsEmpty)
        {
            return state with { EventTables = state.EventTables.Add(newTable), ActiveTable = newTable };
        }

        var combinedTable = state.EventTables.FirstOrDefault(table => table.IsCombined);

        if (combinedTable is not null)
        {
            return state with { EventTables = state.EventTables.Add(newTable), ActiveTable = combinedTable };
        }

        combinedTable = new EventTableModel { IsCombined = true };

        return state with
        {
            EventTables = state.EventTables
                .Add(combinedTable)
                .Add(newTable),
            ActiveTable = combinedTable
        };
    }

    [ReducerMethod]
    public static EventTableState ReduceSetActiveTable(EventTableState state, EventTableAction.SetActiveTable action) =>
        state with { ActiveTable = state.EventTables.First(table => table.Id.Equals(action.TableId)) };

    //[ReducerMethod]
    //public static EventTableState ReduceCloseLog(EventTableState state, EventTableAction.CloseLog action) { }

    [ReducerMethod]
    public static EventTableState ReduceSetOrderBy(EventTableState state, EventTableAction.SetOrderBy action) =>
        state.OrderBy.Equals(action.OrderBy) ?
            SortDisplayEvents(state, null, true) :
            SortDisplayEvents(state, action.OrderBy, state.IsDescending);

    [ReducerMethod]
    public static EventTableState ReduceToggleLoading(EventTableState state, EventTableAction.ToggleLoading action)
    {
        var table = state.EventTables.FirstOrDefault(table => table.LogName.Equals(action.LogName));

        if (table is null) { return state; }

        return state with
        {
            EventTables = state.EventTables
                .Remove(table)
                .Add(table with { IsLoading = !table.IsLoading })
        };
    }

    [ReducerMethod(typeof(EventTableAction.ToggleSorting))]
    public static EventTableState ReduceToggleSorting(EventTableState state) =>
        SortDisplayEvents(state, state.OrderBy, !state.IsDescending);

    [ReducerMethod]
    public static EventTableState ReduceUpdateDisplayedEvents(EventTableState state,
        EventTableAction.UpdateDisplayedEvents action)
    {
        var updatedTables = state.EventTables;

        foreach (var log in action.ActiveLogs)
        {
            var table = updatedTables.First(table => table.LogName.Equals(log.Key));

            var readOnlyList = log.Value.ToList().AsReadOnly();

            updatedTables = updatedTables
                .Remove(table)
                .Add(table with {DisplayedEvents = log.Value.ToList().AsReadOnly()});
        }

        return SortDisplayEvents(state with { EventTables = updatedTables }, state.OrderBy, state.IsDescending);
    }

    private static IEnumerable<DisplayEventModel> GetCombinedEvents(EventTableState state)
    {
        IEnumerable<DisplayEventModel> combinedEvents = [];

        foreach (var log in state.EventTables.Where(table => table.IsCombined is false))
        {
            combinedEvents = combinedEvents.Concat(log.DisplayedEvents);
        }

        return combinedEvents;
    }

    private static EventTableState SortDisplayEvents(EventTableState state, ColumnName? orderBy, bool isDescending)
    {
        var newState = state;

        foreach (var logData in newState.EventTables)
        {
            newState = newState with
            {
                EventTables = newState.EventTables
                    .Remove(logData)
                    .Add(logData with
                    {
                        DisplayedEvents = (logData.IsCombined ? GetCombinedEvents(newState) : logData.DisplayedEvents)
                        .SortEvents(orderBy, isDescending)
                        .ToList()
                        .AsReadOnly()
                    })
            };
        }

        return newState with { OrderBy = orderBy, IsDescending = isDescending };
    }
}
