// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Runtime.EventLog;
using Fluxor;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.LogTable;

internal sealed class RawEventStoreReducers
{
    [ReducerMethod]
    public static RawEventStoreState ReduceAddTable(RawEventStoreState state, AddTableAction action) =>
        state with { ByLog = state.ByLog.SetItem(action.LogData.Id, RawEventList.Empty) };

    [ReducerMethod(typeof(CloseAllLogsAction))]
    public static RawEventStoreState ReduceCloseAll(RawEventStoreState state) =>
        state.ByLog.IsEmpty
            ? state
            : state with { ByLog = ImmutableDictionary<EventLogId, RawEventList>.Empty };

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
            // Open-log guard: AddTable seeds the entry, CloseLog removes it - so a stale post-close ingest cannot
            // resurrect or orphan a log.
            if (!builder.TryGetValue(logId, out var existing)) { continue; }

            var updated = action.Mode switch
            {
                RawIngestMode.Replace => RawEventList.Empty.Append(events),
                RawIngestMode.Append => existing.Append(events),
                RawIngestMode.Prepend => existing.Prepend(events),
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
        if (!state.ByLog.TryGetValue(action.LogData.Id, out var existing)) { return state; }

        var updated = RawEventList.Empty.Append(action.Events);

        return ReferenceEquals(updated, existing)
            ? state
            : state with { ByLog = state.ByLog.SetItem(action.LogData.Id, updated) };
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
