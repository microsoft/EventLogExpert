// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Eventing.Common.EventLogs;

public readonly record struct EventLogId(Guid Value)
{
    public static EventLogId Create() => new(Guid.NewGuid());
}
