// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.Memory;

internal sealed record MemoryGovernorRecomputedAction(
    MemoryPressureLevel Level,
    long CurrentBytes,
    long BudgetBytes,
    ImmutableHashSet<EventLogId> StalePartialLogIds);
