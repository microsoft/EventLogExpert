// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Concurrency;

namespace EventLogExpert.Runtime.Tests.Concurrency;

public sealed class CpuWorkSchedulerTests
{
    private static int Sink;

    [Fact(Timeout = 30000)]
    public async Task Constructor_ClampsReserveBelowBudget_LeavingOneNonInteractiveSlot()
    {
        // reserve 5 on budget 2 clamps to 1; without the clamp the non-interactive cap would go negative and admit none.
        var scheduler = new CpuWorkScheduler(totalBudget: 2, reserve: 5);
        var gates = new List<Gate>();

        try
        {
            Gate interactive = Submit(scheduler, CpuWorkPriority.Interactive);
            gates.Add(interactive);
            await interactive.Started;

            gates.Add(Submit(scheduler, CpuWorkPriority.Bulk));
            gates.Add(Submit(scheduler, CpuWorkPriority.Bulk));

            Assert.Equal(2, scheduler.Running);
            Assert.Equal(1, scheduler.WaiterCounts.Bulk);
        }
        finally { await ReleaseAllAsync(gates); }
    }

    [Fact(Timeout = 30000)]
    public async Task RunAsync_AdmitsUpToBudget_AndQueuesTheRest()
    {
        var scheduler = new CpuWorkScheduler(totalBudget: 2, reserve: 0);
        var gates = new List<Gate>();

        try
        {
            for (int i = 0; i < 4; i++) { gates.Add(Submit(scheduler, CpuWorkPriority.Bulk)); }

            Assert.Equal(2, scheduler.Running);
            Assert.Equal(2, scheduler.WaiterCounts.Bulk);
            Assert.Equal(2, scheduler.MaxObserved);
        }
        finally { await ReleaseAllAsync(gates); }
    }

    [Fact(Timeout = 30000)]
    public async Task RunAsync_CancelingQueuedWaiter_DropsItWithoutRunningWork_AndConservesBudget()
    {
        var scheduler = new CpuWorkScheduler(totalBudget: 1, reserve: 0);
        var gates = new List<Gate>();
        int queuedRan = 0;

        try
        {
            Gate running = Submit(scheduler, CpuWorkPriority.Bulk);
            gates.Add(running);
            await running.Started;

            using var cts = new CancellationTokenSource();
            Task queued = scheduler.RunAsync(_ => Interlocked.Exchange(ref queuedRan, 1), CpuWorkPriority.Bulk, cts.Token);
            Assert.Equal(1, scheduler.WaiterCounts.Bulk);

            await cts.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);

            Assert.Equal(0, Volatile.Read(ref queuedRan));

            // The canceled waiter never consumed the permit: after the running item finishes, a fresh acquire admits.
            running.Release();
            Gate next = Submit(scheduler, CpuWorkPriority.Bulk);
            gates.Add(next);
            await next.Started;
            Assert.Equal(1, scheduler.Running);
        }
        finally { await ReleaseAllAsync(gates); }
    }

    [Fact(Timeout = 30000)]
    public async Task RunAsync_InteractiveWork_MayUseTheReservedSlot()
    {
        var scheduler = new CpuWorkScheduler(totalBudget: 4, reserve: 2);
        var gates = new List<Gate>();

        try
        {
            Gate firstInteractive = Submit(scheduler, CpuWorkPriority.Interactive);
            gates.Add(firstInteractive);
            await firstInteractive.Started;

            for (int i = 0; i < 2; i++) { gates.Add(Submit(scheduler, CpuWorkPriority.Bulk)); }

            Assert.Equal(3, scheduler.Running);

            Gate secondInteractive = Submit(scheduler, CpuWorkPriority.Interactive);
            gates.Add(secondInteractive);
            await secondInteractive.Started;

            // Interactive is only bounded by the total budget, so it takes the reserved slot rather than queuing.
            Assert.Equal(4, scheduler.Running);
            Assert.Equal((0, 0, 0), scheduler.WaiterCounts);
        }
        finally { await ReleaseAllAsync(gates); }
    }

    [Fact(Timeout = 30000)]
    public async Task RunAsync_OnRelease_AdmitsQueuedInteractiveBeforeQueuedBulk()
    {
        var scheduler = new CpuWorkScheduler(totalBudget: 2, reserve: 0);
        var gates = new List<Gate>();

        try
        {
            Gate runningA = Submit(scheduler, CpuWorkPriority.Bulk);
            Gate runningB = Submit(scheduler, CpuWorkPriority.Bulk);
            gates.Add(runningA);
            gates.Add(runningB);
            await Task.WhenAll(runningA.Started, runningB.Started);

            Gate queuedBulk = Submit(scheduler, CpuWorkPriority.Bulk);
            Gate queuedInteractive = Submit(scheduler, CpuWorkPriority.Interactive);
            gates.Add(queuedBulk);
            gates.Add(queuedInteractive);
            Assert.Equal((1, 0, 1), scheduler.WaiterCounts);

            runningA.Release();
            await queuedInteractive.Started;

            // The freed slot went to the interactive waiter; the bulk waiter is still queued.
            Assert.Equal(2, scheduler.Running);
            Assert.Equal(1, scheduler.WaiterCounts.Bulk);
            Assert.False(queuedBulk.Started.IsCompleted);
        }
        finally { await ReleaseAllAsync(gates); }
    }

    [Fact(Timeout = 30000)]
    public async Task RunAsync_OnRelease_AdmitsQueuedUserInitiatedBeforeBulk()
    {
        var scheduler = new CpuWorkScheduler(totalBudget: 1, reserve: 0);
        var gates = new List<Gate>();

        try
        {
            Gate running = Submit(scheduler, CpuWorkPriority.Bulk);
            gates.Add(running);
            await running.Started;

            Gate queuedBulk = Submit(scheduler, CpuWorkPriority.Bulk);
            Gate queuedUserInitiated = Submit(scheduler, CpuWorkPriority.UserInitiated);
            gates.Add(queuedBulk);
            gates.Add(queuedUserInitiated);

            running.Release();
            await queuedUserInitiated.Started;

            Assert.Equal(1, scheduler.Running);
            Assert.Equal(1, scheduler.WaiterCounts.Bulk);
            Assert.False(queuedBulk.Started.IsCompleted);
        }
        finally { await ReleaseAllAsync(gates); }
    }

    [Fact(Timeout = 30000)]
    public async Task RunAsync_UnderConcurrentMixedLoad_NeverExceedsBudget_AndConservesBudget()
    {
        const int budget = 4;
        var scheduler = new CpuWorkScheduler(budget, reserve: 2);

        async Task Worker(int seed)
        {
            var seededRandom = new Random(seed);

            for (int i = 0; i < 1500; i++)
            {
                CpuWorkPriority priority = (CpuWorkPriority)seededRandom.Next(3);
                using var cts = new CancellationTokenSource();

                if (seededRandom.Next(5) == 0) { cts.CancelAfter(TimeSpan.FromMilliseconds(seededRandom.Next(3))); }

                try { await scheduler.RunAsync(_ => Interlocked.Increment(ref Sink), priority, cts.Token); }
                catch (OperationCanceledException) { }
            }
        }

        await Task.WhenAll(Enumerable.Range(0, 8).Select(seed => Task.Run(() => Worker(seed))))
            .WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        // Budget is conserved (no permit leaked); canceled waiters may linger in a queue until a later release drains
        // them (design limitation #4, as with PrioritySemaphore), so waiter counts are not asserted zero here.
        Assert.True(scheduler.MaxObserved <= budget, $"observed {scheduler.MaxObserved} exceeded budget {budget}");
        Assert.Equal(0, scheduler.Running);
    }

    [Fact(Timeout = 30000)]
    public async Task RunAsync_UserInitiated_CannotConsumeTheInteractiveReserve()
    {
        var scheduler = new CpuWorkScheduler(totalBudget: 3, reserve: 2);
        var gates = new List<Gate>();

        try
        {
            Gate interactive = Submit(scheduler, CpuWorkPriority.Interactive);
            gates.Add(interactive);
            await interactive.Started;

            gates.Add(Submit(scheduler, CpuWorkPriority.UserInitiated));
            gates.Add(Submit(scheduler, CpuWorkPriority.UserInitiated));

            // budget - reserve = 1 non-interactive slot; the second user-initiated item queues behind the reserve.
            Assert.Equal(2, scheduler.Running);
            Assert.Equal(1, scheduler.WaiterCounts.UserInitiated);
        }
        finally { await ReleaseAllAsync(gates); }
    }

    [Fact(Timeout = 30000)]
    public async Task RunAsync_WhenLastInteractiveCompletes_AdmitsHeldBackBulk()
    {
        var scheduler = new CpuWorkScheduler(totalBudget: 3, reserve: 2);
        var gates = new List<Gate>();

        try
        {
            Gate interactive = Submit(scheduler, CpuWorkPriority.Interactive);
            gates.Add(interactive);
            await interactive.Started;

            Gate runningBulk = Submit(scheduler, CpuWorkPriority.Bulk);
            Gate heldBulkA = Submit(scheduler, CpuWorkPriority.Bulk);
            Gate heldBulkB = Submit(scheduler, CpuWorkPriority.Bulk);
            gates.Add(runningBulk);
            gates.Add(heldBulkA);
            gates.Add(heldBulkB);
            await runningBulk.Started;
            Assert.Equal(2, scheduler.WaiterCounts.Bulk);

            // Completing the only interactive item collapses the reserve; the held-back bulk must wake.
            interactive.Release();
            await Task.WhenAll(heldBulkA.Started, heldBulkB.Started);

            Assert.Equal(3, scheduler.Running);
            Assert.Equal(0, scheduler.WaiterCounts.Bulk);
        }
        finally { await ReleaseAllAsync(gates); }
    }

    [Fact(Timeout = 30000)]
    public async Task RunAsync_WhenNoInteractiveWork_BulkUsesEntireBudget()
    {
        // Work-conserving: with no interactive work present the reserve collapses, so bulk fills the whole budget.
        var scheduler = new CpuWorkScheduler(totalBudget: 4, reserve: 2);
        var gates = new List<Gate>();

        try
        {
            for (int i = 0; i < 4; i++) { gates.Add(Submit(scheduler, CpuWorkPriority.Bulk)); }

            Assert.Equal(4, scheduler.Running);
            Assert.Equal(4, scheduler.MaxObserved);
            Assert.Equal(0, scheduler.WaiterCounts.Bulk);
        }
        finally { await ReleaseAllAsync(gates); }
    }

    [Fact(Timeout = 30000)]
    public async Task RunAsync_WhenWorkThrows_ReleasesBudget()
    {
        var scheduler = new CpuWorkScheduler(totalBudget: 1, reserve: 0);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scheduler.RunAsync<int>(_ => throw new InvalidOperationException("boom"), CpuWorkPriority.Bulk, TestContext.Current.CancellationToken));

        Assert.Equal(0, scheduler.Running);

        // The faulted item released its slot, so the scheduler still admits new work.
        var gates = new List<Gate>();
        try
        {
            Gate next = Submit(scheduler, CpuWorkPriority.Bulk);
            gates.Add(next);
            await next.Started;
            Assert.Equal(1, scheduler.Running);
        }
        finally { await ReleaseAllAsync(gates); }
    }

    [Fact(Timeout = 30000)]
    public async Task RunAsync_WhileInteractivePresent_HoldsReservedHeadroomForInteractive()
    {
        var scheduler = new CpuWorkScheduler(totalBudget: 4, reserve: 2);
        var gates = new List<Gate>();

        try
        {
            Gate interactive = Submit(scheduler, CpuWorkPriority.Interactive);
            gates.Add(interactive);
            await interactive.Started;

            for (int i = 0; i < 4; i++) { gates.Add(Submit(scheduler, CpuWorkPriority.Bulk)); }

            // 1 interactive + (budget - reserve = 2) bulk running; the remaining 2 bulk queue behind the reserve.
            Assert.Equal(3, scheduler.Running);
            Assert.Equal(2, scheduler.WaiterCounts.Bulk);
        }
        finally { await ReleaseAllAsync(gates); }
    }

    [Fact]
    public async Task RunAsync_WithNullWork_ThrowsArgumentNull()
    {
        var scheduler = new CpuWorkScheduler(totalBudget: 1, reserve: 0);

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            scheduler.RunAsync<int>(null!, CpuWorkPriority.Bulk, TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 30000)]
    public async Task RunAsync_WithPreCanceledToken_DoesNotRunWork_OrConsumeBudget()
    {
        var scheduler = new CpuWorkScheduler(totalBudget: 2, reserve: 0);
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();
        int ran = 0;

        await Assert.ThrowsAsync<TaskCanceledException>(() =>
            scheduler.RunAsync(_ => Interlocked.Exchange(ref ran, 1), CpuWorkPriority.Bulk, canceled.Token));

        Assert.Equal(0, Volatile.Read(ref ran));
        Assert.Equal(0, scheduler.Running);
    }

    [Fact]
    public async Task RunAsync_WithUndefinedPriority_ThrowsArgumentOutOfRange()
    {
        var scheduler = new CpuWorkScheduler(totalBudget: 1, reserve: 0);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            scheduler.RunAsync(_ => 0, (CpuWorkPriority)999, TestContext.Current.CancellationToken));
    }

    private static async Task ReleaseAllAsync(List<Gate> gates)
    {
        foreach (Gate item in gates) { item.Release(); }

        // Await completion so blocked pool threads are freed before the next test and no permit lingers.
        await Task.WhenAll(gates.Select(item => item.Completion)).WaitAsync(TimeSpan.FromSeconds(10));
    }

    private static Gate Submit(CpuWorkScheduler scheduler, CpuWorkPriority priority)
    {
        var item = new Gate();
        item.Bind(scheduler.RunAsync(item.Work, priority));

        return item;
    }

    // A work item that signals when its delegate begins and then blocks until the test releases it, letting the test
    // inspect admission state deterministically while the item "runs".
    private sealed class Gate
    {
        private readonly ManualResetEventSlim _release = new(false);
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Task _completion = Task.CompletedTask;

        public Task Completion => _completion;

        public Task Started => _started.Task;

        public void Bind(Task completion) => _completion = completion;

        public void Release() => _release.Set();

        public int Work(CancellationToken cancellationToken)
        {
            _started.TrySetResult();
            _release.Wait(cancellationToken);

            return 0;
        }
    }
}
