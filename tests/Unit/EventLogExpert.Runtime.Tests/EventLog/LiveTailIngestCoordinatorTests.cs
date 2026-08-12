// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.LogTable;
using NSubstitute;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.Runtime.Tests.EventLog;

public sealed class LiveTailIngestCoordinatorTests
{
    private static readonly TimeSpan s_dormant = Timeout.InfiniteTimeSpan;

    private static readonly TimeSpan s_manual = TimeSpan.FromMinutes(10);

    [Fact]
    public async Task ConcurrentEnqueueAndFlush_DeliversEveryEventExactlyOnceInOrder()
    {
        var recorder = new DispatchRecorder();
        using var coordinator = new LiveTailIngestCoordinator(recorder.Dispatcher, s_manual);
        EventLogId logId = EventLogId.Create();
        const int total = 4000;

        Task producer = Task.Run(
            () =>
            {
                for (int recordId = 1; recordId <= total; recordId++) { coordinator.Enqueue(logId, MakeEvent(recordId)); }
            },
            TestContext.Current.CancellationToken);

        Task flusher = Task.Run(
            () =>
            {
                for (int attempt = 0; attempt < 200; attempt++) { coordinator.Flush(); }
            },
            TestContext.Current.CancellationToken);

        await Task.WhenAll(producer, flusher);

        coordinator.Flush();

        long[] delivered = recorder.RecordIdsFor(logId);

        Assert.Equal(total, delivered.Length);
        Assert.Equal(total, delivered.Distinct().Count());
        Assert.Equal(Enumerable.Range(1, total).Select(value => (long)value), delivered);
    }

    [Fact]
    public void DiscardAll_LeavesNothingToDispatch()
    {
        var recorder = new DispatchRecorder();
        using var coordinator = new LiveTailIngestCoordinator(recorder.Dispatcher, s_manual);
        EventLogId logId = EventLogId.Create();

        coordinator.Enqueue(logId, MakeEvent(1));
        recorder.Clear();

        coordinator.Enqueue(logId, MakeEvent(2));
        coordinator.DiscardAll();
        coordinator.Flush();

        Assert.Empty(recorder.Ingests);
    }

    [Fact]
    public void Discard_DropsOnlyThatLogsPendingEvents()
    {
        var recorder = new DispatchRecorder();
        using var coordinator = new LiveTailIngestCoordinator(recorder.Dispatcher, s_manual);
        EventLogId dropped = EventLogId.Create();
        EventLogId kept = EventLogId.Create();

        coordinator.Enqueue(dropped, MakeEvent(1));
        recorder.Clear();

        coordinator.Enqueue(dropped, MakeEvent(2));
        coordinator.Enqueue(kept, MakeEvent(3));
        coordinator.Discard(dropped);
        coordinator.Flush();

        Assert.Empty(recorder.RecordIdsFor(dropped));
        Assert.Equal([3], recorder.RecordIdsFor(kept));
    }

    [Fact]
    public void Dispose_DropsPendingWorkAndIgnoresLaterUse()
    {
        var recorder = new DispatchRecorder();
        var coordinator = new LiveTailIngestCoordinator(recorder.Dispatcher, s_manual);
        EventLogId logId = EventLogId.Create();

        coordinator.Enqueue(logId, MakeEvent(1));
        recorder.Clear();

        coordinator.Enqueue(logId, MakeEvent(2));
        coordinator.Dispose();

        coordinator.Flush();
        coordinator.Enqueue(logId, MakeEvent(3));
        coordinator.Flush();

        Assert.Empty(recorder.Ingests);
    }

    [Fact]
    public void Enqueue_AfterAFlush_TakesTheLeadingEdgeAgainOnceTheWindowHasElapsed()
    {
        var recorder = new DispatchRecorder();

        using var coordinator = new LiveTailIngestCoordinator(recorder.Dispatcher, TimeSpan.Zero);
        EventLogId logId = EventLogId.Create();

        coordinator.Enqueue(logId, MakeEvent(1));
        coordinator.Enqueue(logId, MakeEvent(2));

        Assert.Equal(2, recorder.Ingests.Count);
    }

    [Fact]
    public void Enqueue_AtThePendingCap_FlushesWithoutWaitingForTheWindow()
    {
        var recorder = new DispatchRecorder();
        using var coordinator = new LiveTailIngestCoordinator(recorder.Dispatcher, s_manual);
        EventLogId logId = EventLogId.Create();

        coordinator.Enqueue(logId, MakeEvent(1));
        recorder.Clear();

        for (int recordId = 0; recordId < 1000; recordId++) { coordinator.Enqueue(logId, MakeEvent(recordId + 2)); }

        Assert.Single(recorder.Ingests);
        Assert.Equal(1000, recorder.RecordIdsFor(logId).Length);
    }

    [Fact]
    public void Enqueue_OnAFreshCoordinatorWithALongWindow_StillFlushesTheLeadingEdge()
    {
        var recorder = new DispatchRecorder();
        using var coordinator = new LiveTailIngestCoordinator(recorder.Dispatcher, TimeSpan.FromDays(40));
        EventLogId logId = EventLogId.Create();

        coordinator.Enqueue(logId, MakeEvent(1));

        Assert.Equal([1], recorder.RecordIdsFor(logId));
    }

    [Fact]
    public void Enqueue_WhenIdle_FlushesImmediatelySoASparseTailPaysNoLatency()
    {
        var recorder = new DispatchRecorder();
        using var coordinator = new LiveTailIngestCoordinator(recorder.Dispatcher, s_manual);
        EventLogId logId = EventLogId.Create();

        coordinator.Enqueue(logId, MakeEvent(1));

        Assert.Equal([1], recorder.RecordIdsFor(logId));
    }

    [Fact]
    public void Enqueue_WhileBusy_CoalescesIntoOneIngestPerLogInArrivalOrder()
    {
        var recorder = new DispatchRecorder();
        using var coordinator = new LiveTailIngestCoordinator(recorder.Dispatcher, s_manual);
        EventLogId logId = EventLogId.Create();

        coordinator.Enqueue(logId, MakeEvent(1));
        recorder.Clear();

        for (int recordId = 2; recordId <= 6; recordId++) { coordinator.Enqueue(logId, MakeEvent(recordId)); }

        Assert.Empty(recorder.Ingests);

        coordinator.Flush();

        Assert.Single(recorder.Ingests);
        Assert.Equal([2, 3, 4, 5, 6], recorder.RecordIdsFor(logId));
    }

    [Fact]
    public void Enqueue_WithANonPositiveWindow_FlushesEveryEventInline()
    {
        var recorder = new DispatchRecorder();
        using var coordinator = new LiveTailIngestCoordinator(recorder.Dispatcher, s_dormant);
        EventLogId logId = EventLogId.Create();

        coordinator.Enqueue(logId, MakeEvent(1));
        coordinator.Enqueue(logId, MakeEvent(2));
        coordinator.Enqueue(logId, MakeEvent(3));

        Assert.Equal(3, recorder.Ingests.Count);
        Assert.Equal([1, 2, 3], recorder.RecordIdsFor(logId));
    }

    [Fact]
    public void Flush_CarriesEveryTouchedLogInOneAction()
    {
        var recorder = new DispatchRecorder();
        using var coordinator = new LiveTailIngestCoordinator(recorder.Dispatcher, s_manual);
        EventLogId first = EventLogId.Create();
        EventLogId second = EventLogId.Create();

        coordinator.Enqueue(first, MakeEvent(1));
        recorder.Clear();

        coordinator.Enqueue(first, MakeEvent(2));
        coordinator.Enqueue(second, MakeEvent(3));
        coordinator.Flush();

        Assert.Single(recorder.Ingests);
        Assert.Equal(2, recorder.Ingests[0].EventsByLog.Count);
        Assert.Equal([2], recorder.RecordIdsFor(first));
        Assert.Equal([3], recorder.RecordIdsFor(second));
    }

    [Fact]
    public void Flush_WhenNothingIsPending_DispatchesNothing()
    {
        var recorder = new DispatchRecorder();
        using var coordinator = new LiveTailIngestCoordinator(recorder.Dispatcher, s_manual);

        coordinator.Flush();
        coordinator.Flush();

        Assert.Empty(recorder.Ingests);
    }

    private static ResolvedEvent MakeEvent(long recordId) =>
        new("Log", LogPathType.Channel)
        {
            RecordId = recordId,
            TimeCreated = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(recordId),
            Id = 1000,
            Level = "Information",
            Source = "Provider.A",
            LogName = "Channel"
        };

    private sealed class DispatchRecorder
    {
        private readonly Lock _gate = new();

        public DispatchRecorder()
        {
            Dispatcher = Substitute.For<IDispatcher>();

            Dispatcher
                .When(dispatcher => dispatcher.Dispatch(Arg.Any<object>()))
                .Do(call =>
                {
                    lock (_gate)
                    {
                        switch (call[0])
                        {
                            case IngestRawEventsAction ingest: Ingests.Add(ingest); break;
                        }
                    }
                });
        }

        public IDispatcher Dispatcher { get; }

        public List<IngestRawEventsAction> Ingests { get; } = [];

        public void Clear()
        {
            lock (_gate)
            {
                Ingests.Clear();
            }
        }

        public long[] RecordIdsFor(EventLogId logId)
        {
            lock (_gate)
            {
                return [.. Ingests
                    .Where(ingest => ingest.EventsByLog.ContainsKey(logId))
                    .SelectMany(ingest => ingest.EventsByLog[logId])
                    .Select(resolved => resolved.RecordId ?? 0)];
            }
        }
    }
}
