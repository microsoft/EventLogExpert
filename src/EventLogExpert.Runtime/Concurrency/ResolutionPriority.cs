// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Concurrency;

/// <summary>Priority class for a <see cref="PrioritySemaphore" /> waiter.</summary>
internal enum ResolutionPriority
{
    /// <summary>A load's first screenful of newest events; preempts in-flight <see cref="Bulk" /> work.</summary>
    FirstScreenful,

    /// <summary>Background bulk resolution; yields to <see cref="FirstScreenful" /> waiters.</summary>
    Bulk
}
