// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Concurrency;

/// <summary>Admission priority for CPU-bound work submitted to <see cref="ICpuWorkScheduler" />.</summary>
public enum CpuWorkPriority
{
    /// <summary>Latency-critical work the user awaits keystroke-by-keystroke (find); admitted first, protected by the reserve.</summary>
    Interactive,

    /// <summary>
    ///     User-initiated work with a visible spinner (modals, click-driven correlation); beats <see cref="Bulk" />, but
    ///     not the interactive reserve.
    /// </summary>
    UserInitiated,

    /// <summary>
    ///     Always-on background analytics; yields to higher priorities and, while interactive work is present, runs only
    ///     within the non-reserved budget.
    /// </summary>
    Bulk,
}
