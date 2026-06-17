// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Runtime.EventLog;
using Fluxor;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.LogTable;

// Mirrors RawEventStoreReducers' FULL lifecycle so RawEventCountState.Total stays equal to the sum of the raw
// store's per-log counts: AddTable seeds 0, CloseLog removes, CloseAll resets, IngestRawEvents replaces/adds by
// mode. The same open-log guard (skip ids not currently present) prevents a stale post-close ingest from
// resurrecting a count.
internal sealed class RawEventCountReducers
{
    [ReducerMethod]
    public static RawEventCountState ReduceAddTable(RawEventCountState state, AddTableAction action) =>
        state with { ByLog = state.ByLog.SetItem(action.LogData.Id, 0) };

    [ReducerMethod(typeof(CloseAllLogsAction))]
    public static RawEventCountState ReduceCloseAll(RawEventCountState state) =>
        state.ByLog.IsEmpty
            ? state
            : state with { ByLog = ImmutableDictionary<EventLogId, int>.Empty };

    [ReducerMethod]
    public static RawEventCountState ReduceCloseLog(RawEventCountState state, CloseLogAction action) =>
        state.ByLog.ContainsKey(action.LogId)
            ? state with { ByLog = state.ByLog.Remove(action.LogId) }
            : state;

    [ReducerMethod]
    public static RawEventCountState ReduceIngestRawEvents(RawEventCountState state, IngestRawEventsAction action)
    {
        if (action.EventsByLog.Count == 0) { return state; }

        var builder = state.ByLog.ToBuilder();
        bool changed = false;

        foreach (var (logId, events) in action.EventsByLog)
        {
            if (!builder.TryGetValue(logId, out var existing)) { continue; }

            var updated = action.Mode switch
            {
                RawIngestMode.Replace => events.Count,
                RawIngestMode.Append or RawIngestMode.Prepend => existing + events.Count,
                _ => throw new ArgumentOutOfRangeException(nameof(action), action.Mode, "Unknown raw ingest mode.")
            };

            if (updated == existing) { continue; }

            builder[logId] = updated;
            changed = true;
        }

        return changed ? state with { ByLog = builder.ToImmutable() } : state;
    }

    [ReducerMethod]
    public static RawEventCountState ReduceLoadEvents(RawEventCountState state, LoadEventsAction action)
    {
        if (!state.ByLog.TryGetValue(action.LogData.Id, out var existing)) { return state; }

        return existing == action.Events.Count
            ? state
            : state with { ByLog = state.ByLog.SetItem(action.LogData.Id, action.Events.Count) };
    }

    [ReducerMethod]
    public static RawEventCountState ReduceLoadEventsPartial(RawEventCountState state, LoadEventsPartialAction action)
    {
        if (!state.ByLog.TryGetValue(action.LogData.Id, out var existing)) { return state; }

        var updated = existing + action.Events.Count;

        return updated == existing
            ? state
            : state with { ByLog = state.ByLog.SetItem(action.LogData.Id, updated) };
    }
}
