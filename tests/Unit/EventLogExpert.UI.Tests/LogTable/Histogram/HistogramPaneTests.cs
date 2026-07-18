// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.FilterLenses;
using EventLogExpert.Runtime.Histogram;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.Settings;
using EventLogExpert.UI.Inputs;
using EventLogExpert.UI.LogTable.Find;
using EventLogExpert.UI.LogTable.Histogram;
using Fluxor;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
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
    private readonly IFindMarkerSource _findMarkers = new FindMarkerSource();
    private readonly IStateSelection<EventLogState, SelectionEntry?> _focus =
        Substitute.For<IStateSelection<EventLogState, SelectionEntry?>>();

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
        _focus.Value.Returns((SelectionEntry?)null);
        _settings.TimeZoneInfo.Returns(TimeZoneInfo.Utc);

        Services.AddSingleton(_activeEventLogId);
        Services.AddSingleton(_activeOriginLog);
        Services.AddSingleton(_activeView);
        Services.AddSingleton(_dimensionRequest);
        Services.AddSingleton(_filterLensCommands);
        Services.AddSingleton(_findMarkers);
        Services.AddSingleton(_focus);
        Services.AddSingleton(_settings);
        Services.AddSingleton(_traceLogger);
        Services.AddFluxor(options => options.ScanAssemblies(typeof(HistogramPane).Assembly));
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
