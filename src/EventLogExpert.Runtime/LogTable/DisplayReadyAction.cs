// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.LogTable;

public sealed record DisplayReadyAction
{
    internal IReadOnlyDictionary<EventLogId, SegmentedSortedList> Lists { get; init; } =
        ImmutableDictionary<EventLogId, SegmentedSortedList>.Empty;
}
