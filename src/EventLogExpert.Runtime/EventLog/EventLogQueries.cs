// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Filtering.Common.Filtering;
using EventLogExpert.Filtering.EventData;
using EventLogExpert.Runtime.LogTable;
using Fluxor;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.EventLog;

internal sealed class EventLogQueries(IState<RawEventStoreState> rawEventStore) : IEventLogQueries
{
    private readonly IState<RawEventStoreState> _rawEventStore = rawEventStore;

    public (DateTime After, DateTime Before) GetEventDateRange(DateTime fallbackUtcNow) =>
        _rawEventStore.Value.TryGetRawEventDateRange().RoundOrFallback(fallbackUtcNow);

    public ImmutableArray<string> GetPropertyValues(EventProperty property)
    {
        var byLog = _rawEventStore.Value.ByLog;

        return EventPropertyValuesCache.GetValues(byLog, byLog.Values.SelectMany(events => events), property);
    }
}
