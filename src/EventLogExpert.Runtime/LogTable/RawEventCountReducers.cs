// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.EventLog;
using Fluxor;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.LogTable;

internal sealed class RawEventCountReducers
{
    [ReducerMethod]
    public static RawEventCountState ReduceAddTable(RawEventCountState state, AddTableAction action) =>
        state with { ByLog = state.ByLog.SetItem(action.LogData.Id, default) };

    [ReducerMethod(typeof(CloseAllLogsAction))]
    public static RawEventCountState ReduceCloseAll(RawEventCountState state) =>
        state.ByLog.IsEmpty ?
            state :
            state with { ByLog = ImmutableDictionary<EventLogId, ProviderResolutionCounts>.Empty };

    [ReducerMethod]
    public static RawEventCountState ReduceCloseLog(RawEventCountState state, CloseLogAction action) =>
        state.ByLog.ContainsKey(action.LogId) ?
            state with { ByLog = state.ByLog.Remove(action.LogId) } :
            state;

    [ReducerMethod]
    public static RawEventCountState ReduceIngestRawEvents(RawEventCountState state, IngestRawEventsAction action)
    {
        if (action.EventsByLog.Count == 0) { return state; }

        var builder = state.ByLog.ToBuilder();
        bool changed = false;

        foreach (var (logId, events) in action.EventsByLog)
        {
            if (!builder.TryGetValue(logId, out var existing)) { continue; }

            var batch = CountBatch(events);

            var updated = action.Mode switch
            {
                RawIngestMode.Replace => batch,
                RawIngestMode.Append or RawIngestMode.Prepend => existing.Add(batch),
                _ => throw new ArgumentOutOfRangeException(nameof(action), action.Mode, "Unknown raw ingest mode.")
            };

            if (updated.Equals(existing)) { continue; }

            builder[logId] = updated;
            changed = true;
        }

        return changed ? state with { ByLog = builder.ToImmutable() } : state;
    }

    [ReducerMethod]
    public static RawEventCountState ReduceLoadEvents(RawEventCountState state, LoadEventsAction action)
    {
        if (!state.ByLog.TryGetValue(action.LogData.Id, out var existing)) { return state; }

        var updated = CountBatch(action.Events);

        return existing.Equals(updated) ?
            state :
            state with { ByLog = state.ByLog.SetItem(action.LogData.Id, updated) };
    }

    [ReducerMethod]
    public static RawEventCountState ReduceLoadEventsPartial(RawEventCountState state, LoadEventsPartialAction action)
    {
        if (!state.ByLog.TryGetValue(action.LogData.Id, out var existing)) { return state; }

        var updated = existing.Add(CountBatch(action.Events));

        return updated.Equals(existing) ?
            state :
            state with { ByLog = state.ByLog.SetItem(action.LogData.Id, updated) };
    }

    private static ProviderResolutionCounts CountBatch(IReadOnlyList<ResolvedEvent> events)
    {
        ProviderResolutionCounts counts = default;

        foreach (var resolvedEvent in events)
        {
            counts = counts.WithStatus(resolvedEvent.ResolutionStatus);
        }

        return counts;
    }
}
