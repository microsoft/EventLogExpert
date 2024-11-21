// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Models;
using EventLogExpert.UI.Models;
// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Interfaces;

public interface IFilterService
{
    bool IsXmlEnabled { get; }

    IDictionary<EventLogId, IEnumerable<DisplayEventModel>> FilterActiveLogs(
        IEnumerable<EventLogData> logData,
        EventFilter eventFilter);

    IEnumerable<DisplayEventModel> GetFilteredEvents(IEnumerable<DisplayEventModel> events, EventFilter eventFilter);

    bool TryParse(FilterModel filterModel, out string comparison);

    bool TryParseExpression(string? expression, out string error, bool ignoreXml = false);
}
