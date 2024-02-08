// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Models;
using EventLogExpert.UI.Models;
using Fluxor;

namespace EventLogExpert.UI.Store.EventTable;

public sealed class EventTableReducers
{
    [ReducerMethod]
    public static EventTableState ReduceAddTable(EventTableState state, EventTableAction.AddTable action)
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
            return state with { EventTables = state.EventTables.Add(newTable), ActiveTableId = newTable.Id };
        }

        var combinedTable = state.EventTables.FirstOrDefault(table => table.IsCombined);

        if (combinedTable is not null)
        {
            return state with { EventTables = state.EventTables.Add(newTable), ActiveTableId = combinedTable.Id };
        }

        combinedTable = new EventTableModel { IsCombined = true };

        return state with
        {
            EventTables = state.EventTables
                .Add(combinedTable)
                .Add(newTable),
            ActiveTableId = combinedTable.Id
        };
    }

    [ReducerMethod(typeof(EventTableAction.CloseAll))]
    public static EventTableState ReduceCloseAll(EventTableState state) =>
        state with { EventTables = [], ActiveTableId = null };

    [ReducerMethod]
    public static EventTableState ReduceSetActiveTable(EventTableState state, EventTableAction.SetActiveTable action) =>
        state with { ActiveTableId = state.EventTables.First(table => table.Id.Equals(action.TableId)).Id };

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
        List<EventTableModel> updatedTables = [];

        foreach (var table in state.EventTables)
        {
            if (table.IsCombined) { continue; }

            var currentActiveLog = action.ActiveLogs.First(log => table.LogName.Equals(log.Key)).Value;

            updatedTables.Add(table.DisplayedEvents.Count == currentActiveLog.Count() ? table : table with
            {
                DisplayedEvents = currentActiveLog.SortEvents(state.OrderBy, state.IsDescending)
                    .ToList()
                    .AsReadOnly()
            });
        }

        if (updatedTables.Count > 1)
        {
            updatedTables.Add(
                state.EventTables.First(table => table.IsCombined) with
                {
                    DisplayedEvents = GetCombinedEvents(action.ActiveLogs.Values.Select(log => log))
                        .SortEvents(state.OrderBy ?? ColumnName.DateAndTime, state.IsDescending)
                        .ToList()
                        .AsReadOnly()
                });
        }

        return state with { EventTables = [.. updatedTables] };
    }

    private static IEnumerable<DisplayEventModel> GetCombinedEvents(IEnumerable<IEnumerable<DisplayEventModel>> eventLists)
    {
        List<DisplayEventModel> combinedEvents = [];

        foreach (var eventList in eventLists)
        {
            combinedEvents.AddRange(eventList);
        }

        return combinedEvents;
    }

    private static EventTableState SortDisplayEvents(EventTableState state, ColumnName? orderBy, bool isDescending)
    {
        List<EventTableModel> updatedTables = [];

        updatedTables.AddRange(
            state.EventTables.Select(
                logData => logData with
                {
                    DisplayedEvents = logData.DisplayedEvents.SortEvents(orderBy, isDescending)
                        .ToList()
                        .AsReadOnly()
                }));

        return state with { EventTables = [.. updatedTables], OrderBy = orderBy, IsDescending = isDescending };
    }
}
