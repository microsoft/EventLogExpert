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

    public EventDataFieldEnumerator EnumerateEventData(EventLocator locator) => new(GetEvent(locator).EventData);

    public UserDataFieldEnumerator EnumerateUserData(EventLocator locator)
    {
        ResolvedEvent resolvedEvent = GetEvent(locator);

        return new UserDataFieldEnumerator(resolvedEvent.UserData, resolvedEvent.UserDataIncomplete);
    }

    // Legacy-adapter convenience: the current store keeps whole ResolvedEvent objects, so a locator resolves directly to
    // one. The real column store will not retain events; its view rehydrates from columns instead.
    public ResolvedEvent GetEvent(EventLocator locator)
    {
        if (locator.LogId != LogId || locator.Generation != Generation)
        {
            throw new ArgumentException("Locator does not belong to this reader's log/generation.", nameof(locator));
        }

        return _events[locator.Index];
    }

    public EventFieldValue GetField(EventLocator locator, EventFieldId field) =>
        ResolvedEventFieldReader.GetField(GetEvent(locator), field);

    public IReadOnlyList<string> GetKeywords(EventLocator locator) => GetEvent(locator).Keywords;

    public StructuredFieldResult GetUserData(EventLocator locator, string storageKey) =>
        GetEvent(locator).TryGetUserDataValues(storageKey);

    public bool GetUserDataIncomplete(EventLocator locator) => GetEvent(locator).UserDataIncomplete;

    public EventLocator LocatorAt(int index) => new(LogId, Generation, index);

    public bool TryGetEventData(EventLocator locator, string fieldName, out EventFieldValue value) =>
        GetEvent(locator).EventData.TryGetValue(fieldName, out value);
}
