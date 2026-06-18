// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;

namespace EventLogExpert.Eventing.Common.EventLogs;

public sealed record EventLogData(string Name, LogPathType Type)
{
    public EventLogId Id { get; init; } = EventLogId.Create();
}
