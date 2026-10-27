// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;

namespace EventLogExpert.Runtime.LogTable.OrderedView;

internal readonly record struct ViewProbe(
    ViewIdentity LiveIdentity,
    IReadOnlyDictionary<EventLogId, IEventColumnReader> Readers,
    SortContext SortContext);
