// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Concurrency;

/// <summary>Shared concurrency limits for background I/O-bound and CPU-bound work across the runtime.</summary>
internal static class ConcurrencyLimits
{
    /// <summary>
    ///     Interactive headroom reserved within <see cref="MaxCpuParallelism" />: while interactive work is present,
    ///     background CPU work is held back by this many slots so a find scan is never queued behind the full analytics
    ///     fan-out. Clamped to the valid range by <see cref="CpuWorkScheduler" />.
    /// </summary>
    internal static int CpuInteractiveReserve { get; } = 2;

    /// <summary>
    ///     Max degree of parallelism for background I/O-bound work (live-log resolution, exported-log folder scans).
    ///     Capped at one below the processor count and floored at 1, so a burst of concurrent file opens leaves a core for the
    ///     UI and never saturates the disk. Centralized here so every I/O fan-out shares one policy.
    /// </summary>
    internal static int MaxBackgroundIoParallelism { get; } = Math.Max(1, Environment.ProcessorCount - 1);

    /// <summary>
    ///     Concurrently-running CPU-bound analytics items admitted through <see cref="CpuWorkScheduler" />, set to the
    ///     processor count. The scheduler is work-conserving, so a single log's background work still uses every core while
    ///     the interactive reserve engages only when the user interacts. Separate from
    ///     <see cref="MaxBackgroundIoParallelism" /> (CPU and I/O fan-outs are governed independently).
    /// </summary>
    internal static int MaxCpuParallelism { get; } = Environment.ProcessorCount;
}
