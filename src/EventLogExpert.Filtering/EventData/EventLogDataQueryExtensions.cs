// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Filtering.Common.Filtering;
using System.Globalization;

namespace EventLogExpert.Filtering.EventData;

internal static class EventLogDataQueryExtensions
{
    /// <summary>Gets a distinct list of values for the specified <paramref name="property" />.</summary>
    public static IEnumerable<string> GetEventValues(this EventLogData log, EventProperty property) =>
        property switch
        {
            EventProperty.Id => log.Events.Select(e => e.Id.ToString(CultureInfo.InvariantCulture)).Distinct(),
            EventProperty.ActivityId => log.Events.Select(e => e.ActivityId?.ToString() ?? string.Empty).Distinct(),
            EventProperty.Level => Enum.GetNames<SeverityLevel>(),
            EventProperty.Keywords => log.Events.SelectMany(e => e.Keywords).Distinct(),
            EventProperty.Source => log.Events.Select(e => e.Source).Distinct(),
            EventProperty.TaskCategory => log.Events.Select(e => e.TaskCategory).Distinct(),
            EventProperty.LogName => log.Events.Select(e => e.LogName).Distinct(),
            _ => [],
        };
}
