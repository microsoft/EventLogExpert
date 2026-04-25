// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Models;
using EventLogExpert.UI.Models;

namespace EventLogExpert.UI.Interfaces;

public interface IFilterService
{
    IReadOnlyDictionary<EventLogId, IReadOnlyList<DisplayEventModel>> FilterActiveLogs(
        IEnumerable<EventLogData> logData,
        EventFilter eventFilter);

    IReadOnlyList<DisplayEventModel> GetFilteredEvents(IEnumerable<DisplayEventModel> events, EventFilter eventFilter);

    bool TryParse(BasicFilter basicFilter, out string comparison);

    bool TryParseExpression(string? expression, out string error);
}
