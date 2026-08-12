// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Filtering.Evaluation;

namespace EventLogExpert.Runtime.LogTable.OrderedView;

internal sealed record ViewRequest(
    ViewIdentity Identity,
    long Sequence,
    IReadOnlyCollection<EventLogId> ScopeLogs,
    IReadOnlyDictionary<EventLogId, IEventColumnReader> ScopeReaders,
    SortContext Context,
    Filter Filter,
    Func<EventLocator, IEventColumnReader, bool> Predicate,
    bool? Hold = null);
