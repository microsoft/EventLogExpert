// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Filtering.Compilation;
using EventLogExpert.Filtering.Evaluation;

namespace EventLogExpert.Runtime.LogTable;

internal static class DisplayViewBuilder
{
    internal static EventColumnView Build(
        EventColumnStore store,
        EventLogId logId,
        Filter filter,
        SortContext context)
    {
        ArgumentNullException.ThrowIfNull(store);

        var reader = store.CreateReader(logId);
        var survivors = FilterService.GetSurvivingOrder(reader, filter);
        int[] order = survivors as int[] ?? [.. survivors];

        return EventColumnView.Create(reader, order, context);
    }
}
