// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
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

        return EventPropertyValuesCache.GetEventDataFieldNames(byLog, byLog.Values.SelectMany(events => events));
    }

    public ImmutableArray<string> GetEventDataFieldValues(string fieldName)
    {
        var byLog = _rawEventStore.Value.ByLog;

        return EventPropertyValuesCache.GetEventDataFieldValues(
            byLog,
            byLog.Values.SelectMany(events => events),
            fieldName);
    }

    public (DateTime After, DateTime Before) GetEventDateRange(DateTime fallbackUtcNow) =>
        _rawEventStore.Value.TryGetRawEventDateRange().RoundOrFallback(fallbackUtcNow);

    public ImmutableArray<string> GetPropertyValues(EventProperty property)
    {
        var byLog = _rawEventStore.Value.ByLog;

        return EventPropertyValuesCache.GetValues(byLog, byLog.Values.SelectMany(events => events), property);
    }
}
