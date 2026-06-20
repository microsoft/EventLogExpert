// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Concurrency;

/// <summary>
///     An async semaphore with two priority classes. On release a <see cref="ResolutionPriority.FirstScreenful" />
///     waiter is woken before any <see cref="ResolutionPriority.Bulk" /> waiter, so a newly-opened log's first screenful
///     preempts in-flight bulk resolution instead of queuing behind it. A single static instance shares permits across all
///     loads; the gate is work-conserving (a lone load uses every permit for both phases).
/// </summary>
internal sealed class PrioritySemaphore
{
    private readonly Queue<TaskCompletionSource> _high = new();
    private readonly Lock _lock = new();
    private readonly Queue<TaskCompletionSource> _low = new();
    private int _available;

    internal PrioritySemaphore(int permits) => _available = permits;

    /// <summary>Available permits. Test-only observability for deterministic permit-conservation assertions.</summary>
    internal int CurrentCount { get { lock (_lock) { return _available; } } }

    /// <summary>Queued (high, low) waiter counts. Test-only; canceled waiters linger until a release skips them.</summary>
    internal (int High, int Low) WaiterCounts { get { lock (_lock) { return (_high.Count, _low.Count); } } }

    internal void Release()
    {
        lock (_lock)
        {
            // Wake the next live waiter, high priority first; canceled waiters are skipped (TrySetResult false)
            // and dropped from the queue, so a released permit is never lost to a waiter that will never run.
            while (_high.Count > 0)
            {
                if (_high.Dequeue().TrySetResult()) { return; }
            }

            while (_low.Count > 0)
            {
                if (_low.Dequeue().TrySetResult()) { return; }
            }

            _available++;
        }
    }

    internal Task WaitAsync(ResolutionPriority priority, CancellationToken token)
    {
        if (token.IsCancellationRequested) { return Task.FromCanceled(token); }

        lock (_lock)
        {
            if (_available > 0)
            {
                _available--;

                return Task.CompletedTask;
            }

            var waiter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            (priority == ResolutionPriority.FirstScreenful ? _high : _low).Enqueue(waiter);

            if (!token.CanBeCanceled) { return waiter.Task; }

            // On cancellation, complete the waiter as canceled. Release skips already-canceled waiters
            // (TrySetResult returns false), so the permit is never lost to a waiter that will never run.
            var registration = token.Register(static (state, cancellationToken) =>
                    ((TaskCompletionSource)state!).TrySetCanceled(cancellationToken),
                waiter);

            // Dispose the registration once the wait settles (granted or canceled) so it isn't retained.
            waiter.Task.ContinueWith(
                static (_, state) => ((CancellationTokenRegistration)state!).Dispose(),
                registration,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

            return waiter.Task;
        }
    }
}
