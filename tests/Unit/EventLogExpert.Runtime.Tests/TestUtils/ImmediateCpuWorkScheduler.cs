// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Concurrency;

namespace EventLogExpert.Runtime.Tests.TestUtils;

/// <summary>
///     Pass-through <see cref="ICpuWorkScheduler" /> test double: offloads each item to the thread pool with no
///     admission limit (pre-scheduler <c>Task.Run</c> behavior).
/// </summary>
internal sealed class ImmediateCpuWorkScheduler : ICpuWorkScheduler
{
    public Task<T> RunAsync<T>(Func<CancellationToken, T> work, CpuWorkPriority priority, CancellationToken cancellationToken = default) =>
        Task.Run(() => work(cancellationToken), cancellationToken);
}
