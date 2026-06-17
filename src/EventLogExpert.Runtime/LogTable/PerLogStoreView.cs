// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;

namespace EventLogExpert.Runtime.LogTable;

internal static class PerLogStoreView
{
    internal static PerLogStore? Assemble(LogTableState logTable, RawEventStoreState raw, EventLogId logId)
    {
        var table = logTable.EventTables.FirstOrDefault(t => t.Id == logId && !t.IsCombined);

        if (table is null) { return null; }

        var displayList = logTable.PerLogEvents.TryGetValue(logId, out var list)
            ? list
            : SegmentedSortedList.CreateSorted([], logTable.SortContext);

        return new PerLogStore(logId, table.LogName, table.LogPathType, RawEventsForLog(raw, logId), displayList);
    }

    internal static IEnumerable<PerLogStore> AssembleAll(LogTableState logTable, RawEventStoreState raw)
    {
        foreach (var table in logTable.EventTables)
        {
            if (table.IsCombined) { continue; }

            if (Assemble(logTable, raw, table.Id) is { } store) { yield return store; }
        }
    }

    internal static RawEventList RawEventsForLog(RawEventStoreState raw, EventLogId logId) =>
        raw.ByLog.TryGetValue(logId, out var list) ? list : RawEventList.Empty;
}
