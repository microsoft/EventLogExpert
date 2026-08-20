// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.ResolutionCoverage;
using EventLogExpert.Runtime.Tests.LogTable.TestSupport;

namespace EventLogExpert.Runtime.Tests.ResolutionCoverage;

public sealed class ResolutionCoverageServiceTests
{
    private static readonly EventLogId s_logId = EventLogId.Create();

    private readonly IResolutionCoverageService _service = new ResolutionCoverageService();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void BuildProviderDetail_CancelledToken_Throws()
    {
        var view = ViewOver(EvL(1, "P", EventResolutionStatus.NoProvider, "Error"));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => _service.BuildProviderDetail(view, "P", cts.Token));
    }

    [Fact]
    public void BuildProviderDetail_CapsEventIds_AndReportsDistinctUnresolvedCount()
    {
        var events = Enumerable.Range(1, 120)
            .Select(id => EvL(id, "P", EventResolutionStatus.NoProvider, "Error"))
            .ToArray();

        var detail = _service.BuildProviderDetail(ViewOver(events), "P", Ct);

        Assert.Equal(ResolutionCoverageService.MaxEventIdRows, detail.EventIds.Count);
        Assert.Equal(120, detail.DistinctUnresolvedEventIdCount);
    }

    [Fact]
    public void BuildProviderDetail_EventIds_ExcludeFullyResolved_AndRankByUnresolvedDescending()
    {
        var view = ViewOver(
            EvL(100, "P", EventResolutionStatus.Resolved, "Error"),
            EvL(200, "P", EventResolutionStatus.NoProvider, "Error"),
            EvL(300, "P", EventResolutionStatus.NoProvider, "Error"),
            EvL(300, "P", EventResolutionStatus.NoMessage, "Error"));

        var detail = _service.BuildProviderDetail(view, "P", Ct);

        Assert.Equal(new[] { 300, 200 }, detail.EventIds.Select(row => row.EventId).ToArray());
        Assert.Equal(2, detail.DistinctUnresolvedEventIdCount);
    }

    [Fact]
    public void BuildProviderDetail_Levels_BreakDownUnresolvedBySeverity_OmittingResolvedOnlyLevels()
    {
        var view = ViewOver(
            EvL(1, "P", EventResolutionStatus.NoProvider, "Critical"),
            EvL(2, "P", EventResolutionStatus.NoProvider, "Error"),
            EvL(3, "P", EventResolutionStatus.NoMessage, "Error"),
            EvL(4, "P", EventResolutionStatus.Resolved, "Warning"),
            EvL(5, "P", EventResolutionStatus.Failed, ""));

        var detail = _service.BuildProviderDetail(view, "P", Ct);

        Assert.Equal(1, UnresolvedAtLevel(detail, SeverityLevel.Critical));
        Assert.Equal(2, UnresolvedAtLevel(detail, SeverityLevel.Error));
        Assert.Equal(1, UnresolvedAtLevel(detail, level: null));
        Assert.DoesNotContain(detail.Levels, row => row.Level == SeverityLevel.Warning);
    }

    [Fact]
    public void BuildProviderDetail_ScopesToProvider_IgnoresOthers()
    {
        var view = ViewOver(
            EvL(1, "P", EventResolutionStatus.NoProvider, "Error"),
            EvL(2, "Other", EventResolutionStatus.NoProvider, "Error"));

        var detail = _service.BuildProviderDetail(view, "P", Ct);

        Assert.Equal(1, detail.DistinctUnresolvedEventIdCount);
        Assert.Equal(1, Assert.Single(detail.EventIds).EventId);
    }

    [Fact]
    public void Build_CancelledToken_Throws()
    {
        var view = ViewOver(Ev(1, "A", EventResolutionStatus.NoProvider));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => _service.Build(view, cts.Token));
    }

    [Fact]
    public void Build_ClassifiesCoverageAndSumsSummary()
    {
        var view = ViewOver(
            Ev(1, "FullProvider", EventResolutionStatus.Resolved),
            Ev(2, "FullProvider", EventResolutionStatus.Resolved),
            Ev(3, "NoneProvider", EventResolutionStatus.NoProvider),
            Ev(4, "PartialProvider", EventResolutionStatus.Resolved),
            Ev(5, "PartialProvider", EventResolutionStatus.NoMessage));

        var report = _service.Build(view, Ct);

        Assert.Equal(3, report.Rows.Count);
        Assert.Equal(5, report.Summary.Total);
        Assert.Equal(3, report.Summary.Resolved);
        Assert.Equal(1, report.Summary.NoProvider);
        Assert.Equal(1, report.Summary.NoMessage);

        var byProvider = report.Rows.ToDictionary(row => row.Provider);
        Assert.Equal(CoverageStatus.Full, byProvider["FullProvider"].Status);
        Assert.Equal(CoverageStatus.None, byProvider["NoneProvider"].Status);
        Assert.Equal(CoverageStatus.Partial, byProvider["PartialProvider"].Status);
    }

    [Fact]
    public void Build_PreservesTotalAndSumOfProviderTotalsMatchesViewCount()
    {
        var view = ViewOver(
            Ev(1, "A", EventResolutionStatus.Resolved),
            Ev(2, "A", EventResolutionStatus.NoProvider),
            Ev(3, "B", EventResolutionStatus.NoMessage),
            Ev(4, "B", EventResolutionStatus.Failed));

        var report = _service.Build(view, Ct);

        foreach (var row in report.Rows)
        {
            Assert.Equal(
                row.Counts.Total,
                row.Counts.Resolved + row.Counts.NoProvider + row.Counts.NoMessage + row.Counts.Failed);
        }

        Assert.Equal(4, report.Rows.Sum(row => row.Counts.Total));
    }

    [Fact]
    public void Build_ProviderWithAllNoMessage_IsPartialNotNone()
    {
        var view = ViewOver(
            Ev(1, "MetadataButNoMatch", EventResolutionStatus.NoMessage),
            Ev(2, "MetadataButNoMatch", EventResolutionStatus.NoMessage));

        var report = _service.Build(view, Ct);

        Assert.Equal(CoverageStatus.Partial, Assert.Single(report.Rows).Status);
    }

    [Fact]
    public void Build_ReturnsAllProviders_OrderedByUnresolvedDescending()
    {
        var view = ViewOver(
            Ev(1, "Low", EventResolutionStatus.Resolved),
            Ev(2, "Low", EventResolutionStatus.NoProvider),
            Ev(3, "High", EventResolutionStatus.NoProvider),
            Ev(4, "High", EventResolutionStatus.NoProvider),
            Ev(5, "High", EventResolutionStatus.NoMessage),
            Ev(6, "Mid", EventResolutionStatus.NoProvider),
            Ev(7, "Mid", EventResolutionStatus.NoMessage));

        var report = _service.Build(view, Ct);

        Assert.Equal(new[] { "High", "Mid", "Low" }, report.Rows.Select(row => row.Provider).ToArray());
    }

    [Fact]
    public void CountResolutionBySource_ColumnValues_AreOnlyTheFourFrozenTokens()
    {
        var view = ViewOver(
            Ev(1, "A", EventResolutionStatus.Resolved),
            Ev(2, "A", EventResolutionStatus.NoProvider),
            Ev(3, "B", EventResolutionStatus.NoMessage),
            Ev(4, "B", EventResolutionStatus.Failed));

        var counts = new Dictionary<string, int>();
        view.CountFieldValues(EventFieldId.ResolutionStatus, counts, Ct);

        string[] expected =
        [
            ResolutionStatusTokens.Failed,
            ResolutionStatusTokens.NoMessage,
            ResolutionStatusTokens.NoProvider,
            ResolutionStatusTokens.Resolved
        ];

        Assert.Equal(expected.Order(), counts.Keys.Order());
    }

    private static ResolvedEvent Ev(int id, string source, EventResolutionStatus status) =>
        new("live", LogPathType.Channel) { Id = id, Source = source, ResolutionStatus = status };

    private static ResolvedEvent EvL(int id, string source, EventResolutionStatus status, string level) =>
        new("live", LogPathType.Channel) { Id = id, Source = source, ResolutionStatus = status, Level = level };

    private static int UnresolvedAtLevel(ProviderCoverageDetail detail, SeverityLevel? level) =>
        detail.Levels.Single(row => row.Level == level).Counts.Unresolved;

    private static IEventColumnView ViewOver(params ResolvedEvent[] events) =>
        AosReferenceView.Create(
            EventColumnStore.Build(events, generation: 0, contentVersion: 0).CreateReader(s_logId),
            [.. Enumerable.Range(0, events.Length)],
            orderBy: null,
            isDescending: false,
            groupBy: null,
            isGroupDescending: false);
}
