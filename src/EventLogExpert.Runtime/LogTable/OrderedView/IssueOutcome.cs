// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;

namespace EventLogExpert.Runtime.LogTable.OrderedView;

internal readonly record struct IssueOutcome(
    IssueKind Kind,
    long Sequence,
    IReadOnlyDictionary<EventLogId, IEventColumnReader>? Readers,
    SortContext SortContext,
    bool ClearFault)
{
    internal static IssueOutcome Deduped { get; } = new(IssueKind.Deduped, 0, null, default, false);

    internal static IssueOutcome Stale { get; } = new(IssueKind.Stale, 0, null, default, false);

    internal static IssueOutcome Issued(long sequence, ViewProbe probe, bool clearFault) =>
        new(IssueKind.Issued, sequence, probe.Readers, probe.SortContext, clearFault);
}
