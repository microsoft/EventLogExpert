// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Filtering.Evaluation;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.Histogram;
using EventLogExpert.Runtime.LogTable.OrderedView;
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

        if (state.EventTables.IsEmpty)
        {
            return ResetGroupCollapseIfActiveChanged(
                state with
                {
                    EventTables = state.EventTables.Add(newTable),
                    ActiveEventLogId = newTable.Id
                },
                state.ActiveEventLogId);
        }

        var combinedTable = state.EventTables.FirstOrDefault(table => table.GroupId?.IsAll == true);

        if (combinedTable is not null)
        {
            // later, by which time the served view it needed to record is already gone.
            return RetainServedView(state, state with
            {
                EventTables = state.EventTables.Add(newTable),
            });
        }

        combinedTable = new LogView(EventLogId.Create()) { GroupId = LogTabGroupId.AllLogs };

        return RetainServedView(state, ResetGroupCollapseIfActiveChanged(
            state with
            {
                EventTables = state.EventTables
                    .Add(combinedTable)
                    .Add(newTable),
                ActiveEventLogId = combinedTable.Id
            },
            state.ActiveEventLogId));
    }

    [ReducerMethod]
    public static LogTableState ReduceApplyFilter(LogTableState state, ApplyFilterAction action) =>
        RetainServedView(state, state with
            {
                AppliedFilter = action.Filter.HasFilteringChangedFrom(state.AppliedFilter) ?
                    action.Filter :
                    state.AppliedFilter
            });

    [ReducerMethod(typeof(CloseAllLogsAction))]
    public static LogTableState ReduceCloseAll(LogTableState state) =>
        ResetGroupCollapse((state with
        {
            EventTables = [],
            Groups = [],
            ActiveEventLogId = null
        }).WithClearedOrderedViewRetention());

    [ReducerMethod]
    public static LogTableState ReduceCloseLog(LogTableState state, CloseLogAction action)
    {
        var closingTable = state.EventTables.FirstOrDefault(table => table.Id == action.LogId);

        if (closingTable is null || closingTable.IsCombined) { return state; }

        var (groups, healedTables) = RemoveLogFromGroups(state.Groups, state.EventTables, action.LogId);
        var remainingTables = healedTables.RemoveAll(table => table.Id == action.LogId);

        int perLogTabsRemaining = remainingTables.Count(table => !table.IsCombined);

        if (perLogTabsRemaining == 0)
        {
            return ResetGroupCollapseIfActiveChanged(
                (state with
                {
                    EventTables = [],
                    Groups = [],
                    ActiveEventLogId = null
                }).WithClearedOrderedViewRetention(),
                state.ActiveEventLogId);
        }

        var finalTables = perLogTabsRemaining == 1
            ? remainingTables.RemoveAll(table => table.GroupId?.IsAll == true)
            : remainingTables;

        var finalTableIds = finalTables.Select(table => table.Id).ToHashSet();

        var updated = state with
        {
            EventTables = finalTables,
            Groups = groups,
            RetainedOrderedViews = state.RetainedOrderedViews.RemoveRange(
                state.RetainedOrderedViews.Keys.Where(id => !finalTableIds.Contains(id))),
        };

        return RetainServedView(state, ResetGroupCollapseIfActiveChanged(RepairActiveTab(updated, null), state.ActiveEventLogId));
    }

    [ReducerMethod]
    public static LogTableState ReduceIngestRawEvents(LogTableState state, IngestRawEventsAction action)
    {
        var tables = state.EventTables;

        foreach (var (logId, events) in action.EventsByLog)
        {
            if (events.Count <= 0) { continue; }

            tables = LatchComputerNameForLog(tables, logId, events);
        }

        return ReferenceEquals(tables, state.EventTables) ? state : state with { EventTables = tables };
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

        bool requestedGroupHidden = updated.RequestedGroupBy is { } requestedGroup && IsHidden(requestedGroup);

        if (!requestedGroupHidden) { return updated; }

        var result = updated with { RequestedGroupBy = null, RequestedIsGroupDescending = false };

        return RetainServedView(state, result);

        bool IsHidden(ColumnName column) =>
            !action.LoadedColumns.TryGetValue(column, out bool isVisible) || !isVisible;
    }

    [ReducerMethod]
    public static LogTableState ReduceLoadEvents(LogTableState state, LoadEventsAction action)
    {
        var table = state.EventTables.FirstOrDefault(candidate => action.LogData.Id == candidate.Id);

        if (table is null || table.IsCombined) { return state; }

        var finalized = SetComputerNameFromRawEvents(table, action.Events);

        if (finalized.IsLoading) { finalized = finalized with { IsLoading = false }; }

        return ReferenceEquals(finalized, table) ?
            state :
            state with { EventTables = state.EventTables.Replace(table, finalized) };
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

            return RetainServedView(state, ResetGroupCollapseIfActiveChanged(RepairActiveTab(ungrouped, null), state.ActiveEventLogId));
        }

        var target = state.Groups.FirstOrDefault(group => group.Id == action.TargetGroupId);

        if (target is null || target.MemberIds.Contains(action.TabId)) { return state; }

        var (groups, tables) = RemoveLogFromGroups(state.Groups, state.EventTables, action.TabId);
        var updatedGroups = groups.Replace(target, target with { MemberIds = target.MemberIds.Add(action.TabId) });
        var headerId = tables.FirstOrDefault(table => table.GroupId == action.TargetGroupId)?.Id;
        var updated = state with { Groups = updatedGroups, EventTables = tables };

        return RetainServedView(state, ResetGroupCollapseIfActiveChanged(
            RedirectActiveToGroupIfHidden(RepairActiveTab(updated, headerId)), state.ActiveEventLogId));
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

        return RetainServedView(state, ResetGroupCollapseIfActiveChanged(RepairActiveTab(updated, header.Id), state.ActiveEventLogId));
    }

    [ReducerMethod]
    public static LogTableState ReduceOrderedViewDisplayFaulted(LogTableState state, OrderedViewDisplayFaultedAction action)
    {
        if (action.Identity is { } faulted && faulted != state.ViewIdentity) { return state; }

        return RetainServedView(state, state with
            {
                OrderedViewDisplayEnabled = false,
                ActiveOrderedView = null,
                FaultCause = Describe(action.Fault)
            });
    }

    [ReducerMethod(typeof(OrderedViewDisplayRecoveredAction))]
    public static LogTableState ReduceOrderedViewDisplayRecovered(
        LogTableState state) =>
        state.OrderedViewDisplayEnabled ?
            state :
            state with
            {
                OrderedViewDisplayEnabled = true,
                FaultCause = null
            };

    [ReducerMethod]
    public static LogTableState ReduceOrderedViewUpdated(LogTableState state, OrderedViewUpdatedAction action)
    {
        LogTableState next = action.Update switch
        {
            OrderedViewReady view
                when view.SnapshotVersion > state.LastPublishedSnapshotVersion
                    && view.Sequence >= state.HighestInvalidationSequence
                    && view.Identity == state.ViewIdentity =>
                AdoptEngineOrdering(state,
                    state with
                    {
                        ActiveOrderedView = view,
                        LastPublishedSnapshotVersion = view.SnapshotVersion,

                        OrderedViewDisplayEnabled = true,
                        FaultCause = null
                    }),
            OrderedViewCleared invalidation
                when invalidation.SnapshotVersion > state.LastPublishedSnapshotVersion
                    && invalidation.Sequence >= state.HighestInvalidationSequence
                    && invalidation.Identity == state.ViewIdentity =>
                RetainServedView(state, state with
                    {
                        ActiveOrderedView = null,
                        LastPublishedSnapshotVersion = invalidation.SnapshotVersion,

                        OrderedViewDisplayEnabled = true,
                        FaultCause = null
                    }),
            _ => state
        };

        return next;
    }

    [ReducerMethod]
    public static LogTableState ReduceRemoveTabFromGroup(LogTableState state, RemoveTabFromGroupAction action)
    {
        if (!state.Groups.Any(group => group.MemberIds.Contains(action.TabId))) { return state; }

        var (groups, tables) = RemoveLogFromGroups(state.Groups, state.EventTables, action.TabId);
        var updated = state with { Groups = groups, EventTables = tables };

        return RetainServedView(state, ResetGroupCollapseIfActiveChanged(RepairActiveTab(updated, null), state.ActiveEventLogId));
    }

    [ReducerMethod]
    public static LogTableState ReduceRenameGroup(LogTableState state, RenameGroupAction action)
    {
        if (string.IsNullOrWhiteSpace(action.NewName)) { return state; }

        var group = state.Groups.FirstOrDefault(candidate => candidate.Id == action.GroupId);

        if (group is null || group.Name == action.NewName) { return state; }

        var groups = state.Groups.Replace(group, group with { Name = action.NewName });
        var header = state.EventTables.FirstOrDefault(table => table.GroupId == action.GroupId);
        var tables = header is null ?
            state.EventTables :
            state.EventTables.Replace(header, header with { LogName = action.NewName });

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

        return RetainServedView(state, ResetGroupCollapseIfActiveChanged(
            state with { ActiveEventLogId = activeTable.Id },
            state.ActiveEventLogId));
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

        return RetainServedView(state, state with
        {
            RequestedGroupBy = action.GroupBy,
            RequestedIsGroupDescending = false
        });
    }

    [ReducerMethod]
    public static LogTableState ReduceSetHistogramVisible(LogTableState state, SetHistogramVisibleAction action)
    {
        if (state.TimelineVisible == action.IsVisible) { return state; }

        return RetainServedView(state, state with
        {
            TimelineVisible = action.IsVisible
        });
    }

    [ReducerMethod]
    public static LogTableState ReduceSetOrderBy(LogTableState state, SetOrderByAction action) =>
        RetainServedView(state, state.RequestedOrderBy.Equals(action.OrderBy) ?
            state with
            {
                RequestedOrderBy = null,
                RequestedIsDescending = true
            } :
            state with
            {
                RequestedOrderBy = action.OrderBy
            });

    [ReducerMethod]
    public static LogTableState ReduceSetTabGroupCollapsed(LogTableState state, SetTabGroupCollapsedAction action)
    {
        var group = state.Groups.FirstOrDefault(candidate => candidate.Id == action.GroupId);

        if (group is null || group.IsCollapsed == action.Collapsed) { return state; }

        var updated = state with { Groups = state.Groups.Replace(group, group with { IsCollapsed = action.Collapsed }) };

        return action.Collapsed ?
            RetainServedView(state, ResetGroupCollapseIfActiveChanged(RedirectActiveToGroupIfHidden(updated), state.ActiveEventLogId)) :
            updated;
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

        return RetainServedView(state, state with
        {
            RequestedIsGroupDescending = !state.RequestedIsGroupDescending
        });
    }

    [ReducerMethod(typeof(ToggleSortingAction))]
    public static LogTableState ReduceToggleSorting(LogTableState state) =>
        RetainServedView(state, state with
        {
            RequestedIsDescending = !state.RequestedIsDescending
        });

    [ReducerMethod]
    public static LogTableState ReduceViewRequestInvalidated(LogTableState state, ViewRequestInvalidatedAction action) =>
        action.Sequence <= state.HighestInvalidationSequence ?
            state :
            state with
            {
                HighestInvalidationSequence = action.Sequence,

                RetainedOrderedViews = state.ServingOrderedView is { } served ?
                    state.RetainOnly(served) :
                    state.RetainedOrderedViews,
                ActiveOrderedView = null
            };

    private static LogTableState AdoptEngineOrdering(LogTableState prior, LogTableState adopted)
    {
        if (!prior.HasPendingSortChange && prior.SortContext == prior.CommittedSortContext) { return adopted; }

        var flipped = adopted with
        {
            OrderBy = adopted.RequestedOrderBy,
            IsDescending = adopted.RequestedIsDescending,
            GroupBy = adopted.RequestedGroupBy,
            IsGroupDescending = adopted.RequestedIsGroupDescending,
            CommittedEffectiveOrderBy = ResolvedEventOrdering.ResolveDefaultOrderBy(
                adopted.RequestedOrderBy,
                adopted.RequestedGroupBy,
                adopted.DisplayedLogCount,
                adopted.TimelineVisible)
        };

        return prior.RequestedGroupBy != prior.GroupBy ? ResetGroupCollapse(flipped) : flipped;
    }

    private static string Describe(Exception fault)
    {
        const int MessageLimit = 200;

        string message = fault.Message ?? string.Empty;

        if (message.Length <= MessageLimit) { return $"{fault.GetType().Name}: {message}"; }

        int cut = MessageLimit;

        if (char.IsHighSurrogate(message[cut - 1])) { cut--; }

        return $"{fault.GetType().Name}: {message[..cut]}...";
    }

    private static string? FirstNonEmptyComputerName(IReadOnlyList<ResolvedEvent> events)
    {
        for (int index = 0; index < events.Count; index++)
        {
            string candidate = events[index].ComputerName;

            if (!string.IsNullOrEmpty(candidate)) { return candidate; }
        }

        return null;
    }

    private static ImmutableList<LogView> LatchComputerNameForLog(
        ImmutableList<LogView> tables,
        EventLogId logId,
        IReadOnlyList<ResolvedEvent> events)
    {
        int index = 0;

        foreach (var table in tables)
        {
            if (table.Id != logId)
            {
                index++;

                continue;
            }

            if (table.IsCombined || !string.IsNullOrEmpty(table.ComputerName)) { return tables; }

            return FirstNonEmptyComputerName(events) is { } resolved ?
                tables.SetItem(index, table with { ComputerName = resolved }) :
                tables;
        }

        return tables;
    }

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

    private static LogTableState RetainServedView(LogTableState prior, LogTableState next) =>
        prior.ServingOrderedView is { } served && next.ServingOrderedView is null ?
            next with { RetainedOrderedViews = next.RetainOnly(served) } :
            next;

    private static LogView SetComputerNameFromRawEvents(LogView table, IReadOnlyList<ResolvedEvent> events)
    {
        if (!string.IsNullOrEmpty(table.ComputerName)) { return table; }

        return FirstNonEmptyComputerName(events) is { } resolved ? table with { ComputerName = resolved } : table;
    }
}
