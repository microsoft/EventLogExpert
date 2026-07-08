// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Structured;

namespace EventLogExpert.Eventing.Common.Events;

public sealed class LegacyEventColumnReader : IEventColumnReader
{
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

    public EventFieldValue GetField(EventHandle handle, EventFieldId field) =>
        ResolvedEventFieldReader.GetField(GetEvent(handle), field);

    public StructuredFieldResult GetUserData(EventHandle handle, string storageKey) =>
        GetEvent(handle).TryGetUserDataValues(storageKey);

    public EventHandle HandleAt(int index) => new(LogId, Generation, index);

    public bool TryGetEventData(EventHandle handle, string fieldName, out EventFieldValue value) =>
        GetEvent(handle).EventData.TryGetValue(fieldName, out value);
}
