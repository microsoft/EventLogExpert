// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using Fluxor;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.Memory;

[FeatureState]
internal sealed record MemoryGovernorState
{
    internal long BaselineBytes { get; init; }

    internal long BudgetBytes { get; init; } = long.MaxValue;

    internal long CurrentBytes { get; init; }

    internal MemoryPressureLevel Level { get; init; } = MemoryPressureLevel.Normal;

    internal ImmutableHashSet<EventLogId> PartiallyLoadedForMemory { get; init; } =
        ImmutableHashSet<EventLogId>.Empty;
}
