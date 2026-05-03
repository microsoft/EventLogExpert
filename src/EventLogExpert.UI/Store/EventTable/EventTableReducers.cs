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

        var counts = state.EventCountByLog.SetItem(newTable.Id, 0);

        if (state.EventTables.IsEmpty)
        {
            return state with
            {
                EventTables = state.EventTables.Add(newTable),
                EventCountByLog = counts,
                ActiveEventLogId = newTable.Id
            };
        }

        var combinedTable = state.EventTables.FirstOrDefault(table => table.IsCombined);

        if (combinedTable is not null)
        {
            return state with
            {
                EventTables = state.EventTables.Add(newTable),
                EventCountByLog = counts
            };
        }

        combinedTable = new EventTableModel(EventLogId.Create()) { IsCombined = true };

        return state with
        {
            EventTables = state.EventTables
                .Add(combinedTable)
                .Add(newTable),
            EventCountByLog = counts,
            ActiveEventLogId = combinedTable.Id
        };
    }

    [ReducerMethod]
    public static EventTableState ReduceAppendTableEvents(EventTableState state, EventTableAction.AppendTableEvents action)
    {
        var table = state.EventTables.FirstOrDefault(t => action.LogId == t.Id);

        if (table is null || table.IsCombined || action.Events.Count == 0) { return state; }

        var merged = FilterMethods.MergeSorted(
            state.DisplayedEvents,
            action.Events,
            GetEffectiveOrderBy(state.OrderBy),
            state.IsDescending);

        var updatedTable = SetComputerNameIfFirstEvent(table, action.Events);
        var counts = IncrementCount(state.EventCountByLog, table.Id, action.Events.Count);

        return state with
        {
            DisplayedEvents = merged,
            EventTables = ReferenceEquals(updatedTable, table) ?
                state.EventTables :
                state.EventTables.Replace(table, updatedTable),
            EventCountByLog = counts
        };
    }

    [ReducerMethod]
    public static EventTableState ReduceAppendTableEventsBatch(
        EventTableState state,
        EventTableAction.AppendTableEventsBatch action)
    {
        if (action.EventsByLog.Count == 0) { return state; }

        // Skip batches for closed logs: avoid resurrecting events and stale counts.
        int totalNew = 0;
        var combinedBatch = new List<DisplayEventModel>();
        var counts = state.EventCountByLog;
        var updatedTables = state.EventTables;

        foreach (var (logId, events) in action.EventsByLog)
        {
            if (events.Count == 0) { continue; }

            var table = updatedTables.FirstOrDefault(t => t.Id == logId);

            if (table is null || table.IsCombined) { continue; }

            combinedBatch.AddRange(events);
            counts = IncrementCount(counts, logId, events.Count);
            totalNew += events.Count;

            var updatedTable = SetComputerNameIfFirstEvent(table, events);

            if (!ReferenceEquals(updatedTable, table))
            {
                updatedTables = updatedTables.Replace(table, updatedTable);
            }
        }

        if (totalNew == 0) { return state; }

        var merged = FilterMethods.MergeSorted(
            state.DisplayedEvents,
            combinedBatch,
            GetEffectiveOrderBy(state.OrderBy),
            state.IsDescending);

        return state with
        {
            DisplayedEvents = merged,
            EventTables = updatedTables,
            EventCountByLog = counts
        };
    }

    [ReducerMethod(typeof(EventTableAction.CloseAll))]
    public static EventTableState ReduceCloseAll(EventTableState state) =>
        state with
        {
            EventTables = [],
            DisplayedEvents = [],
            EventCountByLog = ImmutableDictionary<EventLogId, int>.Empty,
            ActiveEventLogId = null
        };

    [ReducerMethod]
    public static EventTableState ReduceCloseLog(EventTableState state, EventTableAction.CloseLog action)
    {
        var closingTable = state.EventTables.FirstOrDefault(table => table.Id == action.LogId);

        if (closingTable is null || closingTable.IsCombined) { return state; }

        var remainingPerLogTables = state.EventTables
            .Where(table => table.Id != action.LogId && !table.IsCombined)
            .ToImmutableList();

        var counts = state.EventCountByLog.Remove(action.LogId);

        switch (remainingPerLogTables.Count)
        {
            case <= 0:
                return state with
                {
                    EventTables = [],
                    DisplayedEvents = [],
                    EventCountByLog = ImmutableDictionary<EventLogId, int>.Empty,
                    ActiveEventLogId = null
                };
            case 1:
                {
                    var soleRemaining = remainingPerLogTables[0];
                    var filtered = FilterByOwningLog(state.DisplayedEvents, soleRemaining.LogName);

                    return state with
                    {
                        EventTables = remainingPerLogTables,
                        DisplayedEvents = filtered,
                        EventCountByLog = counts,
                        ActiveEventLogId = soleRemaining.Id
                    };
                }
            default:
                {
                    var combinedTable = new EventTableModel(EventLogId.Create()) { IsCombined = true };
                    var filtered = FilterOutOwningLog(state.DisplayedEvents, closingTable.LogName);

                    return state with
                    {
                        EventTables = remainingPerLogTables.Insert(0, combinedTable),
                        DisplayedEvents = filtered,
                        EventCountByLog = counts,
                        ActiveEventLogId = remainingPerLogTables.Any(t => t.Id == state.ActiveEventLogId) ?
                            state.ActiveEventLogId : combinedTable.Id
                    };
                }
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

    [ReducerMethod]
    public static EventTableState ReduceUpdateDisplayedEvents(
        EventTableState state,
        EventTableAction.UpdateDisplayedEvents action)
    {
        // Skip log ids absent from EventTables: log closed while filter ran.
        var tablesById = state.EventTables
            .Where(table => !table.IsCombined)
            .ToDictionary(table => table.Id);

        // Logs absent from ActiveLogs keep their current rows (stale-snapshot protection).
        var logNamesBeingReplaced = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (logId, _) in action.ActiveLogs)
        {
            if (tablesById.TryGetValue(logId, out var table))
            {
                logNamesBeingReplaced.Add(table.LogName);
            }
        }

        // Defensive: orphan rows whose OwningLog has no current table get dropped.
        var currentLogNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var table in tablesById.Values)
        {
            currentLogNames.Add(table.LogName);
        }

        int preservedCount = 0;

        for (int eventIndex = 0; eventIndex < state.DisplayedEvents.Count; eventIndex++)
        {
            var existing = state.DisplayedEvents[eventIndex];

            if (currentLogNames.Contains(existing.OwningLog) &&
                !logNamesBeingReplaced.Contains(existing.OwningLog))
            {
                preservedCount++;
            }
        }

        int totalCount = preservedCount;

        foreach (var (logId, events) in action.ActiveLogs)
        {
            if (tablesById.ContainsKey(logId)) { totalCount += events.Count; }
        }

        var concatenated = new List<DisplayEventModel>(totalCount);

        for (int eventIndex = 0; eventIndex < state.DisplayedEvents.Count; eventIndex++)
        {
            var existing = state.DisplayedEvents[eventIndex];

            if (currentLogNames.Contains(existing.OwningLog) &&
                !logNamesBeingReplaced.Contains(existing.OwningLog))
            {
                concatenated.Add(existing);
            }
        }

        var counts = state.EventCountByLog;
        var tables = state.EventTables;

        foreach (var (logId, events) in action.ActiveLogs)
        {
            if (!tablesById.TryGetValue(logId, out var table)) { continue; }

            concatenated.AddRange(events);
            counts = counts.SetItem(logId, events.Count);

            // Filter-clear may be the first event-bearing path for a log; latch ComputerName.
            var updatedTable = SetComputerNameIfFirstEvent(table, events);

            if (!ReferenceEquals(updatedTable, table))
            {
                tables = tables.Replace(table, updatedTable);
            }
        }

        var sorted = concatenated.SortEvents(GetEffectiveOrderBy(state.OrderBy), state.IsDescending);

        return state with
        {
            DisplayedEvents = sorted,
            EventTables = tables,
            EventCountByLog = counts
        };
    }

    [ReducerMethod]
    public static EventTableState ReduceUpdateTable(EventTableState state, EventTableAction.UpdateTable action)
    {
        var table = state.EventTables.FirstOrDefault(t => action.LogId == t.Id);

        if (table is null || table.IsCombined) { return state; }

        // UpdateTable carries the full slice; drop existing rows before merging.
        bool hasExistingRowsForLog = state.EventCountByLog.TryGetValue(table.Id, out int existingCount) && existingCount > 0;

        var existing = hasExistingRowsForLog
            ? FilterOutOwningLog(state.DisplayedEvents, table.LogName)
            : state.DisplayedEvents;

        var merged = FilterMethods.MergeSorted(
            existing,
            action.Events,
            GetEffectiveOrderBy(state.OrderBy),
            state.IsDescending);

        var updatedTable = SetComputerNameIfFirstEvent(table, action.Events) with { IsLoading = false };
        var counts = state.EventCountByLog.SetItem(table.Id, action.Events.Count);

        return state with
        {
            DisplayedEvents = merged,
            EventTables = state.EventTables.Replace(table, updatedTable),
            EventCountByLog = counts
        };
    }

    private static IReadOnlyList<DisplayEventModel> FilterByOwningLog(
        IReadOnlyList<DisplayEventModel> events,
        string owningLog)
    {
        var filtered = new List<DisplayEventModel>(events.Count);

        for (int eventIndex = 0; eventIndex < events.Count; eventIndex++)
        {
            var current = events[eventIndex];

            if (string.Equals(current.OwningLog, owningLog, StringComparison.Ordinal))
            {
                filtered.Add(current);
            }
        }

        return filtered.AsReadOnly();
    }

    private static IReadOnlyList<DisplayEventModel> FilterOutOwningLog(
        IReadOnlyList<DisplayEventModel> events,
        string owningLog)
    {
        var filtered = new List<DisplayEventModel>(events.Count);

        for (int eventIndex = 0; eventIndex < events.Count; eventIndex++)
        {
            var current = events[eventIndex];

            if (!string.Equals(current.OwningLog, owningLog, StringComparison.Ordinal))
            {
                filtered.Add(current);
            }
        }

        return filtered.AsReadOnly();
    }

    private static ColumnName GetEffectiveOrderBy(ColumnName? orderBy) =>
        orderBy ?? ColumnName.DateAndTime;

    private static ImmutableDictionary<EventLogId, int> IncrementCount(
        ImmutableDictionary<EventLogId, int> counts,
        EventLogId logId,
        int delta)
    {
        int current = counts.TryGetValue(logId, out int existing) ? existing : 0;
        return counts.SetItem(logId, current + delta);
    }

    private static EventTableModel SetComputerNameIfFirstEvent(EventTableModel table, IReadOnlyList<DisplayEventModel> newEvents)
    {
        if (!string.IsNullOrEmpty(table.ComputerName) || newEvents.Count == 0) { return table; }

        // events[0].ComputerName may be empty (resolver miss); scan for first non-empty.
        for (int i = 0; i < newEvents.Count; i++)
        {
            var candidate = newEvents[i];

            if (!string.IsNullOrEmpty(candidate.ComputerName))
            {
                return table with { ComputerName = candidate.ComputerName };
            }
        }

        return table;
    }

    private static EventTableState SortDisplayEvents(EventTableState state, ColumnName? orderBy, bool isDescending)
    {
        var effectiveOrderBy = GetEffectiveOrderBy(orderBy);

        return state with
        {
            DisplayedEvents = state.DisplayedEvents.SortEvents(effectiveOrderBy, isDescending),
            OrderBy = orderBy,
            IsDescending = isDescending
        };
    }
}
