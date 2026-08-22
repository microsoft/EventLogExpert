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

    extension(IServiceCollection services)
    {
        /// <summary>
        ///     Registers a pass-through <see cref="ICpuWorkScheduler" /> (no admission limit) so components under test behave
        ///     as with the pre-scheduler <c>Task.Run</c>.
        /// </summary>
        public void AddImmediateCpuWorkScheduler() =>
            services.AddSingleton<ICpuWorkScheduler>(new ImmediateCpuWorkScheduler());
    }
}
