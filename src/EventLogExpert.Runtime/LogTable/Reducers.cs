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

        var combinedTable = state.EventTables.FirstOrDefault(table => table.GroupId?.IsAll == true);

        if (combinedTable is not null)
        {
            return state with
            {
                EventTables = state.EventTables.Add(newTable),
                EventCountByLog = counts
            };
        }

        combinedTable = new LogView(EventLogId.Create()) { GroupId = LogTabGroupId.AllLogs };

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

        int postCount = state.PerLogEvents.ContainsKey(table.Id)
            ? state.PerLogEvents.Count
            : state.PerLogEvents.Count + 1;
        var context = EffectiveSortContext(
            state.OrderBy, state.IsDescending, state.GroupBy, state.IsGroupDescending, postCount);

        var perLog = AppendToLog(state.PerLogEvents, table.Id, action.Events, context);
        perLog = ReconcileToLogCount(perLog, state);
        var updatedTable = SetComputerNameIfFirstEvent(table, action.Events);
        var counts = IncrementCount(state.EventCountByLog, table.Id, action.Events.Count);

        return state with
        {
            PerLogEvents = perLog,
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
        var perLog = state.PerLogEvents;
        var perLogVersion = state.PerLogListVersion;
        var counts = state.EventCountByLog;
        var updatedTables = state.EventTables;

        // Count new logs first so appends use the post-batch sort context (no boundary re-sort).
        int newLogs = 0;

        foreach (var (logId, events) in action.EventsByLog)
        {
            if (events.Count == 0 || perLog.ContainsKey(logId)) { continue; }

            var table = state.EventTables.FirstOrDefault(t => t.Id == logId);
            if (table is not null && !table.IsCombined) { newLogs++; }
        }

        var context = EffectiveSortContext(
            state.OrderBy, state.IsDescending, state.GroupBy, state.IsGroupDescending, perLog.Count + newLogs);

        foreach (var (logId, events) in action.EventsByLog)
        {
            if (events.Count == 0) { continue; }

            var table = updatedTables.FirstOrDefault(t => t.Id == logId);

            if (table is null || table.IsCombined) { continue; }

            if (action.VersionByLog.TryGetValue(logId, out var version))
            {
                perLogVersion = perLogVersion.SetItem(
                    logId,
                    perLogVersion.TryGetValue(logId, out var existingVersion) ? Math.Min(existingVersion, version) : version);
            }
            else
            {
                perLogVersion = perLogVersion.Remove(logId);
            }

            perLog = AppendToLog(perLog, logId, events, context);
            counts = IncrementCount(counts, logId, events.Count);
            totalNew += events.Count;

            var updatedTable = SetComputerNameIfFirstEvent(table, events);

            if (!ReferenceEquals(updatedTable, table))
            {
                updatedTables = updatedTables.Replace(table, updatedTable);
            }
        }

        if (totalNew == 0) { return state; }

        perLog = ReconcileToLogCount(perLog, state);

        return state with
        {
            PerLogEvents = perLog,
            PerLogListVersion = perLogVersion,
            EventTables = updatedTables,
            EventCountByLog = counts
        };
    }

    [ReducerMethod]
    public static LogTableState ReduceApplyFilter(LogTableState state, ApplyFilterAction action) =>
        state with { DisplayListVersion = state.DisplayListVersion + 1 };

    [ReducerMethod(typeof(CloseAllLogsAction))]
    public static LogTableState ReduceCloseAll(LogTableState state) =>
        ResetGroupCollapse(state with
        {
            EventTables = [],
            Groups = [],
            PerLogEvents = ImmutableDictionary<EventLogId, SegmentedSortedList>.Empty,
            PerLogListVersion = ImmutableDictionary<EventLogId, int>.Empty,
            EventCountByLog = ImmutableDictionary<EventLogId, int>.Empty,
            ActiveEventLogId = null
        });

    [ReducerMethod]
    public static LogTableState ReduceCloseLog(LogTableState state, CloseLogAction action)
    {
        var closingTable = state.EventTables.FirstOrDefault(table => table.Id == action.LogId);

        if (closingTable is null || closingTable.IsCombined) { return state; }

        var (groups, healedTables) = RemoveLogFromGroups(state.Groups, state.EventTables, action.LogId);
        var remainingTables = healedTables.RemoveAll(table => table.Id == action.LogId);

        var counts = state.EventCountByLog.Remove(action.LogId);
        var perLog = ReconcileToLogCount(state.PerLogEvents.Remove(action.LogId), state);
        var perLogVersion = state.PerLogListVersion.Remove(action.LogId);

        int perLogTabsRemaining = remainingTables.Count(table => !table.IsCombined);

        if (perLogTabsRemaining == 0)
        {
            return ResetGroupCollapseIfActiveChanged(
                state with
                {
                    EventTables = [],
                    Groups = [],
                    PerLogEvents = ImmutableDictionary<EventLogId, SegmentedSortedList>.Empty,
                    PerLogListVersion = ImmutableDictionary<EventLogId, int>.Empty,
                    EventCountByLog = ImmutableDictionary<EventLogId, int>.Empty,
                    ActiveEventLogId = null
                },
                state.ActiveEventLogId);
        }

        var finalTables = perLogTabsRemaining == 1
            ? remainingTables.RemoveAll(table => table.GroupId?.IsAll == true)
            : remainingTables;

        var updated = state with
        {
            EventTables = finalTables,
            Groups = groups,
            PerLogEvents = perLog,
            PerLogListVersion = perLogVersion,
            EventCountByLog = counts
        };

        return ResetGroupCollapseIfActiveChanged(RepairActiveTab(updated, null), state.ActiveEventLogId);
    }

    [ReducerMethod]
    public static LogTableState ReduceDisplayReady(
        LogTableState state,
        DisplayReadyAction action)
    {
        if (action.Version != state.DisplayListVersion) { return state; }

        var flipped = state with
        {
            OrderBy = state.RequestedOrderBy,
            IsDescending = state.RequestedIsDescending,
            GroupBy = state.RequestedGroupBy,
            IsGroupDescending = state.RequestedIsGroupDescending
        };

        // Skip log ids absent from EventTables: log closed while filter ran.
        var tablesById = state.EventTables
            .Where(table => !table.IsCombined)
            .ToDictionary(table => table.Id);

        // The lists were built under the requested context; heal any that pre-date it.
        int postCount = 0;

        foreach (var (logId, _) in tablesById)
        {
            if (action.Lists.ContainsKey(logId) || state.PerLogEvents.ContainsKey(logId)) { postCount++; }
        }

        var context = EffectiveSortContext(
            flipped.OrderBy, flipped.IsDescending, flipped.GroupBy, flipped.IsGroupDescending, postCount);
        var perLogBuilder = ImmutableDictionary.CreateBuilder<EventLogId, SegmentedSortedList>();
        var perLogVersion = state.PerLogListVersion;
        var counts = state.EventCountByLog;
        var tables = state.EventTables;

        foreach (var (logId, table) in tablesById)
        {
            if (action.Lists.TryGetValue(logId, out var list))
            {
                perLogBuilder[logId] = list.HasContext(context) ? list : SegmentedSortedList.CreateSorted(list, context);
                perLogVersion = perLogVersion.SetItem(logId, action.Version);
                counts = counts.SetItem(logId, list.Count);

                var updatedTable = SetComputerNameIfFirstEvent(table, list);

                if (!ReferenceEquals(updatedTable, table)) { tables = tables.Replace(table, updatedTable); }
            }
            else if (state.PerLogEvents.TryGetValue(logId, out var existingList))
            {
                perLogBuilder[logId] = existingList.HasContext(context)
                    ? existingList
                    : SegmentedSortedList.CreateSorted(existingList, context);
            }
        }

        var result = flipped with
        {
            PerLogEvents = ReconcileToLogCount(perLogBuilder.ToImmutable(), flipped),
            PerLogListVersion = perLogVersion,
            EventTables = tables,
            EventCountByLog = counts
        };

        return state.RequestedGroupBy != state.GroupBy ? ResetGroupCollapse(result) : result;
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

        bool liveGroupHidden = updated.GroupBy is { } liveGroup && IsHidden(liveGroup);
        bool requestedGroupHidden = updated.RequestedGroupBy is { } requestedGroup && IsHidden(requestedGroup);

        if (!liveGroupHidden && !requestedGroupHidden) { return updated; }

        var result = updated;

        if (requestedGroupHidden)
        {
            result = result with { RequestedGroupBy = null, RequestedIsGroupDescending = false };
        }

        if (liveGroupHidden)
        {
            result = result with
            {
                GroupBy = null,
                IsGroupDescending = false,
                GroupsCollapsedByDefault = false,
                GroupCollapseOverrides = ImmutableHashSet.Create<string>(StringComparer.Ordinal),
                PerLogEvents = ResortAllLogs(
                    updated.PerLogEvents,
                    EffectiveSortContext(updated.OrderBy, updated.IsDescending, null, false, updated.PerLogEvents.Count))
            };
        }

        return result;

        bool IsHidden(ColumnName column) =>
            !action.LoadedColumns.TryGetValue(column, out bool isVisible) || !isVisible;
    }

    [ReducerMethod]
    public static LogTableState ReduceMoveTabToGroup(LogTableState state, MoveTabToGroupAction action)
    {
        var tab = state.EventTables.FirstOrDefault(table => table.Id == action.TabId);

        if (tab is null || tab.IsCombined) { return state; }

        if (action.TargetGroupId.IsAll)
        {
            if (!state.Groups.Any(group => group.MemberIds.Contains(action.TabId))) { return state; }

            var (ungroupedGroups, ungroupedTables) =
                RemoveLogFromGroups(state.Groups, state.EventTables, action.TabId);
            var ungrouped = state with { Groups = ungroupedGroups, EventTables = ungroupedTables };

            return ResetGroupCollapseIfActiveChanged(RepairActiveTab(ungrouped, null), state.ActiveEventLogId);
        }

        var target = state.Groups.FirstOrDefault(group => group.Id == action.TargetGroupId);

        if (target is null || target.MemberIds.Contains(action.TabId)) { return state; }

        var (groups, tables) = RemoveLogFromGroups(state.Groups, state.EventTables, action.TabId);
        var updatedGroups = groups.Replace(target, target with { MemberIds = target.MemberIds.Add(action.TabId) });
        var headerId = tables.FirstOrDefault(table => table.GroupId == action.TargetGroupId)?.Id;
        var updated = state with { Groups = updatedGroups, EventTables = tables };

        return ResetGroupCollapseIfActiveChanged(
            RedirectActiveToGroupIfHidden(RepairActiveTab(updated, headerId)), state.ActiveEventLogId);
    }

    [ReducerMethod]
    public static LogTableState ReduceNewGroupFromTab(LogTableState state, NewGroupFromTabAction action)
    {
        var tab = state.EventTables.FirstOrDefault(table => table.Id == action.TabId);

        if (tab is null || tab.IsCombined) { return state; }

        var (prunedGroups, prunedTables) = RemoveLogFromGroups(state.Groups, state.EventTables, action.TabId);

        var groupId = LogTabGroupId.Create();
        var group = new LogTabGroup(groupId, action.GroupName, ImmutableHashSet.Create(action.TabId));
        var header = new LogView(EventLogId.Create()) { GroupId = groupId, LogName = action.GroupName };

        int childIndex = prunedTables.FindIndex(table => table.Id == action.TabId);
        var tables = prunedTables.Insert(childIndex, header);
        var updated = state with { Groups = prunedGroups.Add(group), EventTables = tables };

        return ResetGroupCollapseIfActiveChanged(RepairActiveTab(updated, header.Id), state.ActiveEventLogId);
    }

    [ReducerMethod]
    public static LogTableState ReduceRemoveTabFromGroup(LogTableState state, RemoveTabFromGroupAction action)
    {
        if (!state.Groups.Any(group => group.MemberIds.Contains(action.TabId))) { return state; }

        var (groups, tables) = RemoveLogFromGroups(state.Groups, state.EventTables, action.TabId);
        var updated = state with { Groups = groups, EventTables = tables };

        return ResetGroupCollapseIfActiveChanged(RepairActiveTab(updated, null), state.ActiveEventLogId);
    }

    [ReducerMethod]
    public static LogTableState ReduceRenameGroup(LogTableState state, RenameGroupAction action)
    {
        if (string.IsNullOrWhiteSpace(action.NewName)) { return state; }

        var group = state.Groups.FirstOrDefault(candidate => candidate.Id == action.GroupId);

        if (group is null || group.Name == action.NewName) { return state; }

        var groups = state.Groups.Replace(group, group with { Name = action.NewName });
        var header = state.EventTables.FirstOrDefault(table => table.GroupId == action.GroupId);
        var tables = header is null
            ? state.EventTables
            : state.EventTables.Replace(header, header with { LogName = action.NewName });

        return state with { Groups = groups, EventTables = tables };
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
        if (state.RequestedGroupBy == action.GroupBy) { return state; }

        return state with
        {
            RequestedGroupBy = action.GroupBy,
            RequestedIsGroupDescending = false,
            DisplayListVersion = state.DisplayListVersion + 1
        };
    }

    [ReducerMethod]
    public static LogTableState ReduceSetOrderBy(LogTableState state, SetOrderByAction action) =>
        state.RequestedOrderBy.Equals(action.OrderBy) ?
            state with
            {
                RequestedOrderBy = null,
                RequestedIsDescending = true,
                DisplayListVersion = state.DisplayListVersion + 1
            } :
            state with
            {
                RequestedOrderBy = action.OrderBy,
                DisplayListVersion = state.DisplayListVersion + 1
            };

    [ReducerMethod]
    public static LogTableState ReduceSetTabGroupCollapsed(LogTableState state, SetTabGroupCollapsedAction action)
    {
        var group = state.Groups.FirstOrDefault(candidate => candidate.Id == action.GroupId);

        if (group is null || group.IsCollapsed == action.Collapsed) { return state; }

        var updated = state with { Groups = state.Groups.Replace(group, group with { IsCollapsed = action.Collapsed }) };

        return action.Collapsed
            ? ResetGroupCollapseIfActiveChanged(RedirectActiveToGroupIfHidden(updated), state.ActiveEventLogId)
            : updated;
    }

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
        if (state.RequestedGroupBy is null) { return state; }

        return state with
        {
            RequestedIsGroupDescending = !state.RequestedIsGroupDescending,
            DisplayListVersion = state.DisplayListVersion + 1
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
        state with
        {
            RequestedIsDescending = !state.RequestedIsDescending,
            DisplayListVersion = state.DisplayListVersion + 1
        };

    [ReducerMethod]
    public static LogTableState ReduceUpdateTable(LogTableState state, UpdateTableAction action)
    {
        var table = state.EventTables.FirstOrDefault(t => action.LogId == t.Id);

        if (table is null || table.IsCombined) { return state; }

        int postCount = state.PerLogEvents.ContainsKey(table.Id)
            ? state.PerLogEvents.Count
            : state.PerLogEvents.Count + 1;
        var context = EffectiveSortContext(
            state.OrderBy, state.IsDescending, state.GroupBy, state.IsGroupDescending, postCount);

        // A tag matching the action version proves no filter change between the off-thread filter pass and this finalize; with HasContext + equal Count the existing list already equals the slice, so skip the re-sort.
        var skip = state.PerLogEvents.TryGetValue(table.Id, out var existing)
            && state.PerLogListVersion.TryGetValue(table.Id, out var listVersion)
            && listVersion == action.Version
            && existing.HasContext(context)
            && existing.Count == action.Events.Count;

        var perLog = skip
            ? state.PerLogEvents
            : SetLog(state.PerLogEvents, table.Id, action.Events, context);
        var perLogVersion = skip
            ? state.PerLogListVersion
            : state.PerLogListVersion.SetItem(table.Id, action.Version);

        perLog = ReconcileToLogCount(perLog, state);
        var updatedTable = SetComputerNameIfFirstEvent(table, action.Events) with { IsLoading = false };
        var counts = state.EventCountByLog.SetItem(table.Id, action.Events.Count);

        return state with
        {
            PerLogEvents = perLog,
            PerLogListVersion = perLogVersion,
            EventTables = state.EventTables.Replace(table, updatedTable),
            EventCountByLog = counts
        };
    }

    private static ImmutableDictionary<EventLogId, SegmentedSortedList> AppendToLog(
        ImmutableDictionary<EventLogId, SegmentedSortedList> perLog,
        EventLogId logId,
        IReadOnlyList<ResolvedEvent> events,
        SortContext context)
    {
        if (perLog.TryGetValue(logId, out var existing) && existing.HasContext(context))
        {
            return perLog.SetItem(logId, existing.Append(events));
        }

        var combined = existing is null ? events : existing.Concat(events);

        return perLog.SetItem(logId, SegmentedSortedList.CreateSorted(combined, context));
    }

    private static SortContext EffectiveSortContext(
        ColumnName? orderBy,
        bool isDescending,
        ColumnName? groupBy,
        bool isGroupDescending,
        int logCount) =>
        new(ResolvedEventOrdering.ResolveDefaultOrderBy(orderBy, groupBy, logCount),
            isDescending,
            groupBy,
            isGroupDescending);

    private static ImmutableDictionary<EventLogId, int> IncrementCount(
        ImmutableDictionary<EventLogId, int> counts,
        EventLogId logId,
        int delta)
    {
        int current = counts.TryGetValue(logId, out int existing) ? existing : 0;
        return counts.SetItem(logId, current + delta);
    }

    private static ImmutableDictionary<EventLogId, SegmentedSortedList> ReconcileToLogCount(
        ImmutableDictionary<EventLogId, SegmentedSortedList> perLog,
        LogTableState state) =>
        ResortAllLogs(perLog,
            EffectiveSortContext(
                state.OrderBy,
                state.IsDescending,
                state.GroupBy,
                state.IsGroupDescending,
                perLog.Count));

    private static LogTableState RedirectActiveToGroupIfHidden(LogTableState state)
    {
        if (state.ActiveEventLogId is not { } activeId) { return state; }

        var group = state.Groups.FirstOrDefault(
            candidate => candidate.IsCollapsed && candidate.MemberIds.Contains(activeId));

        if (group is null) { return state; }

        var header = state.EventTables.FirstOrDefault(table => table.GroupId == group.Id);

        return header is null ? state : state with { ActiveEventLogId = header.Id };
    }

    private static (ImmutableList<LogTabGroup> Groups, ImmutableList<LogView> Tables) RemoveLogFromGroups(
        ImmutableList<LogTabGroup> groups, ImmutableList<LogView> tables, EventLogId logId)
    {
        List<LogTabGroupId>? emptiedGroupIds = null;
        var updatedGroups = groups;

        foreach (var group in groups)
        {
            if (!group.MemberIds.Contains(logId)) { continue; }

            var remaining = group.MemberIds.Remove(logId);

            if (remaining.IsEmpty)
            {
                updatedGroups = updatedGroups.Remove(group);
                (emptiedGroupIds ??= []).Add(group.Id);
            }
            else
            {
                updatedGroups = updatedGroups.Replace(group, group with { MemberIds = remaining });
            }
        }

        if (ReferenceEquals(updatedGroups, groups)) { return (groups, tables); }

        if (emptiedGroupIds is null) { return (updatedGroups, tables); }

        var prunedTables = tables.RemoveAll(
            table => table.GroupId is { IsAll: false } groupId && emptiedGroupIds.Contains(groupId));

        return (updatedGroups, prunedTables);
    }

    private static LogTableState RepairActiveTab(LogTableState state, EventLogId? preferred)
    {
        if (state.ActiveEventLogId is null ||
            state.EventTables.Any(table => table.Id == state.ActiveEventLogId))
        {
            return state;
        }

        EventLogId? fallback =
            preferred is not null && state.EventTables.Any(table => table.Id == preferred)
                ? preferred
                : state.EventTables.FirstOrDefault(table => table.GroupId?.IsAll == true)?.Id
                    ?? state.EventTables.FirstOrDefault(table => !table.IsCombined)?.Id
                    ?? state.EventTables.FirstOrDefault()?.Id;

        return state with { ActiveEventLogId = fallback };
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

    private static ImmutableDictionary<EventLogId, SegmentedSortedList> ResortAllLogs(
        ImmutableDictionary<EventLogId, SegmentedSortedList> perLog,
        SortContext context)
    {
        if (perLog.IsEmpty) { return perLog; }

        var builder = perLog.ToBuilder();

        foreach (var (logId, list) in perLog)
        {
            if (!list.HasContext(context)) { builder[logId] = SegmentedSortedList.CreateSorted(list, context); }
        }

        return builder.ToImmutable();
    }

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

    private static ImmutableDictionary<EventLogId, SegmentedSortedList> SetLog(
        ImmutableDictionary<EventLogId, SegmentedSortedList> perLog,
        EventLogId logId,
        IReadOnlyList<ResolvedEvent> events,
        SortContext context) =>
        perLog.SetItem(logId, SegmentedSortedList.CreateSorted(events, context));
}
