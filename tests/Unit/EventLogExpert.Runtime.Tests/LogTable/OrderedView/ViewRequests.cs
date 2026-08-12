// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.LogTable.OrderedView;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.Tests.LogTable.OrderedView;

internal static class ViewRequests
{
    private static readonly Filter s_emptyFilter = new(null, []);

    private static long s_sequence;

    internal static Filter EmptyFilter => s_emptyFilter;

    internal static RebuildRequest? AdvanceScope(
        OrderedViewState state,
        IReadOnlyCollection<EventLogId> scopeLogs,
        long scopeVersion) =>
        state.TrySetActiveScope(scopeLogs, scopeVersion) ? state.CaptureScopeReseed() : null;

    internal static ViewRequest For(
        SortContext context,
        Filter filter,
        IEnumerable<EventLogId> scope,
        Func<EventLocator, IEventColumnReader, bool>? predicate = null,
        IReadOnlyDictionary<EventLogId, IEventColumnReader>? readers = null,
        bool? hold = null,
        long? sequence = null,
        EventLogId? activeLogId = null)
    {
        EventLogId[] logs = [.. scope];

        return new ViewRequest(
            Identity(logs,
                activeLogId,
                context.OrderBy,
                context.IsDescending,
                context.GroupBy,
                context.IsGroupDescending,
                filter: filter),
            sequence ?? NextSequence(),
            logs,
            readers ?? new Dictionary<EventLogId, IEventColumnReader>(),
            context,
            filter,
            predicate ?? (static (_, _) => true),
            hold);
    }

    internal static ViewIdentity Identity(
        IEnumerable<EventLogId>? scope = null,
        EventLogId? activeLogId = null,
        ColumnName? orderBy = null,
        bool isDescending = false,
        ColumnName? groupBy = null,
        bool isGroupDescending = false,
        bool timelineVisible = false,
        bool isMultiLogDisplay = false,
        Filter? filter = null)
    {
        ImmutableArray<EventLogId> logs = scope is null ?
            [] :
            [.. scope.OrderBy(static logId => logId.Value)];

        return new ViewIdentity(
            activeLogId,
            logs,
            orderBy,
            isDescending,
            groupBy,
            isGroupDescending,
            timelineVisible,
            isMultiLogDisplay,
            filter ?? s_emptyFilter);
    }

    internal static long NextSequence() => Interlocked.Increment(ref s_sequence);
}
