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

    EventFieldValue GetField(EventHandle handle, EventFieldId field);

    StructuredFieldResult GetUserData(EventHandle handle, string storageKey);

    EventHandle HandleAt(int index);

    bool TryGetEventData(EventHandle handle, string fieldName, out EventFieldValue value);
}
