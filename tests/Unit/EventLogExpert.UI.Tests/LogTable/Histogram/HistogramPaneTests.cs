// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.FilterLenses;
using EventLogExpert.Runtime.FilterPane;
using EventLogExpert.Runtime.Histogram;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.Settings;
using EventLogExpert.UI.Inputs;
using EventLogExpert.UI.LogTable.Find;
using EventLogExpert.UI.LogTable.Histogram;
using Fluxor;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using System.Collections.Immutable;
using System.Reflection;

namespace EventLogExpert.UI.Tests.LogTable.Histogram;

public sealed class HistogramPaneTests : BunitContext
{
    private readonly IStateSelection<LogTableState, EventLogId?> _activeEventLogId =
        Substitute.For<IStateSelection<LogTableState, EventLogId?>>();

    private readonly IStateSelection<LogTableState, string?> _activeOriginLog =
        Substitute.For<IStateSelection<LogTableState, string?>>();

    private readonly IStateSelection<LogTableState, IEventColumnView> _activeView =
        Substitute.For<IStateSelection<LogTableState, IEventColumnView>>();

    private readonly IStateSelection<HistogramState, HistogramDimensionRequest?> _dimensionRequest =
        Substitute.For<IStateSelection<HistogramState, HistogramDimensionRequest?>>();

    private readonly IFilterLensCommands _filterLensCommands = Substitute.For<IFilterLensCommands>();
    private readonly IStateSelection<FilterPaneState, ImmutableList<SavedFilter>> _filters =
        Substitute.For<IStateSelection<FilterPaneState, ImmutableList<SavedFilter>>>();
    private readonly IFindMarkerSource _findMarkers = new FindMarkerSource();
    private readonly IStateSelection<EventLogState, SelectionEntry?> _focus =
        Substitute.For<IStateSelection<EventLogState, SelectionEntry?>>();
    private readonly IHighlightSelector _highlightSelector = Substitute.For<IHighlightSelector>();

    private readonly ISettingsService _settings = Substitute.For<ISettingsService>();
    private readonly ITraceLogger _traceLogger = Substitute.For<ITraceLogger>();

    private HistogramDimensionRequest? _dimensionRequestValue;

    public HistogramPaneTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.SetupModule("./_content/EventLogExpert.UI/Inputs/ValueSelect.razor.js");
        JSInterop.SetupModule("./_content/EventLogExpert.UI/LogTable/Histogram/HistogramPane.razor.js");

        _activeEventLogId.Value.Returns((EventLogId?)null);
        _activeOriginLog.Value.Returns((string?)null);
        _activeView.Value.Returns(LogTableState.EmptyView);
        _dimensionRequest.Value.Returns(_ => _dimensionRequestValue);
        _filters.Value.Returns(ImmutableList<SavedFilter>.Empty);
        _focus.Value.Returns((SelectionEntry?)null);
        _highlightSelector.Select(Arg.Any<ImmutableList<SavedFilter>>()).Returns([]);
        _highlightSelector.ComputePredicatePlanKey(Arg.Any<ImmutableList<SavedFilter>>()).Returns(0);
        _settings.TimeZoneInfo.Returns(TimeZoneInfo.Utc);

        Services.AddSingleton(_activeEventLogId);
        Services.AddSingleton(_activeOriginLog);
        Services.AddSingleton(_activeView);
        Services.AddSingleton(_dimensionRequest);
        Services.AddSingleton(_filterLensCommands);
        Services.AddSingleton(_filters);
        Services.AddSingleton(_findMarkers);
        Services.AddSingleton(_focus);
        Services.AddSingleton(_highlightSelector);
        Services.AddSingleton(_settings);
        Services.AddSingleton(_traceLogger);
        Services.AddFluxor(options => options.ScanAssemblies(typeof(HistogramPane).Assembly));
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
    public async Task FilterRefresh_WhenOnlyHighlightColorChanges_DoesNotReadActiveViewForScan()
    {
        SavedFilter red = Filter(HighlightColor.LightRed);
        SavedFilter blue = red with { Color = HighlightColor.LightBlue };
        _highlightSelector.Select(Arg.Any<ImmutableList<SavedFilter>>()).Returns([red], [blue]);
        _highlightSelector.ComputePredicatePlanKey(Arg.Any<ImmutableList<SavedFilter>>()).Returns(7);
        var cut = Render<HistogramPane>();
        await cut.InvokeAsync(() => cut.Instance.OnHistogramResized(500, 100));
        _activeView.ClearReceivedCalls();

        _filters.SelectedValueChanged +=
            Raise.Event<EventHandler<ImmutableList<SavedFilter>>>(_filters, ImmutableList.Create(blue));

        _ = _activeView.DidNotReceive().Value;
    }

    [Fact]
    public async Task FilterRefresh_WhenPredicatePlanChanges_ReadsActiveViewForScan()
    {
        SavedFilter red = Filter(HighlightColor.LightRed);
        SavedFilter blue = Filter(HighlightColor.LightBlue);
        _highlightSelector.Select(Arg.Any<ImmutableList<SavedFilter>>()).Returns([red], [blue]);
        _highlightSelector.ComputePredicatePlanKey(Arg.Any<ImmutableList<SavedFilter>>()).Returns(7, 8);
        var cut = Render<HistogramPane>();
        await cut.InvokeAsync(() => cut.Instance.OnHistogramResized(500, 100));
        _activeView.ClearReceivedCalls();

        _filters.SelectedValueChanged +=
            Raise.Event<EventHandler<ImmutableList<SavedFilter>>>(_filters, ImmutableList.Create(blue));

        _ = _activeView.Received().Value;
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
        _dimensionRequest.SelectedValueChanged +=
            Raise.Event<EventHandler<HistogramDimensionRequest?>>(_dimensionRequest, _dimensionRequestValue);

        cut.WaitForAssertion(() => Assert.Equal(HistogramDimension.EventId, GetDimension(cut)));
    }

    [Fact]
    public async Task Render_WhenStaleDimensionRequestArrivesAfterManualChange_DoesNotOverrideManualDimension()
    {
        _dimensionRequestValue = new HistogramDimensionRequest(HistogramDimension.EventId, 3);
        var cut = Render<HistogramPane>();
        await SelectDimensionAsync(cut, HistogramDimension.Source);

        _dimensionRequest.SelectedValueChanged +=
            Raise.Event<EventHandler<HistogramDimensionRequest?>>(_dimensionRequest, _dimensionRequestValue);

        cut.WaitForAssertion(() => Assert.Equal(HistogramDimension.Source, GetDimension(cut)));
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

    private static Task SelectDimensionAsync(IRenderedComponent<HistogramPane> cut, HistogramDimension dimension)
    {
        var select = cut.FindComponent<ValueSelect<HistogramDimension>>();

        return cut.InvokeAsync(() => select.Instance.UpdateValue(dimension));
    }
}
