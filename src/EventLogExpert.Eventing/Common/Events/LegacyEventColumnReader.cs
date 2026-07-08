// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Readers;
using EventLogExpert.Eventing.Structured;

namespace EventLogExpert.Eventing.Common.Events;

public sealed class LegacyEventColumnReader : IEventColumnReader
{
    private static readonly EventFieldValue s_absent = EventFieldValue.FromProperty(EventProperty.FromReference(null));

    private readonly IReadOnlyList<ResolvedEvent> _events;

    public LegacyEventColumnReader(EventLogId logId, int generation, long contentVersion, IReadOnlyList<ResolvedEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);

        LogId = logId;
        Generation = generation;
        ContentVersion = contentVersion;
        _events = events;
    }

    public long ContentVersion { get; }

    public int Count => _events.Count;

    public IReadOnlyList<ResolvedEvent> Events => _events;

    public int Generation { get; }

    public EventLogId LogId { get; }

    // Legacy-adapter convenience: the current store keeps whole ResolvedEvent objects, so a handle resolves directly to
    // one. The real column store will not retain events; its view rehydrates from columns instead.
    public ResolvedEvent GetEvent(EventHandle handle)
    {
        if (handle.LogId != LogId || handle.Generation != Generation)
        {
            throw new ArgumentException("Handle does not belong to this reader's log/generation.", nameof(handle));
        }

        return _events[handle.Index];
    }

    public EventFieldValue GetField(EventHandle handle, EventFieldId field)
    {
        ResolvedEvent resolvedEvent = GetEvent(handle);

        return field switch
        {
            EventFieldId.Id => EventFieldValue.FromProperty(resolvedEvent.Id),
            EventFieldId.RecordId => resolvedEvent.RecordId is { } recordId ? EventFieldValue.FromProperty(recordId) : s_absent,
            EventFieldId.Level => EventFieldValue.FromProperty(resolvedEvent.Level),
            EventFieldId.TimeCreated => EventFieldValue.FromProperty(resolvedEvent.TimeCreated),
            EventFieldId.ActivityId => resolvedEvent.ActivityId is { } activityId ? EventFieldValue.FromProperty(activityId) : s_absent,
            EventFieldId.LogName => EventFieldValue.FromProperty(resolvedEvent.LogName),
            EventFieldId.ComputerName => EventFieldValue.FromProperty(resolvedEvent.ComputerName),
            EventFieldId.Source => EventFieldValue.FromProperty(resolvedEvent.Source),
            EventFieldId.TaskCategory => EventFieldValue.FromProperty(resolvedEvent.TaskCategory),
            EventFieldId.KeywordsDisplay => EventFieldValue.FromProperty(resolvedEvent.KeywordsDisplayName),
            EventFieldId.ProcessId => resolvedEvent.ProcessId is { } processId ? EventFieldValue.FromProperty(processId) : s_absent,
            EventFieldId.ThreadId => resolvedEvent.ThreadId is { } threadId ? EventFieldValue.FromProperty(threadId) : s_absent,
            EventFieldId.UserId => EventFieldValue.FromProperty(resolvedEvent.UserId),
            EventFieldId.Description => EventFieldValue.FromProperty(resolvedEvent.Description),
            EventFieldId.Xml => EventFieldValue.FromProperty(resolvedEvent.Xml),
            EventFieldId.OwningLog => EventFieldValue.FromProperty(resolvedEvent.OwningLog),
            _ => s_absent
        };
    }

    public StructuredFieldResult GetUserData(EventHandle handle, string storageKey) =>
        GetEvent(handle).TryGetUserDataValues(storageKey);

    public EventHandle HandleAt(int index) => new(LogId, Generation, index);

    public bool TryGetEventData(EventHandle handle, string fieldName, out EventFieldValue value) =>
        GetEvent(handle).EventData.TryGetValue(fieldName, out value);
}
