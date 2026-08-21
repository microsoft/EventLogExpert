// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Memory;

/// <summary>
///     Published by <see cref="MemoryIndicatorEffect" /> when the displayed whole-MiB managed heap or the effective
///     <see cref="MemoryUsageLevel" /> changes. <paramref name="WorkingSetBytes" /> is sampled only at this point.
/// </summary>
internal sealed record MemoryIndicatorRecomputedAction(
    long UsedMebibytes,
    MemoryUsageLevel Level,
    long WorkingSetBytes);
