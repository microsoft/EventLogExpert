// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Filtering.Common.Filtering;
using EventLogExpert.Filtering.EventData;
using EventLogExpert.Runtime.LogTable;
using Fluxor;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.EventLog;

internal sealed class EventLogQueries(
    IState<RawEventStoreState> rawEventStore,
    IState<EventLogState> eventLogState) : IEventLogQueries
{
    private readonly IState<EventLogState> _eventLogState = eventLogState;
    private readonly IState<RawEventStoreState> _rawEventStore = rawEventStore;

    public IReadOnlyList<string> GetChannelNames() =>
    [
        .. _eventLogState.Value.OpenLogs
            .Where(kvp => kvp.Value.Type == LogPathType.Channel)
            .Select(kvp => kvp.Key)
            .Distinct(StringComparer.OrdinalIgnoreCase)
    ];

    public ImmutableArray<string> GetEventDataFieldNames()
    {
        var byLog = _rawEventStore.Value.ByLog;

        return EventPropertyValuesCache.GetEventDataFieldNames(byLog, EnumerateAll(byLog));
    }

    public ImmutableArray<string> GetEventDataFieldValues(string fieldName)
    {
        var byLog = _rawEventStore.Value.ByLog;

        return EventPropertyValuesCache.GetEventDataFieldValues(byLog, EnumerateAll(byLog), fieldName);
    }

    public (DateTime After, DateTime Before) GetEventDateRange(DateTime fallbackUtcNow) =>
        _rawEventStore.Value.TryGetRawEventDateRange().RoundOrFallback(fallbackUtcNow);

    public ImmutableArray<string> GetPropertyValues(EventProperty property)
    {
        var byLog = _rawEventStore.Value.ByLog;

        return EventPropertyValuesCache.GetValues(byLog, EnumerateAll(byLog), property);
    }

    public ImmutableArray<string> GetUserDataFieldNames()
    {
        var byLog = _rawEventStore.Value.ByLog;

        return EventPropertyValuesCache.GetUserDataFieldNames(byLog, EnumerateAll(byLog));
    }

    public ImmutableArray<string> GetUserDataFieldValues(string fieldName)
    {
        var byLog = _rawEventStore.Value.ByLog;

        return EventPropertyValuesCache.GetUserDataFieldValues(byLog, EnumerateAll(byLog), fieldName);
    }

    private static IEnumerable<ResolvedEvent> EnumerateAll(
        ImmutableDictionary<EventLogId, EventColumnStore> byLog)
    {
        foreach (var (logId, store) in byLog)
        {
            var reader = store.CreateReader(logId);

            for (int physical = 0; physical < reader.Count; physical++)
            {
                yield return reader.GetDetail(reader.LocatorAt(physical));
            }
        }
    }
}
