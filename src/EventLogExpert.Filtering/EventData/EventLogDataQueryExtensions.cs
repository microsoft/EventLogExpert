// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Filtering.Common.Filtering;
using System.Globalization;

namespace EventLogExpert.Filtering.EventData;

internal static class EventLogDataQueryExtensions
{
    /// <summary>
    ///     Gets a distinct list of values for the specified <paramref name="property" /> across
    ///     <paramref name="events" />.
    /// </summary>
    public static IEnumerable<string> GetEventValues(this IEnumerable<ResolvedEvent> events, EventProperty property) =>
        property switch
        {
            EventProperty.Id => events.Select(e => e.Id.ToString(CultureInfo.InvariantCulture)).Distinct(),
            EventProperty.ActivityId => events.Select(e => e.ActivityId?.ToString() ?? string.Empty).Distinct(),
            EventProperty.Level => Enum.GetNames<SeverityLevel>(),
            EventProperty.Keywords => events.SelectMany(e => e.Keywords).Distinct(),
            EventProperty.Source => events.Select(e => e.Source).Distinct(),
            EventProperty.TaskCategory => events.Select(e => e.TaskCategory).Distinct(),
            EventProperty.LogName => events.Select(e => e.LogName).Distinct(),
            _ => [],
        };
}
