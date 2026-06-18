// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using Fluxor;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace EventLogExpert.Runtime.LogTable;

[FeatureState]
public sealed record LogTableState
{
    internal ImmutableDictionary<EventLogId, SegmentedSortedList> PerLogEvents { get; init; } =
        ImmutableDictionary<EventLogId, SegmentedSortedList>.Empty;

    public ImmutableList<LogView> EventTables { get; init; } = [];

    // One open log needs no cross-log merge: serve its SegmentedSortedList directly (allocation-free
    // O(log segments) indexer) instead of a CombinedEventView (per-access cursor alloc + K-way stride walk).
    // The multi-log path stays memoized by PerLogEvents identity; each CombinedEventView maps to one SortContext.
    public IReadOnlyList<ResolvedEvent> DisplayedEvents =>
        PerLogEvents.IsEmpty
            ? CombinedEventView.Empty
            : PerLogEvents.Count == 1
                ? SingleLogDisplayList()
                : s_combinedViews.GetValue(PerLogEvents, CreateCombinedView);

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

    internal int DisplayListGeneration { get; init; }

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

    private static readonly ConditionalWeakTable<
        ImmutableDictionary<EventLogId, SegmentedSortedList>, CombinedEventView> s_combinedViews = [];

    private static CombinedEventView CreateCombinedView(
        ImmutableDictionary<EventLogId, SegmentedSortedList> perLog) =>
        new(perLog.Values, perLog.Values.First().Context);

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

        if (activeTable is null) { return []; }

        return activeTable.IsCombined ? DisplayedEvents : EventsForLog(activeTable.Id);
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
