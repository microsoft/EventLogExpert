// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Readers;

namespace EventLogExpert.Eventing.Common.Events;

internal static class ResolvedEventFieldReader
{
    internal static readonly EventFieldValue Absent = EventFieldValue.FromProperty(EventProperty.FromReference(null));

    internal static EventFieldValue GetField(ResolvedEvent resolvedEvent, EventFieldId field)
    {
        return field switch
        {
            EventFieldId.Id => EventFieldValue.FromProperty(resolvedEvent.Id),
            EventFieldId.RecordId => resolvedEvent.RecordId is { } recordId ? EventFieldValue.FromProperty(recordId) : Absent,
            EventFieldId.Level => EventFieldValue.FromProperty(resolvedEvent.Level),
            EventFieldId.TimeCreated => EventFieldValue.FromProperty(resolvedEvent.TimeCreated),
            EventFieldId.ActivityId => resolvedEvent.ActivityId is { } activityId ? EventFieldValue.FromProperty(activityId) : Absent,
            EventFieldId.LogName => EventFieldValue.FromProperty(resolvedEvent.LogName),
            EventFieldId.ComputerName => EventFieldValue.FromProperty(resolvedEvent.ComputerName),
            EventFieldId.Source => EventFieldValue.FromProperty(resolvedEvent.Source),
            EventFieldId.TaskCategory => EventFieldValue.FromProperty(resolvedEvent.TaskCategory),
            EventFieldId.KeywordsDisplay => EventFieldValue.FromProperty(resolvedEvent.KeywordsDisplayName),
            EventFieldId.ProcessId => resolvedEvent.ProcessId is { } processId ? EventFieldValue.FromProperty(processId) : Absent,
            EventFieldId.ThreadId => resolvedEvent.ThreadId is { } threadId ? EventFieldValue.FromProperty(threadId) : Absent,
            EventFieldId.UserId => EventFieldValue.FromProperty(resolvedEvent.UserId),
            EventFieldId.Description => EventFieldValue.FromProperty(resolvedEvent.Description),
            EventFieldId.Xml => EventFieldValue.FromProperty(resolvedEvent.Xml),
            EventFieldId.OwningLog => EventFieldValue.FromProperty(resolvedEvent.OwningLog),
            EventFieldId.Opcode => EventFieldValue.FromProperty(resolvedEvent.Opcode),
            EventFieldId.RelatedActivityId => resolvedEvent.RelatedActivityId is { } relatedActivityId
                ? EventFieldValue.FromProperty(relatedActivityId)
                : Absent,
            _ => Absent
        };
    }
}
