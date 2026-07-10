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
using EventLogExpert.UI.LogTable.Grouping;
using EventLogExpert.UI.Tests.TestUtils;
using Fluxor;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.Virtualization;
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
    private readonly BunitJSModuleInterop _jsModule;
    private readonly EventLogId _logId = EventLogId.Create();
    private readonly ILogTableCommands _logTableCommands = Substitute.For<ILogTableCommands>();
    private readonly IState<LogTableState> _logTableState = Substitute.For<IState<LogTableState>>();
    private readonly IMenuService _menuService = Substitute.For<IMenuService>();
    private readonly IStateSelection<EventLogState, SelectionEntry?> _selectedEvent = Substitute.For<IStateSelection<EventLogState, SelectionEntry?>>();
    private readonly IStateSelection<EventLogState, ImmutableList<SelectionEntry>> _selectedEvents = Substitute.For<IStateSelection<EventLogState, ImmutableList<SelectionEntry>>>();
    private readonly ISettingsService _settings = Substitute.For<ISettingsService>();

    public LogTablePaneGroupingTests()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        JSInterop.Mode = JSRuntimeMode.Loose;
        _jsModule = JSInterop.SetupModule("./_content/EventLogExpert.UI/LogTable/LogTablePane.razor.js");

        _columnDefaults.ColumnOrder.Returns(ImmutableList.Create(ColumnName.Source));
        _filterPaneState.Value.Returns(new FilterPaneState());
        _highlightSelector.Select(Arg.Any<ImmutableList<SavedFilter>>()).Returns([]);
        _highlightSelector.ComputeHighlightKey(Arg.Any<ImmutableList<SavedFilter>>()).Returns(0);
        _settings.TimeZoneInfo.Returns(TimeZoneInfo.Utc);
        _selectedEvent.Value.Returns((SelectionEntry?)null);
        _selectedEvents.Value.Returns(ImmutableList<SelectionEntry>.Empty);

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
    public void Grouped_AllCollapsed_ArrowDown_MovesFocusBetweenHeadersWithoutSelecting()
    {
        var cut = RenderGrouped(Collapsed("Alpha", "Beta"), Event(1, "Alpha"), Event(2, "Beta"));

        Press(cut, "Home"); // header Alpha
        _eventLogCommands.ClearReceivedCalls();
        Press(cut, "ArrowDown"); // header Beta (visible row 1), focus only

        _eventLogCommands.DidNotReceive().SetSelectedEvents(
            Arg.Any<IReadOnlyCollection<SelectionEntry>>(), Arg.Any<SelectionEntry?>());
        _logTableCommands.DidNotReceive().ToggleGroupCollapsed(Arg.Any<string>());
        Assert.Equal(1, LastFocusedRow());
    }

    [Fact]
    public void Grouped_AllCollapsed_ShiftArrowDown_IsNoOp()
    {
        var cut = RenderGrouped(Collapsed("Alpha", "Beta"), Event(1, "Alpha"), Event(2, "Beta"));

        Press(cut, "Home"); // header Alpha
        _eventLogCommands.ClearReceivedCalls();
        Press(cut, "ArrowDown", shift: true); // no event to extend to

        _eventLogCommands.DidNotReceive().SetSelectedEvents(
            Arg.Any<IReadOnlyCollection<SelectionEntry>>(), Arg.Any<SelectionEntry?>());
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
    public void Grouped_EventBelowCollapsedGroup_HasVisibleRowAriaRowIndex()
    {
        // Event 2 sits at visible row 2 (Alpha collapsed, Beta header, event 2) -> aria-rowindex 4.
        var cut = RenderGrouped(Collapsed("Alpha"), Event(1, "Alpha"), Event(2, "Beta"));

        var eventRow = cut.Find("tbody tr.table-row");

        Assert.Equal("4", eventRow.GetAttribute("aria-rowindex"));
    }

    [Fact]
    public void Grouped_EventCursorInCollapsedGroup_ReconcilesToHeader()
    {
        // The selected event sits in a collapsed group, so the cursor must retype to its
        // header on build, proven by Right expanding the group.
        var alpha1 = Event(1, "Alpha");
        _selectedEvent.Value.Returns(Focus(alpha1, 0));
        _logTableState.Value.Returns(
            BuildState(ColumnName.Source, Collapsed("Alpha"), alpha1, Event(2, "Beta")));

        var cut = Render<LogTablePane>();
        Press(cut, "ArrowRight");

        _logTableCommands.Received(1).ToggleGroupCollapsed("Alpha");
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
    public void Grouped_ExternalSelectionIntoCollapsedGroup_RetypesCursorWithoutRebuild()
    {
        var alpha1 = Event(1, "Alpha");
        _logTableState.Value.Returns(BuildState(ColumnName.Source, Collapsed("Alpha"), alpha1, Event(2, "Beta")));

        var cut = Render<LogTablePane>();

        _selectedEvent.Value.Returns(Focus(alpha1, 0));
        RaiseStateChanged(cut);

        Press(cut, "ArrowRight");

        _logTableCommands.Received(1).ToggleGroupCollapsed("Alpha");
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
            Arg.Is<IReadOnlyCollection<SelectionEntry>>(c => c.Count == 2),
            Arg.Any<SelectionEntry?>());
    }

    [Fact]
    public void Grouped_HeaderChevronIcon_IsAriaHidden()
    {
        var cut = RenderGrouped(Collapsed(), Event(1, "Alpha"));

        Assert.Equal("true", cut.Find("tr.group-header-row .group-chevron i").GetAttribute("aria-hidden"));
    }

    [Fact]
    public void Grouped_HeaderEnterKey_TogglesCollapse()
    {
        var cut = RenderGrouped(Collapsed(), Event(1, "Alpha"));

        Press(cut, "Home");  // focus the first header
        Press(cut, "Enter");

        _logTableCommands.Received(1).ToggleGroupCollapsed("Alpha");
    }

    [Fact]
    public void Grouped_HeaderGroupVanishes_FallsBackToNearestSurvivingHeader()
    {
        var alpha1 = Event(1, "Alpha");
        var beta2 = Event(2, "Beta");
        var gamma3 = Event(3, "Gamma");
        _logTableState.Value.Returns(BuildState(ColumnName.Source, Collapsed(), alpha1, beta2, gamma3));

        var cut = Render<LogTablePane>();
        Press(cut, "Home");      // header Alpha
        Press(cut, "ArrowDown"); // event 1
        Press(cut, "ArrowDown"); // header Beta (cursor on the middle header)

        _logTableState.Value.Returns(BuildState(ColumnName.Source, Collapsed(), alpha1, gamma3));
        RaiseStateChanged(cut);
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("tr.group-header-row").Count));

        Press(cut, "Enter");

        _logTableCommands.Received(1).ToggleGroupCollapsed("Gamma");
        _logTableCommands.DidNotReceive().ToggleGroupCollapsed("Beta");
    }

    [Fact]
    public void Grouped_HeaderLeftClick_TogglesCollapseWithoutSelecting()
    {
        var cut = RenderGrouped(Collapsed(), Event(1, "Alpha"), Event(2, "Alpha"));

        cut.Find("tr.group-header-row").Click();

        _logTableCommands.Received(1).ToggleGroupCollapsed("Alpha");
        _eventLogCommands.DidNotReceive()
            .SetSelectedEvents(Arg.Any<IReadOnlyCollection<SelectionEntry>>(), Arg.Any<SelectionEntry?>());
    }

    [Fact]
    public void Grouped_HeaderOmitsAriaSelected()
    {
        var cut = RenderGrouped(Collapsed(), Event(1, "Alpha"));

        Assert.False(cut.Find("tr.group-header-row").HasAttribute("aria-selected"));
    }

    [Fact]
    public void Grouped_HeaderRightClick_DoesNotSelect()
    {
        var cut = RenderGrouped(Collapsed(), Event(1, "Alpha"), Event(2, "Alpha"));

        cut.Find("tr.group-header-row").TriggerEvent("oncontextmenu", new MouseEventArgs());

        _eventLogCommands.DidNotReceive()
            .SetSelectedEvents(Arg.Any<IReadOnlyCollection<SelectionEntry>>(), Arg.Any<SelectionEntry?>());
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
    public void Grouped_Home_FocusesFirstHeaderWithoutSelecting()
    {
        var cut = RenderGrouped(Collapsed(), Event(1, "Alpha"), Event(2, "Alpha"));

        _eventLogCommands.ClearReceivedCalls();
        Press(cut, "Home"); // first visible row is a header, focus only

        _eventLogCommands.DidNotReceive().SetSelectedEvents(
            Arg.Any<IReadOnlyCollection<SelectionEntry>>(), Arg.Any<SelectionEntry?>());
        Assert.Equal(0, LastFocusedRow());
    }

    [Fact]
    public void Grouped_LeftOnCollapsedHeader_DoesNothing()
    {
        var cut = RenderGrouped(Collapsed("Alpha"), Event(1, "Alpha"), Event(2, "Beta"));

        Press(cut, "Home");      // header Alpha (already collapsed)
        Press(cut, "ArrowLeft"); // no-op

        _logTableCommands.DidNotReceive().ToggleGroupCollapsed(Arg.Any<string>());
    }

    [Fact]
    public void Grouped_LeftOnEvent_FocusesParentHeaderWithoutChangingSelection()
    {
        var cut = RenderGrouped(Collapsed(), Event(1, "Alpha"), Event(2, "Alpha"));

        Press(cut, "Home");
        Press(cut, "ArrowDown"); // event 1 selected
        _eventLogCommands.ClearReceivedCalls();
        Press(cut, "ArrowLeft"); // focus parent header (visible row 0); selection unchanged

        _eventLogCommands.DidNotReceive().SetSelectedEvents(
            Arg.Any<IReadOnlyCollection<SelectionEntry>>(), Arg.Any<SelectionEntry?>());
        _logTableCommands.DidNotReceive().ToggleGroupCollapsed(Arg.Any<string>());
        Assert.Equal(0, LastFocusedRow());
    }

    [Fact]
    public void Grouped_LeftOnExpandedHeader_Collapses()
    {
        var cut = RenderGrouped(Collapsed(), Event(1, "Alpha"));

        Press(cut, "Home");      // header Alpha (expanded)
        Press(cut, "ArrowLeft"); // collapse

        _logTableCommands.Received(1).ToggleGroupCollapsed("Alpha");
    }

    [Fact]
    public void Grouped_PageDown_MovesToLastVisibleEvent()
    {
        var cut = RenderGrouped(Collapsed(), Event(1, "Alpha"), Event(2, "Beta"));
        // rows: header Alpha(0), event 1(1), header Beta(2), event 2(3)
        Press(cut, "Home");
        _eventLogCommands.ClearReceivedCalls();
        Press(cut, "PageDown"); // clamps to the last visible row (event 2)

        _eventLogCommands.Received(1).SetSelectedEvents(
            Arg.Is<IReadOnlyCollection<SelectionEntry>>(c => c.Count == 1),
            Arg.Any<SelectionEntry?>());
        Assert.Equal(3, LastFocusedRow());
    }

    [Fact]
    public void Grouped_PlainArrowOntoEvent_SelectsEvent()
    {
        var cut = RenderGrouped(Collapsed(), Event(1, "Alpha"), Event(2, "Alpha"));

        Press(cut, "Home");      // focus header (focus-only)
        _eventLogCommands.ClearReceivedCalls();
        Press(cut, "ArrowDown"); // land on the first event

        _eventLogCommands.Received(1).SetSelectedEvents(
            Arg.Is<IReadOnlyCollection<SelectionEntry>>(c => c.Count == 1),
            Arg.Any<SelectionEntry?>());
    }

    [Fact]
    public void Grouped_PlainArrowOntoHeader_FocusesOnlyWithoutSelecting()
    {
        var cut = RenderGrouped(Collapsed(), Event(1, "Alpha"), Event(2, "Beta"));

        Press(cut, "Home");      // header Alpha
        Press(cut, "ArrowDown"); // event 1 (selects it)
        _eventLogCommands.ClearReceivedCalls();
        Press(cut, "ArrowDown"); // header Beta (visible row 2), focus only

        _eventLogCommands.DidNotReceive().SetSelectedEvents(
            Arg.Any<IReadOnlyCollection<SelectionEntry>>(), Arg.Any<SelectionEntry?>());
        Assert.Equal(2, LastFocusedRow());
    }

    [Fact]
    public async Task Grouped_ReactiveScroll_TargetsVisibleRowNotEventIndex()
    {
        // Event 2 is at event index 1 but visible row 3, so a reactive scroll must target 3.
        var alpha1 = Event(1, "Alpha");
        var beta2 = Event(2, "Beta");
        _selectedEvent.Value.Returns(Focus(beta2, 1));
        _logTableState.Value.Returns(BuildState(ColumnName.Source, Collapsed(), alpha1, beta2));

        var cut = Render<LogTablePane>();
        await cut.InvokeAsync(() => Services.GetRequiredService<IStore>().InitializeAsync());
        await cut.InvokeAsync(() =>
            Services.GetRequiredService<IDispatcher>().Dispatch(new SetActiveTableAction(EventLogId.Create())));

        cut.WaitForAssertion(() =>
        {
            var scrolls = _jsModule.Invocations["scrollToRow"];
            Assert.NotEmpty(scrolls);
            Assert.Equal(3, (int)scrolls.Last().Arguments[0]!);
        });
    }

    [Fact]
    public async Task Grouped_ReactiveScroll_WhenSelectedEventGroupCollapsed_TargetsGroupHeaderRow()
    {
        // beta2 lives in the collapsed "Beta" group, so its event row is hidden. A reactive scroll must target the
        // Beta header (visible row 2), the collapsed group the selection lives in, not the hidden event row.
        var alpha1 = Event(1, "Alpha");
        var beta2 = Event(2, "Beta");
        _selectedEvent.Value.Returns(Focus(beta2, 1));
        _logTableState.Value.Returns(BuildState(ColumnName.Source, Collapsed("Beta"), alpha1, beta2));

        var cut = Render<LogTablePane>();
        await cut.InvokeAsync(() => Services.GetRequiredService<IStore>().InitializeAsync());
        await cut.InvokeAsync(() =>
            Services.GetRequiredService<IDispatcher>().Dispatch(new SetActiveTableAction(EventLogId.Create())));

        cut.WaitForAssertion(() =>
        {
            var scrolls = _jsModule.Invocations["scrollToRow"];
            Assert.NotEmpty(scrolls);
            Assert.Equal(2, (int)scrolls.Last().Arguments[0]!);
        });
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
    public void Grouped_RightOnCollapsedHeader_Expands()
    {
        var cut = RenderGrouped(Collapsed("Alpha"), Event(1, "Alpha"), Event(2, "Beta"));

        Press(cut, "Home");       // header Alpha (collapsed)
        Press(cut, "ArrowRight"); // expand

        _logTableCommands.Received(1).ToggleGroupCollapsed("Alpha");
    }

    [Fact]
    public void Grouped_RightOnExpandedHeader_SelectsFirstChild()
    {
        var cut = RenderGrouped(Collapsed(), Event(1, "Alpha"), Event(2, "Alpha"));

        Press(cut, "Home"); // header Alpha (expanded)
        _eventLogCommands.ClearReceivedCalls();
        Press(cut, "ArrowRight"); // focus + select first child

        _eventLogCommands.Received(1).SetSelectedEvents(
            Arg.Is<IReadOnlyCollection<SelectionEntry>>(c => c.Count == 1),
            Arg.Any<SelectionEntry?>());
        _logTableCommands.DidNotReceive().ToggleGroupCollapsed(Arg.Any<string>());
    }

    [Fact]
    public void Grouped_ShiftArrowDown_AcrossCollapsedGroup_IncludesHiddenEvents()
    {
        var cut = RenderGrouped(
            Collapsed("Beta"),
            Event(1, "Alpha"), Event(2, "Beta"), Event(3, "Beta"), Event(4, "Gamma"));

        Press(cut, "Home");      // header Alpha
        Press(cut, "ArrowDown"); // event 1 (anchor)
        _eventLogCommands.ClearReceivedCalls();
        Press(cut, "ArrowDown", shift: true); // skips the collapsed Beta header to event 4

        _eventLogCommands.Received(1).SetSelectedEvents(
            Arg.Is<IReadOnlyCollection<SelectionEntry>>(c => c.Count == 4),
            Arg.Any<SelectionEntry?>());
    }

    [Fact]
    public void Grouped_ShiftArrowUp_AcrossCollapsedGroup_IncludesHiddenEvents()
    {
        var cut = RenderGrouped(
            Collapsed("Beta"),
            Event(1, "Alpha"), Event(2, "Beta"), Event(3, "Beta"), Event(4, "Gamma"));

        Press(cut, "End"); // event 4 (anchor)
        _eventLogCommands.ClearReceivedCalls();
        Press(cut, "ArrowUp", shift: true); // skips the collapsed Beta header to event 1

        _eventLogCommands.Received(1).SetSelectedEvents(
            Arg.Is<IReadOnlyCollection<SelectionEntry>>(c => c.Count == 4),
            Arg.Any<SelectionEntry?>());
    }

    [Fact]
    public void Grouped_TableHasTreegridRoleAndAriaLevels()
    {
        var cut = RenderGrouped(Collapsed(), Event(1, "Alpha"));

        Assert.Equal("treegrid", cut.Find("table#eventTable").GetAttribute("role"));
        Assert.Equal("1", cut.Find("tr.group-header-row").GetAttribute("aria-level"));
        Assert.Equal("2", cut.Find("tbody tr.table-row").GetAttribute("aria-level"));
    }

    [Fact]
    public void Grouped_UngroupWithHeaderCursor_ResumesFromFormerGroupsFirstEvent()
    {
        // Ungrouped order (by time) differs from grouped order (by Source).
        var alpha = Event(2, "Alpha");
        var beta = Event(1, "Beta");
        _logTableState.Value.Returns(BuildState(ColumnName.Source, Collapsed(), alpha, beta));

        var cut = Render<LogTablePane>();
        Press(cut, "Home");      // header Alpha
        Press(cut, "ArrowDown"); // event Alpha
        Press(cut, "ArrowDown"); // header Beta (cursor on a non-first header)

        // Ungroup resorts the DISPLAY order (by time) while the underlying columnar store, and therefore every
        // event's physical locator, is unchanged: production reuses the same store across a group-by change and only
        // rebuilds the view's order. Keep the same ingestion order as the grouped state so locators stay stable across
        // the transition; a stale index (physical treated as display position) would still mispoint.
        _logTableState.Value.Returns(BuildState(groupBy: null, Collapsed(), alpha, beta));
        RaiseStateChanged(cut);
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("tr.group-header-row")));

        _eventLogCommands.ClearReceivedCalls();
        Press(cut, "ArrowDown");

        _eventLogCommands.Received(1).SetSelectedEvents(
            Arg.Is<IReadOnlyCollection<SelectionEntry>>(c => c.Count == 1 && c.Any(entry => entry.ReloadKey!.Value.RecordId == alpha.RecordId)),
            Arg.Any<SelectionEntry?>());
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
    public void GroupedByLog_HeaderShowsLogNameNotRepresentativeOwningLog()
    {
        var combinedId = EventLogId.Create();
        var appId1 = EventLogId.Create();
        var appId2 = EventLogId.Create();
        var e1 = LogEvent(1, @"C:\logs\App1.evtx", "Application");
        var e2 = LogEvent(2, @"C:\logs\App2.evtx", "Application");

        _logTableState.Value.Returns(new LogTableState
        {
            ActiveEventLogId = combinedId,
            EventTables = ImmutableList.Create(
                new LogView(combinedId) { LogName = "Combined", GroupId = LogTabGroupId.AllLogs },
                new LogView(appId1) { LogName = "Application" },
                new LogView(appId2) { LogName = "Application" }),
            EventCountByLog = ImmutableDictionary<EventLogId, int>.Empty.Add(appId1, 1).Add(appId2, 1),
            Columns = ImmutableDictionary<ColumnName, bool>.Empty.Add(ColumnName.Log, true),
            ColumnOrder = ImmutableList.Create(ColumnName.Log),
            GroupBy = ColumnName.Log,
            GroupCollapseOverrides = Collapsed()
        }.WithLogEvents(appId1, e1).WithLogEvents(appId2, e2));

        var cut = Render<LogTablePane>();
        var header = cut.Find("tr.group-header-row");

        Assert.Contains("Application", header.TextContent);
        Assert.DoesNotContain("App1.evtx", header.TextContent);
        Assert.DoesNotContain("App2.evtx", header.TextContent);
    }

    [Fact]
    public async Task GroupedVirtualize_SetsRowHeightItemSize_SoScrollModelMatchesCss()
    {
        // ItemSize must equal the CSS `tr { height: 22px }`; without it Virtualize defaults to 50px and the
        // row-height-based scrollToRow lands jump-to-selected at the wrong position.
        _logTableState.Value.Returns(BuildState(ColumnName.Source, Collapsed(), Event(1, "Alpha")));

        var cut = Render<LogTablePane>();
        await cut.InvokeAsync(() => Services.GetRequiredService<IStore>().InitializeAsync());
        await cut.InvokeAsync(() =>
            Services.GetRequiredService<IDispatcher>().Dispatch(new SetActiveTableAction(EventLogId.Create())));

        cut.WaitForAssertion(() =>
            Assert.Equal(22f, cut.FindComponent<Virtualize<TableRow>>().Instance.ItemSize));
    }

    [Fact]
    public void Ungrouped_ArrowDown_SelectsNextEvent()
    {
        _logTableState.Value.Returns(BuildState(groupBy: null, Collapsed(), Event(1, "Alpha"), Event(2, "Beta")));

        var cut = Render<LogTablePane>();

        Press(cut, "Home");      // flat Home selects the first event
        _eventLogCommands.ClearReceivedCalls();
        Press(cut, "ArrowDown"); // selects the next event

        _eventLogCommands.Received(1).SetSelectedEvents(
            Arg.Is<IReadOnlyCollection<SelectionEntry>>(c => c.Count == 1),
            Arg.Any<SelectionEntry?>());
    }

    [Fact]
    public void Ungrouped_RendersNoGroupHeaders()
    {
        _logTableState.Value.Returns(BuildState(groupBy: null, Collapsed(), Event(1, "Alpha"), Event(2, "Beta")));

        var cut = Render<LogTablePane>();

        Assert.Empty(cut.FindAll("tr.group-header-row"));
        Assert.Equal(2, cut.FindAll("tbody tr.table-row").Count);
    }

    [Fact]
    public void Ungrouped_TableHasGridRoleAndNoEventAriaLevel()
    {
        _logTableState.Value.Returns(BuildState(groupBy: null, Collapsed(), Event(1, "Alpha"), Event(2, "Beta")));

        var cut = Render<LogTablePane>();

        Assert.Equal("grid", cut.Find("table#eventTable").GetAttribute("role"));
        Assert.False(cut.Find("tbody tr.table-row").HasAttribute("aria-level"));
    }

    [Fact]
    public async Task UngroupedVirtualize_SetsRowHeightItemSize_SoScrollModelMatchesCss()
    {
        // Same 22px scroll-model requirement for the ungrouped ItemsProvider path (the one the redesign regressed).
        _logTableState.Value.Returns(BuildState(groupBy: null, Collapsed(), Event(1, "Alpha")));

        var cut = Render<LogTablePane>();
        await cut.InvokeAsync(() => Services.GetRequiredService<IStore>().InitializeAsync());
        await cut.InvokeAsync(() =>
            Services.GetRequiredService<IDispatcher>().Dispatch(new SetActiveTableAction(EventLogId.Create())));

        cut.WaitForAssertion(() =>
            Assert.Equal(22f, cut.FindComponent<Virtualize<DisplayRow>>().Instance.ItemSize));
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

    private static ResolvedEvent LogEvent(int id, string owningLog, string logName) =>
        new(owningLog, LogPathType.Channel)
        {
            Id = id,
            RecordId = id,
            LogName = logName,
            TimeCreated = new DateTime(2024, 1, 1, 0, 0, id, DateTimeKind.Utc),
            Description = $"event {id}"
        };

    private static void Press(IRenderedComponent<LogTablePane> cut, string code, bool shift = false) =>
        cut.Find(".table-container").KeyDown(new KeyboardEventArgs { Code = code, Key = code, ShiftKey = shift });

    private LogTableState BuildState(
        ColumnName? groupBy,
        ImmutableHashSet<string> collapsed,
        params ResolvedEvent[] events)
    {
        return new LogTableState
        {
            ActiveEventLogId = _logId,
            EventTables = ImmutableList.Create(new LogView(_logId) { LogName = LogName }),
            EventCountByLog = ImmutableDictionary<EventLogId, int>.Empty.Add(_logId, events.Length),
            Columns = ImmutableDictionary<ColumnName, bool>.Empty.Add(ColumnName.Source, true),
            ColumnOrder = ImmutableList.Create(ColumnName.Source),
            IsDescending = false,
            GroupBy = groupBy,
            GroupCollapseOverrides = collapsed
        }.WithLogEvents(_logId, events);
    }

    private SelectionEntry Focus(ResolvedEvent evt, int physicalIndex)
    {
        var handle = new EventLocator(_logId, 0, physicalIndex);
        ValueKey.TryCreate(evt, out var reloadKey);
        return new SelectionEntry(handle, handle, reloadKey);
    }

    // Visible-row index of the most recent programmatic focus (proves DOM focus moved).
    private int LastFocusedRow()
    {
        var invocations = _jsModule.Invocations["focusEventTableRow"];

        Assert.NotEmpty(invocations);

        return (int)invocations.Last().Arguments[0]!;
    }

    // Re-render the component against the latest substituted state (after updating a
    // _logTableState/_selectedEvent return value mid-test).
    private void RaiseStateChanged(IRenderedComponent<LogTablePane> cut) =>
        cut.InvokeAsync(() => _logTableState.StateChanged += Raise.Event<EventHandler>(_logTableState, EventArgs.Empty));

    private IRenderedComponent<LogTablePane> RenderGrouped(ImmutableHashSet<string> collapsed, params ResolvedEvent[] events)
    {
        _logTableState.Value.Returns(BuildState(ColumnName.Source, collapsed, events));

        return Render<LogTablePane>();
    }
}
