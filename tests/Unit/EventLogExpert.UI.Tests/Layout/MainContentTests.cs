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
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using DetailsPaneComponent = EventLogExpert.UI.DetailsPane.DetailsPane;
using FilterPaneComponent = EventLogExpert.UI.FilterPane.FilterPane;

namespace EventLogExpert.UI.Tests.Layout;

public sealed class MainContentTests : BunitContext
{
    private readonly IHistogramVisibilitySource _histogramVisibility = Substitute.For<IHistogramVisibilitySource>();
    private readonly IOpenLogsPresenceSource _openLogs = Substitute.For<IOpenLogsPresenceSource>();

    public MainContentTests()
    {
        Services.AddSingleton(_openLogs);
        Services.AddSingleton(_histogramVisibility);

        ComponentFactories.AddStub<FilterPaneComponent>();
        ComponentFactories.AddStub<LensBreadcrumb>();
        ComponentFactories.AddStub<HistogramPane>();
        ComponentFactories.AddStub<LogTablePane>();
        ComponentFactories.AddStub<DetailsPaneComponent>();
        ComponentFactories.AddStub<EmptyStateDashboard>();
    }

    [Fact]
    public void HistogramVisibilityChangeAfterRender_TogglesTheHistogramPane_ThroughTheSource()
    {
        _openLogs.HasOpenLogs.Returns(true);
        _histogramVisibility.IsVisible.Returns(false);
        var cut = Render<MainContent>();
        Assert.Empty(cut.FindComponents<Stub<HistogramPane>>());

        _histogramVisibility.IsVisible.Returns(true);
        _histogramVisibility.Changed += Raise.Event<Action>();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindComponents<Stub<HistogramPane>>()));
    }

    [Fact]
    public void OpenLogsChangeAfterRender_SwapsTheDashboardForThePanes_ThroughTheSource()
    {
        _openLogs.HasOpenLogs.Returns(false);
        var cut = Render<MainContent>();
        Assert.NotEmpty(cut.FindComponents<Stub<EmptyStateDashboard>>());

        _openLogs.HasOpenLogs.Returns(true);
        _openLogs.Changed += Raise.Event<Action>();

        cut.WaitForAssertion(() =>
        {
            Assert.NotEmpty(cut.FindComponents<Stub<FilterPaneComponent>>());
            Assert.Empty(cut.FindComponents<Stub<EmptyStateDashboard>>());
        });
    }

    [Fact]
    public void Render_WhenLogsActiveAndTimelineHidden_DoesNotRenderHistogramPane()
    {
        _openLogs.HasOpenLogs.Returns(true);
        _histogramVisibility.IsVisible.Returns(false);

        var cut = Render<MainContent>();

        Assert.Empty(cut.FindComponents<Stub<HistogramPane>>());
    }

    [Fact]
    public void Render_WhenLogsActiveAndTimelineVisible_RendersHistogramPane()
    {
        _openLogs.HasOpenLogs.Returns(true);
        _histogramVisibility.IsVisible.Returns(true);

        var cut = Render<MainContent>();

        Assert.NotEmpty(cut.FindComponents<Stub<HistogramPane>>());
    }

    [Fact]
    public void Render_WhenLogsActive_RendersPanesNotDashboard()
    {
        _openLogs.HasOpenLogs.Returns(true);

        var cut = Render<MainContent>();

        Assert.NotEmpty(cut.FindComponents<Stub<FilterPaneComponent>>());
        Assert.Empty(cut.FindComponents<Stub<EmptyStateDashboard>>());
    }

    [Fact]
    public void Render_WhenNoActiveLogs_RendersDashboardNotPanes()
    {
        _openLogs.HasOpenLogs.Returns(false);

        var cut = Render<MainContent>();

        Assert.NotEmpty(cut.FindComponents<Stub<EmptyStateDashboard>>());
        Assert.Empty(cut.FindComponents<Stub<FilterPaneComponent>>());
    }
}
