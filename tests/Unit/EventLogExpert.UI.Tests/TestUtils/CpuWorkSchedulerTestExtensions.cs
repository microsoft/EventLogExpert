// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Concurrency;
using Microsoft.Extensions.DependencyInjection;

namespace EventLogExpert.UI.Tests.TestUtils;

internal static class CpuWorkSchedulerTestExtensions
{
    private sealed class ImmediateCpuWorkScheduler : ICpuWorkScheduler
    {
        public Task<T> RunAsync<T>(Func<CancellationToken, T> work, CpuWorkPriority priority, CancellationToken cancellationToken = default) =>
            Task.Run(() => work(cancellationToken), cancellationToken);
    }

    private sealed class InlineCpuWorkScheduler : ICpuWorkScheduler
    {
        public Task<T> RunAsync<T>(Func<CancellationToken, T> work, CpuWorkPriority priority, CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled<T>(cancellationToken);
            }

            try
            {
                return Task.FromResult(work(cancellationToken));
            }
            catch (OperationCanceledException canceled) when (canceled.CancellationToken == cancellationToken)
            {
                // Mirror Task.Run: a cancellation bound to this token surfaces as a canceled task, not a faulted one.
                return Task.FromCanceled<T>(cancellationToken);
            }
            catch (Exception exception)
            {
                // Mirror Task.Run's exception capture so awaiters observe a faulted task rather than a synchronous throw.
                return Task.FromException<T>(exception);
            }
        }
    }

    extension(IServiceCollection services)
    {
        /// <summary>
        ///     Registers a pass-through <see cref="ICpuWorkScheduler" /> (no admission limit) so components under test behave
        ///     as with the pre-scheduler <c>Task.Run</c>.
        /// </summary>
        public void AddImmediateCpuWorkScheduler() =>
            services.AddSingleton<ICpuWorkScheduler>(new ImmediateCpuWorkScheduler());

        /// <summary>
        ///     Registers an <see cref="ICpuWorkScheduler" /> that runs work synchronously on the calling thread, returning an
        ///     already-completed task (canceled if the token is signaled, faulted if the work throws) to mirror <c>Task.Run</c>'s
        ///     result, cancellation, and exception semantics. Unlike <see cref="AddImmediateCpuWorkScheduler" /> (which offloads
        ///     via <c>Task.Run</c>), this keeps component render tests deterministic by removing the scheduling race entirely.
        ///     Trade-off: because the task is already complete, an awaiting continuation resumes synchronously, so any
        ///     intermediate loading render pass is collapsed and a stray <c>ConfigureAwait(false)</c> on that continuation is no
        ///     longer observable. Use only where neither is asserted.
        /// </summary>
        public void AddInlineCpuWorkScheduler() =>
            services.AddSingleton<ICpuWorkScheduler>(new InlineCpuWorkScheduler());
    }
}
