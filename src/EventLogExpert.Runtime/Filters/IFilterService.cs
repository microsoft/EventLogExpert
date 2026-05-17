// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Filtering.Basic;
using EventLogExpert.Filtering.Runtime;

namespace EventLogExpert.Runtime.Filters;

public interface IFilterService
{
    IReadOnlyDictionary<EventLogId, IReadOnlyList<ResolvedEvent>> FilterActiveLogs(
        IEnumerable<EventLogData> logData,
        Filter filter);

    IReadOnlyList<ResolvedEvent> GetFilteredEvents(IEnumerable<ResolvedEvent> events, Filter filter);

    bool TryFormat(BasicFilter basicFilter, out string comparison);
}
