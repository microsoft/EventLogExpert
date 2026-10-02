// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Localization;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.FilterPane;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.Settings;
using EventLogExpert.UI.LogTable;
using EventLogExpert.UI.LogTable.Find;
using EventLogExpert.UI.Menu;
using EventLogExpert.UI.Tests.TestUtils;
using Fluxor;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using NSubstitute;
using System.Collections.Immutable;
using System.Globalization;

namespace EventLogExpert.UI.Tests.LogTable;

[Collection(CultureSensitiveCollection.Name)]
public sealed class LogTablePaneLocalizationTests : CultureSensitiveBunitContext
{
    private const string LogName = "Application";

    private readonly ILogTableColumnDefaultsProvider _columnDefaults = Substitute.For<ILogTableColumnDefaultsProvider>();
    private readonly IEventLogCommands _eventLogCommands = Substitute.For<IEventLogCommands>();
    private readonly EventLogId _logId = EventLogId.Create();
    private readonly ILogTableCommands _logTableCommands = Substitute.For<ILogTableCommands>();
    private readonly IState<LogTableState> _logTableState = Substitute.For<IState<LogTableState>>();
    private readonly IMenuService _menuService = Substitute.For<IMenuService>();
    private readonly IEventFocusSource _selectedEvent = Substitute.For<IEventFocusSource>();
    private readonly ISettingsService _settings = Substitute.For<ISettingsService>();
    private readonly IOrderedViewSource _viewSource = Substitute.For<IOrderedViewSource>();

    private IReadOnlyList<MenuItem>? _capturedMenu;
    private OrderedViewPresentation? _presentation;
    private long _presentationRevision;

    public LogTablePaneLocalizationTests()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.SetupModule("./_content/EventLogExpert.UI/LogTable/LogTablePane.razor.js");
        JSInterop.SetupModule("./_content/EventLogExpert.UI/LogTable/Find/FindBar.razor.js");

        _columnDefaults.ColumnOrder.Returns(ImmutableList.Create(ColumnName.Source, ColumnName.DateAndTime));
        _selectedEvent.Current.Returns((SelectionEntry?)null);
        _settings.TimeZoneInfo.Returns(TimeZoneInfo.Local);
        _viewSource.Current.Returns(_ => _presentation);
        _menuService
            .When(menu => menu.OpenAt(
                Arg.Any<double>(),
                Arg.Any<double>(),
                Arg.Any<IReadOnlyList<MenuItem>>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<bool>()))
            .Do(call => _capturedMenu = call.ArgAt<IReadOnlyList<MenuItem>>(2));

        Services.AddLogTablePaneDependencies();
        Services.AddSingleton<IStringLocalizer<SharedResource>>(new MarkerLocalizer());
        Services.AddImmediateCpuWorkScheduler();
        Services.AddSingleton(_columnDefaults);
        Services.AddSingleton(_eventLogCommands);
        var filterPaneState = Substitute.For<IState<FilterPaneState>>();
        filterPaneState.Value.Returns(new FilterPaneState());
        Services.AddSingleton(filterPaneState);
        var highlightSelector = Substitute.For<IHighlightSelector>();
        highlightSelector.Select(Arg.Any<ImmutableList<SavedFilter>>()).Returns([]);
        highlightSelector.ComputeHighlightKey(Arg.Any<ImmutableList<SavedFilter>>()).Returns(0);
        Services.AddSingleton(highlightSelector);
        Services.AddSingleton(_logTableState);
        Services.AddSingleton(_selectedEvent);
        var eventSelection = Substitute.For<IEventSelectionSource>();
        eventSelection.Current.Returns(ImmutableList<SelectionEntry>.Empty);
        Services.AddSingleton(eventSelection);
        Services.AddSingleton(_settings);
        Services.AddSingleton(_logTableCommands);
        Services.AddSingleton(_menuService);
        Services.AddSingleton(_viewSource);
        Services.AddFluxor(options => options.ScanAssemblies(typeof(LogTablePane).Assembly));
    }

    [Fact]
    public void CellContextMenu_GroupableCell_IncludesDirectGroupByItemWithoutGroupBySubmenu()
    {
        var cut = RenderTable(
            groupBy: null,
            ImmutableHashSet<string>.Empty,
            orderBy: ColumnName.Level,
            [ColumnName.Level, ColumnName.DateAndTime],
            Event(1, "Alpha"));

        OpenMenu(cut, "tbody tr.table-row td:nth-child(1)");

        AssertMenuContains("[[LogTable_GroupByColumn([[Column_Level]])]]");
        Assert.DoesNotContain(_capturedMenu!, item => item.Label == "[[LogTable_GroupBy]]");
    }

    [Fact]
    public async Task CellContextMenu_GroupableCell_WhenAlreadyGrouped_IncludesDirectUngroupItem()
    {
        var cut = RenderTable(
            groupBy: ColumnName.Level,
            ImmutableHashSet<string>.Empty,
            orderBy: ColumnName.Level,
            [ColumnName.Level, ColumnName.DateAndTime],
            Event(1, "Alpha"));

        OpenMenu(cut, "tbody tr.table-row td:nth-child(1)");
        _logTableCommands.ClearReceivedCalls();

        var item = _capturedMenu!.Single(item => item.Label == "[[LogTable_UnGroupByColumn([[Column_Level]])]]");
        Assert.DoesNotContain(_capturedMenu!, menuItem => menuItem.Label == "[[LogTable_GroupBy]]");

        await item.OnClickAsync!();

        _logTableCommands.Received().SetGroupBy(null);
    }

    [Fact]
    public void CellContextMenu_NonGroupableCell_OmitsDirectGroupByItemButKeepsGroupBySubmenu()
    {
        var cut = RenderTable(
            groupBy: null,
            ImmutableHashSet<string>.Empty,
            orderBy: ColumnName.Level,
            [ColumnName.Level, ColumnName.DateAndTime],
            Event(1, "Alpha"));

        OpenMenu(cut, "tbody tr.table-row td:nth-child(2)");

        Assert.DoesNotContain(
            _capturedMenu!,
            item => item.Label.StartsWith("[[LogTable_GroupByColumn(", StringComparison.Ordinal));
        Assert.DoesNotContain(
            _capturedMenu!,
            item => item.Label.StartsWith("[[LogTable_UnGroupByColumn(", StringComparison.Ordinal));
        AssertMenuContains("[[LogTable_GroupBy]]");
        AssertMenuContains("[[LogTable_GroupByNone]]", ChildrenOf("[[LogTable_GroupBy]]"));
    }

    [Fact]
    public void CellFilterMenu_RendersColumnAndLevelValueMarkers()
    {
        var @event = Event(1, "Alpha") with { Level = "Information" };
        var cut = RenderTable(groupBy: null, ImmutableHashSet<string>.Empty, orderBy: ColumnName.Level, [ColumnName.Level], @event);

        OpenMenu(cut, "tbody tr.table-row td");

        AssertMenuContains("[[CellFilter_IncludeWhereEquals([[Column_Level]]|[[Severity_Level_Information]])]]");
        AssertMenuContains("[[CellFilter_ExcludeWhereEquals([[Column_Level]]|[[Severity_Level_Information]])]]");
    }

    [Fact]
    public void ColumnDisplaySites_RenderColumnMarkers()
    {
        var cut = RenderTable(groupBy: null, ImmutableHashSet<string>.Empty, orderBy: ColumnName.Source, [ColumnName.Source, ColumnName.Level], Event(1, "Alpha"));

        Assert.Contains("[[Column_Source]]", cut.Find("th.source").TextContent);
        Assert.Contains("[[Column_Level]]", cut.Find("th.level").TextContent);

        OpenMenu(cut, "thead");

        AssertMenuContains("[[Column_Source]]");
        AssertMenuContains("[[Column_Level]]");
        AssertMenuContains("[[Column_Source]]", ChildrenOf("[[LogTable_OrderBy]]"));
        AssertMenuContains("[[Column_Level]]", ChildrenOf("[[LogTable_OrderBy]]"));
        AssertMenuContains("[[Column_Source]]", ChildrenOf("[[LogTable_GroupBy]]"));
        AssertMenuContains("[[Column_Level]]", ChildrenOf("[[LogTable_GroupBy]]"));
    }

    [Fact]
    public void ColumnMenu_GroupBySubmenu_ContainsOnlyGroupableColumnMarkers()
    {
        var cut = RenderTable(groupBy: null, ImmutableHashSet<string>.Empty, orderBy: ColumnName.Source, Event(1, "Alpha"));

        OpenMenu(cut, "thead");

        var groupBy = ChildrenOf("[[LogTable_GroupBy]]");
        AssertMenuContains("[[Column_Source]]", groupBy);
        AssertMenuContains("[[Column_Level]]", groupBy);
        AssertMenuContains("[[Column_EventId]]", groupBy);
        Assert.DoesNotContain(groupBy, item => item.Label == "[[Column_DateAndTime]]");
        AssertMenuContains("[[Column_DateAndTime]]");
        AssertMenuContains("[[Column_DateAndTime]]", ChildrenOf("[[LogTable_OrderBy]]"));
    }

    [Fact]
    public async Task ColumnMenu_OrderByDefault_DispatchesSetOrderByNull()
    {
        var cut = RenderTable(groupBy: null, ImmutableHashSet<string>.Empty, orderBy: ColumnName.Source, Event(1, "Alpha"));

        OpenMenu(cut, "thead");
        _logTableCommands.ClearReceivedCalls();
        var defaultItem = ChildrenOf("[[LogTable_OrderBy]]").Single(item => item.Label == "[[LogTable_OrderByDefault]]");

        await defaultItem.OnClickAsync!();

        _logTableCommands.Received().SetOrderBy(null);
    }

    [Fact]
    public void ColumnMenu_RoutesEveryChromeItemThroughMarkerLocalizer()
    {
        var cut = RenderTable(groupBy: null, ImmutableHashSet<string>.Empty, orderBy: ColumnName.Source, Event(1, "Alpha"));

        OpenMenu(cut, "thead");

        AssertMenuContains("[[LogTable_OrderBy]]");
        AssertMenuContains("[[LogTable_OrderByDefault]]", ChildrenOf("[[LogTable_OrderBy]]"));
        AssertMenuContains("[[LogTable_GroupBy]]");
        AssertMenuContains("[[LogTable_GroupByNone]]", ChildrenOf("[[LogTable_GroupBy]]"));
        AssertMenuContains("[[LogTable_ResetColumnDefaults]]");
    }

    [Fact]
    public void EventContextMenu_RoutesEveryEventActionThroughMarkerLocalizer()
    {
        var @event = Event(1, "Alpha");
        var cut = RenderTable(groupBy: null, ImmutableHashSet<string>.Empty, orderBy: ColumnName.Source, @event);
        _selectedEvent.Current.Returns(Focus(@event, 0));

        OpenMenu(cut, "tbody");

        AssertMenuContains("[[Menu_Edit_CopySelected]]");
        AssertMenuContains("[[Menu_Edit_CopySelectedSimple]]");
        AssertMenuContains("[[Menu_Edit_CopySelectedXml]]");
        AssertMenuContains("[[Menu_Edit_CopySelectedFull]]");
        AssertMenuContains("[[LogTable_ExcludeEventsBefore]]");
        AssertMenuContains("[[LogTable_ExcludeEventsAfter]]");
        AssertMenuContains("[[LogTable_ShowRelatedByActivityId]]");
        AssertMenuContains("[[LogTable_ShowSharingRelatedActivityId]]");
        AssertMenuContains("[[LogTable_ShowParentActivity]]");
        Assert.Contains(_capturedMenu!, item => item.DisabledReason == "[[LogTable_NoActivityIdReason]]");
        Assert.Contains(_capturedMenu!, item => item.DisabledReason == "[[LogTable_NoRelatedActivityIdReason]]");

        var nearTime = ChildrenOf("[[LogTable_ShowEventsNearTime]]");
        AssertMenuContains("[[LogTable_NearTime_30Seconds]]", nearTime);
        AssertMenuContains("[[LogTable_NearTime_1Minute]]", nearTime);
        AssertMenuContains("[[LogTable_NearTime_5Minutes]]", nearTime);
        AssertMenuContains("[[LogTable_NearTime_15Minutes]]", nearTime);
        AssertMenuContains("[[LogTable_NearTime_1Hour]]", nearTime);

        AssertMenuContains("[[LogTable_GroupBy]]");
        AssertMenuContains("[[LogTable_GroupByNone]]", ChildrenOf("[[LogTable_GroupBy]]"));

        var moreFields = ChildrenOf("[[LogTable_MoreFields]]");
        AssertMenuContains("[[LogTable_Include]]", moreFields);
        AssertMenuContains("[[LogTable_Exclude]]", moreFields);
        Assert.Contains(moreFields.SelectMany(item => item.Children ?? []), item => item.DisabledReason == "[[LogTable_NoCellValue]]");
    }

    [Fact]
    public void EventContextMenu_RowMenu_IncludesGroupBySubmenuWithoutDirectGroupByItem()
    {
        var @event = Event(1, "Alpha");
        var cut = RenderTable(groupBy: null, ImmutableHashSet<string>.Empty, orderBy: ColumnName.Source, @event);
        _selectedEvent.Current.Returns(Focus(@event, 0));

        OpenMenu(cut, "tbody");

        AssertMenuContains("[[LogTable_GroupBy]]");
        AssertMenuContains("[[LogTable_GroupByNone]]", ChildrenOf("[[LogTable_GroupBy]]"));
        Assert.DoesNotContain(
            _capturedMenu!,
            item => item.Label.StartsWith("[[LogTable_GroupByColumn(", StringComparison.Ordinal));
    }

    [Fact]
    public void EventFieldIncludeExcludeMenu_RendersPropertyMarkers()
    {
        var @event = Event(1, "Alpha");
        var cut = RenderTable(groupBy: null, ImmutableHashSet<string>.Empty, orderBy: ColumnName.Source, @event);
        _selectedEvent.Current.Returns(Focus(@event, 0));

        OpenMenu(cut, "tbody");

        var moreFields = ChildrenOf("[[LogTable_MoreFields]]");
        var include = moreFields.Single(item => item.Label == "[[LogTable_Include]]").Children!;
        var exclude = moreFields.Single(item => item.Label == "[[LogTable_Exclude]]").Children!;

        AssertMenuContains("[[FilterLens_Property_Level]]", include);
        AssertMenuContains("[[FilterLens_Property_TaskCategory]]", include);
        AssertMenuContains("[[FilterLens_Property_Level]]", exclude);
        AssertMenuContains("[[FilterLens_Property_TaskCategory]]", exclude);
    }

    [Fact]
    public void Find_MatchesAndHighlightsLocalizedLevelDisplayText()
    {
        var @event = Event(1, "Alpha") with { Level = "Information" };
        var cut = RenderTable(groupBy: null, ImmutableHashSet<string>.Empty, orderBy: ColumnName.Level, [ColumnName.Level], @event);

        OpenFind(cut);
        cut.Find(".find-input").Input("[[Severity_Level_Information]]");

        cut.WaitForAssertion(() =>
        {
            var rows = cut.FindAll("tr[data-find]");
            Assert.Single(rows);
            Assert.Equal("current", rows[0].GetAttribute("data-find"));
            Assert.Equal("[[Severity_Level_Information]]", cut.Find("mark.find-mark").TextContent);
        }, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task GroupByMenu_WhenCommittedColumnClicked_DispatchesSetGroupBy()
    {
        var cut = RenderTable(groupBy: ColumnName.Source, ImmutableHashSet<string>.Empty, orderBy: ColumnName.Source, Event(1, "Alpha"));

        OpenMenu(cut, "thead");
        _logTableCommands.ClearReceivedCalls();
        var source = ChildrenOf("[[LogTable_GroupBy]]").Single(item => item.Label == "[[Column_Source]]");

        await source.OnClickAsync!();

        _logTableCommands.Received().SetGroupBy(ColumnName.Source);
    }

    [Fact]
    public async Task GroupByMenu_WhenNoneClickedWhileUngrouped_DispatchesSetGroupBy()
    {
        var cut = RenderTable(groupBy: null, ImmutableHashSet<string>.Empty, orderBy: ColumnName.Source, Event(1, "Alpha"));

        OpenMenu(cut, "thead");
        _logTableCommands.ClearReceivedCalls();
        var none = ChildrenOf("[[LogTable_GroupBy]]").Single(item => item.Label == "[[LogTable_GroupByNone]]");

        await none.OnClickAsync!();

        _logTableCommands.Received().SetGroupBy(null);
    }

    [Fact]
    public void GroupContextMenu_RoutesEveryGroupActionThroughMarkerLocalizer()
    {
        var expanded = RenderTable(ColumnName.Source, ImmutableHashSet<string>.Empty, orderBy: ColumnName.Source, Event(1, "Alpha"));
        OpenMenu(expanded, "tr.group-header-row");

        AssertMenuContains("[[LogTable_CollapseGroup]]");
        AssertMenuContains("[[Menu_View_ExpandAllGroups]]");
        AssertMenuContains("[[Menu_View_CollapseAllGroups]]");
        AssertMenuContains("[[Menu_View_GroupDescending]]");
        AssertMenuContains("[[LogTable_SelectGroup]]");
        AssertMenuContains("[[LogTable_UnGroupByColumn([[Column_Source]])]]");

        var collapsed = RenderTable(ColumnName.Source, ImmutableHashSet.Create(StringComparer.Ordinal, "Alpha"), orderBy: ColumnName.Source, Event(1, "Alpha"));
        OpenMenu(collapsed, "tr.group-header-row");

        AssertMenuContains("[[LogTable_ExpandGroup]]");
    }

    [Fact]
    public async Task GroupContextMenu_Ungroup_DispatchesSetGroupByNull()
    {
        var cut = RenderTable(ColumnName.Source, ImmutableHashSet<string>.Empty, orderBy: ColumnName.Source, Event(1, "Alpha"));

        OpenMenu(cut, "tr.group-header-row");
        _logTableCommands.ClearReceivedCalls();
        var item = _capturedMenu!.Single(item => item.Label == "[[LogTable_UnGroupByColumn([[Column_Source]])]]");

        await item.OnClickAsync!();

        _logTableCommands.Received().SetGroupBy(null);
    }

    [Fact]
    public void GroupHeader_WhenGroupValueIsEmpty_RoutesPlaceholderThroughMarkerLocalizer()
    {
        var cut = RenderTable(ColumnName.Source, ImmutableHashSet<string>.Empty, orderBy: ColumnName.Source, Event(1, string.Empty));

        Assert.Contains("[[LogTable_GroupValueNone]]", cut.Find("tr.group-header-row").TextContent);
    }

    [Fact]
    public void GroupHeader_WhenGroupedByLevel_RendersColumnAndSeverityMarkers()
    {
        var @event = Event(1, "Alpha") with { Level = "Information" };
        var cut = RenderTable(ColumnName.Level, ImmutableHashSet<string>.Empty, orderBy: ColumnName.Level, [ColumnName.Level], @event);

        string header = cut.Find("tr.group-header-row").TextContent;

        Assert.Contains("[[Column_Level]]", header);
        Assert.Contains("[[Severity_Level_Information]]", header);
    }

    [Fact]
    public void LevelCell_RendersSeverityMarker()
    {
        var @event = Event(1, "Alpha") with { Level = "Information" };
        var cut = RenderTable(groupBy: null, ImmutableHashSet<string>.Empty, orderBy: ColumnName.Level, [ColumnName.Level], @event);

        Assert.Contains("[[Severity_Level_Information]]", cut.Find("tbody tr.table-row td").TextContent);
    }

    [Fact]
    public void TableDom_RoutesAriaSortAndDescriptionHeaderThroughMarkerLocalizer()
    {
        var cut = RenderTable(groupBy: null, ImmutableHashSet<string>.Empty, orderBy: ColumnName.Source, Event(1, "Alpha"));

        Assert.Equal("[[Table_Aria]]", cut.Find("table#eventTable").GetAttribute("aria-label"));
        Assert.Equal("[[Table_ToggleSortAria]]", cut.Find("button.menu-toggle").GetAttribute("aria-label"));
        Assert.Contains("[[Table_ColumnHeader_Description]]", cut.Find("th.description").TextContent);
    }

    private static void AssertMenuContains(string label, IReadOnlyList<MenuItem> items) =>
        Assert.Contains(items, item => item.Label == label);

    private static ResolvedEvent Event(int id, string source) =>
        new(LogName, LogPathType.Channel)
        {
            Id = id,
            RecordId = id,
            Source = source,
            TimeCreated = new DateTime(2024, 1, 1, 0, 0, id, DateTimeKind.Utc),
            Description = $"event {id}"
        };

    private void AssertMenuContains(string label) => AssertMenuContains(label, _capturedMenu!);

    private IReadOnlyList<MenuItem> ChildrenOf(string label)
    {
        var item = _capturedMenu!.First(item => item.Label == label);
        Assert.NotNull(item.Children);
        return item.Children!;
    }

    private SelectionEntry Focus(ResolvedEvent evt, int physicalIndex)
    {
        var handle = new EventLocator(_logId, 0, physicalIndex);
        ValueKey.TryCreate(evt, out var reloadKey);
        return new SelectionEntry(handle, handle, reloadKey);
    }

    private void OpenFind(IRenderedComponent<LogTablePane> cut)
    {
        var coordinator = Services.GetRequiredService<IFindCoordinator>();
        cut.InvokeAsync(() => coordinator.RequestOpen());
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".find-input")));
    }

    private void OpenMenu(IRenderedComponent<LogTablePane> cut, string selector)
    {
        _capturedMenu = null;
        cut.Find(selector).TriggerEvent("oncontextmenu", new MouseEventArgs());
        Assert.NotNull(_capturedMenu);
    }

    private IRenderedComponent<LogTablePane> RenderTable(
        ColumnName? groupBy,
        ImmutableHashSet<string> collapsed,
        ColumnName? orderBy,
        params ResolvedEvent[] events) =>
        RenderTable(groupBy, collapsed, orderBy, [ColumnName.Source, ColumnName.DateAndTime], events);

    private IRenderedComponent<LogTablePane> RenderTable(
        ColumnName? groupBy,
        ImmutableHashSet<string> collapsed,
        ColumnName? orderBy,
        ImmutableList<ColumnName> columns,
        params ResolvedEvent[] events)
    {
        _columnDefaults.ColumnOrder.Returns(columns);
        _presentation = DisplayViewTestFactory.Presentation(
            _logId,
            events,
            orderBy,
            isDescending: false,
            groupBy,
            groupCollapseOverrides: collapsed,
            revision: ++_presentationRevision);

        _logTableState.Value.Returns(new LogTableState
        {
            ActiveEventLogId = _logId,
            EventTables = ImmutableList.Create(new LogView(_logId) { LogName = LogName }),
            Columns = columns.ToImmutableDictionary(column => column, _ => true),
            ColumnOrder = columns,
            OrderBy = orderBy,
            GroupBy = groupBy,
            GroupCollapseOverrides = collapsed
        });

        return Render<LogTablePane>();
    }
}
