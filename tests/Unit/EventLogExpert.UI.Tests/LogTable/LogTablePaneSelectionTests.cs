// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using AngleSharp.Dom;
using Bunit;
using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.FilterPane;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.Settings;
using EventLogExpert.UI.LogTable;
using EventLogExpert.UI.Tests.TestUtils;
using Fluxor;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using System.Collections.Immutable;
using System.Globalization;
using System.Security.Principal;

namespace EventLogExpert.UI.Tests.LogTable;

public sealed class LogTablePaneSelectionTests : BunitContext
{
    private const string LogName = "Application";

    private readonly ILogTableColumnDefaultsProvider _columnDefaults = Substitute.For<ILogTableColumnDefaultsProvider>();
    private readonly ResolvedEvent _event1 = NewEvent(1);
    private readonly ResolvedEvent _event2 = NewEvent(2);
    private readonly ResolvedEvent _event3 = NewEvent(3);
    private readonly IEventLogCommands _eventLogCommands = Substitute.For<IEventLogCommands>();
    private readonly IState<FilterPaneState> _filterPaneState = Substitute.For<IState<FilterPaneState>>();
    private readonly IHighlightSelector _highlightSelector = Substitute.For<IHighlightSelector>();
    private readonly IState<LogTableState> _logTableState = Substitute.For<IState<LogTableState>>();
    private readonly IStateSelection<EventLogState, ResolvedEvent?> _selectedEvent = Substitute.For<IStateSelection<EventLogState, ResolvedEvent?>>();
    private readonly IStateSelection<EventLogState, ImmutableList<ResolvedEvent>> _selectedEvents = Substitute.For<IStateSelection<EventLogState, ImmutableList<ResolvedEvent>>>();
    private readonly ISettingsService _settings = Substitute.For<ISettingsService>();

    private ResolvedEvent? _dispatchedActive;
    private IReadOnlyCollection<ResolvedEvent>? _dispatchedEvents;

    public LogTablePaneSelectionTests()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.SetupModule("./_content/EventLogExpert.UI/LogTable/LogTablePane.razor.js");

        _columnDefaults.ColumnOrder.Returns(ImmutableList.Create(ColumnName.Level));

        _filterPaneState.Value.Returns(new FilterPaneState());
        _highlightSelector.Select(Arg.Any<ImmutableList<SavedFilter>>()).Returns([]);
        _highlightSelector.ComputeHighlightKey(Arg.Any<ImmutableList<SavedFilter>>()).Returns(0);
        _settings.TimeZoneInfo.Returns(TimeZoneInfo.Utc);

        _selectedEvent.Value.Returns((ResolvedEvent?)null);
        SetSelection();

        _eventLogCommands
            .When(c => c.SetSelectedEvents(Arg.Any<IReadOnlyCollection<ResolvedEvent>>(), Arg.Any<ResolvedEvent?>()))
            .Do(callInfo =>
            {
                _dispatchedEvents = callInfo.Arg<IReadOnlyCollection<ResolvedEvent>>();
                _dispatchedActive = callInfo.Arg<ResolvedEvent?>();
            });

        Services.AddLogTablePaneDependencies();
        Services.AddSingleton(_columnDefaults);
        Services.AddSingleton(_eventLogCommands);
        Services.AddSingleton(_filterPaneState);
        Services.AddSingleton(_highlightSelector);
        Services.AddSingleton(_logTableState);
        Services.AddSingleton(_selectedEvent);
        Services.AddSingleton(_selectedEvents);
        Services.AddSingleton(_settings);

        Services.AddFluxor(options => options.ScanAssemblies(typeof(LogTablePane).Assembly));
    }

    [Fact]
    public void AllColumnsEnabled_EachCellRendersItsMappedEventProperty()
    {
        var activityId = Guid.NewGuid();
        var userId = new SecurityIdentifier("S-1-5-18");
        var displayedEvent = new ResolvedEvent("Application", LogPathType.Channel)
        {
            Level = "Warning",
            TimeCreated = new DateTime(2024, 6, 15, 9, 30, 0, DateTimeKind.Utc),
            ActivityId = activityId,
            ComputerName = "TEST-PC",
            Source = "TestSource",
            Id = 4242,
            TaskCategory = "TestCategory",
            Keywords = ["AuditKeyword"],
            ProcessId = 111,
            ThreadId = 222,
            UserId = userId,
            Description = "test description"
        };

        var row = RenderAllColumns(displayedEvent).Find("tbody tr[role=row]");

        Assert.Equal(13, row.QuerySelectorAll("td").Length);
        Assert.Contains("warning", Cell(row, 1).QuerySelector("span")!.ClassName);
        Assert.Contains("Warning", Cell(row, 1).TextContent);
        Assert.Contains("2024", Cell(row, 2).TextContent);
        Assert.Equal(activityId.ToString(), Cell(row, 3).TextContent.Trim());
        Assert.Equal("Application", Cell(row, 4).TextContent.Trim());
        Assert.Equal("TEST-PC", Cell(row, 5).TextContent.Trim());
        Assert.Equal("TestSource", Cell(row, 6).TextContent.Trim());
        Assert.Equal("4242", Cell(row, 7).TextContent.Trim());
        Assert.Equal("TestCategory", Cell(row, 8).TextContent.Trim());
        Assert.Equal("AuditKeyword", Cell(row, 9).TextContent.Trim());
        Assert.Equal("111", Cell(row, 10).TextContent.Trim());
        Assert.Equal("222", Cell(row, 11).TextContent.Trim());
        Assert.Equal(userId.ToString(), Cell(row, 12).TextContent.Trim());
        Assert.Equal("test description", Cell(row, 13).TextContent.Trim());
    }

    [Fact]
    public void Click_WithNoModifiers_ReplacesSelectionWithClickedEvent()
    {
        var rows = RenderRows();

        rows[1].MouseDown(new MouseEventArgs { Button = 0 });

        AssertDispatched([_event2], _event2);
    }

    [Fact]
    public void CtrlClick_AddsClickedEventToExistingSelection()
    {
        SetSelection(_event1);
        var rows = RenderRows();

        rows[1].MouseDown(new MouseEventArgs { Button = 0, CtrlKey = true });

        AssertDispatched([_event1, _event2], _event2);
    }

    [Fact]
    public void CtrlClick_RemovesClickedEventWhenAlreadySelected()
    {
        SetSelection(_event1, _event2);
        var rows = RenderRows();

        rows[1].MouseDown(new MouseEventArgs { Button = 0, CtrlKey = true });

        AssertDispatched([_event1], _event2);
    }

    [Fact]
    public void CtrlShiftClick_DeduplicatesEventsAlreadyInSelection()
    {
        SetSelection(_event2);
        var rows = RenderRows();

        rows[0].MouseDown(new MouseEventArgs { Button = 0 });
        rows[2].MouseDown(new MouseEventArgs { Button = 0, CtrlKey = true, ShiftKey = true });

        AssertDispatched([_event1, _event2, _event3], _event3);
    }

    [Fact]
    public void CtrlShiftClick_MergesExistingSelectionWithRange()
    {
        // Pins SelectEvent's merge formula (current selection + range): the mock doesn't propagate the
        // first plain click's dispatch, so the store stays frozen at [e3] and the merge yields
        // [e3] + range[e1,e2] = [e1,e2,e3].
        SetSelection(_event3);
        var rows = RenderRows();

        rows[0].MouseDown(new MouseEventArgs { Button = 0 });
        rows[1].MouseDown(new MouseEventArgs { Button = 0, CtrlKey = true, ShiftKey = true });

        AssertDispatched([_event1, _event2, _event3], _event2);
    }

    [Fact]
    public void Header_ShowsSortIndicatorOnlyOnTheOrderByColumn()
    {
        var cut = RenderAllColumns(NewEvent(1), orderBy: ColumnName.Source, isDescending: true);

        var sortedHeader = cut.Find("th[data-column='Source']");
        Assert.Equal("descending", sortedHeader.GetAttribute("aria-sort"));
        Assert.NotNull(sortedHeader.QuerySelector(".menu-toggle"));

        var unsortedHeader = cut.Find("th[data-column='Level']");
        Assert.Equal("none", unsortedHeader.GetAttribute("aria-sort"));
        Assert.Null(unsortedHeader.QuerySelector(".menu-toggle"));
    }

    [Fact]
    public void RightClickOnSelectedRow_PreservesSelectionAndMovesActive()
    {
        SetSelection(_event1, _event2);
        var rows = RenderRows();

        rows[1].MouseDown(new MouseEventArgs { Button = 2 });

        AssertDispatched([_event1, _event2], _event2);
    }

    [Fact]
    public void RightClickOnUnselectedRow_ReplacesSelectionWithClickedEvent()
    {
        SetSelection(_event1);
        var rows = RenderRows();

        rows[1].MouseDown(new MouseEventArgs { Button = 2 });

        AssertDispatched([_event2], _event2);
    }

    [Fact]
    public void ShiftClick_SelectsRangeFromAnchorToClickedEvent()
    {
        var rows = RenderRows();

        rows[0].MouseDown(new MouseEventArgs { Button = 0 });
        rows[2].MouseDown(new MouseEventArgs { Button = 0, ShiftKey = true });

        AssertDispatched([_event1, _event2, _event3], _event3);
    }

    [Fact]
    public void ShiftClick_WithoutAnchor_SelectsOnlyClickedEvent()
    {
        var rows = RenderRows();

        rows[1].MouseDown(new MouseEventArgs { Button = 0, ShiftKey = true });

        AssertDispatched([_event2], _event2);
    }

    private static IElement Cell(IElement row, int columnIndex) =>
        row.QuerySelector($"td[aria-colindex='{columnIndex}']")!;

    private static ResolvedEvent NewEvent(int id) =>
        new(LogName, LogPathType.Channel)
        {
            Id = id,
            RecordId = id,
            TimeCreated = new DateTime(2024, 1, 1, 0, 0, id, DateTimeKind.Utc),
            Description = $"event {id}",
            Level = "Information"
        };

    private void AssertDispatched(ResolvedEvent[] expectedEvents, ResolvedEvent? expectedActive)
    {
        Assert.NotNull(_dispatchedEvents);
        Assert.Equal(expectedEvents, _dispatchedEvents);
        Assert.Same(expectedActive, _dispatchedActive);
    }

    private IRenderedComponent<LogTablePane> RenderAllColumns(
        ResolvedEvent displayedEvent, ColumnName? orderBy = null, bool isDescending = false)
    {
        var logId = EventLogId.Create();
        ColumnName[] allColumns =
        [
            ColumnName.Level, ColumnName.DateAndTime, ColumnName.ActivityId, ColumnName.Log,
            ColumnName.ComputerName, ColumnName.Source, ColumnName.EventId, ColumnName.TaskCategory,
            ColumnName.Keywords, ColumnName.ProcessId, ColumnName.ThreadId, ColumnName.User
        ];

        _logTableState.Value.Returns(new LogTableState
        {
            ActiveEventLogId = logId,
            EventTables = ImmutableList.Create(new LogView(logId) { LogName = LogName }),
            EventCountByLog = ImmutableDictionary<EventLogId, int>.Empty.Add(logId, 1),
            Columns = allColumns.ToImmutableDictionary(column => column, _ => true),
            ColumnOrder = [.. allColumns],
            OrderBy = orderBy,
            IsDescending = isDescending
        }.WithLogEvents(logId, displayedEvent));

        return Render<LogTablePane>();
    }

    private IReadOnlyList<IElement> RenderRows()
    {
        var logId = EventLogId.Create();

        _logTableState.Value.Returns(new LogTableState
        {
            ActiveEventLogId = logId,
            EventTables = ImmutableList.Create(new LogView(logId) { LogName = LogName }),
            EventCountByLog = ImmutableDictionary<EventLogId, int>.Empty.Add(logId, 3),
            Columns = ImmutableDictionary<ColumnName, bool>.Empty.Add(ColumnName.Level, true),
            ColumnOrder = ImmutableList.Create(ColumnName.Level),
            IsDescending = false
        }.WithLogEvents(logId, _event1, _event2, _event3));

        var cut = Render<LogTablePane>();

        return cut.FindAll("tbody tr[role=row]");
    }

    private void SetSelection(params ResolvedEvent[] selection) =>
        _selectedEvents.Value.Returns(ImmutableList.Create(selection));
}
