// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.UI.EventLog;
using EventLogExpert.UI.Models;

namespace EventLogExpert.UI.Interfaces;

public interface IFilterService
{
    IReadOnlyDictionary<EventLogId, IReadOnlyList<ResolvedEvent>> FilterActiveLogs(
        IEnumerable<EventLogData> logData,
        EventFilter eventFilter);

    IReadOnlyList<ResolvedEvent> GetFilteredEvents(IEnumerable<ResolvedEvent> events, EventFilter eventFilter);

    bool TryParse(BasicFilter basicFilter, out string comparison);

    bool TryParseExpression(string? expression, out string error);
}
