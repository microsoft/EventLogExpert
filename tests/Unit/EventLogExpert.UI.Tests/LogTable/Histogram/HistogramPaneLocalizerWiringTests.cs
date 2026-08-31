// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Localization;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Concurrency;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.FilterLenses;
using EventLogExpert.Runtime.FilterPane;
using EventLogExpert.Runtime.Histogram;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.LogTable.OrderedView;
using EventLogExpert.Runtime.Settings;
using EventLogExpert.UI.LogTable.Find;
using EventLogExpert.UI.LogTable.Histogram;
using EventLogExpert.UI.Tests.TestUtils;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using NSubstitute;
using System.Collections.Immutable;

namespace EventLogExpert.UI.Tests.LogTable.Histogram;

public sealed class HistogramPaneLocalizerWiringTests : BunitContext
{
    private static readonly DateTime s_start = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan s_wait = TimeSpan.FromSeconds(10);

    private readonly IActiveEventLogSource _activeEventLog = Substitute.For<IActiveEventLogSource>();
    private readonly QueuedHistogramDataScheduler _cpuScheduler = new();
    private readonly IHistogramDimensionRequestSource _dimensionRequest = Substitute.For<IHistogramDimensionRequestSource>();
    private readonly IEventFocusSource _eventFocus = Substitute.For<IEventFocusSource>();
    private readonly IFilterLensCommands _filterLensCommands = Substitute.For<IFilterLensCommands>();
    private readonly IActiveFiltersSource _filters = Substitute.For<IActiveFiltersSource>();
    private readonly IHighlightSelector _highlightSelector = Substitute.For<IHighlightSelector>();
    private readonly IEventColumnView _scanView = Substitute.For<IEventColumnView>();
    private readonly ISettingsService _settings = Substitute.For<ISettingsService>();
    private readonly EventLogId _tabId = EventLogId.Create();
    private readonly ITraceLogger _traceLogger = Substitute.For<ITraceLogger>();
    private readonly IOrderedViewSource _viewSource = Substitute.For<IOrderedViewSource>();

    public HistogramPaneLocalizerWiringTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.SetupModule("./_content/EventLogExpert.UI/Inputs/ValueSelect.razor.js");
        JSInterop.SetupModule("./_content/EventLogExpert.UI/LogTable/Histogram/HistogramPane.razor.js");

        _activeEventLog.Current.Returns((EventLogId?)null);
        _dimensionRequest.Current.Returns((HistogramDimensionRequest?)null);
        _eventFocus.Current.Returns((SelectionEntry?)null);
        _filters.Current.Returns(ImmutableList<SavedFilter>.Empty);
        _highlightSelector.Select(Arg.Any<ImmutableList<SavedFilter>>()).Returns([]);
        _highlightSelector.ComputePredicatePlanKey(Arg.Any<ImmutableList<SavedFilter>>()).Returns(0);
        _settings.TimeZoneInfo.Returns(TimeZoneInfo.Utc);
        _viewSource.Current.Returns(_ => new OrderedViewPresentation(
            _scanView,
            _tabId,
            default,
            PresentationState.Current,
            1)
        {
            ContentToken = ViewContentToken.FromStamps(default, [], 1)
        });

        Services.AddSingleton<IStringLocalizer<SharedResource>>(new MarkerLocalizer());
        Services.AddSingleton<ICpuWorkScheduler>(_cpuScheduler);
        Services.AddSingleton(_activeEventLog);
        Services.AddSingleton(_dimensionRequest);
        Services.AddSingleton(_eventFocus);
        Services.AddSingleton(_filterLensCommands);
        Services.AddSingleton(_filters);
        Services.AddSingleton<IFindMarkerSource>(new FindMarkerSource());
        Services.AddSingleton(_highlightSelector);
        Services.AddSingleton(_settings);
        Services.AddSingleton(_traceLogger);
        Services.AddSingleton(_viewSource);
    }

    [Fact]
    public void ChromeToolbarAndTimelineLabels_AreDrivenByTheLocalizer()
    {
        var cut = Render<HistogramPane>();

        Assert.Contains("[[Histogram_GroupBy]]", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("[[Histogram_Dimension_Severity]]", cut.Markup, StringComparison.Ordinal);
        Assert.Equal("[[Histogram_TimelineRoleDescription]]", cut.Find(".histogram-scroll").GetAttribute("aria-label"));
        Assert.Equal("[[Histogram_TimelineRoleDescription]]", cut.Find(".histogram-scroll").GetAttribute("aria-roledescription"));
        Assert.Equal("[[Histogram_ZoomOut]]", cut.FindAll(".histogram-button")[0].GetAttribute("aria-label"));
        Assert.Equal("[[Histogram_ZoomIn]]", cut.FindAll(".histogram-button")[1].GetAttribute("title"));
        Assert.Contains("[[Histogram_Undo]]", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("[[Histogram_Fit]]", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("[[Histogram_Scope]]", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("[[Histogram_Building]]", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DataBearingRender_ComposesRegionWindowTooltipAndCursorFromTypedInputs()
    {
        _cpuScheduler.NextHistogramData = MixedGroupHistogramData();

        var cut = Render<HistogramPane>();
        await cut.InvokeAsync(() => cut.Instance.OnHistogramResized(420, 120));
        cut.WaitForState(() => cut.FindAll("g[data-tip]").Count == 6, s_wait);

        string allDataBreakdown =
            "[[Histogram_BreakdownItem(12|Alpha)]]" +
            "[[Histogram_Breakdown_Separator]]" +
            "[[Histogram_BreakdownItem(9|[[Histogram_Severity_Errors]])]]";
        string allDataAria =
            $"[[Histogram_RegionAria_Breakdown(21|[[Histogram_EventNoun_Events_Many(21)]]|{s_start}|{s_start.AddHours(6)}|{allDataBreakdown})]]";

        Assert.Equal(allDataAria, cut.Find(".histogram-scroll").GetAttribute("aria-label"));
        Assert.Contains("[[Histogram_Severity_Errors]]", cut.Find(".histogram-legend").TextContent, StringComparison.Ordinal);
        Assert.Contains("Alpha", cut.Find(".histogram-legend").TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("[[Alpha]]", cut.Markup, StringComparison.Ordinal);

        await cut.InvokeAsync(() => cut.Instance.OnHistogramDragSelected(0, 0.5, scope: false));
        cut.WaitForState(() => cut.FindAll("g[data-tip]").Count == 4, s_wait);

        string alphaTooltip = cut.FindAll("g[data-tip]")[1].GetAttribute("data-tip")!;
        Assert.Equal(
            "[[Histogram_BarTooltip_Breakdown(2|[[Histogram_EventNoun_Events_Many(2)]]|01:00:00|01:59:59|[[Histogram_BreakdownItem(2|Alpha)]])]]",
            alphaTooltip);

        cut.Find(".histogram-scroll").KeyDown(new KeyboardEventArgs { Key = "ArrowRight", ShiftKey = true });
        cut.Find(".histogram-scroll").KeyDown(new KeyboardEventArgs { Key = "ArrowRight", ShiftKey = true });

        string binStart = s_start.AddHours(1).ToString();
        string binEnd = s_start.AddHours(2).AddTicks(-1).ToString();
        Assert.Equal(
            $"[[Histogram_BinCursor_Breakdown({binStart}|{binEnd}|2|[[Histogram_EventNoun_Events_Many(2)]]|[[Histogram_BreakdownItem(2|Alpha)]])]]",
            cut.FindAll(".histogram-status")[1].TextContent);

        string windowBreakdown =
            "[[Histogram_BreakdownItem(6|Alpha)]]" +
            "[[Histogram_Breakdown_Separator]]" +
            "[[Histogram_BreakdownItem(4|[[Histogram_Severity_Errors]])]]";
        string windowStart = s_start.ToString();
        string windowEnd = s_start.AddHours(4).AddTicks(-1).ToString();
        cut.WaitForAssertion(
            () => Assert.Equal(
                $"[[Histogram_WindowAnnouncement_Breakdown(10|[[Histogram_EventNoun_Events_Many(10)]]|{windowStart}|{windowEnd}|{windowBreakdown})]]",
                cut.FindAll(".histogram-status")[0].TextContent),
            s_wait);
    }

    private static HistogramData MixedGroupHistogramData()
    {
        IReadOnlyList<HistogramGroup> groups =
        [
            new(new HistogramGroupLabel.SeverityBucket(HistogramSeverityBucket.Errors), "histogram-bar-error", "errors", [0]),
            new(new HistogramGroupLabel.DataValue("Alpha"), "histogram-cat-0", "alpha", [1])
        ];

        return new(
            [1, 0, 0, 2, 3, 0, 0, 4, 5, 0, 0, 6],
            2,
            6,
            s_start,
            s_start.AddHours(6),
            21,
            TimeSpan.FromHours(1).Ticks,
            groups);
    }

    private sealed class QueuedHistogramDataScheduler : ICpuWorkScheduler
    {
        public HistogramData? NextHistogramData { get; set; }

        public Task<T> RunAsync<T>(
            Func<CancellationToken, T> work,
            CpuWorkPriority priority,
            CancellationToken cancellationToken = default)
        {
            if (NextHistogramData is { } data)
            {
                NextHistogramData = null;
                return Task.FromResult((T)(object)data);
            }

            return Task.FromResult(work(cancellationToken));
        }
    }
}
