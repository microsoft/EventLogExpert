// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.LogTable.OrderedView;
using EventLogExpert.Runtime.Tests.LogTable.TestSupport;

namespace EventLogExpert.Runtime.Tests.LogTable.OrderedView;

public sealed class OrderedViewWriterTests
{
    private const int MaxGenerationPollAttempts = 300;
    private const int MaxRenderPollAttempts = 200;
    private const int PollDelayMilliseconds = 10;

    private static readonly SortContext s_context = new(ColumnName.RecordId, false, null, false);
    private static readonly Filter s_emptyFilter = new(null, []);
    private static readonly TimeSpan s_settleWindow = TimeSpan.FromMilliseconds(500);

    [Fact]
    public async Task Cadence_RendersLowRateEvents_WithoutExplicitPublish()
    {
        EventLogId logId = EventLogId.Create();
        IEventColumnReader reader = Reader(logId, count: 5, generation: 0, recordIdBase: 0);

        await using var writer = new OrderedViewWriter(publishEvery: 10_000, publishIntervalMs: 8);

        writer.EnqueueReconcile(logId, reader);

        bool rendered = false;
        for (int attempt = 0; attempt < MaxRenderPollAttempts && !rendered; attempt++)
        {
            await Task.Delay(PollDelayMilliseconds, TestContext.Current.CancellationToken);
            if (writer.Current.Count == 5) { rendered = true; }
        }

        Assert.True(rendered);
        Assert.Null(writer.Faulted);
    }

    [Fact]
    public async Task Dispose_CancelsAndAwaitsInFlightBuilds_WithoutHanging()
    {
        EventLogId logId = EventLogId.Create();
        IEventColumnReader reader = Reader(logId, count: 40_000, generation: 0, recordIdBase: 0);

        using var entered = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        int examined = 0;
        int gateArmed = 1;

        bool Predicate(EventLocator locator, IEventColumnReader columnReader)
        {
            Interlocked.Increment(ref examined);

            if (Interlocked.Exchange(ref gateArmed, 0) == 1)
            {
                entered.Set();
                release.Wait(OrderedViewTestTimeouts.Default);
            }

            return true;
        }

        var writer = new OrderedViewWriter(publishEvery: 5000);

        writer.EnqueueReconcile(logId, reader);

        writer.EnqueueViewRequest(ViewRequests.For(new SortContext(ColumnName.Source, false, null, false), s_emptyFilter, [logId], Predicate));

        Assert.True(entered.Wait(OrderedViewTestTimeouts.Default, TestContext.Current.CancellationToken),
            "the build never started, so disposal would not have had one to cancel");

        Task disposal = writer.DisposeAsync().AsTask();

        await Task.WhenAny(disposal, Task.Delay(s_settleWindow, TestContext.Current.CancellationToken));

        Assert.False(disposal.IsCompleted, "disposal returned while a build was still running inside its predicate");

        release.Set();

        await disposal.WaitAsync(OrderedViewTestTimeouts.Default, TestContext.Current.CancellationToken);

        Assert.True(Volatile.Read(ref examined) < 40_000,
            $"the build ran to completion instead of being cancelled: {examined} of 40,000 rows");
    }

    [Fact]
    public async Task DrainAsync_AfterDispose_ReturnsWithoutHanging()
    {
        EventLogId logId = EventLogId.Create();
        IEventColumnReader reader = Reader(logId, count: 20, generation: 0, recordIdBase: 0);

        var writer = new OrderedViewWriter(publishEvery: 100);
        writer.EnqueueReconcile(logId, reader);

        await writer.DisposeAsync();

        OrderedViewSnapshot snapshot =
            await writer.DrainAsync().WaitAsync(OrderedViewTestTimeouts.Default, TestContext.Current.CancellationToken);

        Assert.NotNull(snapshot);
    }

    [Fact]
    public async Task FirstContent_PublishesWithoutWaitingForTheCounterOrTheCadence()
    {
        EventLogId logId = EventLogId.Create();
        IEventColumnReader reader = Reader(logId, count: 256, generation: 0, recordIdBase: 0);

        await using var writer = new OrderedViewWriter(publishEvery: int.MaxValue, publishIntervalMs: 600_000);

        writer.EnqueueReconcile(logId, reader);

        bool rendered = false;

        for (int attempt = 0; attempt < MaxRenderPollAttempts && !rendered; attempt++)
        {
            await Task.Delay(PollDelayMilliseconds, TestContext.Current.CancellationToken);

            if (writer.Current.Count == 256) { rendered = true; }
        }

        Assert.True(rendered);
        Assert.Null(writer.Faulted);
    }

    [Fact]
    public async Task HeldRebuild_FaultingInOffThreadBuild_ClearsHoldSoReconcileResumes()
    {
        EventLogId logId = EventLogId.Create();

        await using var writer = new OrderedViewWriter(publishEvery: 1000, publishIntervalMs: 8);

        writer.EnqueueReconcile(logId, Reader(logId, count: 100, generation: 0, recordIdBase: 0));

        writer.EnqueueViewRequest(ViewRequests.For(s_context, s_emptyFilter, [logId], static (_, _) => throw new InvalidOperationException("build boom"), hold: true));
        OrderedViewSnapshot afterFault =
            await writer.DrainAsync().WaitAsync(OrderedViewTestTimeouts.Default, TestContext.Current.CancellationToken);

        Assert.IsType<InvalidOperationException>(writer.Faulted);

        writer.EnqueueReconcile(logId, Reader(logId, count: 150, generation: 0, recordIdBase: 0));
        OrderedViewSnapshot resumed =
            await writer.DrainAsync().WaitAsync(OrderedViewTestTimeouts.Default, TestContext.Current.CancellationToken);

        Assert.True(resumed.Count > afterFault.Count, $"reconcile stranded: {afterFault.Count} -> {resumed.Count}");
        Assert.True(resumed.Contains(logId, 0, 149));
    }

    [Fact]
    public async Task RebuildFault_SurfacesAndDoesNotHangDrain()
    {
        EventLogId logId = EventLogId.Create();
        IEventColumnReader reader = Reader(logId, count: 2000, generation: 0, recordIdBase: 0);

        await using var writer = new OrderedViewWriter(publishEvery: 1000);

        writer.EnqueueReconcile(logId, reader);

        writer.EnqueueViewRequest(ViewRequests.For(s_context, s_emptyFilter, [logId], static (_, _) => throw new InvalidOperationException("boom")));

        OrderedViewSnapshot snapshot = await writer.DrainAsync().WaitAsync(OrderedViewTestTimeouts.Default, TestContext.Current.CancellationToken);

        Assert.NotNull(snapshot);
        Assert.IsType<InvalidOperationException>(writer.Faulted);
    }

    [Fact]
    public async Task ResetLog_DropsStaleGeneration()
    {
        EventLogId logId = EventLogId.Create();
        IEventColumnReader generation0 = Reader(logId, count: 300, generation: 0, recordIdBase: 0);
        IEventColumnReader generation1 = Reader(logId, count: 120, generation: 1, recordIdBase: 5000);

        await using var writer = new OrderedViewWriter(publishEvery: 100);

        writer.EnqueueReconcile(logId, generation0);

        writer.EnqueueResetLog(logId, newGeneration: 1);
        writer.EnqueueReconcile(logId, generation1);

        OrderedViewSnapshot snapshot = await writer.DrainAsync().WaitAsync(OrderedViewTestTimeouts.Default, TestContext.Current.CancellationToken);

        Assert.Equal(120, snapshot.Count);
        Assert.False(snapshot.Contains(logId, 0, 0));
        Assert.True(snapshot.Contains(logId, 1, 0));
        Assert.Null(writer.Faulted);
    }

    [Fact]
    public async Task SingleLogInScope_ReflectsTheAdoptedGeneration_AcrossAReset()
    {
        EventLogId logId = EventLogId.Create();

        await using var writer = new OrderedViewWriter(publishEvery: 100);

        var gate = new object();
        var readies = new List<OrderedViewReady>();

        writer.Updated += update =>
        {
            if (update is OrderedViewReady ready && ready.SingleLogId == logId)
            {
                lock (gate) { readies.Add(ready); }
            }
        };

        async Task<OrderedViewReady> WaitForGeneration(int generation)
        {
            var key = new LogGeneration(logId, generation);

            for (int attempt = 0; attempt < MaxGenerationPollAttempts; attempt++)
            {
                lock (gate)
                {
                    var match = readies.LastOrDefault(ready => ready.InScope.Contains(key));

                    if (match is not null) { return match; }
                }

                await Task.Delay(PollDelayMilliseconds, TestContext.Current.CancellationToken);
            }

            Assert.Fail($"no single-log publish carried generation {generation}");

            throw new InvalidOperationException("unreachable");
        }

        writer.EnqueueReconcile(logId, Reader(logId, count: 50, generation: 0, recordIdBase: 0));
        writer.EnqueueViewRequest(ViewRequests.For(s_context, s_emptyFilter, [logId], static (_, _) => true));
        await writer.DrainAsync().WaitAsync(OrderedViewTestTimeouts.Default, TestContext.Current.CancellationToken);

        OrderedViewReady gen0 = await WaitForGeneration(0);
        Assert.Equal(new LogGeneration(logId, 0), Assert.Single(gen0.InScope));

        writer.EnqueueResetLog(logId, newGeneration: 1);
        writer.EnqueueReconcile(logId, Reader(logId, count: 30, generation: 1, recordIdBase: 5000));
        writer.EnqueueViewRequest(ViewRequests.For(
            new SortContext(ColumnName.Source, false, null, false), s_emptyFilter, [logId], static (_, _) => true));
        await writer.DrainAsync().WaitAsync(OrderedViewTestTimeouts.Default, TestContext.Current.CancellationToken);

        OrderedViewReady gen1 = await WaitForGeneration(1);
        Assert.Equal(new LogGeneration(logId, 1), Assert.Single(gen1.InScope));
        Assert.NotSame(gen0.InScope, gen1.InScope);
        Assert.Null(writer.Faulted);
    }

    [Fact]
    public async Task TailReplayFault_DoesNotHangDrain()
    {
        EventLogId logId = EventLogId.Create();

        await using var writer = new OrderedViewWriter(publishEvery: 5000);

        writer.EnqueueReconcile(logId, Reader(logId, count: 3000, generation: 0, recordIdBase: 0));

        writer.EnqueueViewRequest(ViewRequests.For(s_context, s_emptyFilter, [logId], static (locator, _) => locator.Index >= 3000 ? throw new InvalidOperationException("tail") : true));
        writer.EnqueueReconcile(logId, Reader(logId, count: 3200, generation: 0, recordIdBase: 0));

        OrderedViewSnapshot snapshot = await writer.DrainAsync().WaitAsync(OrderedViewTestTimeouts.Default, TestContext.Current.CancellationToken);

        Assert.NotNull(snapshot);
    }

    private static IEventColumnReader Reader(EventLogId logId, int count, int generation, long recordIdBase)
    {
        var clock = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        string[] sources = ["Provider.A", "Provider.B", "Provider.C"];
        var events = new List<ResolvedEvent>(count);

        for (int i = 0; i < count; i++)
        {
            events.Add(new ResolvedEvent("Log", LogPathType.Channel)
            {
                RecordId = recordIdBase + i,
                TimeCreated = clock.AddMilliseconds(recordIdBase + i),
                Id = 1000,
                Level = "Information",
                Source = sources[i % sources.Length],
                LogName = "Channel"
            });
        }

        return EventColumnStore.Build(events, generation, generation).CreateReader(logId);
    }
}
