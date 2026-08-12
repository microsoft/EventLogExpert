// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Filtering.Evaluation;
using EventLogExpert.Runtime.LogTable.OrderedView;
using Fluxor;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace EventLogExpert.Runtime.LogTable;

[FeatureState]
public sealed record LogTableState
{
    public ImmutableList<LogView> EventTables { get; init; } = [];

    public ImmutableList<LogTabGroup> Groups { get; init; } = [];

    public static IEventColumnView EmptyView => EmptyColumnView.Instance;

    public EventLogId? ActiveEventLogId { get; init; }

    internal OrderedViewReady? ActiveOrderedView { get; init; }

    internal ImmutableDictionary<EventLogId, OrderedViewReady> RetainedOrderedViews { get; init; } =
        ImmutableDictionary<EventLogId, OrderedViewReady>.Empty;

    internal Filter AppliedFilter { get; init; } = new(null, []);

    internal string? FaultCause { get; init; }

    internal bool OrderedViewDisplayEnabled { get; init; } = true;

    internal long HighestInvalidationSequence { get; init; }

    internal long LastPublishedSnapshotVersion { get; init; } = -1;

    public ImmutableDictionary<ColumnName, bool> Columns { get; init; } = ImmutableDictionary<ColumnName, bool>.Empty;

    public ImmutableDictionary<ColumnName, int> ColumnWidths { get; init; } =
        ImmutableDictionary<ColumnName, int>.Empty;

    public ImmutableList<ColumnName> ColumnOrder { get; init; } = [];

    public ColumnName? OrderBy { get; init; }

    public bool IsDescending { get; init; } = true;

    public ColumnName? GroupBy { get; init; }

    public bool IsGroupDescending { get; init; }

    public bool TimelineVisible { get; init; }

    internal ColumnName? RequestedOrderBy { get; init; }

    internal bool RequestedIsDescending { get; init; } = true;

    internal ColumnName? RequestedGroupBy { get; init; }

    internal bool RequestedIsGroupDescending { get; init; }

    public bool GroupsCollapsedByDefault { get; init; }

    public ImmutableHashSet<string> GroupCollapseOverrides { get; init; } =
        ImmutableHashSet.Create<string>(StringComparer.Ordinal);

    internal int DisplayedLogCount => EventTables.Count(table => !table.IsCombined);

    internal SortContext SortContext =>
        new(ResolvedEventOrdering.ResolveDefaultOrderBy(RequestedOrderBy, RequestedGroupBy, DisplayedLogCount, TimelineVisible),
            RequestedIsDescending,
            RequestedGroupBy,
            RequestedIsGroupDescending);

    internal ColumnName? CommittedEffectiveOrderBy { get; init; }

    internal SortContext CommittedSortContext =>
        new(CommittedEffectiveOrderBy,
            IsDescending,
            GroupBy,
            IsGroupDescending);

    private ImmutableArray<EventLogId> ActiveScope()
    {
        LogView? active = null;

        foreach (LogView tab in EventTables)
        {
            if (tab.Id == ActiveEventLogId)
            {
                active = tab;

                break;
            }
        }

        if (active is null) { return []; }

        if (active.GroupId is not { } groupId) { return [active.Id]; }

        if (groupId.IsAll)
        {
            var openLogs = new List<EventLogId>(EventTables.Count);

            foreach (LogView tab in EventTables)
            {
                if (!tab.IsCombined) { openLogs.Add(tab.Id); }
            }

            return Canonical(openLogs);
        }

        foreach (LogTabGroup candidate in Groups)
        {
            if (candidate.Id == groupId) { return Canonical([.. candidate.MemberIds]); }
        }

        return [];
    }

    private static ImmutableArray<EventLogId> Canonical(List<EventLogId> logIds)
    {
        logIds.Sort(static (left, right) => left.Value.CompareTo(right.Value));

        return [.. logIds];
    }

    private ViewIdentity BuildViewIdentity() =>
        new(ActiveEventLogId,
            ActiveScope(),
            RequestedOrderBy,
            RequestedIsDescending,
            RequestedGroupBy,
            RequestedIsGroupDescending,
            TimelineVisible,
            DisplayedLogCount > 1,
            AppliedFilter);

    internal bool HasPendingSortChange =>
        RequestedOrderBy != OrderBy ||
        RequestedIsDescending != IsDescending ||
        RequestedGroupBy != GroupBy ||
        RequestedIsGroupDescending != IsGroupDescending;

    private static readonly ConditionalWeakTable<LogTableState, ViewIdentity> s_ViewIdentitys = [];

    internal ViewIdentity ViewIdentity =>
        s_ViewIdentitys.GetValue(this, static state => state.BuildViewIdentity());

    internal bool OrderingIsStale
    {
        get
        {
            var activeTable = EventTables.FirstOrDefault(table => table.Id == ActiveEventLogId);

            if (activeTable is null) { return false; }

            if (activeTable.GroupId is null) { return OrderingIsStaleForLog(activeTable.Id); }

            if (IsCombinedOrderedViewCurrent(activeTable) && ActiveOrderedView != null) { return false; }

            if (!activeTable.GroupId.Value.IsAll &&
                Groups.All(candidate => candidate.Id != activeTable.GroupId.Value))
            {
                return false;
            }

            return IsRetainedViewServable(activeTable.Id) &&
                RetainedOrderedViews[activeTable.Id].Config != SortContext;
        }
    }

    private bool OrderingIsStaleForLog(EventLogId logId)
    {
        if (IsOrderedViewServing(logId) && ActiveOrderedView != null) { return false; }

        return IsRetainedViewServable(logId) &&
            RetainedOrderedViews[logId].Config != SortContext;
    }

    public IEventColumnView DisplayedEventsForTab(LogView tab) =>
        RoutedReadyForTab(tab)?.View ?? EmptyColumnView.Instance;

    internal ViewContentToken ContentTokenForTab(LogView tab) =>
        RoutedReadyForTab(tab)?.ContentToken ?? ViewContentToken.Empty;

    private OrderedViewReady? RoutedReadyForTab(LogView tab)
    {
        if (tab.GroupId is null) { return RoutedReadyForLog(tab.Id); }

        if (tab.GroupId.Value.IsAll)
        {
            return IsCombinedOrderedViewCurrent(tab) && ActiveOrderedView != null ?
                ActiveOrderedView :
                RetainedReadyFor(tab.Id);
        }

        var group = Groups.FirstOrDefault(candidate => candidate.Id == tab.GroupId.Value);

        if (group is null) { return null; }

        return IsCombinedOrderedViewCurrent(tab) && ActiveOrderedView != null ?
            ActiveOrderedView :
            RetainedReadyFor(tab.Id);
    }

    internal IEventColumnView EventsForLog(EventLogId logId) =>
        RoutedReadyForLog(logId)?.View ?? EmptyColumnView.Instance;

    private OrderedViewReady? RoutedReadyForLog(EventLogId logId)
    {
        if (IsOrderedViewServing(logId) && ActiveOrderedView != null)
        {
            return ActiveOrderedView;
        }

        return RetainedReadyFor(logId);
    }

    internal ImmutableDictionary<EventLogId, OrderedViewReady> RetainOnly(OrderedViewReady served)
    {
        if (served.Identity?.ActiveLogId is not { } servedTabId) { return RetainedOrderedViews; }

        var openTabIds = EventTables.Select(table => table.Id).ToHashSet();

        var pruned = RetainedOrderedViews;

        foreach (var tabId in RetainedOrderedViews.Keys)
        {
            if (!openTabIds.Contains(tabId)) { pruned = pruned.Remove(tabId); }
        }

        return openTabIds.Contains(servedTabId) ? pruned.SetItem(servedTabId, served) : pruned;
    }

    internal LogTableState WithClearedOrderedViewRetention() =>
        this with
        {
            ActiveOrderedView = null,
            RetainedOrderedViews = ImmutableDictionary<EventLogId, OrderedViewReady>.Empty
        };

    private OrderedViewReady? RetainedReadyFor(EventLogId tabId) =>
        IsRetainedViewServable(tabId) ? RetainedOrderedViews[tabId] : null;

    internal bool IsRetainedViewServable(EventLogId tabId) =>
        tabId == ActiveEventLogId &&
        RetainedOrderedViews.TryGetValue(tabId, out var retained) &&
        retained.Identity is { } identity &&
        identity.Scope.SequenceEqual(ActiveScope()) &&
        retained.Config == CommittedSortContext &&
        !retained.Filter.HasFilteringChangedFrom(AppliedFilter);

    internal bool IsOrderedViewServing(EventLogId logId) =>
        OrderedViewDisplayEnabled &&
        ActiveOrderedView != null &&
        ActiveOrderedView.SingleLogId == logId &&
        logId == ActiveEventLogId &&
        !HasPendingSortChange &&
        ActiveOrderedView.Config == SortContext &&
        !ActiveOrderedView.Filter.HasFilteringChangedFrom(AppliedFilter);

    // is not serving simply falls back to its retained-or-empty view. The tab must be the active one (the engine holds exactly one
    // scope - the active tab's), and the view must have been published for the identity this state is asking for. That
    // identity CARRIES the active tab's resolved scope, so identity equality already proves the engine's scope is exactly this
    // tab's membership - no separate AllLogs/group set comparison is needed. Grouped display routes under exactly the fence
    // ungrouped already uses: HasPendingSortChange covers the group members, and SortContext is built from the requested
    // pair, so !HasPendingSortChange with Config == SortContext proves the routed view was ordered under the very GroupBy
    // the pane is about to group it by (see LogTablePane.RebuildGroupedRowView).
    private bool IsCombinedOrderedViewCurrent(LogView tab) =>
        OrderedViewDisplayEnabled &&
        ActiveOrderedView != null &&
        tab.Id == ActiveEventLogId &&
        !HasPendingSortChange &&
        ActiveOrderedView.Identity == ViewIdentity &&
        ActiveOrderedView.Config == SortContext &&

        // SEMANTIC, not `==`: Filter's record equality is reference-based on its collections, while the identity above
        // compares filters semantically. A reference check here would reject a view whose identity already matched -
        // e.g. re-applying an equivalent filter built from fresh collections - and park the display on the fallback view
        // with no further request to repair it.
        !ActiveOrderedView.Filter.HasFilteringChangedFrom(AppliedFilter);

    public IEventColumnView GetActiveDisplayedEvents()
    {
        var activeTable = EventTables.FirstOrDefault(table => table.Id == ActiveEventLogId);

        return activeTable is null ? EmptyColumnView.Instance : DisplayedEventsForTab(activeTable);
    }

    internal PresentationState PresentationState
    {
        get
        {
            var activeTable = EventTables.FirstOrDefault(table => table.Id == ActiveEventLogId);

            if (activeTable is null) { return PresentationState.Current; }

            if (activeTable.GroupId is { IsAll: false } groupId &&
                Groups.All(candidate => candidate.Id != groupId))
            {
                return PresentationState.Current;
            }

            if (!OrderedViewDisplayEnabled) { return PresentationState.Faulted; }

            return ServingOrderedView != null ? PresentationState.Current : PresentationState.Updating;
        }
    }

    internal OrderedViewReady? ServingOrderedView
    {
        get
        {
            if (ActiveOrderedView is null) { return null; }

            var activeTable = EventTables.FirstOrDefault(table => table.Id == ActiveEventLogId);

            if (activeTable is null) { return null; }

            bool serving = activeTable.GroupId is null ?
                IsOrderedViewServing(activeTable.Id) :
                IsCombinedOrderedViewCurrent(activeTable);

            return serving ? ActiveOrderedView : null;
        }
    }

    public IReadOnlyList<ColumnName> GetOrderedEnabledColumns(ILogTableColumnDefaultsProvider columnDefaults) =>
        ResolveOrderedEnabledColumns(Columns, ColumnOrder, columnDefaults);

    public static IReadOnlyList<ColumnName> ResolveOrderedEnabledColumns(
        ImmutableDictionary<ColumnName, bool> columns,
        ImmutableList<ColumnName> columnOrder,
        ILogTableColumnDefaultsProvider columnDefaults)
    {
        ArgumentNullException.ThrowIfNull(columnDefaults);

        var enabledColumns = columns
            .Where(column => column.Value)
            .Select(column => column.Key)
            .ToHashSet();

        var order = columnOrder.IsEmpty ? columnDefaults.ColumnOrder : columnOrder;

        HashSet<ColumnName> present = [];
        List<ColumnName> ordered = [];

        foreach (var column in order)
        {
            if (enabledColumns.Contains(column) && present.Add(column))
            {
                ordered.Add(column);
            }
        }

        foreach (var column in columnDefaults.ColumnOrder)
        {
            if (enabledColumns.Contains(column) && present.Add(column))
            {
                ordered.Add(column);
            }
        }

        return ordered;
    }

    public bool IsGroupCollapsed(string groupKey) =>
        GroupsCollapsedByDefault ^ GroupCollapseOverrides.Contains(groupKey);
}
