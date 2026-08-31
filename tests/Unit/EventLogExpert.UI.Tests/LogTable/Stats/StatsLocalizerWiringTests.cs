// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using Bunit.TestDoubles;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Localization;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.FilterLenses;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.LogTable.OrderedView;
using EventLogExpert.Runtime.Stats;
using EventLogExpert.UI.LogTable.Stats;
using EventLogExpert.UI.Modal;
using EventLogExpert.UI.Tests.TestUtils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using NSubstitute;
using System.Collections.Immutable;

namespace EventLogExpert.UI.Tests.LogTable.Stats;

public sealed class StatsLocalizerWiringTests : BunitContext
{
    private static readonly EventLogId s_tokenLog = EventLogId.Create();
    private static readonly TimeSpan s_wait = TimeSpan.FromSeconds(10);

    private readonly IFilterLensCommands _lensCommands = Substitute.For<IFilterLensCommands>();
    private readonly IFilterLensSource _lensSource = Substitute.For<IFilterLensSource>();
    private readonly IModalCoordinator _modalCoordinator = Substitute.For<IModalCoordinator>();
    private readonly ModalId _modalId = new(1L);
    private readonly IModalService _modalService = Substitute.For<IModalService>();
    private readonly IOpenLogsPresenceSource _openLogs = Substitute.For<IOpenLogsPresenceSource>();
    private readonly IStatsDrawerPreferencesProvider _preferences = Substitute.For<IStatsDrawerPreferencesProvider>();
    private readonly IStatsService _statsService = Substitute.For<IStatsService>();
    private readonly IStatsVisibilitySource _statsVisibility = Substitute.For<IStatsVisibilitySource>();
    private readonly EventLogId _tabId = EventLogId.Create();
    private readonly ITraceLogger _traceLogger = Substitute.For<ITraceLogger>();
    private readonly IEventColumnView _view = Substitute.For<IEventColumnView>();
    private readonly IOrderedViewSource _viewSource = Substitute.For<IOrderedViewSource>();

    public StatsLocalizerWiringTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.SetupModule("./_content/EventLogExpert.UI/LogTable/Stats/StatsDrawer.razor.js");
        JSInterop.SetupModule("./_content/EventLogExpert.UI/LogTable/Stats/StatsPane.razor.js");

        _lensSource.Lenses.Returns(ImmutableList<FilterLensSummary>.Empty);
        _modalService.ActiveModalId.Returns(_modalId);
        _openLogs.HasOpenLogs.Returns(true);
        _viewSource.Current.Returns(_ => PresentationWith(ViewContentToken.FromStamps(default, [new ViewContentTokenReaderStamp(s_tokenLog, 0, 1, 1)], 1)));

        Services.AddBannerHostDependencies();
        Services.AddMenuMocks();
        Services.AddSingleton<IStringLocalizer<SharedResource>>(new MarkerLocalizer());
        Services.AddImmediateCpuWorkScheduler();
        Services.AddSingleton(_lensCommands);
        Services.AddSingleton(_lensSource);
        Services.AddSingleton(_modalCoordinator);
        Services.AddSingleton(_modalService);
        Services.AddSingleton(_openLogs);
        Services.AddSingleton(_preferences);
        Services.AddSingleton(_statsService);
        Services.AddSingleton(_statsVisibility);
        Services.AddSingleton(_traceLogger);
        Services.AddSingleton(_viewSource);
    }

    [Fact]
    public void StatsDetailModal_ChromeSearchAndRowActions_AreDrivenByTheLocalizer()
    {
        SetupDimension(Dim(StatsDimension.Source, total: 100, distinct: 1, ("Alpha", 100)));

        var cut = Render<StatsDetailModal>(parameters => parameters
            .Add(component => component.Dimension, StatsDimension.Source)
            .Add(component => component.View, _view));
        cut.WaitForState(() => cut.FindAll(".stats-detail-row").Count == 1, s_wait);

        Assert.Contains("[[Stats_Detail_Title([[Stats_Dimension_Source]])]]", cut.Markup, StringComparison.Ordinal);
        Assert.Equal("[[Stats_Detail_FilterValuesAria]]", cut.Find(".stats-detail-search").GetAttribute("aria-label"));
        Assert.Equal("[[Stats_Detail_FilterValuesPlaceholder]]", cut.Find(".stats-detail-search").GetAttribute("placeholder"));
        Assert.Equal("[[Stats_Row_IncludeAria([[Stats_Dimension_Source]]|Alpha)]]", cut.Find(".stats-detail-include").GetAttribute("aria-label"));
        Assert.Equal("[[Stats_Row_ExcludeTitle]]", cut.Find(".stats-detail-exclude").GetAttribute("title"));
    }

    [Fact]
    public void StatsDrawer_ResizeChrome_IsDrivenByTheLocalizer()
    {
        _statsVisibility.IsVisible.Returns(true);
        ComponentFactories.AddStub<StatsPane>();

        var cut = Render<StatsDrawer>();

        Assert.Equal("[[Stats_Drawer_ResizeTitle]]", cut.Find(".stats-drawer-resizer").GetAttribute("title"));
        Assert.NotEmpty(cut.FindComponents<Stub<StatsPane>>());
    }

    [Fact]
    public void StatsPane_ChromeLabelsAndAria_AreDrivenByTheLocalizer()
    {
        SetupSeverity(total: 100, Slots(critical: 2, error: 3, warning: 0, info: 95, verbose: 0, unknown: 0));
        SetupDimension(Dim(StatsDimension.Source, total: 100, distinct: 1, ("Alpha", 60)));
        SetupDimension(Dim(StatsDimension.EventId, total: 100, distinct: 1, ("4624", 100)));
        SetupDimension(Dim(StatsDimension.TaskCategory, total: 100, distinct: 1, ("Task", 100)));
        SetupDimension(Dim(StatsDimension.User, total: 100, distinct: 1, ("User", 100)));

        var cut = Render<StatsPane>();
        cut.WaitForState(() => cut.FindAll(".stats-coverage").Count == 4, s_wait);

        Assert.Equal("[[Stats_AriaLabel]]", cut.Find(".stats-pane").GetAttribute("aria-label"));
        Assert.Equal("[[Stats_Title]]", cut.Find(".stats-title").TextContent);
        Assert.Equal("[[Stats_ResolutionCoverage]]", cut.Find(".stats-coverage-link").TextContent.Trim());
        Assert.Equal("[[Stats_ResolutionCoverageAria]]", cut.Find(".stats-coverage-link").GetAttribute("aria-label"));
        Assert.Equal(
            "[[Stats_Headline_Events_Many(100)]][[Stats_Headline_ErrorCritical_Many(5)]][[Stats_Headline_TopSources_One(1|60)]]",
            cut.Find(".stats-headline").TextContent);
        Assert.Equal("[[Stats_SeverityBarLabel]]", cut.Find(".stats-severity-bar").GetAttribute("aria-label"));
        Assert.Contains("[[Stats_SeveritySegmentTooltip", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("[[Severity_Level_Information]]", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("[[Stats_Dimension_Source]]", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("[[Stats_Row_IncludeAria([[Stats_Dimension_Source]]|Alpha)]]", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("[[Stats_Coverage_All_Source_One", cut.Markup, StringComparison.Ordinal);
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

    private static int[] Slots(int critical, int error, int warning, int info, int verbose, int unknown) =>
        [unknown, critical, error, warning, info, verbose];

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
            .Returns(new SeverityStats(total, slots));
}
