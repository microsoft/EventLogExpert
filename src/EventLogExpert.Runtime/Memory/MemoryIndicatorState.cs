// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Fluxor;

namespace EventLogExpert.Runtime.Memory;

/// <summary>
///     The advisory memory-indicator state projected to the status bar. <see cref="UsedMebibytes" /> is the managed
///     heap quantized to whole MiB (the displayed value); <see cref="WorkingSetBytes" /> is the process working set for
///     the reconciliation tooltip; <see cref="Level" /> drives the chip color.
/// </summary>
[FeatureState]
internal sealed record MemoryIndicatorState
{
    internal long UsedMebibytes { get; init; }

    internal long WorkingSetBytes { get; init; }

    internal MemoryUsageLevel Level { get; init; } = MemoryUsageLevel.Normal;
}
