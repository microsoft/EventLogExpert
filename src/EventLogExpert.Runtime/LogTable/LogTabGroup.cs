// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.LogTable;

public sealed record LogTabGroup(LogTabGroupId Id, string Name, ImmutableHashSet<EventLogId> MemberIds)
{
    public bool IsCollapsed { get; init; }
}
