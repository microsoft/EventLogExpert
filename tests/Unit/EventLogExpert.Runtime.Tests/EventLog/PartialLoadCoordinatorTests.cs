// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.LogTable;
using Fluxor;
using NSubstitute;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.Runtime.Tests.EventLog;

public sealed class PartialLoadCoordinatorTests
{
    [Fact]
    public void Discard_RemovesBufferAndAllowsReopen()
    {
        var harness = Create();
        var logId = EventLogId.Create();
        harness.Seed(logId, 1);

        harness.Coordinator.Enqueue(logId, 0); // first-seen: flushes immediately
        harness.Coordinator.Enqueue(logId, 0); // already seen: buffered
        harness.Dispatched.Clear();

        harness.Coordinator.Discard(logId);
        harness.Coordinator.Flush();
        Assert.Empty(harness.Dispatched);

        harness.Coordinator.Enqueue(logId, 0); // seen was cleared, so first-seen again
        Assert.Single(harness.Dispatched);
    }

    [Fact]
    public void DiscardAll_ClearsAllBuffers()
    {
        var harness = Create();
        var first = EventLogId.Create();
        var second = EventLogId.Create();
        harness.Seed(first, 1);
        harness.Seed(second, 1);

        harness.Coordinator.Enqueue(first, 0);
        harness.Coordinator.Enqueue(second, 0);
        harness.Coordinator.Enqueue(first, 0);
        harness.Coordinator.Enqueue(second, 0);
        harness.Dispatched.Clear();

        harness.Coordinator.DiscardAll();
        harness.Coordinator.Flush();

        Assert.Empty(harness.Dispatched);
    }

    [Fact]
    public void Dispose_ThenEnqueueAndFlush_IsSilent()
    {
        var harness = Create();
        var logId = EventLogId.Create();
        harness.Seed(logId, 1);

        harness.Coordinator.Dispose();
        harness.Coordinator.Enqueue(logId, 0);
        harness.Coordinator.Flush();

        Assert.Empty(harness.Dispatched);
    }

    [Fact]
    public void Enqueue_BufferStraddlingVersions_FlushesMinVersion()
    {
        var harness = Create();
        var logId = EventLogId.Create();
        harness.Seed(logId, 1);

        harness.Coordinator.Enqueue(logId, 5);
        harness.Dispatched.Clear();

        harness.Coordinator.Enqueue(logId, 3);
        harness.Coordinator.Enqueue(logId, 8);
        harness.Coordinator.Flush();

        var batch = Assert.IsType<AppendTableEventsBatchAction>(Assert.Single(harness.Dispatched));
        Assert.Equal(3, batch.VersionByLog[logId]);
    }

    [Fact]
    public void Enqueue_FirstDeltaForLog_FlushesImmediately()
    {
        var harness = Create();
        var logId = EventLogId.Create();
        harness.Seed(logId, 1);

        harness.Coordinator.Enqueue(logId, 0);

        var batch = Assert.IsType<AppendTableEventsBatchAction>(Assert.Single(harness.Dispatched));
        Assert.True(batch.ViewsByLog.ContainsKey(logId));
        Assert.Equal(1, batch.ViewsByLog[logId].Count);
    }

    [Fact]
    public void Enqueue_FirstSeen_EmitsVersionByLog()
    {
        var harness = Create();
        var logId = EventLogId.Create();
        harness.Seed(logId, 1);

        harness.Coordinator.Enqueue(logId, 7);

        var batch = Assert.IsType<AppendTableEventsBatchAction>(Assert.Single(harness.Dispatched));
        Assert.Equal(7, batch.VersionByLog[logId]);
    }

    [Fact]
    public void Enqueue_ForLogAbsentFromStore_DispatchesNothing()
    {
        // In the columnar model a delta is only a dirty signal; the events live in the raw store. A log that the
        // store does not hold (closed, or never ingested) produces no view and therefore dispatches nothing.
        var harness = Create();

        harness.Coordinator.Enqueue(EventLogId.Create(), 0);

        Assert.Empty(harness.Dispatched);
    }

    [Fact]
    public void Flush_BuffersAcrossLogs_DispatchesOneBatchForAllLogs()
    {
        var harness = Create();
        var first = EventLogId.Create();
        var second = EventLogId.Create();
        harness.Seed(first, 1);
        harness.Seed(second, 1);

        // The first delta per log flushes immediately; subsequent deltas buffer for the next flush.
        harness.Coordinator.Enqueue(first, 0);
        harness.Coordinator.Enqueue(second, 0);
        harness.Dispatched.Clear();

        harness.Coordinator.Enqueue(first, 0);
        harness.Coordinator.Enqueue(second, 0);
        harness.Coordinator.Flush();

        var batch = Assert.IsType<AppendTableEventsBatchAction>(Assert.Single(harness.Dispatched));
        Assert.Equal(2, batch.ViewsByLog.Count);
    }

    [Fact]
    public void MarkFinalized_DropsAlreadyBufferedDelta()
    {
        var harness = Create();
        var logId = EventLogId.Create();
        harness.Seed(logId, 1);

        harness.Coordinator.Enqueue(logId, 0); // first-seen: flushes immediately and clears the buffer
        harness.Coordinator.Enqueue(logId, 0); // already seen: buffered, not yet flushed
        harness.Dispatched.Clear();

        harness.Coordinator.MarkFinalized(logId);
        harness.Coordinator.Flush();

        Assert.Empty(harness.Dispatched);
    }

    [Fact]
    public void MarkFinalized_ThenLateEnqueue_DropsTheLateEvents()
    {
        var harness = Create();
        var logId = EventLogId.Create();
        harness.Seed(logId, 1);

        harness.Coordinator.Enqueue(logId, 0);
        harness.Dispatched.Clear();

        harness.Coordinator.MarkFinalized(logId);
        harness.Coordinator.Enqueue(logId, 0);
        harness.Coordinator.Flush();

        Assert.Empty(harness.Dispatched);
    }

    private static Harness Create()
    {
        var dispatched = new List<object>();
        var dispatcher = Substitute.For<IDispatcher>();
        dispatcher.When(d => d.Dispatch(Arg.Any<object>())).Do(call => dispatched.Add(call.Arg<object>()));

        var storeHolder = new StoreHolder();
        var rawState = Substitute.For<IState<RawEventStoreState>>();
        rawState.Value.Returns(_ => storeHolder.State);

        var eventLogState = Substitute.For<IState<EventLogState>>();
        eventLogState.Value.Returns(new EventLogState());

        var logTableState = Substitute.For<IState<LogTableState>>();
        logTableState.Value.Returns(new LogTableState());

        var coordinator = new PartialLoadCoordinator(
            dispatcher, rawState, eventLogState, logTableState, Timeout.InfiniteTimeSpan);

        return new Harness(coordinator, dispatched, storeHolder);
    }

    private static ResolvedEvent NewEvent(long recordId) =>
        new("Application", LogPathType.Channel) { RecordId = recordId };

    private sealed class Harness(PartialLoadCoordinator coordinator, List<object> dispatched, StoreHolder store)
    {
        public PartialLoadCoordinator Coordinator => coordinator;

        public List<object> Dispatched => dispatched;

        // Ingest is what populates the raw store in production; the coordinator only rebuilds the view from it. Seed the
        // store so a flush for this log produces a non-empty view.
        public void Seed(EventLogId logId, int count)
        {
            var events = new List<ResolvedEvent>(count);

            for (int i = 0; i < count; i++) { events.Add(NewEvent(i + 1)); }

            store.State = store.State with
            {
                ByLog = store.State.ByLog.SetItem(logId, EventColumnStore.Build(events, 0, 0))
            };
        }
    }

    private sealed class StoreHolder
    {
        public RawEventStoreState State { get; set; } = new();
    }
}
