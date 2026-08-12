// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.LogTable.OrderedView;
using EventLogExpert.Runtime.Tests.LogTable.TestSupport;

namespace EventLogExpert.Runtime.Tests.LogTable.OrderedView;

public sealed class OrderedViewWriterConcurrencyTests
{
    private const int ReconcileChunkSize = 1;
    private const int SliceBufferSize = 256;

    private static readonly Filter s_emptyFilter = new(null, []);

    [Fact]
    public async Task ConcurrentReader_ObservesMonotonicConsistentSnapshots_AcrossLiveReconcileAndRebuild()
    {
        EventLogId logId = EventLogId.Create();
        var events = MakeEvents(count: 8000);
        var finalContext = new SortContext(ColumnName.Level, true, null, false);

        await using var writer = new OrderedViewWriter(publishEvery: 64, publishIntervalMs: 4);
        writer.EnqueueViewRequest(ViewRequests.For(new SortContext(ColumnName.Source, false, null, false), s_emptyFilter, [logId]));

        long failures = 0;
        Exception? readerError = null;
        using var stop = new ManualResetEventSlim(false);

        var readerThread = new Thread(() =>
        {
            try
            {
                var buffer = new OrderKey[SliceBufferSize];
                long lastVersion = -1;

                while (!stop.IsSet)
                {
                    OrderedViewSnapshot snapshot = writer.Current;
                    int written = snapshot.SliceInto(0, buffer.Length, buffer);

                    if (snapshot.Version < lastVersion) { Interlocked.Increment(ref failures); }

                    if (written > 0 && snapshot.RankOf(new OrderKey(snapshot.At(0).Locator)) != 0) { Interlocked.Increment(ref failures); }
                    if (written > 1 && snapshot.RankOf(new OrderKey(buffer[written - 1].Locator)) != written - 1) { Interlocked.Increment(ref failures); }

                    lastVersion = snapshot.Version;
                }
            }
            catch (Exception ex) { readerError = ex; }
        });

        readerThread.Start();

        EventColumnStore store = EventColumnStore.Empty;
        bool rebuildRequested = false;

        for (int from = 0; from < events.Count; from += ReconcileChunkSize)
        {
            int size = Math.Min(ReconcileChunkSize, events.Count - from);
            store = store.Append(events.GetRange(from, size));
            writer.EnqueueReconcile(logId, store.CreateReader(logId));

            if (!rebuildRequested && from + size >= events.Count / 2)
            {
                writer.EnqueueViewRequest(ViewRequests.For(finalContext, s_emptyFilter, [logId]));
                rebuildRequested = true;
            }
        }

        OrderedViewSnapshot final = await writer.DrainAsync().WaitAsync(OrderedViewTestTimeouts.Default, TestContext.Current.CancellationToken);
        stop.Set();
        readerThread.Join();

        Assert.Null(readerError);
        Assert.Equal(0, Interlocked.Read(ref failures));
        Assert.Null(writer.Faulted);
        CombinedViewParityAsserts.AssertSnapshotOrderMatchesReference(final, logId, events, finalContext);
    }

    [Fact]
    public async Task Writer_ReplaysTailArrivingDuringRebuild_MatchesLiveView()
    {
        EventLogId logId = EventLogId.Create();
        var events = MakeEvents(count: 400);

        var context = new SortContext(ColumnName.Source, false, null, false);

        await using var writer = new OrderedViewWriter(publishEvery: 32, publishIntervalMs: 4);

        EventColumnStore store = EventColumnStore.Build(events.GetRange(0, 120), generation: 0, contentVersion: 0);

        writer.EnqueueReconcile(logId, store.CreateReader(logId));

        writer.EnqueueViewRequest(ViewRequests.For(context, s_emptyFilter, [logId]));

        for (int index = 120; index < events.Count; index++)
        {
            store = store.Append(events.GetRange(index, 1));
            writer.EnqueueReconcile(logId, store.CreateReader(logId));
        }

        OrderedViewSnapshot snapshot = await writer.DrainAsync();

        Assert.Null(writer.Faulted);
        CombinedViewParityAsserts.AssertSnapshotOrderMatchesReference(snapshot, logId, events, context);
    }

    [Fact]
    public async Task Writer_SupersededRebuilds_ConvergeToLatestOrdering_NoFault()
    {
        EventLogId logId = EventLogId.Create();
        var events = MakeEvents(count: 350);
        IEventColumnReader reader = EventColumnStore.Build(events, generation: 0, contentVersion: 0).CreateReader(logId);

        await using var writer = new OrderedViewWriter(publishEvery: 16, publishIntervalMs: 4);

        writer.EnqueueReconcile(logId, reader);

        writer.EnqueueViewRequest(ViewRequests.For(new SortContext(ColumnName.Level, false, null, false), s_emptyFilter, [logId]));
        writer.EnqueueViewRequest(ViewRequests.For(new SortContext(ColumnName.EventId, true, null, false), s_emptyFilter, [logId]));
        writer.EnqueueViewRequest(ViewRequests.For(new SortContext(ColumnName.DateAndTime, false, ColumnName.Source, false), s_emptyFilter, [logId]));
        var finalContext = new SortContext(ColumnName.Source, true, null, false);
        writer.EnqueueViewRequest(ViewRequests.For(finalContext, s_emptyFilter, [logId]));

        OrderedViewSnapshot snapshot = await writer.DrainAsync();

        Assert.Null(writer.Faulted);
        CombinedViewParityAsserts.AssertSnapshotOrderMatchesReference(snapshot, logId, events, finalContext);
    }

    private static List<ResolvedEvent> MakeEvents(int count)
    {
        var rng = new Random(20260212);
        var clock = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        string[] sources = ["Provider.A", "Provider.B", "Provider.C", "Provider.D"];
        string[] levels = ["Information", "Warning", "Error", "Critical"];
        var events = new List<ResolvedEvent>(count);

        for (int i = 0; i < count; i++)
        {
            clock = clock.AddMilliseconds(1 + rng.Next(10));

            events.Add(new ResolvedEvent("Log", LogPathType.Channel)
            {
                RecordId = i + 1,
                TimeCreated = clock,
                Id = 1000 + rng.Next(5),
                Level = levels[rng.Next(levels.Length)],
                Source = sources[rng.Next(sources.Length)],
                LogName = "Channel"
            });
        }

        return events;
    }
}
