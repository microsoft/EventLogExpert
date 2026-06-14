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

    // Memoized by PerLogEvents identity; each instance maps to one SortContext.
    public IReadOnlyList<ResolvedEvent> DisplayedEvents =>
        PerLogEvents.IsEmpty
            ? CombinedEventView.Empty
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

    public bool GroupsCollapsedByDefault { get; init; }

    public ImmutableHashSet<string> GroupCollapseOverrides { get; init; } =
        ImmutableHashSet.Create<string>(StringComparer.Ordinal);

    internal SortContext SortContext =>
        new(ResolvedEventOrdering.ResolveDefaultOrderBy(OrderBy, GroupBy, PerLogEvents.Count),
            IsDescending,
            GroupBy,
            IsGroupDescending);

    private static readonly ConditionalWeakTable<
        ImmutableDictionary<EventLogId, SegmentedSortedList>, CombinedEventView> s_combinedViews = [];

    private static CombinedEventView CreateCombinedView(
        ImmutableDictionary<EventLogId, SegmentedSortedList> perLog) =>
        new(perLog.Values, perLog.Values.First().Context);

    public bool IsGroupCollapsed(string groupKey) =>
        GroupsCollapsedByDefault ^ GroupCollapseOverrides.Contains(groupKey);

    public IReadOnlyList<ResolvedEvent> EventsForLog(EventLogId logId) =>
        PerLogEvents.TryGetValue(logId, out var list) ? list : [];

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
