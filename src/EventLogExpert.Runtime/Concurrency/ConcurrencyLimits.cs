// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Concurrency;

/// <summary>Shared concurrency limits for background, I/O-bound work across the runtime.</summary>
internal static class ConcurrencyLimits
{
    /// <summary>
    ///     Max degree of parallelism for background I/O-bound work (live-log resolution, exported-log folder scans).
    ///     Capped at one below the processor count and floored at 1, so a burst of concurrent file opens leaves a core for the
    ///     UI and never saturates the disk. Centralized here so every I/O fan-out shares one policy.
    /// </summary>
    internal static int MaxBackgroundIoParallelism { get; } = Math.Max(1, Environment.ProcessorCount - 1);
}
