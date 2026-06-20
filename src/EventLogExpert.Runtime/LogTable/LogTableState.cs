// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using Fluxor;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace EventLogExpert.Runtime.LogTable;

[FeatureState]
public sealed record LogTableState
{
    internal ImmutableDictionary<EventLogId, SegmentedSortedList> PerLogEvents { get; init; } =
        ImmutableDictionary<EventLogId, SegmentedSortedList>.Empty;

    public ImmutableList<LogView> EventTables { get; init; } = [];

    public ImmutableList<LogTabGroup> Groups { get; init; } = [];

    public IReadOnlyList<ResolvedEvent> DisplayedEvents =>
        PerLogEvents.IsEmpty
            ? CombinedEventView.Empty
            : PerLogEvents.Count == 1
                ? SingleLogDisplayList()
                : AllLogsView();

    public ImmutableDictionary<EventLogId, int> EventCountByLog { get; init; } =
        ImmutableDictionary<EventLogId, int>.Empty;

    public EventLogId? ActiveEventLogId { get; init; }

    public ImmutableDictionary<ColumnName, bool> Columns { get; init; } = ImmutableDictionary<ColumnName, bool>.Empty;

    public ImmutableDictionary<ColumnName, int> ColumnWidths { get; init; } =
        ImmutableDictionary<ColumnName, int>.Empty;

    public ImmutableList<ColumnName> ColumnOrder { get; init; } = [];

    public ColumnName? OrderBy { get; init; }

    public bool IsDescending { get; init; } = true;

    public ColumnName? GroupBy { get; init; }

    public bool IsGroupDescending { get; init; }

    internal ColumnName? RequestedOrderBy { get; init; }

    internal bool RequestedIsDescending { get; init; } = true;

    internal ColumnName? RequestedGroupBy { get; init; }

    internal bool RequestedIsGroupDescending { get; init; }

    internal int DisplayListVersion { get; init; }

    public bool GroupsCollapsedByDefault { get; init; }

    public ImmutableHashSet<string> GroupCollapseOverrides { get; init; } =
        ImmutableHashSet.Create<string>(StringComparer.Ordinal);

    internal SortContext SortContext =>
        new(ResolvedEventOrdering.ResolveDefaultOrderBy(RequestedOrderBy, RequestedGroupBy, PerLogEvents.Count),
            RequestedIsDescending,
            RequestedGroupBy,
            RequestedIsGroupDescending);

    internal bool HasPendingSortChange =>
        RequestedOrderBy != OrderBy ||
        RequestedIsDescending != IsDescending ||
        RequestedGroupBy != GroupBy ||
        RequestedIsGroupDescending != IsGroupDescending;

    internal ImmutableDictionary<EventLogId, int> PerLogListVersion { get; init; } =
        ImmutableDictionary<EventLogId, int>.Empty;

    private static readonly object s_allLogsKey = new();

    private static readonly ConditionalWeakTable<
        ImmutableDictionary<EventLogId, SegmentedSortedList>,
        ConcurrentDictionary<object, CombinedEventView>> s_viewsByGeneration = [];

    public IReadOnlyList<ResolvedEvent> DisplayedEventsForTab(LogView tab)
    {
        if (tab.GroupId is null) { return EventsForLog(tab.Id); }

        if (tab.GroupId.Value.IsAll) { return DisplayedEvents; }

        var group = Groups.FirstOrDefault(candidate => candidate.Id == tab.GroupId.Value);

        return group is null ? CombinedEventView.Empty : GroupView(group);
    }

    private IReadOnlyList<ResolvedEvent> AllLogsView() =>
        InnerCache().GetOrAdd(
            s_allLogsKey,
            static (_, perLog) => new CombinedEventView(perLog.Values, perLog.Values.First().Context),
            PerLogEvents);

    private IReadOnlyList<ResolvedEvent> GroupView(LogTabGroup group)
    {
        SegmentedSortedList? firstPresent = null;
        int presentCount = 0;

        foreach (var memberId in group.MemberIds)
        {
            if (PerLogEvents.TryGetValue(memberId, out var list))
            {
                firstPresent ??= list;
                presentCount++;
            }
        }

        if (presentCount == 0) { return CombinedEventView.Empty; }

        if (presentCount == 1) { return firstPresent!; }

        return InnerCache().GetOrAdd(
            group.MemberIds,
            static (_, args) => BuildGroupView(args.PerLogEvents, args.MemberIds),
            (PerLogEvents, group.MemberIds));
    }

    private static CombinedEventView BuildGroupView(
        ImmutableDictionary<EventLogId, SegmentedSortedList> perLog,
        ImmutableHashSet<EventLogId> memberIds)
    {
        var lists = new List<SegmentedSortedList>(memberIds.Count);

        foreach (var memberId in memberIds)
        {
            if (perLog.TryGetValue(memberId, out var list)) { lists.Add(list); }
        }

        return new CombinedEventView(lists, lists[0].Context);
    }

    private ConcurrentDictionary<object, CombinedEventView> InnerCache() =>
        s_viewsByGeneration.GetValue(
            PerLogEvents, static _ => new ConcurrentDictionary<object, CombinedEventView>());

    // Caller guarantees PerLogEvents.Count == 1. Return that sole list via the struct enumerator instead of
    // LINQ .Values.First(), which boxes an enumerator on this render-path property read.
    private SegmentedSortedList SingleLogDisplayList()
    {
        using var enumerator = PerLogEvents.GetEnumerator();
        enumerator.MoveNext();

        return enumerator.Current.Value;
    }

    public IReadOnlyList<ResolvedEvent> EventsForLog(EventLogId logId) =>
        PerLogEvents.TryGetValue(logId, out var list) ? list : [];

    public IReadOnlyList<ResolvedEvent> GetActiveDisplayedEvents()
    {
        var activeTable = EventTables.FirstOrDefault(table => table.Id == ActiveEventLogId);

        return activeTable is null ? [] : DisplayedEventsForTab(activeTable);
    }

    public IReadOnlyList<ColumnName> GetOrderedEnabledColumns(ILogTableColumnDefaultsProvider columnDefaults)
    {
        ArgumentNullException.ThrowIfNull(columnDefaults);

        var enabledColumns = Columns
            .Where(column => column.Value)
            .Select(column => column.Key)
            .ToHashSet();

        var order = ColumnOrder.IsEmpty ? columnDefaults.ColumnOrder : ColumnOrder;

        HashSet<ColumnName> present = [];
        List<ColumnName> ordered = [];

        // De-duplicate while preserving first occurrence: a persisted ColumnOrder may contain duplicates
        // that would otherwise become duplicate export headers (rejected by TabularExportWriter).
        foreach (var column in order)
        {
            if (enabledColumns.Contains(column) && present.Add(column))
            {
                ordered.Add(column);
            }
        }

        // Append any enabled column missing from the active order (e.g. enabled but absent from a persisted
        // ColumnOrder) so it is never silently dropped from the table or an export.
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

    // Each call must carry a single log's events (one OwningLog).
    internal LogTableState WithLogEvents(EventLogId logId, params ResolvedEvent[] events)
    {
        for (int i = 1; i < events.Length; i++)
        {
            if (!string.Equals(events[i].OwningLog, events[0].OwningLog, StringComparison.Ordinal))
            {
                throw new ArgumentException("All events must share one OwningLog.", nameof(events));
            }
        }

        int newCount = PerLogEvents.ContainsKey(logId) ? PerLogEvents.Count : PerLogEvents.Count + 1;

        var context = new SortContext(
            ResolvedEventOrdering.ResolveDefaultOrderBy(OrderBy, GroupBy, newCount),
            IsDescending,
            GroupBy,
            IsGroupDescending);

        var builder = PerLogEvents.ToBuilder();
        builder[logId] = SegmentedSortedList.CreateSorted(events, context);

        foreach (var (id, list) in PerLogEvents)
        {
            if (id != logId && !list.HasContext(context))
            {
                builder[id] = SegmentedSortedList.CreateSorted(list, context);
            }
        }

        return this with { PerLogEvents = builder.ToImmutable() };
    }
}
