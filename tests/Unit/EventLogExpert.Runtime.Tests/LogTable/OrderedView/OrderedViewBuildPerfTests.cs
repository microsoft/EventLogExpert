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
}
