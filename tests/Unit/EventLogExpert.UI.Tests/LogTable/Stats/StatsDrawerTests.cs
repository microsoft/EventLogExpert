// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using Bunit.TestDoubles;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.Stats;
using EventLogExpert.UI.LogTable.Stats;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace EventLogExpert.UI.Tests.LogTable.Stats;

public sealed class StatsDrawerTests : BunitContext
{
    private readonly IOpenLogsPresenceSource _openLogs = Substitute.For<IOpenLogsPresenceSource>();
    private readonly IStatsDrawerPreferencesProvider _preferences = Substitute.For<IStatsDrawerPreferencesProvider>();
    private readonly IStatsVisibilitySource _statsVisibility = Substitute.For<IStatsVisibilitySource>();

    public StatsDrawerTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddEventLogLocalization();
        JSInterop.SetupModule("./_content/EventLogExpert.UI/LogTable/Stats/StatsDrawer.razor.js");

        _openLogs.HasOpenLogs.Returns(true);

        Services.AddSingleton(_openLogs);
        Services.AddSingleton(_preferences);
        Services.AddSingleton(_statsVisibility);
        ComponentFactories.AddStub<StatsPane>();
    }

    [Fact]
    public void Drawer_CarriesOpenClass_WhenVisible()
    {
        _statsVisibility.IsVisible.Returns(true);

        var cut = Render<StatsDrawer>();

        Assert.NotNull(cut.Find(".stats-drawer.stats-drawer-open"));
    }

    [Fact]
    public void Drawer_ClosesWhenNoLogRemainsOpen_EvenWhenVisible()
    {
        _statsVisibility.IsVisible.Returns(true);
        _openLogs.HasOpenLogs.Returns(false);

        var cut = Render<StatsDrawer>();

        Assert.Empty(cut.FindComponents<Stub<StatsPane>>());
        Assert.Empty(cut.FindAll(".stats-drawer.stats-drawer-open"));
    }

    [Fact]
    public void Drawer_RendersStatsPane_OnlyWhenVisible()
    {
        _statsVisibility.IsVisible.Returns(false);
        var cut = Render<StatsDrawer>();
        Assert.Empty(cut.FindComponents<Stub<StatsPane>>());

        _statsVisibility.IsVisible.Returns(true);
        _statsVisibility.Changed += Raise.Event<Action>();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindComponents<Stub<StatsPane>>()));
    }

    [Fact]
    public void OnStatsDrawerHeightChanged_PersistsPositiveHeight()
    {
        _statsVisibility.IsVisible.Returns(true);
        var cut = Render<StatsDrawer>();

        cut.Instance.OnStatsDrawerHeightChanged(300);

        _preferences.Received().StatsDrawerHeightPreference = 300;
    }
}
