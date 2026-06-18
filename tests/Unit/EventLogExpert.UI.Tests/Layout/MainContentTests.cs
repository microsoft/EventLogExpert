// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using Bunit.TestDoubles;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.UI.Dashboard;
using EventLogExpert.UI.Layout;
using EventLogExpert.UI.LogTable;
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

    public MainContentTests()
    {
        Services.AddSingleton(_hasActiveLogs);
        Services.AddFluxor(options => options.ScanAssemblies(typeof(MainContent).Assembly));
        JSInterop.Mode = JSRuntimeMode.Loose;

        ComponentFactories.AddStub<FilterPaneComponent>();
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
    public void Render_WhenNoActiveLogs_RendersDashboardNotPanes()
    {
        _hasActiveLogs.Value.Returns(false);

        var cut = Render<MainContent>();

        Assert.NotEmpty(cut.FindComponents<Stub<EmptyStateDashboard>>());
        Assert.Empty(cut.FindComponents<Stub<FilterPaneComponent>>());
    }
}
