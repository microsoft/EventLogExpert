// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.LogTable;

namespace EventLogExpert.Runtime.Tests.TestUtils;

/// <summary>
///     Builds per-log <see cref="EventColumnView" />s over a real <see cref="EventColumnStore" /> for tests that seed
///     display state, mirroring the production <c>DisplayViewBuilder</c> path so stored views are physically resolvable.
/// </summary>
internal static class DisplayViewTestFactory
{
    // Builds an unsorted view (physical ingest order). The display reducers re-heal it to the state's sort context on
    // store, so seeding order does not matter for correctness.
    internal static EventColumnView Build(EventLogId logId, IReadOnlyList<ResolvedEvent> events) =>
        EventColumnView.Create(
            Reader(logId, events),
            Survivors(events.Count),
            orderBy: null,
            isDescending: false,
            groupBy: null,
            isGroupDescending: false);

    // Builds a view already sorted under the given context, for tests that read the view directly without a reducer.
    internal static EventColumnView Build(EventLogId logId, IReadOnlyList<ResolvedEvent> events, SortContext context) =>
        EventColumnView.Create(Reader(logId, events), Survivors(events.Count), context);

    private static IEventColumnReader Reader(EventLogId logId, IReadOnlyList<ResolvedEvent> events) =>
        EventColumnStore.Build(events, generation: 0, contentVersion: 0).CreateReader(logId);

    private static int[] Survivors(int count) => Enumerable.Range(0, count).ToArray();
}
