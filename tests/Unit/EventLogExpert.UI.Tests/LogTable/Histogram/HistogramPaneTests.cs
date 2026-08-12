// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.FilterLenses;
using EventLogExpert.Runtime.FilterPane;
using EventLogExpert.Runtime.Histogram;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.LogTable.OrderedView;
using EventLogExpert.Runtime.Settings;
using EventLogExpert.UI.Inputs;
using EventLogExpert.UI.LogTable.Find;
using EventLogExpert.UI.LogTable.Histogram;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using System.Collections.Immutable;
using System.Reflection;
using TestContext = Xunit.TestContext;

namespace EventLogExpert.UI.Tests.LogTable.Histogram;

public sealed class HistogramPaneTests : BunitContext
{
    private static readonly TimeSpan s_settleWindow = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan s_testTimeout = TimeSpan.FromSeconds(10);

    private static readonly EventLogId s_tokenLog = EventLogId.Create();
    private readonly IActiveEventLogSource _activeEventLog = Substitute.For<IActiveEventLogSource>();
    private readonly IHistogramDimensionRequestSource _dimensionRequest =
        Substitute.For<IHistogramDimensionRequestSource>();
    private readonly IEventFocusSource _eventFocus = Substitute.For<IEventFocusSource>();
    private readonly IFilterLensCommands _filterLensCommands = Substitute.For<IFilterLensCommands>();
    private readonly IActiveFiltersSource _filters = Substitute.For<IActiveFiltersSource>();
    private readonly IFindMarkerSource _findMarkers = new FindMarkerSource();
    private readonly IHighlightSelector _highlightSelector = Substitute.For<IHighlightSelector>();

    private readonly IEventColumnView _scanView = Substitute.For<IEventColumnView>();
    private readonly ISettingsService _settings = Substitute.For<ISettingsService>();
    private readonly EventLogId _tabId = EventLogId.Create();
    private readonly ITraceLogger _traceLogger = Substitute.For<ITraceLogger>();
    private readonly IOrderedViewSource _viewSource = Substitute.For<IOrderedViewSource>();

    private HistogramDimensionRequest? _dimensionRequestValue;
    private ImmutableList<SavedFilter> _filtersValue = ImmutableList<SavedFilter>.Empty;

    private OrderedViewPresentation _presentation;

    public HistogramPaneTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.SetupModule("./_content/EventLogExpert.UI/Inputs/ValueSelect.razor.js");
        JSInterop.SetupModule("./_content/EventLogExpert.UI/LogTable/Histogram/HistogramPane.razor.js");

        _activeEventLog.Current.Returns((EventLogId?)null);
        _presentation = new OrderedViewPresentation(_scanView, _tabId, default, PresentationState.Current, 1)
        {
            ContentToken = Token(1)
        };
        _viewSource.Current.Returns(_ => _presentation);
        _dimensionRequest.Current.Returns(_ => _dimensionRequestValue);
        _filters.Current.Returns(_ => _filtersValue);
        _eventFocus.Current.Returns((SelectionEntry?)null);
        _highlightSelector.Select(Arg.Any<ImmutableList<SavedFilter>>()).Returns([]);
        _highlightSelector.ComputePredicatePlanKey(Arg.Any<ImmutableList<SavedFilter>>()).Returns(0);
        _settings.TimeZoneInfo.Returns(TimeZoneInfo.Utc);

        Services.AddSingleton(_activeEventLog);
        Services.AddSingleton(_viewSource);
        Services.AddSingleton(_dimensionRequest);
        Services.AddSingleton(_eventFocus);
        Services.AddSingleton(_filterLensCommands);
        Services.AddSingleton(_filters);
        Services.AddSingleton(_findMarkers);
        Services.AddSingleton(_highlightSelector);
        Services.AddSingleton(_settings);
        Services.AddSingleton(_traceLogger);
    }

    public static TheoryData<uint, SavedFilter[], string?, string> GroupHighlightCases()

    {
        var lightRed = Filter(HighlightColor.LightRed);
        var anotherLightRed = Filter(HighlightColor.LightRed);
        var lightBlue = Filter(HighlightColor.LightBlue);
        var none = Filter(HighlightColor.None);

        return new TheoryData<uint, SavedFilter[], string?, string>
        {
            { (1u << 1) | (1u << 2), [lightRed, anotherLightRed], "lightred", "Light red highlight" },
            { (1u << 1) | (1u << 2), [lightRed, lightBlue], null, "Mixed highlights" },
            { 1u | (1u << 1), [lightRed], null, "Mixed highlights" },
            { (1u << 1) | (1u << 2), [none, lightRed], null, "Mixed highlights" },
            { 0u, [lightRed], null, "Uncolored" },
            { 1u, [lightRed], null, "Uncolored" },
            { 1u << 1, [none], null, "Uncolored" },
            { 1u << 3, [lightRed], null, "Mixed highlights" }
        };
    }

    [Fact]
    public void AChartThatHasNotBeenBuiltYet_SaysSo_RatherThanClaimingThereAreNoEvents()
    {
        var cut = Render<HistogramPane>();

        Assert.Contains("Building the timeline", cut.Markup);
        Assert.DoesNotContain("No events to chart", cut.Markup);
    }

    [Fact]
    public async Task AFailureAfterAChartHasDrawn_LeavesNothingStaleToActOn()
    {
        var cut = Render<HistogramPane>();
        await cut.InvokeAsync(() => cut.Instance.OnHistogramResized(500, 100));

        cut.WaitForAssertion(() => _scanView.Received().TryGetTimeTicksRange(
            out Arg.Any<long>(), out Arg.Any<long>(), Arg.Any<CancellationToken>()));

        long start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks;
        long end = start + TimeSpan.FromMinutes(1).Ticks;

        await cut.InvokeAsync(() => SetPrivateField(
            cut,
            "_render",
            new HistogramRender([new HistogramRenderBin(start, end, 2, [2])], start, end, 2, 2, [2])));

        var rescanView = Substitute.For<IEventColumnView>();

        rescanView
            .When(view => view.TryGetTimeTicksRange(out Arg.Any<long>(), out Arg.Any<long>(), Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException("the rescan blew up"));

        _presentation = new OrderedViewPresentation(rescanView, _tabId, default, PresentationState.Current, 2)
        {
            ContentToken = Token(2)
        };
        await cut.InvokeAsync(() => _viewSource.Updated += Raise.Event<Action<OrderedViewPresentation>>(_presentation));

        cut.WaitForAssertion(() => Assert.Contains("could not be built", cut.Markup));

        await cut.InvokeAsync(() => cut.Instance.OnHistogramScopeBin(0.5));

        _filterLensCommands.DidNotReceive().ShowTimeRange(
            Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<TimeZoneInfo>(), Arg.Any<string?>());
    }

    [Fact]
    public async Task AFirstSameViewPublication_DoesNotScheduleARescan()
    {
        var cut = Render<HistogramPane>();
        await cut.InvokeAsync(() => cut.Instance.OnHistogramResized(500, 100));

        int rendersBeforeTheRepublish = cut.RenderCount;

        _presentation = _presentation with { Revision = 2 };
        await cut.InvokeAsync(() => _viewSource.Updated += Raise.Event<Action<OrderedViewPresentation>>(_presentation));

        cut.WaitForState(() => cut.RenderCount > rendersBeforeTheRepublish);

        Assert.False(GetPrivateField<bool>(cut, "_recomputePending"));
    }

    [Fact]
    public async Task AScanThatFindsNothing_ReportsEmptinessAsTheAnswer()
    {
        var cut = Render<HistogramPane>();
        await cut.InvokeAsync(() => cut.Instance.OnHistogramResized(500, 100));

        cut.WaitForAssertion(() => Assert.Contains("No events to chart", cut.Markup));

        Assert.DoesNotContain("Building the timeline", cut.Markup);
    }

    [Fact]
    public async Task AScanThatThrows_SaysTheTimelineFailed_RatherThanClaimingThereAreNoEvents()
    {
        _scanView
            .When(view => view.TryGetTimeTicksRange(out Arg.Any<long>(), out Arg.Any<long>(), Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException("bucketing blew up"));

        var cut = Render<HistogramPane>();
        await cut.InvokeAsync(() => cut.Instance.OnHistogramResized(500, 100));

        cut.WaitForAssertion(() => Assert.Contains("could not be built", cut.Markup));

        Assert.DoesNotContain("No events to chart", cut.Markup);
    }

    [Fact]
    public async Task ActiveLogChange_DropsZoomAndRescansForTheNewTab()
    {
        var cut = Render<HistogramPane>();
        await cut.InvokeAsync(() => cut.Instance.OnHistogramResized(500, 100));

        cut.WaitForAssertion(() => _scanView.Received().TryGetTimeTicksRange(
            out Arg.Any<long>(), out Arg.Any<long>(), Arg.Any<CancellationToken>()));
        SetPrivateField(cut, "_isZoomed", true);
        _scanView.ClearReceivedCalls();

        _activeEventLog.Current.Returns(EventLogId.Create());
        await cut.InvokeAsync(() => _activeEventLog.Changed += Raise.Event<Action>());

        cut.WaitForAssertion(() => Assert.False(GetPrivateField<bool>(cut, "_isZoomed")));
        cut.WaitForAssertion(
            () => _scanView.Received().TryGetTimeTicksRange(
                out Arg.Any<long>(), out Arg.Any<long>(), Arg.Any<CancellationToken>()),
            s_testTimeout);
    }

    [Fact]
    public async Task FilterRefresh_WhenColorOnlyEditWithNoActiveCursor_ClearsStaleBinAnnouncement()
    {
        SavedFilter red = Filter(HighlightColor.LightRed);
        SavedFilter blue = Filter(HighlightColor.LightBlue);
        _highlightSelector.Select(Arg.Any<ImmutableList<SavedFilter>>()).Returns([blue]);
        _highlightSelector.ComputePredicatePlanKey(Arg.Any<ImmutableList<SavedFilter>>()).Returns(7);
        var cut = Render<HistogramPane>();

        SetPrivateField(cut, "_tieHighlightFilters", new[] { red });
        SetPrivateField(cut, "_binCursor", null);
        SetPrivateField(cut, "_binAnnouncement", "2 events (1 Alpha, Light red highlight)");
        await cut.InvokeAsync(() => { });
        Assert.Contains("Light red", GetPrivateField<string>(cut, "_binAnnouncement"));

        _filters.Changed += Raise.Event<Action>();
        await cut.InvokeAsync(() => { });

        Assert.Equal(string.Empty, GetPrivateField<string>(cut, "_binAnnouncement"));
    }

    [Fact]
    public async Task FilterRefresh_WhenDisarmedWithNoActiveCursor_ClearsStaleBinAnnouncement()
    {
        SavedFilter red = Filter(HighlightColor.LightRed);
        _highlightSelector.Select(Arg.Any<ImmutableList<SavedFilter>>()).Returns([]);
        _highlightSelector.ComputePredicatePlanKey(Arg.Any<ImmutableList<SavedFilter>>()).Returns(7);
        var cut = Render<HistogramPane>();

        SetPrivateField(cut, "_tieHighlightFilters", new[] { red });
        SetPrivateField(cut, "_binCursor", null);
        SetPrivateField(cut, "_binAnnouncement", "2 events (1 Alpha, Light red highlight)");
        await cut.InvokeAsync(() => { });
        Assert.Contains("highlight", GetPrivateField<string>(cut, "_binAnnouncement"));

        _filters.Changed += Raise.Event<Action>();
        await cut.InvokeAsync(() => { });

        Assert.Equal(string.Empty, GetPrivateField<string>(cut, "_binAnnouncement"));
    }

    [Fact]
    public async Task FilterRefresh_WhenDisarmed_ClearsGroupHighlightMasksSynchronously()
    {
        SavedFilter red = Filter(HighlightColor.LightRed);
        _highlightSelector.Select(Arg.Any<ImmutableList<SavedFilter>>()).Returns([]);
        _highlightSelector.ComputePredicatePlanKey(Arg.Any<ImmutableList<SavedFilter>>()).Returns(7);
        var cut = Render<HistogramPane>();

        SetPrivateField(cut, "_tieHighlightFilters", new[] { red });
        await PublishBaseDataAsync(cut, ArmedCategoryData());

        cut.WaitForAssertion(() => Assert.Equal("histogram-cat-hl", AlphaSwatchClass(cut)));

        _filters.Changed += Raise.Event<Action>();
        await cut.InvokeAsync(() => { });

        cut.WaitForAssertion(() => Assert.NotEqual("histogram-cat-hl", AlphaSwatchClass(cut)));
        Assert.Null(GetPrivateField<HistogramData>(cut, "_baseData").GroupHighlightMasks);
    }

    [Fact]
    public async Task FilterRefresh_WhenOnlyHighlightColorChanges_DoesNotRescan()
    {
        SavedFilter red = Filter(HighlightColor.LightRed);
        SavedFilter blue = red with { Color = HighlightColor.LightBlue };
        _highlightSelector.Select(Arg.Any<ImmutableList<SavedFilter>>()).Returns([red], [blue]);
        _highlightSelector.ComputePredicatePlanKey(Arg.Any<ImmutableList<SavedFilter>>()).Returns(7);
        var cut = Render<HistogramPane>();
        await cut.InvokeAsync(() => cut.Instance.OnHistogramResized(500, 100));

        cut.WaitForAssertion(() => _scanView.Received().TryGetTimeTicksRange(out Arg.Any<long>(), out Arg.Any<long>(), Arg.Any<CancellationToken>()));
        _scanView.ClearReceivedCalls();

        _filters.Changed += Raise.Event<Action>();
        await cut.InvokeAsync(() => { });

        _scanView.DidNotReceive().TryGetTimeTicksRange(out Arg.Any<long>(), out Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FilterRefresh_WhenPredicatePlanChangesButTieStaysUnarmed_DoesNotRescan()
    {
        SavedFilter firstUncoloured = Filter(HighlightColor.None);
        SavedFilter secondUncoloured = Filter(HighlightColor.None);
        _highlightSelector.Select(Arg.Any<ImmutableList<SavedFilter>>()).Returns([firstUncoloured], [secondUncoloured]);
        _highlightSelector.ComputePredicatePlanKey(Arg.Any<ImmutableList<SavedFilter>>()).Returns(7, 8);
        var cut = Render<HistogramPane>();
        await cut.InvokeAsync(() => cut.Instance.OnHistogramResized(500, 100));

        cut.WaitForAssertion(() => _scanView.Received().TryGetTimeTicksRange(out Arg.Any<long>(), out Arg.Any<long>(), Arg.Any<CancellationToken>()));
        _scanView.ClearReceivedCalls();

        _filters.Changed += Raise.Event<Action>();
        await cut.InvokeAsync(() => { });

        _scanView.DidNotReceive().TryGetTimeTicksRange(out Arg.Any<long>(), out Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FilterRefresh_WhenPredicatePlanChanges_Rescans()
    {
        SavedFilter red = Filter(HighlightColor.LightRed);
        SavedFilter blue = Filter(HighlightColor.LightBlue);
        _highlightSelector.Select(Arg.Any<ImmutableList<SavedFilter>>()).Returns([red], [blue]);
        _highlightSelector.ComputePredicatePlanKey(Arg.Any<ImmutableList<SavedFilter>>()).Returns(7, 8);
        var cut = Render<HistogramPane>();
        await cut.InvokeAsync(() => cut.Instance.OnHistogramResized(500, 100));

        cut.WaitForAssertion(() => _scanView.Received().TryGetTimeTicksRange(out Arg.Any<long>(), out Arg.Any<long>(), Arg.Any<CancellationToken>()));
        _scanView.ClearReceivedCalls();

        _filters.Changed += Raise.Event<Action>();

        cut.WaitForAssertion(() => _scanView.Received().TryGetTimeTicksRange(out Arg.Any<long>(), out Arg.Any<long>(), Arg.Any<CancellationToken>()));
    }

    [Fact]
    public async Task FocusChange_ResolvesTheFocusedTickFromTheView()
    {
        var handle = new EventLocator(_tabId, 0, 0);
        long focusedTicks = new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc).Ticks;
        _scanView.Rank(handle).Returns(0);
        _scanView.TryGetTimeTicks(handle, out Arg.Any<long>()).Returns(call => { call[1] = focusedTicks; return true; });
        var cut = Render<HistogramPane>();

        var focusedTicksField = typeof(HistogramPane).GetField(
            "_focusedTicks", BindingFlags.Instance | BindingFlags.NonPublic)!;

        Assert.Null(focusedTicksField.GetValue(cut.Instance));

        _eventFocus.Current.Returns(new SelectionEntry(handle, handle, null));
        await cut.InvokeAsync(() => _eventFocus.Changed += Raise.Event<Action>());

        cut.WaitForAssertion(() => Assert.Equal(focusedTicks, (long?)focusedTicksField.GetValue(cut.Instance)));
    }

    [Fact]
    public async Task OrderingOnlyPublication_NewViewSameContent_DoesNotRescan()
    {
        var cut = Render<HistogramPane>();
        await cut.InvokeAsync(() => cut.Instance.OnHistogramResized(500, 100));
        cut.WaitForAssertion(() => _scanView.Received().TryGetTimeTicksRange(
            out Arg.Any<long>(), out Arg.Any<long>(), Arg.Any<CancellationToken>()));

        int rendersBefore = cut.RenderCount;

        var reordered = Substitute.For<IEventColumnView>();
        var next = new OrderedViewPresentation(reordered, _tabId, default, PresentationState.Current, 2)
        {
            ContentToken = Token(1)
        };
        _presentation = next;
        await cut.InvokeAsync(() => _viewSource.Updated += Raise.Event<Action<OrderedViewPresentation>>(next));

        cut.WaitForState(() => cut.RenderCount > rendersBefore);

        Assert.False(GetPrivateField<bool>(cut, "_recomputePending"));
        reordered.DidNotReceive().TryGetTimeTicksRange(
            out Arg.Any<long>(), out Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Publication_ForTheSameTab_RescansAgainstTheNewView()
    {
        var cut = Render<HistogramPane>();
        await cut.InvokeAsync(() => cut.Instance.OnHistogramResized(500, 100));

        cut.WaitForAssertion(() => _scanView.Received().TryGetTimeTicksRange(
            out Arg.Any<long>(), out Arg.Any<long>(), Arg.Any<CancellationToken>()));

        var appended = Substitute.For<IEventColumnView>();
        var next = new OrderedViewPresentation(appended, _tabId, default, PresentationState.Current, 2)
        {
            ContentToken = Token(2)
        };

        _presentation = next;
        await cut.InvokeAsync(() => _viewSource.Updated += Raise.Event<Action<OrderedViewPresentation>>(next));

        cut.WaitForAssertion(
            () => appended.Received().TryGetTimeTicksRange(
                out Arg.Any<long>(), out Arg.Any<long>(), Arg.Any<CancellationToken>()),
            s_testTimeout);
    }

    [Fact]
    public void Render_WhenDimensionRequestExists_AppliesRequestedDimensionOnMount()
    {
        _dimensionRequestValue = new HistogramDimensionRequest(HistogramDimension.Log, 1);

        var cut = Render<HistogramPane>();

        Assert.Equal("Log", cut.Find(".histogram-dimension-select").GetAttribute("value"));
        Assert.Equal(HistogramDimension.Log, GetDimension(cut));
    }

    [Fact]
    public void Render_WhenLaterDimensionRequestHasHigherToken_AppliesRequestedDimension()
    {
        _dimensionRequestValue = new HistogramDimensionRequest(HistogramDimension.Log, 1);
        var cut = Render<HistogramPane>();

        _dimensionRequestValue = new HistogramDimensionRequest(HistogramDimension.EventId, 2);
        _dimensionRequest.Changed += Raise.Event<Action>();

        cut.WaitForAssertion(() => Assert.Equal(HistogramDimension.EventId, GetDimension(cut)));
    }

    [Fact]
    public async Task Render_WhenLegendHasLongLabel_WrapsVisibleValueInTitledSpan()
    {
        const string longLabel = "Microsoft-Windows-Servicing-CbsPackageChangeState";
        IReadOnlyList<HistogramGroup> groups = HistogramGroups.ForCategories([longLabel], [longLabel], otherLabel: null);
        var data = new HistogramData(
            [1, 0],
            2,
            1,
            new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2024, 1, 1, 1, 0, 0, DateTimeKind.Utc),
            1,
            TimeSpan.FromHours(1).Ticks,
            groups);
        var cut = Render<HistogramPane>();

        await PublishBaseDataAsync(cut, data);

        var label = cut.Find(".histogram-legend-label");
        Assert.Equal(longLabel, label.TextContent);
        Assert.Equal(longLabel, label.GetAttribute("title"));
        Assert.Contains($"Hide {longLabel}", cut.Find(".histogram-legend-item").GetAttribute("aria-label"));
    }

    [Fact]
    public async Task Render_WhenStaleDimensionRequestArrivesAfterManualChange_DoesNotOverrideManualDimension()
    {
        _dimensionRequestValue = new HistogramDimensionRequest(HistogramDimension.EventId, 3);
        var cut = Render<HistogramPane>();
        await SelectDimensionAsync(cut, HistogramDimension.Source);

        _dimensionRequest.Changed += Raise.Event<Action>();

        cut.WaitForAssertion(() => Assert.Equal(HistogramDimension.Source, GetDimension(cut)));
    }

    [Fact]
    public async Task Render_WhenTieModeIncludesCategoricalOther_RendersOtherAsRecessiveGray()
    {
        IReadOnlyList<HistogramGroup> groups = HistogramGroups.ForCategories(["alpha"], ["Alpha"], "Other");
        var data = new HistogramData(
            [1, 1],
            2,
            1,
            new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2024, 1, 1, 1, 0, 0, DateTimeKind.Utc),
            2,
            TimeSpan.FromHours(1).Ticks,
            groups,
            [1u << 1, 1u << 1]);
        var cut = Render<HistogramPane>();

        await PublishBaseDataAsync(cut, data);

        var otherButton = cut.FindAll(".histogram-legend-item").Single(button => button.TextContent == "Other");
        var otherSwatch = otherButton.QuerySelector("rect");
        Assert.Equal("Hide Other", otherButton.GetAttribute("aria-label"));
        Assert.Equal("histogram-cat-other", otherSwatch?.GetAttribute("class"));
        Assert.Null(otherSwatch?.GetAttribute("data-highlight"));
    }

    [Theory]
    [MemberData(nameof(GroupHighlightCases))]
    public void ResolveGroupHighlight_MapsMasksToCssAndText(
        uint mask,
        SavedFilter[] filters,
        string? expectedCssName,
        string expectedDescription)
    {
        (string? cssName, string description) = HistogramPane.ResolveGroupHighlight(mask, filters);

        Assert.Equal(expectedCssName, cssName);
        Assert.Equal(expectedDescription, description);
    }

    [Fact]
    public async Task ScanThatFinishesAfterItsTabIsLeft_DoesNotPublish()
    {
        // because the switch only schedules a THROTTLED rescan, so no supersede has happened yet when this lands.
        //
        // The tab moves from inside the scan's own first view read, so no thread is parked waiting for it. Completion
        // is awaited through a signal rather than WaitForAssertion: a rejected scan renders nothing, and
        // WaitForAssertion only re-evaluates on render, so it would wait out its whole timeout no matter what happened.
        var scanRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        _scanView.TryGetTimeTicksRange(out Arg.Any<long>(), out Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                _presentation = new OrderedViewPresentation(
                    _scanView, EventLogId.Create(), default, PresentationState.Current, 2);

                call[0] = 0L;
                call[1] = TimeSpan.TicksPerHour;
                scanRead.TrySetResult();

                return true;
            });

        var cut = Render<HistogramPane>();

        var seeded = ArmedCategoryData();

        await PublishBaseDataAsync(cut, seeded);
        await cut.InvokeAsync(() => cut.Instance.OnHistogramResized(500, 100));

        await scanRead.Task.WaitAsync(s_testTimeout, TestContext.Current.CancellationToken);

        await Task.Delay(s_settleWindow, TestContext.Current.CancellationToken);
        await cut.InvokeAsync(() => { });

        Assert.Same(seeded, GetPrivateFieldOrNull<HistogramData>(cut, "_baseData"));
    }

    [Fact]
    public void ShouldArmTie_WhenMoreThanThirtyOneFilters_ReturnsFalse()
    {
        SavedFilter[] filters = Enumerable.Range(0, 32)
            .Select(_ => Filter(HighlightColor.LightRed))
            .ToArray();
        var method = typeof(HistogramPane).GetMethod(
            "ShouldArmTie",
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(method);
        bool shouldArmTie = Assert.IsType<bool>(method.Invoke(null, [filters]));

        Assert.False(shouldArmTie);
    }

    [Fact]
    public void SourceFiles_DefineExtendedHistogramPaletteAndForcedColorHatches()
    {
        string razor = File.ReadAllText(ResolveRepoPath("src", "EventLogExpert.UI", "LogTable", "Histogram", "HistogramPane.razor"));
        string css = File.ReadAllText(ResolveRepoPath("src", "EventLogExpert.UI", "LogTable", "Histogram", "HistogramPane.razor.css"));

        for (int index = 4; index <= 7; index++)
        {
            Assert.Contains($".histogram-cat-{index}", css);
        }

        for (int index = 0; index <= 12; index++)
        {
            Assert.Contains($"id=\"histogram-hatch-cat-{index}\"", razor);
            Assert.Contains($"data-stackpos=\"{index}\"", css);
        }
    }

    [Fact]
    public async Task StartScan_WhenViewportZero_BumpsScanVersionToSupersedeQueuedPublish()
    {
        var cut = Render<HistogramPane>();
        int before = GetPrivateField<int>(cut, "_scanVersion");

        await cut.InvokeAsync(() => InvokePrivate(cut, "StartScan"));

        Assert.Equal(before + 1, GetPrivateField<int>(cut, "_scanVersion"));
    }

    [Fact]
    public async Task TabSwitchWithSimultaneousPublication_RescansAgainstTheNewView()
    {
        var cut = Render<HistogramPane>();
        await cut.InvokeAsync(() => cut.Instance.OnHistogramResized(500, 100));
        cut.WaitForAssertion(() => _scanView.Received().TryGetTimeTicksRange(
            out Arg.Any<long>(), out Arg.Any<long>(), Arg.Any<CancellationToken>()));

        SetPrivateField(cut, "_isZoomed", true);

        var newTab = EventLogId.Create();
        var newView = Substitute.For<IEventColumnView>();
        var newPresentation = new OrderedViewPresentation(newView, newTab, default, PresentationState.Current, 2)
        {
            ContentToken = Token(2)
        };
        _activeEventLog.Current.Returns(newTab);
        _presentation = newPresentation;

        await cut.InvokeAsync(() =>
        {
            _activeEventLog.Changed += Raise.Event<Action>();
            _viewSource.Updated += Raise.Event<Action<OrderedViewPresentation>>(newPresentation);
        });

        cut.WaitForAssertion(() => Assert.False(GetPrivateField<bool>(cut, "_isZoomed")));
        cut.WaitForAssertion(() => Assert.Equal(Token(2), ScannedToken(cut)));
        cut.WaitForAssertion(
            () => newView.Received().TryGetTimeTicksRange(
                out Arg.Any<long>(), out Arg.Any<long>(), Arg.Any<CancellationToken>()),
            s_testTimeout);
    }

    private static string? AlphaSwatchClass(IRenderedComponent<HistogramPane> cut) =>
        cut.FindAll(".histogram-legend-item")
            .Single(button => button.TextContent == "Alpha")
            .QuerySelector("rect")?
            .GetAttribute("class");

    private static HistogramData ArmedCategoryData() =>
        new(
            [1, 1],
            2,
            1,
            new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2024, 1, 1, 1, 0, 0, DateTimeKind.Utc),
            2,
            TimeSpan.FromHours(1).Ticks,
            HistogramGroups.ForCategories(["alpha"], ["Alpha"], "Other"),
            [1u << 1, 1u << 1]);

    private static SavedFilter Filter(HighlightColor color) =>
        new() { Color = color, IsEnabled = true, ComparisonText = "Id == 1", Compiled = null! };

    private static HistogramDimension GetDimension(IRenderedComponent<HistogramPane> cut)
    {
        var field = typeof(HistogramPane).GetField(
            "_dimension",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(field);
        return Assert.IsType<HistogramDimension>(field.GetValue(cut.Instance));
    }

    private static T GetPrivateField<T>(IRenderedComponent<HistogramPane> cut, string name)
    {
        var field = typeof(HistogramPane).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return Assert.IsType<T>(field.GetValue(cut.Instance));
    }

    private static T? GetPrivateFieldOrNull<T>(IRenderedComponent<HistogramPane> cut, string name)
        where T : class
    {
        var field = typeof(HistogramPane).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return field.GetValue(cut.Instance) as T;
    }

    private static void InvokePrivate(IRenderedComponent<HistogramPane> cut, string name)
    {
        var method = typeof(HistogramPane).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method.Invoke(cut.Instance, null);
    }

    private static Task PublishBaseDataAsync(IRenderedComponent<HistogramPane> cut, HistogramData data)
    {
        var field = typeof(HistogramPane).GetField(
            "_baseData",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var stateHasChanged = typeof(ComponentBase).GetMethod(
            "StateHasChanged",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(field);
        Assert.NotNull(stateHasChanged);
        field.SetValue(cut.Instance, data);
        return cut.InvokeAsync(() => stateHasChanged.Invoke(cut.Instance, null));
    }

    private static string ResolveRepoPath(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "EventLogExpert.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);

        string path = Path.Combine([directory.FullName, .. segments]);
        Assert.True(File.Exists(path), $"Expected source file at {path} to exist.");

        return path;
    }

    private static ViewContentToken? ScannedToken(IRenderedComponent<HistogramPane> cut) =>
        (ViewContentToken?)typeof(HistogramPane)
            .GetField("_lastScannedToken", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(cut.Instance);

    private static Task SelectDimensionAsync(IRenderedComponent<HistogramPane> cut, HistogramDimension dimension)
    {
        var select = cut.FindComponent<ValueSelect<HistogramDimension>>();

        return cut.InvokeAsync(() => select.Instance.UpdateValue(dimension));
    }

    private static void SetPrivateField(IRenderedComponent<HistogramPane> cut, string name, object? value)
    {
        var field = typeof(HistogramPane).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field.SetValue(cut.Instance, value);
    }

    private static ViewContentToken Token(long contentVersion) =>
        ViewContentToken.FromStamps(default, [new ViewContentTokenReaderStamp(s_tokenLog, 0, contentVersion, 1)], 1);
}
