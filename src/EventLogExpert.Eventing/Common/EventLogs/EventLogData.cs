// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.Events;

namespace EventLogExpert.Eventing.Common.EventLogs;

public sealed record EventLogData(
    string Name,
    LogPathType Type,
    IReadOnlyList<ResolvedEvent> Events)
{
    public EventLogId Id { get; } = EventLogId.Create();
}
