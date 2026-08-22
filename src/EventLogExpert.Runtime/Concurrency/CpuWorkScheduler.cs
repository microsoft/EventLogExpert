// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Concurrency;

/// <summary>
///     Bounded, priority-aware admission gate for CPU-bound work. At most <c>totalBudget</c> items run at once,
///     admitted Interactive → UserInitiated → Bulk. While interactive work is present or queued, non-interactive work is
///     admitted only below <c>totalBudget - reserve</c>, reserving headroom for an arriving interactive item; with no
///     interactive work the reserve collapses so background work uses the whole budget (a single un-interacted log takes
///     no throughput hit). The guarantee is admission, not preemption: already-running work is never suspended, so the
///     non-interactive count can briefly exceed <c>totalBudget - reserve</c> right after an interactive item arrives.
///     Reuses the thread pool; mirrors the reserved-headroom / cancellation model of <see cref="PrioritySemaphore" />.
/// </summary>
internal sealed class CpuWorkScheduler : ICpuWorkScheduler
{
    private readonly Queue<TaskCompletionSource> _bulkWaiters = new();
    private readonly Lock _gate = new();
    private readonly Queue<TaskCompletionSource> _interactiveWaiters = new();
    private readonly int _reserve;
    private readonly int _totalBudget;
    private readonly Queue<TaskCompletionSource> _userInitiatedWaiters = new();

    private int _maxObserved;
    private int _nonInteractiveRunning;
    private int _running;

    internal CpuWorkScheduler(int totalBudget, int reserve)
    {
        _totalBudget = Math.Max(1, totalBudget);
        _reserve = Math.Clamp(reserve, 0, _totalBudget - 1);
    }

    /// <summary>High-water mark of concurrently-running items; test-only.</summary>
    internal int MaxObserved { get { lock (_gate) { return _maxObserved; } } }

    /// <summary>Currently-running item count; test-only.</summary>
    internal int Running { get { lock (_gate) { return _running; } } }

    /// <summary>Queued (interactive, userInitiated, bulk) waiter counts; test-only.</summary>
    internal (int Interactive, int UserInitiated, int Bulk) WaiterCounts
    {
        get { lock (_gate) { return (_interactiveWaiters.Count, _userInitiatedWaiters.Count, _bulkWaiters.Count); } }
    }

    public async Task<T> RunAsync<T>(Func<CancellationToken, T> work, CpuWorkPriority priority, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);

        if (!Enum.IsDefined(priority)) { throw new ArgumentOutOfRangeException(nameof(priority), priority, "Unknown CPU work priority."); }

        // Validation throws before any permit is taken, so nothing leaks.
        await AcquireAsync(priority, cancellationToken).ConfigureAwait(false);

        try
        {
            // Admission (not the pool) bounds concurrency; Task.Run just offloads. The token drives both in-flight and
            // pre-start cancellation.
            return await Task.Run(() => work(cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Release(priority);
        }
    }

    private Task AcquireAsync(CpuWorkPriority priority, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested) { return Task.FromCanceled(cancellationToken); }

        lock (_gate)
        {
            if (CanAdmit(priority))
            {
                Occupy(priority);

                return Task.CompletedTask;
            }

            var waiter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            if (cancellationToken.CanBeCanceled)
            {
                // Register before enqueueing so a throwing Register can't strand a waiter that later steals a permit;
                // pass the token through so a queued cancel carries the caller's token (like Task.Run(work, token)).
                CancellationTokenRegistration registration = cancellationToken.Register(
                    static (state, token) => ((TaskCompletionSource)state!).TrySetCanceled(token),
                    waiter);

                // Dispose the registration once the wait settles (granted or canceled) so it isn't retained.
                waiter.Task.ContinueWith(
                    static (_, state) => ((CancellationTokenRegistration)state!).Dispose(),
                    registration,
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }

            WaitersFor(priority).Enqueue(waiter);

            return waiter.Task;
        }
    }

    private bool CanAdmit(CpuWorkPriority priority)
    {
        if (_running >= _totalBudget) { return false; }

        return priority == CpuWorkPriority.Interactive || _nonInteractiveRunning < _totalBudget - EffectiveReserve();
    }

    private int EffectiveReserve() =>
        _running - _nonInteractiveRunning > 0 || _interactiveWaiters.Count > 0 ? _reserve : 0;

    private void Occupy(CpuWorkPriority priority)
    {
        _running++;

        if (_running > _maxObserved) { _maxObserved = _running; }

        if (priority != CpuWorkPriority.Interactive) { _nonInteractiveRunning++; }
    }

    private void Release(CpuWorkPriority priority)
    {
        lock (_gate)
        {
            Unoccupy(priority);

            // Wake in strict priority order; RunContinuationsAsynchronously keeps a woken continuation off this locked stack.
            WakeWaiters(_interactiveWaiters, CpuWorkPriority.Interactive);
            WakeWaiters(_userInitiatedWaiters, CpuWorkPriority.UserInitiated);
            WakeWaiters(_bulkWaiters, CpuWorkPriority.Bulk);
        }
    }

    private void Unoccupy(CpuWorkPriority priority)
    {
        _running--;

        if (priority != CpuWorkPriority.Interactive) { _nonInteractiveRunning--; }
    }

    private Queue<TaskCompletionSource> WaitersFor(CpuWorkPriority priority) => priority switch
    {
        CpuWorkPriority.Interactive => _interactiveWaiters,
        CpuWorkPriority.UserInitiated => _userInitiatedWaiters,
        _ => _bulkWaiters,
    };

    private void WakeWaiters(Queue<TaskCompletionSource> waiters, CpuWorkPriority priority)
    {
        while (waiters.Count > 0 && CanAdmit(priority))
        {
            Occupy(priority);

            // A canceled waiter refuses the grant; roll the permit back and keep draining so it isn't lost.
            if (!waiters.Dequeue().TrySetResult()) { Unoccupy(priority); }
        }
    }
}
