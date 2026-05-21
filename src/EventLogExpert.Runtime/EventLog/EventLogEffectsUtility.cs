// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.EventLog;

internal static class EventLogEffectsUtility
{
    internal static EventLogData AddEventsToOneLog(EventLogData logData, List<ResolvedEvent> eventsToAdd)
    {
        if (eventsToAdd.Count == 0) { return logData; }

        eventsToAdd.AddRange(logData.Events);

        return logData with { Events = eventsToAdd.AsReadOnly() };
    }

    internal static ImmutableDictionary<string, EventLogData> DistributeEventsToManyLogs(
        ImmutableDictionary<string, EventLogData> logsToUpdate,
        IEnumerable<ResolvedEvent> eventsToDistribute)
    {
        var eventsByLog = new Dictionary<string, List<ResolvedEvent>>();

        foreach (var e in eventsToDistribute)
        {
            if (!logsToUpdate.ContainsKey(e.OwningLog)) { continue; }

            if (!eventsByLog.TryGetValue(e.OwningLog, out var list))
            {
                list = [];
                eventsByLog[e.OwningLog] = list;
            }

            list.Add(e);
        }

        var newLogs = logsToUpdate;

        foreach (var (logName, newEvents) in eventsByLog)
        {
            var log = logsToUpdate[logName];
            var newLogData = AddEventsToOneLog(log, newEvents);
            newLogs = newLogs.SetItem(logName, newLogData);
        }

        return newLogs;
    }

    internal static async Task StopProducerAsync(Task producerTask)
    {
        try { await producerTask; }
        catch { /* Intentionally swallowed — caller handles error reporting. */ }
    }
}
