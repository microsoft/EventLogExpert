// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Filtering;

namespace EventLogExpert.UI.Filter;

public interface IFilterService
{
    IReadOnlyDictionary<EventLogId, IReadOnlyList<ResolvedEvent>> FilterActiveLogs(
        IEnumerable<EventLogData> logData,
        EventFilter eventFilter);

    IReadOnlyList<ResolvedEvent> GetFilteredEvents(IEnumerable<ResolvedEvent> events, EventFilter eventFilter);

    bool TryParse(BasicFilter basicFilter, out string comparison);
}
