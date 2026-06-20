// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Concurrency;

namespace EventLogExpert.Runtime.Tests.Concurrency;

public sealed class PrioritySemaphoreTests
{
    [Fact]
    public void PermitCount_IsConserved_AcrossCancellationAndRelease()
    {
        var semaphore = new PrioritySemaphore(1);
        semaphore.WaitAsync(ResolutionPriority.Bulk, CancellationToken.None);

        using var cts = new CancellationTokenSource();
        semaphore.WaitAsync(ResolutionPriority.FirstScreenful, cts.Token);
        var live = semaphore.WaitAsync(ResolutionPriority.FirstScreenful, CancellationToken.None);
        cts.Cancel();

        semaphore.Release(); // serves the live waiter (skipping the canceled one)
        Assert.True(live.IsCompletedSuccessfully);

        semaphore.Release(); // the live waiter's permit returns
        Assert.Equal(1, semaphore.CurrentCount);

        // Exactly `permits` (1) uncontended acquire succeeds; the next blocks.
        Assert.True(semaphore.WaitAsync(ResolutionPriority.Bulk, CancellationToken.None).IsCompletedSuccessfully);
        Assert.False(semaphore.WaitAsync(ResolutionPriority.Bulk, CancellationToken.None).IsCompleted);
    }

    [Fact]
    public void Release_DrainsAccumulatedCanceledWaiters_AndRestoresPermit()
    {
        const int permits = 1;
        var semaphore = new PrioritySemaphore(permits);
        semaphore.WaitAsync(ResolutionPriority.Bulk, CancellationToken.None); // consume the permit

        using var cts = new CancellationTokenSource();
        var waiters = new List<Task>();
        for (int i = 0; i < 5; i++)
        {
            waiters.Add(semaphore.WaitAsync(ResolutionPriority.FirstScreenful, cts.Token));
        }

        cts.Cancel();
        Assert.All(waiters, waiter => Assert.True(waiter.IsCanceled));

        semaphore.Release(); // scans past every canceled waiter, finds no live one, restores the permit

        Assert.Equal(permits, semaphore.CurrentCount);
        Assert.Equal((0, 0), semaphore.WaiterCounts); // the release scan dequeued the canceled waiters
    }

    [Fact]
    public void Release_IsFifoWithinAPriorityClass()
    {
        var semaphore = new PrioritySemaphore(1);
        semaphore.WaitAsync(ResolutionPriority.FirstScreenful, CancellationToken.None); // consume the permit

        var first = semaphore.WaitAsync(ResolutionPriority.FirstScreenful, CancellationToken.None);
        var second = semaphore.WaitAsync(ResolutionPriority.FirstScreenful, CancellationToken.None);

        semaphore.Release();
        Assert.True(first.IsCompletedSuccessfully);
        Assert.False(second.IsCompleted);

        semaphore.Release();
        Assert.True(second.IsCompletedSuccessfully);
    }

    [Fact]
    public void Release_PrefersFirstScreenfulOverBulk()
    {
        var semaphore = new PrioritySemaphore(1);
        semaphore.WaitAsync(ResolutionPriority.Bulk, CancellationToken.None); // consume the only permit

        var bulk = semaphore.WaitAsync(ResolutionPriority.Bulk, CancellationToken.None);
        var firstScreenful = semaphore.WaitAsync(ResolutionPriority.FirstScreenful, CancellationToken.None);
        Assert.False(bulk.IsCompleted);
        Assert.False(firstScreenful.IsCompleted);
        Assert.Equal((1, 1), semaphore.WaiterCounts);

        semaphore.Release();

        Assert.True(firstScreenful.IsCompletedSuccessfully);
        Assert.False(bulk.IsCompleted);
        Assert.Equal(0, semaphore.CurrentCount);
    }

    [Fact]
    public void Release_SkipsCanceledWaiter_AndServesLiveWaiterWithoutLosingThePermit()
    {
        var semaphore = new PrioritySemaphore(1);
        semaphore.WaitAsync(ResolutionPriority.Bulk, CancellationToken.None); // consume the permit

        using var cts = new CancellationTokenSource();
        var canceled = semaphore.WaitAsync(ResolutionPriority.FirstScreenful, cts.Token);
        var live = semaphore.WaitAsync(ResolutionPriority.FirstScreenful, CancellationToken.None);
        cts.Cancel();
        Assert.True(canceled.IsCanceled);

        semaphore.Release();

        Assert.True(live.IsCompletedSuccessfully);
        Assert.Equal(0, semaphore.CurrentCount); // permit transferred to the live waiter, not lost
    }

    [Fact]
    public void Release_SkipsManyCanceledHighs_AndServesLiveLow()
    {
        var semaphore = new PrioritySemaphore(1);
        semaphore.WaitAsync(ResolutionPriority.Bulk, CancellationToken.None); // consume the permit

        using var cts = new CancellationTokenSource();
        var canceledHighs = new List<Task>();
        for (int i = 0; i < 3; i++)
        {
            canceledHighs.Add(semaphore.WaitAsync(ResolutionPriority.FirstScreenful, cts.Token));
        }

        var liveLow = semaphore.WaitAsync(ResolutionPriority.Bulk, CancellationToken.None);
        cts.Cancel();
        Assert.All(canceledHighs, high => Assert.True(high.IsCanceled));

        semaphore.Release(); // skip the canceled highs, then serve the live low

        Assert.True(liveLow.IsCompletedSuccessfully);
        Assert.Equal(0, semaphore.CurrentCount);
    }

    [Fact]
    public void Release_WithNoWaiters_RestoresPermit()
    {
        var semaphore = new PrioritySemaphore(1);
        semaphore.WaitAsync(ResolutionPriority.Bulk, CancellationToken.None);
        Assert.Equal(0, semaphore.CurrentCount);

        semaphore.Release();
        Assert.Equal(1, semaphore.CurrentCount);

        Assert.True(semaphore.WaitAsync(ResolutionPriority.Bulk, CancellationToken.None).IsCompletedSuccessfully);
    }

    [Fact]
    public async Task Stress_ConcurrentAcquireReleaseWithCancellation_NeverOverGrantsAndConservesPermits()
    {
        const int permits = 4;
        var semaphore = new PrioritySemaphore(permits);
        int concurrentlyHeld = 0;
        int maxObserved = 0;

        async Task Worker(int seed)
        {
            var seededRandom = new Random(seed);

            for (int i = 0; i < 2000; i++)
            {
                var priority = seededRandom.Next(2) == 0 ? ResolutionPriority.FirstScreenful : ResolutionPriority.Bulk;
                using var cts = new CancellationTokenSource();

                if (seededRandom.Next(5) == 0) { cts.CancelAfter(TimeSpan.FromMilliseconds(seededRandom.Next(3))); }

                try { await semaphore.WaitAsync(priority, cts.Token); }
                catch (OperationCanceledException) { continue; }

                try
                {
                    int held = Interlocked.Increment(ref concurrentlyHeld);
                    int observed;
                    while (held > (observed = Volatile.Read(ref maxObserved)) &&
                        Interlocked.CompareExchange(ref maxObserved, held, observed) != observed) { }

                    await Task.Yield();
                    Interlocked.Decrement(ref concurrentlyHeld);
                }
                finally { semaphore.Release(); }
            }
        }

        await Task.WhenAll(Enumerable.Range(0, 8).Select(seed => Task.Run(() => Worker(seed))));

        Assert.True(maxObserved <= permits, $"concurrent grants {maxObserved} exceeded permits {permits}");
        Assert.Equal(permits, semaphore.CurrentCount); // no permit leaked at quiescence
    }

    [Fact]
    public async Task WaitAsync_WhenPermitsAvailable_CompletesSynchronouslyAndTracksCount()
    {
        var semaphore = new PrioritySemaphore(2);
        Assert.Equal(2, semaphore.CurrentCount);

        var first = semaphore.WaitAsync(ResolutionPriority.Bulk, CancellationToken.None);
        Assert.True(first.IsCompletedSuccessfully);
        Assert.Equal(1, semaphore.CurrentCount);

        await semaphore.WaitAsync(ResolutionPriority.FirstScreenful, CancellationToken.None);
        Assert.Equal(0, semaphore.CurrentCount);

        semaphore.Release();
        semaphore.Release();
        Assert.Equal(2, semaphore.CurrentCount);
    }
}
