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
    private readonly EventLogId _logId = EventLogId.Create();
    private readonly IState<LogTableState> _logTableState = Substitute.For<IState<LogTableState>>();
    private readonly IEventFocusSource _selectedEvent = Substitute.For<IEventFocusSource>();
    private readonly IEventSelectionSource _selectedEvents = Substitute.For<IEventSelectionSource>();
    private readonly ISettingsService _settings = Substitute.For<ISettingsService>();
    private readonly IOrderedViewSource _viewSource = Substitute.For<IOrderedViewSource>();

    private SelectionEntry? _dispatchedActive;
    private IReadOnlyCollection<SelectionEntry>? _dispatchedEvents;

    private OrderedViewPresentation? _presentation;

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

        _selectedEvent.Current.Returns((SelectionEntry?)null);
        SetSelection();

        _eventLogCommands
            .When(c => c.SetSelectedEvents(Arg.Any<IReadOnlyCollection<SelectionEntry>>(), Arg.Any<SelectionEntry?>()))
            .Do(callInfo =>
            {
                _dispatchedEvents = callInfo.Arg<IReadOnlyCollection<SelectionEntry>>();
                _dispatchedActive = callInfo.Arg<SelectionEntry?>();
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

        _viewSource.Current.Returns(_ => _presentation);
        Services.AddSingleton(_viewSource);

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
            UserDisplayName = @"NT AUTHORITY\SYSTEM",
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
        Assert.Equal(@"NT AUTHORITY\SYSTEM", Cell(row, 12).TextContent.Trim());
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
        Assert.Equal(
            expectedEvents.Select(testEvent => testEvent.RecordId!.Value).ToArray(),
            _dispatchedEvents!.Select(entry => entry.ReloadKey!.Value.RecordId).ToArray());
        Assert.Equal(expectedActive?.RecordId, _dispatchedActive?.ReloadKey?.RecordId);
    }

    private SelectionEntry EntryFor(ResolvedEvent evt)
    {
        var handle = new EventLocator(_logId, 0, (int)(evt.RecordId!.Value - 1));
        ValueKey.TryCreate(evt, out var reloadKey);
        return new SelectionEntry(handle, handle, reloadKey);
    }

    private IRenderedComponent<LogTablePane> RenderAllColumns(
        ResolvedEvent displayedEvent, ColumnName? orderBy = null, bool isDescending = false)
    {
        ColumnName[] allColumns =
        [
            ColumnName.Level, ColumnName.DateAndTime, ColumnName.ActivityId, ColumnName.Log,
            ColumnName.ComputerName, ColumnName.Source, ColumnName.EventId, ColumnName.TaskCategory,
            ColumnName.Keywords, ColumnName.ProcessId, ColumnName.ThreadId, ColumnName.User
        ];

        _presentation = DisplayViewTestFactory.Presentation(
            _logId, [displayedEvent], orderBy: orderBy, isDescending: isDescending) with
        {
            Columns = allColumns.ToImmutableDictionary(column => column, _ => true),
            ColumnOrder = [.. allColumns]
        };

        _logTableState.Value.Returns(new LogTableState
        {
            ActiveEventLogId = _logId,
            EventTables = ImmutableList.Create(new LogView(_logId) { LogName = LogName }),
            Columns = allColumns.ToImmutableDictionary(column => column, _ => true),
            ColumnOrder = [.. allColumns],
            OrderBy = orderBy,
            IsDescending = isDescending
        });

        return Render<LogTablePane>();
    }

    private IReadOnlyList<IElement> RenderRows()
    {
        _presentation = DisplayViewTestFactory.Presentation(_logId, [_event1, _event2, _event3]);

        _logTableState.Value.Returns(new LogTableState
        {
            ActiveEventLogId = _logId,
            EventTables = ImmutableList.Create(new LogView(_logId) { LogName = LogName }),
            Columns = ImmutableDictionary<ColumnName, bool>.Empty.Add(ColumnName.Level, true),
            ColumnOrder = ImmutableList.Create(ColumnName.Level),
            IsDescending = false
        });

        var cut = Render<LogTablePane>();

        return cut.FindAll("tbody tr[role=row]");
    }

    private void SetSelection(params ResolvedEvent[] selection) =>
        _selectedEvents.Current.Returns(selection.Select(EntryFor).ToImmutableList());
}
