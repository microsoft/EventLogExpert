// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.LogTable.OrderedView;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.Tests.TestUtils;

internal static class RetainedViewTestFactory
{
    internal static OrderedViewReady? RetainedFor(this LogTableState state, OrderedViewReady view) =>
        state.RetainedOrderedViews.GetValueOrDefault(TabOf(view));

    internal static ImmutableDictionary<EventLogId, OrderedViewReady> RetainedMap(params OrderedViewReady[] views)
    {
        var map = ImmutableDictionary<EventLogId, OrderedViewReady>.Empty;

        foreach (var view in views)
        {
            map = map.SetItem(TabOf(view), view);
        }

        return map;
    }

    private static EventLogId TabOf(OrderedViewReady view) =>
        view.Identity?.ActiveLogId ??
        throw new InvalidOperationException("A retained view must carry the tab it was served for.");
}
