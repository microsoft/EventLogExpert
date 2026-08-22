// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Filtering.Common.Filtering;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.FilterLenses;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.LogTable.OrderedView;
using EventLogExpert.Runtime.Stats;
using EventLogExpert.UI.LogTable.Stats;
using EventLogExpert.UI.Modal;
using EventLogExpert.UI.Tests.TestUtils;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using System.Collections.Immutable;

namespace EventLogExpert.UI.Tests.LogTable.Stats;

public sealed class StatsPaneTests : BunitContext
{
    private static readonly EventLogId s_tokenLog = EventLogId.Create();
    private static readonly TimeSpan s_wait = TimeSpan.FromSeconds(10);

    private readonly IFilterLensCommands _lensCommands = Substitute.For<IFilterLensCommands>();
    private readonly IFilterLensSource _lensSource = Substitute.For<IFilterLensSource>();
    private readonly IModalCoordinator _modalCoordinator = Substitute.For<IModalCoordinator>();
    private readonly ManualResetEventSlim _severityGate = new(initialState: true);
    private readonly IStatsService _statsService = Substitute.For<IStatsService>();
    private readonly EventLogId _tabId = EventLogId.Create();
    private readonly ITraceLogger _traceLogger = Substitute.For<ITraceLogger>();
    private readonly IEventColumnView _view = Substitute.For<IEventColumnView>();
    private readonly IOrderedViewSource _viewSource = Substitute.For<IOrderedViewSource>();

    private OrderedViewPresentation _presentation;

    public StatsPaneTests()
    {
        _presentation = PresentationWith(Token(1));
        _viewSource.Current.Returns(_ => _presentation);
        _lensSource.Lenses.Returns(ImmutableList<FilterLensSummary>.Empty);

        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.SetupModule("./_content/EventLogExpert.UI/LogTable/Stats/StatsPane.razor.js");

        Services.AddImmediateCpuWorkScheduler();
        Services.AddSingleton(_lensCommands);
        Services.AddSingleton(_lensSource);
        Services.AddSingleton(_modalCoordinator);
        Services.AddSingleton(_statsService);
        Services.AddSingleton(_traceLogger);
        Services.AddSingleton(_viewSource);
    }

    [Fact]
    public void AllExcluded_HeadlineAsksToRemoveLens()
    {
        SetupSeverity(total: 0, Slots(critical: 0, error: 0, warning: 0, info: 0, verbose: 0, unknown: 0));
        _lensSource.Lenses.Returns([new FilterLensSummary(new FilterLensId(Guid.NewGuid()), "Source excludes Alpha")]);

        var cut = Render<StatsPane>();

        cut.WaitForAssertion(() => Assert.Contains("remove a lens", cut.Find(".stats-headline").TextContent), s_wait);
    }

    [Fact]
    public void EmptyView_HeadlineSaysNoEvents()
    {
        SetupSeverity(total: 0, Slots(critical: 0, error: 0, warning: 0, info: 0, verbose: 0, unknown: 0));

        var cut = Render<StatsPane>();

        cut.WaitForAssertion(() => Assert.Contains("No events in the current view", cut.Find(".stats-headline").TextContent), s_wait);
    }

    [Fact]
    public async Task ExcludeClick_EventId_PushesExcludeEventIdWithParsedValue()
    {
        SetupSeverity(total: 100, Slots(critical: 0, error: 0, warning: 0, info: 100, verbose: 0, unknown: 0));
        SetupDimension(SourceStats(total: 100, missing: 0, distinct: 1, ("Alpha", 100)));
        SetupDimension(EventIdStats(total: 100, distinct: 2, ("4624", 60), ("4625", 40)));
        SetupDimension(Dim(StatsDimension.TaskCategory, total: 100, distinct: 1, ("Task", 100)));
        SetupDimension(Dim(StatsDimension.User, total: 100, distinct: 1, ("User", 100)));

        var cut = Render<StatsPane>();
        cut.WaitForState(() => cut.FindAll(".stats-coverage").Count == 4, s_wait);

        await cut.InvokeAsync(() => cut.Find("[aria-label='Exclude Event ID 4624']").Click());

        _lensCommands.Received(1).ExcludeEventId(4624, "live");
    }

    [Fact]
    public async Task ExcludeClick_StringDimension_PushesExcludeLensWithRawValue()
    {
        SetupSeverity(total: 100, Slots(critical: 0, error: 0, warning: 0, info: 100, verbose: 0, unknown: 0));
        SetupDimension(SourceStats(total: 100, missing: 0, distinct: 2, ("Alpha", 60), ("Bravo", 40)));
        SetupDimension(EventIdStats(total: 100, distinct: 1, ("1", 100)));
        SetupDimension(Dim(StatsDimension.TaskCategory, total: 100, distinct: 1, ("Task", 100)));
        SetupDimension(Dim(StatsDimension.User, total: 100, distinct: 1, ("User", 100)));

        var cut = Render<StatsPane>();
        cut.WaitForState(() => cut.FindAll(".stats-coverage").Count == 4, s_wait);

        Assert.False(cut.Find("[aria-label='Exclude Source Alpha']").HasAttribute("disabled"));

        // Find + click atomically on the renderer's dispatcher: the per-row onclick lambda is recreated every render,
        // so a progressive-scan re-render between a separate find and click would retire the captured handler id.
        await cut.InvokeAsync(() => cut.Find("[aria-label='Exclude Source Alpha']").Click());

        _lensCommands.Received(1).ExcludeValue(EventProperty.Source, "Alpha", "live");
    }

    [Fact]
    public async Task IncludeClick_EventId_PushesIncludeEventIdWithParsedValue()
    {
        SetupSeverity(total: 100, Slots(critical: 0, error: 0, warning: 0, info: 100, verbose: 0, unknown: 0));
        SetupDimension(SourceStats(total: 100, missing: 0, distinct: 1, ("Alpha", 100)));
        SetupDimension(EventIdStats(total: 100, distinct: 2, ("4624", 60), ("4625", 40)));
        SetupDimension(Dim(StatsDimension.TaskCategory, total: 100, distinct: 1, ("Task", 100)));
        SetupDimension(Dim(StatsDimension.User, total: 100, distinct: 1, ("User", 100)));

        var cut = Render<StatsPane>();
        cut.WaitForState(() => cut.FindAll(".stats-coverage").Count == 4, s_wait);

        await cut.InvokeAsync(() => cut.Find("[aria-label='Filter to Event ID 4624']").Click());

        _lensCommands.Received(1).IncludeEventId(4624, "live");
    }

    [Fact]
    public async Task IncludeClick_StringDimension_PushesIncludeLensWithRawValue()
    {
        SetupSeverity(total: 100, Slots(critical: 0, error: 0, warning: 0, info: 100, verbose: 0, unknown: 0));
        SetupDimension(SourceStats(total: 100, missing: 0, distinct: 2, ("Alpha", 60), ("Bravo", 40)));
        SetupDimension(EventIdStats(total: 100, distinct: 1, ("1", 100)));
        SetupDimension(Dim(StatsDimension.TaskCategory, total: 100, distinct: 1, ("Task", 100)));
        SetupDimension(Dim(StatsDimension.User, total: 100, distinct: 1, ("User", 100)));

        var cut = Render<StatsPane>();
        cut.WaitForState(() => cut.FindAll(".stats-coverage").Count == 4, s_wait);

        await cut.InvokeAsync(() => cut.Find("[aria-label='Filter to Source Alpha']").Click());

        _lensCommands.Received(1).IncludeValue(EventProperty.Source, "Alpha", "live");
    }

    [Fact]
    public async Task Recomputing_KeepsRowsMarksUpdatingAndDisablesExclude()
    {
        SetupSeverity(total: 100, Slots(critical: 0, error: 0, warning: 0, info: 100, verbose: 0, unknown: 0));
        SetupDimension(SourceStats(total: 100, missing: 0, distinct: 1, ("Alpha", 100)));
        SetupDimension(EventIdStats(total: 100, distinct: 1, ("1", 100)));

        var cut = Render<StatsPane>();
        cut.WaitForAssertion(() => Assert.False(cut.Find("[aria-label='Exclude Source Alpha']").HasAttribute("disabled")), s_wait);

        // Block the recompute so the in-flight (stale) state is observable rather than a 500ms timing race.
        _severityGate.Reset();
        _presentation = PresentationWith(Token(2));
        await cut.InvokeAsync(() => _viewSource.Updated += Raise.Event<Action<OrderedViewPresentation>>(_presentation));

        try
        {
            cut.WaitForAssertion(() =>
            {
                Assert.Contains("Updating...", cut.Markup);
                Assert.Contains("Alpha", cut.Markup);
                Assert.True(cut.Find("[aria-label='Exclude Source Alpha']").HasAttribute("disabled"));
            }, s_wait);
        }
        finally
        {
            _severityGate.Set();
        }
    }

    [Fact]
    public void Render_MissingValues_RendersNonExcludableNoneBucket()
    {
        SetupSeverity(total: 100, Slots(critical: 0, error: 0, warning: 0, info: 100, verbose: 0, unknown: 0));
        SetupDimension(SourceStats(total: 100, missing: 15, distinct: 2, ("Alpha", 50), ("Bravo", 35)));
        SetupDimension(EventIdStats(total: 100, distinct: 1, ("1", 100)));

        var cut = Render<StatsPane>();

        cut.WaitForAssertion(() =>
        {
            var noneRow = cut.Find(".stats-row-none");
            Assert.Contains("(none)", noneRow.TextContent);
            Assert.Contains("15", noneRow.TextContent);
            Assert.Empty(noneRow.QuerySelectorAll("button.stats-exclude"));
        }, s_wait);
    }

    [Fact]
    public void Render_ShowsHeadlineSeverityLegendAndTopRows()
    {
        SetupSeverity(total: 100, Slots(critical: 2, error: 8, warning: 10, info: 70, verbose: 5, unknown: 5));
        SetupDimension(SourceStats(total: 100, missing: 0, distinct: 3, ("Alpha", 40), ("Bravo", 30), ("Charlie", 20)));
        SetupDimension(EventIdStats(total: 100, distinct: 2, ("4624", 60), ("4625", 40)));

        var cut = Render<StatsPane>();

        cut.WaitForAssertion(() =>
        {
            string headline = cut.Find(".stats-headline").TextContent;
            Assert.Contains("100 events", headline);
            Assert.Contains("10 error/critical", headline);
            Assert.Contains("top 3 sources = 90%", headline);
            Assert.Contains("Critical", cut.Markup);
            Assert.Contains("Alpha", cut.Markup);
        }, s_wait);
    }

    [Fact]
    public async Task Resize_FitsDefaultRowCountToPaneHeight()
    {
        SetupSeverity(total: 100, Slots(critical: 0, error: 0, warning: 0, info: 100, verbose: 0, unknown: 0));
        SetupDimension(SourceStats(total: 100, missing: 0, distinct: 20, ("Alpha", 100)));
        SetupDimension(EventIdStats(total: 100, distinct: 1, ("1", 100)));
        SetupDimension(Dim(StatsDimension.TaskCategory, total: 100, distinct: 1, ("Task", 100)));
        SetupDimension(Dim(StatsDimension.User, total: 100, distinct: 1, ("User", 100)));

        var cut = Render<StatsPane>();
        cut.WaitForState(() => cut.FindAll(".stats-coverage").Count == 4, s_wait);
        _statsService.ClearReceivedCalls();

        // A tall section fits more rows than the default: (500 - 68) / 22 = 19, within the 40-row cap.
        await cut.InvokeAsync(() => cut.Instance.OnStatsResized(500));

        cut.WaitForAssertion(
            () => _statsService.Received().BuildDimension(
                Arg.Any<IEventColumnView>(), Arg.Is(StatsDimension.Source), 19, Arg.Any<CancellationToken>()),
            s_wait);
    }

    [Fact]
    public async Task TabChangeWithSameContentToken_TriggersRescan()
    {
        SetupSeverity(total: 100, Slots(critical: 0, error: 0, warning: 0, info: 100, verbose: 0, unknown: 0));
        SetupDimension(SourceStats(total: 100, missing: 0, distinct: 1, ("Alpha", 100)));
        SetupDimension(EventIdStats(total: 100, distinct: 1, ("1", 100)));

        var cut = Render<StatsPane>();
        cut.WaitForState(() => cut.FindAll(".stats-coverage").Count == 2, s_wait);
        _statsService.ClearReceivedCalls();

        // Switch to a different tab that resolves to the same content token (e.g. two empty/equivalent views). The
        // publish guard enforces ActiveTabId, so the recompute key must key on it too or the pane never rescans.
        _presentation = new OrderedViewPresentation(_view, EventLogId.Create(), default, PresentationState.Current, 2)
        {
            ContentToken = Token(1),
            ActiveLogName = "other"
        };
        await cut.InvokeAsync(() => _viewSource.Updated += Raise.Event<Action<OrderedViewPresentation>>(_presentation));

        cut.WaitForAssertion(
            () => _statsService.Received().BuildSeverity(Arg.Any<IEventColumnView>(), Arg.Any<CancellationToken>()),
            s_wait);
    }

    [Fact]
    public async Task ViewAllClick_OpensDetailModalForDimension()
    {
        SetupSeverity(total: 100, Slots(critical: 0, error: 0, warning: 0, info: 100, verbose: 0, unknown: 0));
        SetupDimension(SourceStats(total: 100, missing: 0, distinct: 20, ("Alpha", 60), ("Bravo", 40)));
        SetupDimension(EventIdStats(total: 100, distinct: 1, ("1", 100)));
        SetupDimension(Dim(StatsDimension.TaskCategory, total: 100, distinct: 1, ("Task", 100)));
        SetupDimension(Dim(StatsDimension.User, total: 100, distinct: 1, ("User", 100)));

        var cut = Render<StatsPane>();
        cut.WaitForState(() => cut.FindAll(".stats-coverage").Count == 4, s_wait);

        // Only Source has more distinct values than shown, so it is the single "View all".
        var viewAll = cut.FindAll("button").First(button => button.TextContent.Trim() == "View all");
        await cut.InvokeAsync(() => viewAll.Click());

        _ = _modalCoordinator.Received(1).PushAsync<StatsDetailModal, bool>(Arg.Any<IDictionary<string, object?>>());
    }

    private static DimensionStats Dim(StatsDimension dimension, int total, int distinct, params (string Value, int Count)[] top) =>
        new()
        {
            Dimension = dimension,
            Total = total,
            DistinctCount = distinct,
            MissingCount = 0,
            Top = top.Select(entry => new StatsContributor(entry.Value, entry.Count)).ToList()
        };

    private static DimensionStats EventIdStats(int total, int distinct, params (string Value, int Count)[] top) =>
        new()
        {
            Dimension = StatsDimension.EventId,
            Total = total,
            DistinctCount = distinct,
            MissingCount = 0,
            Top = top.Select(entry => new StatsContributor(entry.Value, entry.Count)).ToList()
        };

    private static int[] Slots(int critical, int error, int warning, int info, int verbose, int unknown) =>
        [unknown, critical, error, warning, info, verbose];

    private static DimensionStats SourceStats(int total, int missing, int distinct, params (string Value, int Count)[] top) =>
        new()
        {
            Dimension = StatsDimension.Source,
            Total = total,
            DistinctCount = distinct,
            MissingCount = missing,
            Top = top.Select(entry => new StatsContributor(entry.Value, entry.Count)).ToList()
        };

    private static ViewContentToken Token(long contentVersion) =>
        ViewContentToken.FromStamps(default, [new ViewContentTokenReaderStamp(s_tokenLog, 0, contentVersion, 1)], 1);

    private OrderedViewPresentation PresentationWith(ViewContentToken token) =>
        new(_view, _tabId, default, PresentationState.Current, 1)
        {
            ContentToken = token,
            ActiveLogName = "live"
        };

    private void SetupDimension(DimensionStats stats) =>
        _statsService
            .BuildDimension(Arg.Any<IEventColumnView>(), Arg.Is(stats.Dimension), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(stats);

    private void SetupSeverity(int total, int[] slots) =>
        _statsService.BuildSeverity(Arg.Any<IEventColumnView>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                _severityGate.Wait();
                return new SeverityStats(total, slots);
            });
}
