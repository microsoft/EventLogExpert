// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Filtering.Evaluation;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.FilterLenses;
using EventLogExpert.Runtime.FilterPane;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.StatusBar;
using Fluxor;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using System.Collections.Immutable;

namespace EventLogExpert.UI.Tests.StatusBar;

public sealed class StatusBarTests : BunitContext
{
    private readonly IStateSelection<EventLogState, (Filter, bool, int, bool, int)> _eventLog =
        Substitute.For<IStateSelection<EventLogState, (Filter, bool, int, bool, int)>>();

    private readonly IStateSelection<FilterPaneState, bool> _filterActive =
        Substitute.For<IStateSelection<FilterPaneState, bool>>();

    private readonly IStateSelection<FilterLensState, int> _lensCount =
        Substitute.For<IStateSelection<FilterLensState, int>>();

    private readonly IStateSelection<LogTableState, (
        EventLogId?, ImmutableList<LogView>, int, ImmutableDictionary<EventLogId, int>, ImmutableList<LogTabGroup>)> _logTable =
        Substitute.For<IStateSelection<LogTableState, (
            EventLogId?, ImmutableList<LogView>, int, ImmutableDictionary<EventLogId, int>, ImmutableList<LogTabGroup>)>>();

    private readonly IStateSelection<RawEventCountState, (int, ImmutableDictionary<EventLogId, int>)> _rawCount =
        Substitute.For<IStateSelection<RawEventCountState, (int, ImmutableDictionary<EventLogId, int>)>>();

    private readonly IStateSelection<StatusBarState, (ImmutableDictionary<StatusActivityId, (int, int)>, string)> _statusBar =
        Substitute.For<IStateSelection<StatusBarState, (ImmutableDictionary<StatusActivityId, (int, int)>, string)>>();

    public StatusBarTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        Services.AddFluxor(options => options.ScanAssemblies(typeof(UI.StatusBar.StatusBar).Assembly));
        Services.AddSingleton(_eventLog);
        Services.AddSingleton(_logTable);
        Services.AddSingleton(_rawCount);
        Services.AddSingleton(_statusBar);
        Services.AddSingleton(_filterActive);
        Services.AddSingleton(_lensCount);

        _eventLog.Value.Returns((Unfiltered, false, 0, false, 0));
        _logTable.Value.Returns((null, ImmutableList<LogView>.Empty, 0,
            ImmutableDictionary<EventLogId, int>.Empty, ImmutableList<LogTabGroup>.Empty));
        _rawCount.Value.Returns((0, ImmutableDictionary<EventLogId, int>.Empty));
        _statusBar.Value.Returns((ImmutableDictionary<StatusActivityId, (int, int)>.Empty, string.Empty));
        _filterActive.Value.Returns(false);
        _lensCount.Value.Returns(0);
    }

    private static Filter Filtered => new(new DateFilter { IsEnabled = true }, []);

    private static Filter Unfiltered => new(null, []);

    [Fact]
    public void ChannelNewEventsCounter_IsNotAnnounced()
    {
        var id = EventLogId.Create();
        var channel = new LogView(id) { LogName = "Application", LogPathType = LogPathType.Channel };
        _logTable.Value.Returns((id, ImmutableList.Create(channel), 0,
            ImmutableDictionary<EventLogId, int>.Empty.Add(id, 0), ImmutableList<LogTabGroup>.Empty));
        _rawCount.Value.Returns((0, ImmutableDictionary<EventLogId, int>.Empty.Add(id, 0)));
        _eventLog.Value.Returns((Unfiltered, false, 42, false, 0));

        var cut = Render<UI.StatusBar.StatusBar>();

        var newEvents = cut.FindAll(".status-bar-activity").Single(node => node.TextContent.Contains("New Events"));
        Assert.Equal("off", newEvents.GetAttribute("aria-live"));
    }

    [Fact]
    public void Filtered_ShowsShownOfTotal_AndFilteredIndicator()
    {
        _filterActive.Value.Returns(true);
        SetActiveLog(total: 1500, shown: 200, filter: Filtered, selected: 0);

        var cut = Render<UI.StatusBar.StatusBar>();

        Assert.Contains($"{200:N0} of {1500:N0} shown", cut.Markup);

        var indicator = cut.Find(".status-bar-filter");
        Assert.Equal("Filter active", indicator.GetAttribute("title"));
    }

    [Fact]
    public void LensOnlyNarrowing_ShowsShown_AndLensTooltip()
    {
        // Persistent filter off, but lenses narrow the composed AppliedFilter - the "shown" count and funnel must still
        // appear (the corrected lens-awareness), and the tooltip names the lens mechanism.
        _filterActive.Value.Returns(false);
        _lensCount.Value.Returns(2);
        SetActiveLog(total: 1500, shown: 300, filter: Filtered, selected: 0);

        var cut = Render<UI.StatusBar.StatusBar>();

        Assert.Contains($"{300:N0} of {1500:N0} shown", cut.Markup);
        Assert.Equal("2 lenses", cut.Find(".status-bar-filter").GetAttribute("title"));
    }

    [Fact]
    public void MultiSelect_ShowsSelectedSuffix_SingleSelectDoesNot()
    {
        SetActiveLog(total: 500, shown: 500, filter: Unfiltered, selected: 3);
        Assert.Contains("3 selected", Render<UI.StatusBar.StatusBar>().Markup);

        _eventLog.Value.Returns((Unfiltered, false, 0, false, 1));
        Assert.DoesNotContain("selected", Render<UI.StatusBar.StatusBar>().Markup);
    }

    [Fact]
    public void NoActiveLog_ShowsNoLogOpen_AndNoCounts()
    {
        var cut = Render<UI.StatusBar.StatusBar>();

        Assert.Contains("No log open", cut.Markup);
        Assert.Empty(cut.FindAll(".status-bar-counts"));
    }

    [Fact]
    public void Root_HasNoLiveRegion_ButAnnounceRegionIsPoliteStatus()
    {
        SetActiveLog(total: 500, shown: 500, filter: Unfiltered, selected: 0);

        var cut = Render<UI.StatusBar.StatusBar>();

        var root = cut.Find(".status-bar");
        Assert.False(root.HasAttribute("aria-live"));
        Assert.False(root.HasAttribute("role"));

        var announce = cut.Find(".status-bar-announce");
        Assert.Equal("status", announce.GetAttribute("role"));
        Assert.Equal("polite", announce.GetAttribute("aria-live"));
    }

    [Fact]
    public void Unfiltered_ShowsTotalEvents_WithoutFilteredIndicator()
    {
        SetActiveLog(total: 1500, shown: 1500, filter: Unfiltered, selected: 0);

        var cut = Render<UI.StatusBar.StatusBar>();

        Assert.Contains($"{1500:N0} events", cut.Markup);
        Assert.DoesNotContain("shown", cut.Markup);
        Assert.Empty(cut.FindAll(".status-bar-filter"));
    }

    private void SetActiveLog(int total, int shown, Filter filter, int selected)
    {
        var id = EventLogId.Create();
        var log = new LogView(id) { LogName = "Application", LogPathType = LogPathType.Channel };

        _logTable.Value.Returns((id, ImmutableList.Create(log), shown,
            ImmutableDictionary<EventLogId, int>.Empty.Add(id, shown), ImmutableList<LogTabGroup>.Empty));
        _rawCount.Value.Returns((total, ImmutableDictionary<EventLogId, int>.Empty.Add(id, total)));
        _eventLog.Value.Returns((filter, false, 0, false, selected));
    }
}
