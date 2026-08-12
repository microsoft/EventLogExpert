// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Filtering.Evaluation;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.LogTable.OrderedView;

internal abstract record OrderedViewUpdate(long SnapshotVersion, ViewIdentity? Identity, long Sequence);

internal sealed record OrderedViewReady(
    long SnapshotVersion,
    ViewIdentity? Identity,
    long Sequence,
    EventLogId? SingleLogId,
    ImmutableHashSet<LogGeneration> InScope,
    IEventColumnView View,
    SortContext Config,
    Filter Filter) : OrderedViewUpdate(SnapshotVersion, Identity, Sequence)
{
    public ViewContentToken ContentToken { get; init; } = ViewContentToken.Empty;
}

internal sealed record OrderedViewCleared(long SnapshotVersion, ViewIdentity? Identity, long Sequence)
    : OrderedViewUpdate(SnapshotVersion, Identity, Sequence);
