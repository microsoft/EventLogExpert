// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using Bunit.TestDoubles;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.Histogram;
using EventLogExpert.UI.Dashboard;
using EventLogExpert.UI.FilterLenses;
using EventLogExpert.UI.Layout;
using EventLogExpert.UI.LogTable;
using EventLogExpert.UI.LogTable.Histogram;
using Fluxor;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using DetailsPaneComponent = EventLogExpert.UI.DetailsPane.DetailsPane;
using FilterPaneComponent = EventLogExpert.UI.FilterPane.FilterPane;

namespace EventLogExpert.UI.Tests.Layout;

public sealed class MainContentTests : BunitContext
{
    private readonly IStateSelection<EventLogState, bool> _hasActiveLogs =
        Substitute.For<IStateSelection<EventLogState, bool>>();

    private readonly IStateSelection<HistogramState, bool> _histogramVisible =
        Substitute.For<IStateSelection<HistogramState, bool>>();

    public MainContentTests()
    {
        Services.AddSingleton(_hasActiveLogs);
        Services.AddSingleton(_histogramVisible);
        Services.AddFluxor(options => options.ScanAssemblies(typeof(MainContent).Assembly));
        JSInterop.Mode = JSRuntimeMode.Loose;

        ComponentFactories.AddStub<FilterPaneComponent>();
        ComponentFactories.AddStub<LensBreadcrumb>();
        ComponentFactories.AddStub<HistogramPane>();
        ComponentFactories.AddStub<LogTablePane>();
        ComponentFactories.AddStub<DetailsPaneComponent>();
        ComponentFactories.AddStub<EmptyStateDashboard>();
    }

    [Fact]
    public void Render_WhenLogsActive_RendersPanesNotDashboard()
    {
        _hasActiveLogs.Value.Returns(true);

        var cut = Render<MainContent>();

        Assert.NotEmpty(cut.FindComponents<Stub<FilterPaneComponent>>());
        Assert.Empty(cut.FindComponents<Stub<EmptyStateDashboard>>());
    }

    [Fact]
    public void Render_WhenLogsActiveAndTimelineHidden_DoesNotRenderHistogramPane()
    {
        _hasActiveLogs.Value.Returns(true);
        _histogramVisible.Value.Returns(false);

        var cut = Render<MainContent>();

        Assert.Empty(cut.FindComponents<Stub<HistogramPane>>());
    }

    [Fact]
    public void Render_WhenLogsActiveAndTimelineVisible_RendersHistogramPane()
    {
        _hasActiveLogs.Value.Returns(true);
        _histogramVisible.Value.Returns(true);

        var cut = Render<MainContent>();

        Assert.NotEmpty(cut.FindComponents<Stub<HistogramPane>>());
    }

    [Fact]
    public void Render_WhenNoActiveLogs_RendersDashboardNotPanes()
    {
        _hasActiveLogs.Value.Returns(false);

        var cut = Render<MainContent>();

        Assert.NotEmpty(cut.FindComponents<Stub<EmptyStateDashboard>>());
        Assert.Empty(cut.FindComponents<Stub<FilterPaneComponent>>());
    }
}
