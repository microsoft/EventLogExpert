// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.EventLog;
using Fluxor;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.LogTable;

internal sealed class Reducers
{
    [ReducerMethod]
    public static LogTableState ReduceAddTable(LogTableState state, AddTableAction action)
    {
        var newTable = new LogView(action.LogData.Id)
        {
            FileName = action.LogData.Type == LogPathType.Channel ? null : action.LogData.Name,
            LogName = action.LogData.Name,
            LogPathType = action.LogData.Type,
            IsLoading = true
        };

        var counts = state.EventCountByLog.SetItem(newTable.Id, 0);

        if (state.EventTables.IsEmpty)
        {
            return ResetGroupCollapseIfActiveChanged(
                state with
                {
                    EventTables = state.EventTables.Add(newTable),
                    EventCountByLog = counts,
                    ActiveEventLogId = newTable.Id
                },
                state.ActiveEventLogId);
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

        combinedTable = new LogView(EventLogId.Create()) { IsCombined = true };

        return ResetGroupCollapseIfActiveChanged(
            state with
            {
                EventTables = state.EventTables
                    .Add(combinedTable)
                    .Add(newTable),
                EventCountByLog = counts,
                ActiveEventLogId = combinedTable.Id
            },
            state.ActiveEventLogId);
    }

    [ReducerMethod]
    public static LogTableState ReduceAppendTableEvents(LogTableState state, AppendTableEventsAction action)
    {
        var table = state.EventTables.FirstOrDefault(t => action.LogId == t.Id);

        if (table is null || table.IsCombined || action.Events.Count == 0) { return state; }

        var merged = ResolvedEventOrdering.MergeSorted(
            state.DisplayedEvents,
            action.Events,
            GetEffectiveOrderBy(state.OrderBy),
            state.IsDescending,
            state.GroupBy,
            state.IsGroupDescending);

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
    public static LogTableState ReduceAppendTableEventsBatch(
        LogTableState state,
        AppendTableEventsBatchAction action)
    {
        if (action.EventsByLog.Count == 0) { return state; }

        // Skip batches for closed logs: avoid resurrecting events and stale counts.
        int totalNew = 0;
        var combinedBatch = new List<ResolvedEvent>();
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

        var merged = ResolvedEventOrdering.MergeSorted(
            state.DisplayedEvents,
            combinedBatch,
            GetEffectiveOrderBy(state.OrderBy),
            state.IsDescending,
            state.GroupBy,
            state.IsGroupDescending);

        return state with
        {
            DisplayedEvents = merged,
            EventTables = updatedTables,
            EventCountByLog = counts
        };
    }

    [ReducerMethod(typeof(CloseAllLogsAction))]
    public static LogTableState ReduceCloseAll(LogTableState state) =>
        ResetGroupCollapse(state with
        {
            EventTables = [],
            DisplayedEvents = [],
            EventCountByLog = ImmutableDictionary<EventLogId, int>.Empty,
            ActiveEventLogId = null
        });

    [ReducerMethod]
    public static LogTableState ReduceCloseLog(LogTableState state, CloseLogAction action)
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
                return ResetGroupCollapseIfActiveChanged(
                    state with
                    {
                        EventTables = [],
                        DisplayedEvents = [],
                        EventCountByLog = ImmutableDictionary<EventLogId, int>.Empty,
                        ActiveEventLogId = null
                    },
                    state.ActiveEventLogId);
            case 1:
                {
                    var soleRemaining = remainingPerLogTables[0];
                    var filtered = FilterByOwningLog(state.DisplayedEvents, soleRemaining.LogName);

                    return ResetGroupCollapseIfActiveChanged(
                        state with
                        {
                            EventTables = remainingPerLogTables,
                            DisplayedEvents = filtered,
                            EventCountByLog = counts,
                            ActiveEventLogId = soleRemaining.Id
                        },
                        state.ActiveEventLogId);
                }
            default:
                {
                    var combinedTable = new LogView(EventLogId.Create()) { IsCombined = true };
                    var filtered = FilterOutOwningLog(state.DisplayedEvents, closingTable.LogName);

                    return ResetGroupCollapseIfActiveChanged(
                        state with
                        {
                            EventTables = remainingPerLogTables.Insert(0, combinedTable),
                            DisplayedEvents = filtered,
                            EventCountByLog = counts,
                            ActiveEventLogId = remainingPerLogTables.Any(t => t.Id == state.ActiveEventLogId) ?
                                state.ActiveEventLogId : combinedTable.Id
                        },
                        state.ActiveEventLogId);
                }
        }
    }

    [ReducerMethod]
    public static LogTableState ReduceLoadColumnsCompleted(
        LogTableState state,
        LoadColumnsCompletedAction action)
    {
        var updated = state with
        {
            Columns = action.LoadedColumns,
            ColumnWidths = action.ColumnWidths,
            ColumnOrder = action.ColumnOrder
        };

        bool groupColumnHidden = updated.GroupBy is { } groupColumn &&
            (!action.LoadedColumns.TryGetValue(groupColumn, out bool isVisible) || !isVisible);

        if (!groupColumnHidden) { return updated; }

        return updated with
        {
            GroupBy = null,
            IsGroupDescending = false,
            GroupsCollapsedByDefault = false,
            GroupCollapseOverrides = ImmutableHashSet.Create<string>(StringComparer.Ordinal),
            DisplayedEvents = updated.DisplayedEvents.SortEvents(
                GetEffectiveOrderBy(updated.OrderBy),
                updated.IsDescending)
        };
    }

    [ReducerMethod]
    public static LogTableState ReduceReorderColumn(LogTableState state, ReorderColumnAction action)
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
    public static LogTableState ReduceSetActiveTable(LogTableState state, SetActiveTableAction action)
    {
        var activeTable = state.EventTables.FirstOrDefault(table => table.Id == action.LogId);

        if (activeTable is null) { return state; }

        return ResetGroupCollapseIfActiveChanged(
            state with { ActiveEventLogId = activeTable.Id },
            state.ActiveEventLogId);
    }

    [ReducerMethod]
    public static LogTableState ReduceSetAllGroupsCollapsed(
        LogTableState state,
        SetAllGroupsCollapsedAction action)
    {
        if (state.GroupBy is null) { return state; }

        return state.GroupsCollapsedByDefault == action.Collapsed && state.GroupCollapseOverrides.IsEmpty ?
            state :
            state with
            {
                GroupsCollapsedByDefault = action.Collapsed,
                GroupCollapseOverrides = ImmutableHashSet.Create<string>(StringComparer.Ordinal)
            };
    }

    [ReducerMethod]
    public static LogTableState ReduceSetColumnWidth(LogTableState state, SetColumnWidthAction action) =>
        state with { ColumnWidths = state.ColumnWidths.SetItem(action.ColumnName, action.Width) };

    [ReducerMethod]
    public static LogTableState ReduceSetGroupBy(LogTableState state, SetGroupByAction action)
    {
        if (state.GroupBy == action.GroupBy) { return state; }

        return state with
        {
            GroupBy = action.GroupBy,
            IsGroupDescending = false,
            GroupsCollapsedByDefault = false,
            GroupCollapseOverrides = ImmutableHashSet.Create<string>(StringComparer.Ordinal),
            DisplayedEvents = state.DisplayedEvents.SortEvents(
                GetEffectiveOrderBy(state.OrderBy),
                state.IsDescending,
                action.GroupBy)
        };
    }

    [ReducerMethod]
    public static LogTableState ReduceSetOrderBy(LogTableState state, SetOrderByAction action) =>
        state.OrderBy.Equals(action.OrderBy) ?
            SortDisplayEvents(state, null, true) :
            SortDisplayEvents(state, action.OrderBy, state.IsDescending);

    [ReducerMethod]
    public static LogTableState ReduceToggleGroupCollapsed(
        LogTableState state,
        ToggleGroupCollapsedAction action)
    {
        if (state.GroupBy is null) { return state; }

        return state with
        {
            GroupCollapseOverrides = state.GroupCollapseOverrides.Contains(action.GroupKey) ?
                state.GroupCollapseOverrides.Remove(action.GroupKey) :
                state.GroupCollapseOverrides.Add(action.GroupKey)
        };
    }

    [ReducerMethod(typeof(ToggleGroupSortingAction))]
    public static LogTableState ReduceToggleGroupSorting(LogTableState state)
    {
        if (state.GroupBy is null) { return state; }

        bool isGroupDescending = !state.IsGroupDescending;

        return state with
        {
            IsGroupDescending = isGroupDescending,
            DisplayedEvents = state.DisplayedEvents.SortEvents(
                GetEffectiveOrderBy(state.OrderBy),
                state.IsDescending,
                state.GroupBy,
                isGroupDescending)
        };
    }

    [ReducerMethod]
    public static LogTableState ReduceToggleLoading(LogTableState state, ToggleLoadingAction action)
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

    [ReducerMethod(typeof(ToggleSortingAction))]
    public static LogTableState ReduceToggleSorting(LogTableState state) =>
        SortDisplayEvents(state, state.OrderBy, !state.IsDescending);

    [ReducerMethod]
    public static LogTableState ReduceUpdateDisplayedEvents(
        LogTableState state,
        UpdateDisplayedEventsAction action)
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

        var concatenated = new List<ResolvedEvent>(totalCount);

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

        var sorted = concatenated.SortEvents(
            GetEffectiveOrderBy(state.OrderBy),
            state.IsDescending,
            state.GroupBy,
            state.IsGroupDescending);

        return state with
        {
            DisplayedEvents = sorted,
            EventTables = tables,
            EventCountByLog = counts
        };
    }

    [ReducerMethod]
    public static LogTableState ReduceUpdateTable(LogTableState state, UpdateTableAction action)
    {
        var table = state.EventTables.FirstOrDefault(t => action.LogId == t.Id);

        if (table is null || table.IsCombined) { return state; }

        // UpdateTable carries the full slice; drop existing rows before merging.
        bool hasExistingRowsForLog = state.EventCountByLog.TryGetValue(table.Id, out int existingCount) && existingCount > 0;

        var existing = hasExistingRowsForLog
            ? FilterOutOwningLog(state.DisplayedEvents, table.LogName)
            : state.DisplayedEvents;

        var merged = ResolvedEventOrdering.MergeSorted(
            existing,
            action.Events,
            GetEffectiveOrderBy(state.OrderBy),
            state.IsDescending,
            state.GroupBy,
            state.IsGroupDescending);

        var updatedTable = SetComputerNameIfFirstEvent(table, action.Events) with { IsLoading = false };
        var counts = state.EventCountByLog.SetItem(table.Id, action.Events.Count);

        return state with
        {
            DisplayedEvents = merged,
            EventTables = state.EventTables.Replace(table, updatedTable),
            EventCountByLog = counts
        };
    }

    private static IReadOnlyList<ResolvedEvent> FilterByOwningLog(
        IReadOnlyList<ResolvedEvent> events,
        string owningLog)
    {
        var filtered = new List<ResolvedEvent>(events.Count);

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

    private static IReadOnlyList<ResolvedEvent> FilterOutOwningLog(
        IReadOnlyList<ResolvedEvent> events,
        string owningLog)
    {
        var filtered = new List<ResolvedEvent>(events.Count);

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

    private static LogTableState ResetGroupCollapse(LogTableState state) =>
        state is { GroupsCollapsedByDefault: false, GroupCollapseOverrides.IsEmpty: true }
            ? state
            : state with
            {
                GroupsCollapsedByDefault = false,
                GroupCollapseOverrides = ImmutableHashSet.Create<string>(StringComparer.Ordinal)
            };

    private static LogTableState ResetGroupCollapseIfActiveChanged(
        LogTableState updated,
        EventLogId? previousActiveId) =>
        updated.ActiveEventLogId == previousActiveId ? updated : ResetGroupCollapse(updated);

    private static LogView SetComputerNameIfFirstEvent(LogView table, IReadOnlyList<ResolvedEvent> newEvents)
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

    private static LogTableState SortDisplayEvents(LogTableState state, ColumnName? orderBy, bool isDescending)
    {
        var effectiveOrderBy = GetEffectiveOrderBy(orderBy);

        return state with
        {
            DisplayedEvents = state.DisplayedEvents.SortEvents(
                effectiveOrderBy,
                isDescending,
                state.GroupBy,
                state.IsGroupDescending),
            OrderBy = orderBy,
            IsDescending = isDescending
        };
    }
}
