// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Concurrency;

/// <summary>
///     Process-wide admission gate for CPU-bound work: bounds how many items run at once and admits them by
///     <see cref="CpuWorkPriority" /> so background analytics neither starve interactive work nor oversubscribe the cores.
///     Reuses the thread pool; only admission is governed.
/// </summary>
public interface ICpuWorkScheduler
{
    /// <summary>
    ///     Runs <paramref name="work" /> on the thread pool once admitted at <paramref name="priority" />, passing it
    ///     <paramref name="cancellationToken" />; the token also drops the request if canceled before admission (throwing like
    ///     a canceled <see cref="Task.Run{TResult}(Func{TResult}, CancellationToken)" />). The delegate must be leaf CPU work
    ///     and must not call back into the scheduler.
    /// </summary>
    Task<T> RunAsync<T>(Func<CancellationToken, T> work, CpuWorkPriority priority, CancellationToken cancellationToken = default);
}
