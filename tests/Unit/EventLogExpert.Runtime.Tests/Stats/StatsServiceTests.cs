// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.Stats;
using EventLogExpert.Runtime.Tests.LogTable.TestSupport;
using EventLogExpert.Runtime.Tests.TestUtils;

namespace EventLogExpert.Runtime.Tests.Stats;

public sealed class StatsServiceTests
{
    private static readonly EventLogId s_logId = EventLogId.Create();

    private readonly IStatsService _service = new StatsService();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void BuildDimension_CancelledToken_Throws()
    {
        var view = ViewOver(Ev(1, source: "A"), Ev(2, source: "B"), Ev(3, source: "C"));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Cancellation is honored across the whole build (counting AND ranking), so a closed "View all" modal cannot
        // keep sorting a high-cardinality dimension after its token is cancelled.
        Assert.ThrowsAny<OperationCanceledException>(
            () => _service.BuildDimension(view, StatsDimension.Source, topN: 8, cts.Token));
    }

    [Fact]
    public void BuildDimension_CapsAtTopN_ButCountsAllDistinct()
    {
        var view = ViewOver(Ev(1, source: "A"), Ev(2, source: "B"), Ev(3, source: "C"), Ev(4, source: "D"));

        var stats = _service.BuildDimension(view, StatsDimension.Source, topN: 2, Ct);

        Assert.Equal(2, stats.Top.Count);
        Assert.Equal(4, stats.DistinctCount);
    }

    [Fact]
    public void BuildDimension_EventId_HasNoMissing()
    {
        var view = ViewOver(Ev(4624), Ev(4624), Ev(4625));

        var stats = _service.BuildDimension(view, StatsDimension.EventId, topN: 8, Ct);

        Assert.Equal(3, stats.Total);
        Assert.Equal(0, stats.MissingCount);
        Assert.Equal(["4624", "4625"], stats.Top.Select(contributor => contributor.Value));
        Assert.Equal([2, 1], stats.Top.Select(contributor => contributor.Count));
    }

    [Fact]
    public void BuildDimension_OverFilteredView_CountsOnlySurvivors()
    {
        ResolvedEvent[] events = [Ev(1, source: "Keep"), Ev(2, source: "Drop"), Ev(3, source: "Keep")];
        var reader = EventColumnStore.Build(events, generation: 0, contentVersion: 0).CreateReader(s_logId);
        var view = AosReferenceView.Create(
            reader, [0, 2], orderBy: null, isDescending: false, groupBy: null, isGroupDescending: false);

        var stats = _service.BuildDimension(view, StatsDimension.Source, topN: 8, Ct);

        Assert.Equal(2, stats.Total);
        Assert.Equal(["Keep"], stats.Top.Select(contributor => contributor.Value));
        Assert.Equal([2], stats.Top.Select(contributor => contributor.Count));
    }

    [Fact]
    public void BuildDimension_Source_RanksByCountThenOrdinal_AndCountsMissing()
    {
        var view = ViewOver(
            Ev(1, source: "Bravo"), Ev(2, source: "Alpha"), Ev(3, source: "Alpha"), Ev(4, source: "Bravo"),
            Ev(5, source: "Charlie"),
            Ev(6, source: ""), Ev(7, source: "")); // absent source -> missing bucket, still in Total

        var stats = _service.BuildDimension(view, StatsDimension.Source, topN: 8, Ct);

        Assert.Equal(7, stats.Total);
        Assert.Equal(3, stats.DistinctCount);
        Assert.Equal(2, stats.MissingCount);
        // Alpha(2) and Bravo(2) tie on count -> broken by ordinal (Alpha before Bravo); Charlie(1) last.
        Assert.Equal(["Alpha", "Bravo", "Charlie"], stats.Top.Select(contributor => contributor.Value));
        Assert.Equal([2, 2, 1], stats.Top.Select(contributor => contributor.Count));
        Assert.Equal(5, stats.ShownEventCount);
    }

    [Fact]
    public void BuildSeverity_TalliesSlotsAndTotal_AbsentToUnknown()
    {
        var view = ViewOver(
            Ev(1, level: "Critical"), Ev(2, level: "Error"), Ev(3, level: "Error"),
            Ev(4, level: "Warning"), Ev(5, level: "Information"), Ev(6, level: "Verbose"), Ev(7, level: ""));

        var stats = _service.BuildSeverity(view, Ct);

        Assert.Equal(7, stats.Total);
        Assert.Equal(1, stats.Slots[(int)SeverityLevel.Critical]);
        Assert.Equal(2, stats.Slots[(int)SeverityLevel.Error]);
        Assert.Equal(1, stats.Slots[(int)SeverityLevel.Warning]);
        Assert.Equal(1, stats.Slots[(int)SeverityLevel.Information]);
        Assert.Equal(1, stats.Slots[(int)SeverityLevel.Verbose]);
        Assert.Equal(1, stats.Slots[0]); // absent level -> Unknown
        Assert.Equal(7, stats.Slots.Sum());
    }

    private static ResolvedEvent Ev(int id, string source = "TestSource", string level = "Information") =>
        new("live", LogPathType.Channel) { Id = id, Source = source, Level = level };

    private static IEventColumnView ViewOver(params ResolvedEvent[] events) =>
        DisplayViewTestFactory.Build(s_logId, events);
}
