// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.FilterPane;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.Menu;
using EventLogExpert.Runtime.Settings;
using EventLogExpert.UI.LogTable;
using EventLogExpert.UI.Tests.TestUtils;
using Fluxor;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using System.Collections.Immutable;
using System.Globalization;

namespace EventLogExpert.UI.Tests.LogTable;

public sealed class LogTablePaneGroupingTests : BunitContext
{
    private const string LogName = "Application";

    private readonly ILogTableColumnDefaultsProvider _columnDefaults = Substitute.For<ILogTableColumnDefaultsProvider>();
    private readonly IEventLogCommands _eventLogCommands = Substitute.For<IEventLogCommands>();
    private readonly IState<FilterPaneState> _filterPaneState = Substitute.For<IState<FilterPaneState>>();
    private readonly IHighlightSelector _highlightSelector = Substitute.For<IHighlightSelector>();
    private readonly ILogTableCommands _logTableCommands = Substitute.For<ILogTableCommands>();
    private readonly IState<LogTableState> _logTableState = Substitute.For<IState<LogTableState>>();
    private readonly IMenuService _menuService = Substitute.For<IMenuService>();
    private readonly IStateSelection<EventLogState, ResolvedEvent?> _selectedEvent = Substitute.For<IStateSelection<EventLogState, ResolvedEvent?>>();
    private readonly IStateSelection<EventLogState, ImmutableList<ResolvedEvent>> _selectedEvents = Substitute.For<IStateSelection<EventLogState, ImmutableList<ResolvedEvent>>>();
    private readonly ISettingsService _settings = Substitute.For<ISettingsService>();

    public LogTablePaneGroupingTests()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.SetupModule("./_content/EventLogExpert.UI/LogTable/LogTablePane.razor.js");

        _columnDefaults.ColumnOrder.Returns(ImmutableList.Create(ColumnName.Source));
        _filterPaneState.Value.Returns(new FilterPaneState());
        _highlightSelector.Select(Arg.Any<ImmutableList<SavedFilter>>()).Returns([]);
        _highlightSelector.ComputeHighlightKey(Arg.Any<ImmutableList<SavedFilter>>()).Returns(0);
        _settings.TimeZoneInfo.Returns(TimeZoneInfo.Utc);
        _selectedEvent.Value.Returns((ResolvedEvent?)null);
        _selectedEvents.Value.Returns(ImmutableList<ResolvedEvent>.Empty);

        Services.AddLogTablePaneDependencies();
        Services.AddSingleton(_columnDefaults);
        Services.AddSingleton(_eventLogCommands);
        Services.AddSingleton(_filterPaneState);
        Services.AddSingleton(_highlightSelector);
        Services.AddSingleton(_logTableState);
        Services.AddSingleton(_selectedEvent);
        Services.AddSingleton(_selectedEvents);
        Services.AddSingleton(_settings);
        Services.AddSingleton(_logTableCommands);
        Services.AddSingleton(_menuService);

        Services.AddFluxor(options => options.ScanAssemblies(typeof(LogTablePane).Assembly));
    }

    [Fact]
    public void Grouped_CollapsedHeader_HasAriaExpandedFalse()
    {
        var cut = RenderGrouped(
            Collapsed("Alpha"),
            Event(1, "Alpha"));

        var header = cut.Find("tr.group-header-row");

        Assert.Equal("false", header.GetAttribute("aria-expanded"));
        Assert.Equal("true", header.QuerySelector(".group-chevron")!.GetAttribute("data-collapsed"));
    }

    [Fact]
    public void Grouped_EventRowStripe_ResetsPerGroup()
    {
        var cut = RenderGrouped(Collapsed(), Event(1, "Alpha"), Event(2, "Alpha"), Event(3, "Beta"));

        var rows = cut.FindAll("tbody tr.table-row");

        Assert.Equal("0", rows[0].GetAttribute("data-stripe"));
        Assert.Equal("1", rows[1].GetAttribute("data-stripe"));
        Assert.Equal("0", rows[2].GetAttribute("data-stripe"));
    }

    [Fact]
    public async Task Grouped_GroupContextMenu_CollapseAll_DispatchesCommand()
    {
        IReadOnlyList<MenuItem>? items = null;
        _menuService
            .When(m => m.OpenAt(
                Arg.Any<double>(), Arg.Any<double>(), Arg.Any<IReadOnlyList<MenuItem>>(),
                Arg.Any<bool>(), Arg.Any<bool>()))
            .Do(call => items = call.Arg<IReadOnlyList<MenuItem>>());

        var cut = RenderGrouped(Collapsed(), Event(1, "Alpha"));
        cut.Find("tr.group-header-row").TriggerEvent("oncontextmenu", new MouseEventArgs());

        await items!.First(m => m.Label == "Collapse All Groups").OnClickAsync!();

        _logTableCommands.Received(1).SetAllGroupsCollapsed(true);
    }

    [Fact]
    public async Task Grouped_GroupContextMenu_SelectGroup_SelectsGroupedEvents()
    {
        IReadOnlyList<MenuItem>? items = null;
        _menuService
            .When(m => m.OpenAt(
                Arg.Any<double>(), Arg.Any<double>(), Arg.Any<IReadOnlyList<MenuItem>>(),
                Arg.Any<bool>(), Arg.Any<bool>()))
            .Do(call => items = call.Arg<IReadOnlyList<MenuItem>>());

        var cut = RenderGrouped(Collapsed(), Event(1, "Alpha"), Event(2, "Alpha"), Event(3, "Beta"));
        cut.Find("tr.group-header-row").TriggerEvent("oncontextmenu", new MouseEventArgs());

        await items!.First(item => item.Label == "Select Group").OnClickAsync!();

        _eventLogCommands.Received(1).SetSelectedEvents(
            Arg.Is<IReadOnlyCollection<ResolvedEvent>>(c => c.Count == 2),
            Arg.Any<ResolvedEvent?>());
    }

    [Fact]
    public void Grouped_HeaderEnterKey_TogglesCollapse()
    {
        var cut = RenderGrouped(Collapsed(), Event(1, "Alpha"));

        cut.Find("tr.group-header-row").KeyDown(new KeyboardEventArgs { Key = "Enter" });

        _logTableCommands.Received(1).ToggleGroupCollapsed("Alpha");
    }

    [Fact]
    public void Grouped_HeaderLeftClick_TogglesCollapseWithoutSelecting()
    {
        var cut = RenderGrouped(Collapsed(), Event(1, "Alpha"), Event(2, "Alpha"));

        cut.Find("tr.group-header-row").Click();

        _logTableCommands.Received(1).ToggleGroupCollapsed("Alpha");
        _eventLogCommands.DidNotReceive()
            .SetSelectedEvents(Arg.Any<IReadOnlyCollection<ResolvedEvent>>(), Arg.Any<ResolvedEvent?>());
    }

    [Fact]
    public void Grouped_HeaderRightClick_DoesNotSelect()
    {
        var cut = RenderGrouped(Collapsed(), Event(1, "Alpha"), Event(2, "Alpha"));

        cut.Find("tr.group-header-row").TriggerEvent("oncontextmenu", new MouseEventArgs());

        _eventLogCommands.DidNotReceive()
            .SetSelectedEvents(Arg.Any<IReadOnlyCollection<ResolvedEvent>>(), Arg.Any<ResolvedEvent?>());
    }

    [Fact]
    public void Grouped_HeaderShowsGroupNameValueAndCount()
    {
        var cut = RenderGrouped(
            Collapsed(),
            Event(1, "Alpha"),
            Event(2, "Alpha"));

        var header = cut.FindAll("tr.group-header-row")[0];

        Assert.Contains("Source", header.TextContent);
        Assert.Contains("Alpha", header.TextContent);
        Assert.Contains("(2)", header.TextContent);
    }

    [Fact]
    public void Grouped_RendersOneHeaderRowPerGroup()
    {
        var cut = RenderGrouped(
            Collapsed(),
            Event(1, "Alpha"),
            Event(2, "Alpha"),
            Event(3, "Beta"));

        Assert.Equal(2, cut.FindAll("tr.group-header-row").Count);
    }

    [Fact]
    public void Grouped_RightClickHeader_OpensGroupContextMenuWithActions()
    {
        IReadOnlyList<MenuItem>? items = null;
        _menuService
            .When(m => m.OpenAt(
                Arg.Any<double>(), Arg.Any<double>(), Arg.Any<IReadOnlyList<MenuItem>>(),
                Arg.Any<bool>(), Arg.Any<bool>()))
            .Do(call => items = call.Arg<IReadOnlyList<MenuItem>>());

        var cut = RenderGrouped(Collapsed(), Event(1, "Alpha"), Event(2, "Alpha"));
        cut.Find("tr.group-header-row").TriggerEvent("oncontextmenu", new MouseEventArgs());

        Assert.NotNull(items);
        Assert.Contains(items!, m => m.Label == "Expand All Groups");
        Assert.Contains(items!, m => m.Label == "Collapse All Groups");
        Assert.Contains(items!, m => m.Label == "Group Descending");
        Assert.Contains(items!, m => m.Label == "Select Group");
    }

    [Fact]
    public void Grouped_WhenAllExpanded_RendersEveryEventRow()
    {
        var cut = RenderGrouped(
            Collapsed(),
            Event(1, "Alpha"),
            Event(2, "Alpha"),
            Event(3, "Beta"));

        Assert.Equal(3, cut.FindAll("tbody tr.table-row").Count);
    }

    [Fact]
    public void Grouped_WhenGroupCollapsed_HidesOnlyThatGroupsEvents()
    {
        var cut = RenderGrouped(
            Collapsed("Alpha"),
            Event(1, "Alpha"),
            Event(2, "Alpha"),
            Event(3, "Beta"));

        Assert.Equal(2, cut.FindAll("tr.group-header-row").Count);
        var eventRows = cut.FindAll("tbody tr.table-row");
        Assert.Single(eventRows);
        Assert.Contains("Beta", eventRows[0].TextContent);
    }

    [Fact]
    public void Ungrouped_RendersNoGroupHeaders()
    {
        _logTableState.Value.Returns(BuildState(groupBy: null, Collapsed(), Event(1, "Alpha"), Event(2, "Beta")));

        var cut = Render<LogTablePane>();

        Assert.Empty(cut.FindAll("tr.group-header-row"));
        Assert.Equal(2, cut.FindAll("tbody tr.table-row").Count);
    }

    private static LogTableState BuildState(
        ColumnName? groupBy,
        ImmutableHashSet<string> collapsed,
        params ResolvedEvent[] events)
    {
        var logId = EventLogId.Create();

        return new LogTableState
        {
            ActiveEventLogId = logId,
            EventTables = ImmutableList.Create(new LogView(logId) { LogName = LogName }),
            DisplayedEvents = events,
            EventCountByLog = ImmutableDictionary<EventLogId, int>.Empty.Add(logId, events.Length),
            Columns = ImmutableDictionary<ColumnName, bool>.Empty.Add(ColumnName.Source, true),
            ColumnOrder = ImmutableList.Create(ColumnName.Source),
            IsDescending = false,
            GroupBy = groupBy,
            GroupCollapseOverrides = collapsed
        };
    }

    private static ImmutableHashSet<string> Collapsed(params string[] keys) =>
        ImmutableHashSet.Create(StringComparer.Ordinal, keys);

    private static ResolvedEvent Event(int id, string source) =>
        new(LogName, LogPathType.Channel)
        {
            Id = id,
            RecordId = id,
            Source = source,
            TimeCreated = new DateTime(2024, 1, 1, 0, 0, id, DateTimeKind.Utc),
            Description = $"event {id}"
        };

    private IRenderedComponent<LogTablePane> RenderGrouped(ImmutableHashSet<string> collapsed, params ResolvedEvent[] events)
    {
        _logTableState.Value.Returns(BuildState(ColumnName.Source, collapsed, events));

        return Render<LogTablePane>();
    }
}
