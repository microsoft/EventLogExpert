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
    public void ColumnMenu_RoutesEveryChromeItemThroughMarkerLocalizer()
    {
        var cut = RenderTable(groupBy: null, ImmutableHashSet<string>.Empty, orderBy: ColumnName.Source, Event(1, "Alpha"));

        OpenMenu(cut, "thead");

        AssertMenuContains("[[LogTable_OrderBy]]");
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

        var moreFields = ChildrenOf("[[LogTable_MoreFields]]");
        AssertMenuContains("[[LogTable_Include]]", moreFields);
        AssertMenuContains("[[LogTable_Exclude]]", moreFields);
        Assert.Contains(moreFields.SelectMany(item => item.Children ?? []), item => item.DisabledReason == "[[LogTable_NoCellValue]]");
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

        var collapsed = RenderTable(ColumnName.Source, ImmutableHashSet.Create(StringComparer.Ordinal, "Alpha"), orderBy: ColumnName.Source, Event(1, "Alpha"));
        OpenMenu(collapsed, "tr.group-header-row");

        AssertMenuContains("[[LogTable_ExpandGroup]]");
    }

    [Fact]
    public void GroupHeader_WhenGroupValueIsEmpty_RoutesPlaceholderThroughMarkerLocalizer()
    {
        var cut = RenderTable(ColumnName.Source, ImmutableHashSet<string>.Empty, orderBy: ColumnName.Source, Event(1, string.Empty));

        Assert.Contains("[[LogTable_GroupValueNone]]", cut.Find("tr.group-header-row").TextContent);
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
        params ResolvedEvent[] events)
    {
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
            Columns = ImmutableDictionary<ColumnName, bool>.Empty
                .Add(ColumnName.Source, true)
                .Add(ColumnName.DateAndTime, true),
            ColumnOrder = ImmutableList.Create(ColumnName.Source, ColumnName.DateAndTime),
            OrderBy = orderBy,
            GroupBy = groupBy,
            GroupCollapseOverrides = collapsed
        });

        return Render<LogTablePane>();
    }
}
