// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Filtering;

namespace EventLogExpert.UI.EventLog;

public sealed record EventLogData(
    string Name,
    LogPathType Type,
    IReadOnlyList<ResolvedEvent> Events)
{
    public EventLogId Id { get; } = EventLogId.Create();

    /// <summary>Gets a distinct list of values for the specified <paramref name="property" />.</summary>
    public IEnumerable<string> GetEventValues(EventProperty property) =>
        property switch
        {
            EventProperty.Id => Events.Select(e => e.Id.ToString()).Distinct(),
            EventProperty.ActivityId => Events.Select(e => e.ActivityId?.ToString() ?? string.Empty).Distinct(),
            EventProperty.Level => Enum.GetNames<SeverityLevel>(),
            EventProperty.Keywords => Events.SelectMany(e => e.Keywords).Distinct(),
            EventProperty.Source => Events.Select(e => e.Source).Distinct(),
            EventProperty.TaskCategory => Events.Select(e => e.TaskCategory).Distinct(),
            _ => [],
        };
}
