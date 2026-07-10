// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.EventLog;
using Fluxor;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.LogTable;

internal sealed class RawEventStoreReducers
{
    [ReducerMethod]
    public static RawEventStoreState ReduceAddTable(RawEventStoreState state, AddTableAction action) =>
        state with { ByLog = state.ByLog.SetItem(action.LogData.Id, EventColumnStore.Empty) };

    [ReducerMethod(typeof(CloseAllLogsAction))]
    public static RawEventStoreState ReduceCloseAll(RawEventStoreState state) =>
        state.ByLog.IsEmpty
            ? state
            : state with { ByLog = ImmutableDictionary<EventLogId, EventColumnStore>.Empty };

    [ReducerMethod]
    public static RawEventStoreState ReduceCloseLog(RawEventStoreState state, CloseLogAction action) =>
        state.ByLog.ContainsKey(action.LogId)
            ? state with { ByLog = state.ByLog.Remove(action.LogId) }
            : state;

    [ReducerMethod]
    public static RawEventStoreState ReduceIngestRawEvents(RawEventStoreState state, IngestRawEventsAction action)
    {
        if (action.EventsByLog.Count == 0) { return state; }

        var builder = state.ByLog.ToBuilder();
        bool changed = false;

        foreach (var (logId, events) in action.EventsByLog)
        {
            // Open-log guard: AddTable seeds the entry and CloseLog removes it, so a stale post-close ingest cannot
            // resurrect or orphan a log.
            if (!builder.TryGetValue(logId, out var existing)) { continue; }

            var updated = action.Mode switch
            {
                // Replace rebuilds the key with a strictly monotonic ContentVersion (never reset to 0), so a filter pass
                // captured before the rebuild still observes the change (the M1 race guard).
                RawIngestMode.Replace => EventColumnStore.Build(
                    events, existing.Generation, existing.ContentVersion + 1),

                // Prepend maps to Append: physical order is not display order, so tail-appended events still sort
                // correctly and every prior locator index stays valid.
                RawIngestMode.Append or RawIngestMode.Prepend => existing.Append(events),
                _ => throw new ArgumentOutOfRangeException(nameof(action), action.Mode, "Unknown raw ingest mode.")
            };

            if (!ReferenceEquals(updated, existing))
            {
                builder[logId] = updated;
                changed = true;
            }
        }

        return changed ? state with { ByLog = builder.ToImmutable() } : state;
    }

    [ReducerMethod]
    public static RawEventStoreState ReduceLoadEvents(RawEventStoreState state, LoadEventsAction action)
    {
        if (!state.ByLog.TryGetValue(action.LogData.Id, out var existing) ||
            (existing.Count == 0 && action.Events.Count == 0)) { return state; }

        // Full (re)build with a monotonic ContentVersion (M1) so the filter effect's pre-Task.Run capture detects the
        // swap even after a prior partial left the key non-empty.
        var updated = EventColumnStore.Build(action.Events, existing.Generation, existing.ContentVersion + 1);

        return state with { ByLog = state.ByLog.SetItem(action.LogData.Id, updated) };
    }

    [ReducerMethod]
    public static RawEventStoreState ReduceLoadEventsPartial(RawEventStoreState state, LoadEventsPartialAction action)
    {
        if (!state.ByLog.TryGetValue(action.LogData.Id, out var existing)) { return state; }

        var updated = existing.Append(action.Events);

        return ReferenceEquals(updated, existing)
            ? state
            : state with { ByLog = state.ByLog.SetItem(action.LogData.Id, updated) };
    }
}
