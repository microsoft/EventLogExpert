// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.LogTable.OrderedView;
using System.Diagnostics;

namespace EventLogExpert.Runtime.Tests.LogTable.OrderedView;

public sealed class OrderedViewBuildPerfTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    [Fact]
    public void BuildIndex_Combined_vs_SingleLog_SameEventCount()
    {
        const int EventCount = 200_000;
        const int CombinedBudgetMilliseconds = 10_000;

        (string Label, SortContext Context)[] contexts =
        [
            ("DateAndTime desc", new SortContext(ColumnName.DateAndTime, true, null, false)),
            ("Level grouped by Source", new SortContext(ColumnName.Level, false, ColumnName.Source, true))
        ];

        foreach ((string label, SortContext context) in contexts)
        {
            long singleMs = MeasureBulkBuild(logCount: 1, EventCount, context);

            foreach (int logCount in new[] { 2, 5, 10 })
            {
                long combinedMs = MeasureBulkBuild(logCount, EventCount, context);
                double ratio = singleMs == 0 ? double.PositiveInfinity : (double)combinedMs / singleMs;

                // The ratio is a logged diagnostic only; a wall-clock ratio assertion flakes on contended
                // runners. The absolute budget below is the regression guard, and the combined k-way merge's
                // correctness is pinned deterministically by the CombinedBulk_MatchesIncremental_* parity tests.
                _output.WriteLine(
                    $"{label}: single-log {singleMs} ms vs combined k={logCount} {combinedMs} ms ({ratio:F1}x) for {EventCount:N0} events");

                Assert.True(
                    combinedMs < CombinedBudgetMilliseconds,
                    $"{label}: combined k={logCount} bulk build took {combinedMs} ms, over the {CombinedBudgetMilliseconds} ms budget");
            }
        }
    }

    [Fact]
    public void BuildIndex_FullReproject_LatencyAcrossContexts()
    {
        const int EventCount = 200_000;
        const int BudgetMilliseconds = 10_000;

        var sample = new OrderedViewSample(seed: 20260102, logCount: 1);
        sample.Append(0, EventCount);
        var logId = sample.LogId(0);
        var reader = sample.Reader(0);

        (string Label, SortContext Context)[] contexts =
        [
            ("default (record order)", new SortContext(null, true, null, false)),
            ("DateAndTime desc", new SortContext(ColumnName.DateAndTime, true, null, false)),
            ("Level grouped by Source", new SortContext(ColumnName.Level, false, ColumnName.Source, true))
        ];

        foreach ((string label, SortContext context) in contexts)
        {
            var state = new OrderedViewState();
            state.ReconcileLog(logId, reader);
            RebuildRequest request = state.BeginRebuild(static (_, _) => true, context);

            var stopwatch = Stopwatch.StartNew();
            ChunkedOrderIndex index = OrderedViewState.BuildIndex(request, TestContext.Current.CancellationToken);
            stopwatch.Stop();

            Assert.Equal(EventCount, index.Count);
            _output.WriteLine($"BuildIndex {label}: {stopwatch.ElapsedMilliseconds} ms for {EventCount:N0} events");
            Assert.True(
                stopwatch.ElapsedMilliseconds < BudgetMilliseconds,
                $"BuildIndex {label} took {stopwatch.ElapsedMilliseconds} ms, over the {BudgetMilliseconds} ms budget");
        }
    }

    private static long MeasureBulkBuild(int logCount, int eventCount, SortContext context)
    {
        var sample = new OrderedViewSample(seed: 20260102, logCount);
        sample.SeedInterleaved(eventCount);

        var state = new OrderedViewState();

        for (int k = 0; k < sample.LogCount; k++) { state.ReconcileLog(sample.LogId(k), sample.Reader(k)); }

        RebuildRequest request = state.BeginRebuild(static (_, _) => true, context);

        // bulkThreshold:0 + unbounded budget force the bulk path so this isolates single-bulk vs combined k-way-merge.
        // Build once to warm up the JIT, then measure.
        OrderedViewState.BuildIndex(request, TestContext.Current.CancellationToken, bulkThreshold: 0, memoryBudgetBytes: long.MaxValue);

        var stopwatch = Stopwatch.StartNew();
        ChunkedOrderIndex index = OrderedViewState.BuildIndex(
            request, TestContext.Current.CancellationToken, bulkThreshold: 0, memoryBudgetBytes: long.MaxValue);
        stopwatch.Stop();

        Assert.Equal(eventCount, index.Count);

        return stopwatch.ElapsedMilliseconds;
    }
}
