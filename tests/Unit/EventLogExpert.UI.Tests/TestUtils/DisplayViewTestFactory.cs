// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.Tests.LogTable.TestSupport;
using System.Collections.Immutable;

namespace EventLogExpert.UI.Tests.TestUtils;

internal static class DisplayViewTestFactory
{
    internal static readonly ImmutableDictionary<ColumnName, bool> DefaultColumns = new[]
    {
        ColumnName.Level,
        ColumnName.DateAndTime,
        ColumnName.Source,
        ColumnName.EventId,
        ColumnName.TaskCategory
    }.ToImmutableDictionary(column => column, _ => true);

    internal static OrderedViewPresentation CombinedPresentation(
        EventLogId tabId,
        IReadOnlyList<(EventLogId LogId, IReadOnlyList<ResolvedEvent> Events)> members,
        ColumnName? orderBy = null,
        bool isDescending = false,
        ColumnName? groupBy = null,
        bool isGroupDescending = false,
        ImmutableHashSet<string>? groupCollapseOverrides = null,
        bool groupsCollapsedByDefault = false,
        long revision = 1)
    {
        var views = new List<AosReferenceView>(members.Count);

        foreach (var (logId, events) in members)
        {
            views.Add((AosReferenceView)IdentityFor(logId, events, groupBy, isGroupDescending, isDescending));
        }

        return new OrderedViewPresentation(
            new AosReferenceCombinedView(views, views[0].Context),
            tabId,
            new DisplayOrdering(orderBy, isDescending, groupBy, isGroupDescending),
            PresentationState.Current,
            revision)
        {
            Columns = DefaultColumns,
            GroupsCollapsedByDefault = groupsCollapsedByDefault,
            GroupCollapseOverrides = groupCollapseOverrides ?? ImmutableHashSet.Create<string>(StringComparer.Ordinal)
        };
    }

    internal static IEventColumnView Identity(IReadOnlyList<ResolvedEvent> events, ColumnName? groupBy = null)
    {
        var reader = EventColumnStore.Build(events, 0, 0).CreateReader(EventLogId.Create());
        int[] survivors = new int[reader.Count];

        for (int i = 0; i < survivors.Length; i++) { survivors[i] = i; }

        return AosReferenceView.Create(reader, survivors, orderBy: null, isDescending: false, groupBy: groupBy, isGroupDescending: false);
    }

    internal static IEventColumnView IdentityFor(
        EventLogId logId,
        IReadOnlyList<ResolvedEvent> events,
        ColumnName? groupBy = null,
        bool isGroupDescending = false,
        bool isDescending = false)
    {
        var reader = EventColumnStore.Build(events, 0, 0).CreateReader(logId);
        int[] survivors = new int[reader.Count];

        for (int i = 0; i < survivors.Length; i++) { survivors[i] = i; }

        return AosReferenceView.Create(reader, survivors, orderBy: null, isDescending, groupBy, isGroupDescending);
    }

    internal static OrderedViewPresentation Presentation(
        EventLogId tabId,
        IReadOnlyList<ResolvedEvent> events,
        ColumnName? orderBy = null,
        bool isDescending = false,
        ColumnName? groupBy = null,
        bool isGroupDescending = false,
        ImmutableHashSet<string>? groupCollapseOverrides = null,
        bool groupsCollapsedByDefault = false,
        long revision = 1) =>
        new(IdentityFor(tabId, events, groupBy, isGroupDescending, isDescending),
            tabId,
            new DisplayOrdering(orderBy, isDescending, groupBy, isGroupDescending),
            PresentationState.Current,
            revision)
        {
            Columns = DefaultColumns,
            GroupsCollapsedByDefault = groupsCollapsedByDefault,
            GroupCollapseOverrides = groupCollapseOverrides ?? ImmutableHashSet.Create<string>(StringComparer.Ordinal)
        };
}
