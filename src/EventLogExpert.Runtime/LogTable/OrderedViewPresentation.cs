// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Runtime.LogTable.OrderedView;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.LogTable;

public sealed record OrderedViewPresentation(
    IEventColumnView View,
    EventLogId? ActiveTabId,
    DisplayOrdering Ordering,
    PresentationState State,
    long Revision,
    string? FaultCause = null,
    bool OrderingIsStale = false)
{
    public DisplayIndicatorKind IndicatorKind =>
        State switch
        {
            PresentationState.Faulted => DisplayIndicatorKind.Fault,
            PresentationState.Updating when View.Count == 0 => DisplayIndicatorKind.EmptyPending,
            PresentationState.Updating when OrderingIsStale => DisplayIndicatorKind.ReorderPending,
            _ => DisplayIndicatorKind.None
        };

    public bool GroupsCollapsedByDefault { get; init; }

    public ViewContentToken ContentToken { get; init; } = ViewContentToken.Empty;

    public ImmutableHashSet<string> GroupCollapseOverrides { get; init; } =
        ImmutableHashSet.Create<string>(StringComparer.Ordinal);

    public string? ActiveLogName { get; init; }

    public ImmutableDictionary<ColumnName, bool> Columns { get; init; } = ImmutableDictionary<ColumnName, bool>.Empty;

    public ImmutableList<ColumnName> ColumnOrder { get; init; } = [];

    public ImmutableDictionary<ColumnName, int> ColumnWidths { get; init; } = ImmutableDictionary<ColumnName, int>.Empty;

    public bool IsGroupCollapsed(string groupKey) =>
        GroupsCollapsedByDefault ^ GroupCollapseOverrides.Contains(groupKey);
}
