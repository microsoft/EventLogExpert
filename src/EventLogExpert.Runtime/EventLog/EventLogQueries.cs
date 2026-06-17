// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using Fluxor;

namespace EventLogExpert.Runtime.EventLog;

internal sealed class EventLogQueries(IState<EventLogState> eventLogState) : IEventLogQueries
{
    private readonly IState<EventLogState> _eventLogState = eventLogState;

    public (DateTime After, DateTime Before) GetEventDateRange(DateTime fallbackUtcNow) =>
        _eventLogState.Value.ActiveLogs.Values.GetEventDateRange(fallbackUtcNow);
}
