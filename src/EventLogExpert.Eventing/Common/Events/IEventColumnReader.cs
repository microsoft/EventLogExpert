// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Structured;

namespace EventLogExpert.Eventing.Common.Events;

public interface IEventColumnReader
{
    long ContentVersion { get; }

    int Count { get; }

    int Generation { get; }

    EventLogId LogId { get; }

    EventDataFieldEnumerator EnumerateEventData(EventLocator locator);

    UserDataFieldEnumerator EnumerateUserData(EventLocator locator);

    EventFieldValue GetField(EventLocator locator, EventFieldId field);

    IReadOnlyList<string> GetKeywords(EventLocator locator);

    StructuredFieldResult GetUserData(EventLocator locator, string storageKey);

    bool GetUserDataIncomplete(EventLocator locator);

    EventLocator LocatorAt(int index);

    bool TryGetEventData(EventLocator locator, string fieldName, out EventFieldValue value);
}
