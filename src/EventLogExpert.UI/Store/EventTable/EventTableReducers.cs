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
        var newState = state;

        for (var i = 0; i < newState.EventTables.Count; i++)
        {
            if (newState.EventTables[i].IsCombined) { continue; }

            var currentActiveLog = action.ActiveLogs.First(log => newState.EventTables[i].LogName.Equals(log.Key)).Value;

            if (newState.EventTables[i].DisplayedEvents.Count == currentActiveLog.Count()) { continue; }

            newState = newState with
            {
                EventTables = newState.EventTables
                    .Remove(newState.EventTables[i])
                    .Add(newState.EventTables[i] with
                    {
                        DisplayedEvents = currentActiveLog.SortEvents(state.OrderBy, state.IsDescending)
                            .ToList()
                            .AsReadOnly()
                    })
            };
        }

        if (newState.EventTables.Count <= 1)
        {
            return newState;
        }

        var table = newState.EventTables.First(table => table.IsCombined);

        return newState with
        {
            EventTables = newState.EventTables
                .Remove(table)
                .Add(table with
                {
                    DisplayedEvents = GetCombinedEvents(action.ActiveLogs.Values.Select(log => log))
                        .SortEvents((state.OrderBy ?? ColumnName.DateAndTime), state.IsDescending)
                        .ToList()
                        .AsReadOnly()
                })
        };
    }

    private static IEnumerable<DisplayEventModel> GetCombinedEvents(IEnumerable<IEnumerable<DisplayEventModel>> eventLists)
    {
        IEnumerable<DisplayEventModel> combinedEvents = [];

        foreach (var eventList in eventLists)
        {
            combinedEvents = combinedEvents.Concat(eventList);
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
                        DisplayedEvents = logData.DisplayedEvents
                            .SortEvents(orderBy, isDescending)
                            .ToList()
                            .AsReadOnly()
                    })
            };
        }

        return newState with { OrderBy = orderBy, IsDescending = isDescending };
    }
}
