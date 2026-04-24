// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Models;
using EventLogExpert.UI.Models;
using Fluxor;
using System.Collections.Immutable;

namespace EventLogExpert.UI.Store.EventTable;

public sealed class EventTableReducers
{
    [ReducerMethod]
    public static EventTableState ReduceAddTable(EventTableState state, EventTableAction.AddTable action)
    {
        var newTable = new EventTableModel(action.LogData.Id)
        {
            FileName = action.LogData.Type == PathType.LogName ? null : action.LogData.Name,
            LogName = action.LogData.Name,
            PathType = action.LogData.Type,
            IsLoading = true
        };

        if (state.EventTables.IsEmpty)
        {
            return state with
            {
                EventTables = state.EventTables.Add(newTable),
                ActiveEventLogId = newTable.Id
            };
        }

        var combinedTable = state.EventTables.FirstOrDefault(table => table.IsCombined);

        if (combinedTable is not null)
        {
            return state with { EventTables = state.EventTables.Add(newTable) };
        }

        combinedTable = new EventTableModel(EventLogId.Create()) { IsCombined = true };

        return state with
        {
            EventTables = state.EventTables
                .Add(combinedTable)
                .Add(newTable),
            ActiveEventLogId = combinedTable.Id
        };
    }

    [ReducerMethod]
    public static EventTableState ReduceAppendTableEvents(EventTableState state, EventTableAction.AppendTableEvents action)
    {
        var table = state.EventTables.FirstOrDefault(t => action.LogId == t.Id);

        if (table is null) { return state; }

        var merged = FilterMethods.MergeSorted(
            table.DisplayedEvents,
            action.Events,
            state.OrderBy,
            state.IsDescending);

        return state with
        {
            EventTables = state.EventTables.Replace(table, table with
            {
                DisplayedEvents = merged
            })
        };
    }

    [ReducerMethod]
    public static EventTableState ReduceAppendTableEventsBatch(
        EventTableState state,
        EventTableAction.AppendTableEventsBatch action)
    {
        if (action.EventsByLog.Count == 0) { return state; }

        var updatedTables = new List<EventTableModel>(state.EventTables.Count);
        var changed = false;

        foreach (var table in state.EventTables)
        {
            if (!action.EventsByLog.TryGetValue(table.Id, out var newEvents) || newEvents.Count == 0)
            {
                updatedTables.Add(table);

                continue;
            }

            var merged = FilterMethods.MergeSorted(
                table.DisplayedEvents,
                newEvents,
                state.OrderBy,
                state.IsDescending);

            updatedTables.Add(table with { DisplayedEvents = merged });
            changed = true;
        }

        if (!changed) { return state; }

        return state with { EventTables = [.. updatedTables] };
    }

    [ReducerMethod(typeof(EventTableAction.CloseAll))]
    public static EventTableState ReduceCloseAll(EventTableState state) =>
        state with { EventTables = [], ActiveEventLogId = null };

    [ReducerMethod]
    public static EventTableState ReduceCloseLog(EventTableState state, EventTableAction.CloseLog action)
    {
        var updatedTables = state.EventTables
            .Where(table => table.Id != action.LogId && !table.IsCombined)
            .ToImmutableList();

        switch (updatedTables.Count)
        {
            case <= 0: return state with { EventTables = [], ActiveEventLogId = null };
            case <= 1:
                return state with
                {
                    EventTables = updatedTables,
                    ActiveEventLogId = updatedTables.First().Id
                };
            default:
                var combinedTable = new EventTableModel(EventLogId.Create())
                {
                    IsCombined = true,
                    DisplayedEvents = GetCombinedEvents(
                        updatedTables.Select(log => log.DisplayedEvents),
                        state.OrderBy ?? ColumnName.DateAndTime,
                        state.IsDescending)
                };

                return state with
                {
                    EventTables = updatedTables.Add(combinedTable),
                    ActiveEventLogId =
                    updatedTables.FirstOrDefault(table => table.Id == state.ActiveEventLogId) is not null ?
                        state.ActiveEventLogId : combinedTable.Id
                };
        }
    }

    [ReducerMethod]
    public static EventTableState ReduceLoadColumnsCompleted(
        EventTableState state,
        EventTableAction.LoadColumnsCompleted action) =>
        state with
        {
            Columns = action.LoadedColumns.ToImmutableDictionary(),
            ColumnWidths = action.ColumnWidths.ToImmutableDictionary(),
            ColumnOrder = action.ColumnOrder
        };

    [ReducerMethod]
    public static EventTableState ReduceReorderColumn(EventTableState state, EventTableAction.ReorderColumn action)
    {
        var order = state.ColumnOrder;

        if (!order.Contains(action.ColumnName) || !order.Contains(action.TargetColumn) ||
            action.ColumnName == action.TargetColumn)
        {
            return state;
        }

        order = order.Remove(action.ColumnName);
        var targetIndex = order.IndexOf(action.TargetColumn);
        var insertIndex = action.InsertAfter ? targetIndex + 1 : targetIndex;
        order = order.Insert(insertIndex, action.ColumnName);

        return state with { ColumnOrder = order };
    }

    [ReducerMethod]
    public static EventTableState ReduceSetActiveTable(EventTableState state, EventTableAction.SetActiveTable action)
    {
        var activeTable = state.EventTables.FirstOrDefault(table => table.Id == action.LogId);

        if (activeTable is null) { return state; }

        return state with { ActiveEventLogId = activeTable.Id };
    }

    [ReducerMethod]
    public static EventTableState ReduceSetColumnWidth(EventTableState state, EventTableAction.SetColumnWidth action) =>
        state with { ColumnWidths = state.ColumnWidths.SetItem(action.ColumnName, action.Width) };

    [ReducerMethod]
    public static EventTableState ReduceSetOrderBy(EventTableState state, EventTableAction.SetOrderBy action) =>
        state.OrderBy.Equals(action.OrderBy) ?
            SortDisplayEvents(state, null, true) :
            SortDisplayEvents(state, action.OrderBy, state.IsDescending);

    [ReducerMethod]
    public static EventTableState ReduceToggleLoading(EventTableState state, EventTableAction.ToggleLoading action)
    {
        var table = state.EventTables.FirstOrDefault(table => table.Id == action.LogId);

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

    [ReducerMethod(typeof(EventTableAction.UpdateCombinedEvents))]
    public static EventTableState ReduceUpdateCombinedEvents(EventTableState state)
    {
        if (state.EventTables.Count <= 1) { return state; }

        var nonCombinedTables = state.EventTables.Where(table => !table.IsCombined);

        if (nonCombinedTables.All(table => table.IsLoading)) { return state; }

        var existingCombinedTable = state.EventTables.FirstOrDefault(table => table.IsCombined);

        if (existingCombinedTable is null) { return state; }

        var combinedEvents = GetCombinedEvents(
            state.EventTables
                .Where(table => !table.IsCombined)
                .Select(table => table.DisplayedEvents),
            state.OrderBy ?? ColumnName.DateAndTime,
            state.IsDescending);

        if (combinedEvents.Count == existingCombinedTable.DisplayedEvents.Count &&
            combinedEvents.Select(e => e.RecordId)
                .SequenceEqual(existingCombinedTable.DisplayedEvents.Select(e => e.RecordId)))
        {
            return state;
        }

        return state with
        {
            EventTables = state.EventTables
                .Remove(existingCombinedTable)
                .Add(existingCombinedTable with { DisplayedEvents = combinedEvents })
        };
    }

    [ReducerMethod]
    public static EventTableState ReduceUpdateDisplayedEvents(
        EventTableState state,
        EventTableAction.UpdateDisplayedEvents action)
    {
        List<EventTableModel> updatedTables = [];

        foreach (var table in state.EventTables)
        {
            if (table.IsCombined)
            {
                updatedTables.Add(table);

                continue;
            }

            if (!action.ActiveLogs.TryGetValue(table.Id, out var currentActiveLog))
            {
                updatedTables.Add(table);

                continue;
            }

            updatedTables.Add(table with
            {
                DisplayedEvents = currentActiveLog.SortEvents(state.OrderBy, state.IsDescending)
            });
        }

        return state with { EventTables = [.. updatedTables] };
    }

    [ReducerMethod]
    public static EventTableState ReduceUpdateTable(EventTableState state, EventTableAction.UpdateTable action)
    {
        var table = state.EventTables.FirstOrDefault(t => action.LogId == t.Id);

        if (table is null) { return state; }

        return state with
        {
            EventTables = state.EventTables.Replace(table, table with
            {
                DisplayedEvents = action.Events.SortEvents(state.OrderBy, state.IsDescending),
                IsLoading = false
            })
        };
    }

    private static IReadOnlyList<DisplayEventModel> GetCombinedEvents(
        IEnumerable<IEnumerable<DisplayEventModel>> eventLists,
        ColumnName? orderBy = null,
        bool isDescending = false)
    {
        List<DisplayEventModel> combinedEvents = [];

        foreach (var eventList in eventLists)
        {
            combinedEvents.AddRange(eventList);
        }

        // Sort in-place instead of creating a new list
        combinedEvents.Sort(FilterMethods.GetComparer(orderBy, isDescending));

        return combinedEvents.AsReadOnly();
    }

    private static EventTableState SortDisplayEvents(EventTableState state, ColumnName? orderBy, bool isDescending)
    {
        List<EventTableModel> updatedTables = [];

        updatedTables.AddRange(
            state.EventTables.Select(
                logData => logData with
                {
                    DisplayedEvents = logData.DisplayedEvents.SortEvents(orderBy, isDescending)
                }));

        return state with
        {
            EventTables = [.. updatedTables],
            OrderBy = orderBy,
            IsDescending = isDescending
        };
    }
}
