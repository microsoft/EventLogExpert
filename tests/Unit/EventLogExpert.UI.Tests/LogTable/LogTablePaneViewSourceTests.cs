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
using EventLogExpert.Runtime.Settings;
using EventLogExpert.UI.LogTable;
using EventLogExpert.UI.Menu;
using EventLogExpert.UI.Tests.TestUtils;
using Fluxor;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using System.Collections.Immutable;
using System.Globalization;

namespace EventLogExpert.UI.Tests.LogTable;

public sealed class LogTablePaneViewSourceTests : BunitContext
{
    private const string LogName = "Application";

    private static readonly ImmutableDictionary<ColumnName, bool> s_dateColumn =
        ImmutableDictionary<ColumnName, bool>.Empty.Add(ColumnName.DateAndTime, true);

    private static readonly ImmutableDictionary<ColumnName, bool> s_sourceColumn =
        ImmutableDictionary<ColumnName, bool>.Empty.Add(ColumnName.Source, true);

    private readonly ILogTableColumnDefaultsProvider _columnDefaults = Substitute.For<ILogTableColumnDefaultsProvider>();
    private readonly IEventLogCommands _eventLogCommands = Substitute.For<IEventLogCommands>();
    private readonly IState<FilterPaneState> _filterPaneState = Substitute.For<IState<FilterPaneState>>();
    private readonly IActiveFiltersSource _filterSelection = Substitute.For<IActiveFiltersSource>();
    private readonly IHighlightSelector _highlightSelector = Substitute.For<IHighlightSelector>();
    private readonly EventLogId _logId = EventLogId.Create();
    private readonly ILogTableCommands _logTableCommands = Substitute.For<ILogTableCommands>();
    private readonly IState<LogTableState> _logTableState = Substitute.For<IState<LogTableState>>();
    private readonly IMenuService _menuService = Substitute.For<IMenuService>();
    private readonly IRevealFocusSource _revealFocus = Substitute.For<IRevealFocusSource>();
    private readonly IEventFocusSource _selectedEvent = Substitute.For<IEventFocusSource>();
    private readonly IEventSelectionSource _selectedEvents = Substitute.For<IEventSelectionSource>();
    private readonly ISettingsService _settings = Substitute.For<ISettingsService>();
    private readonly BunitJSModuleInterop _tableJsModule;
    private readonly IOrderedViewSource _viewSource = Substitute.For<IOrderedViewSource>();

    public LogTablePaneViewSourceTests()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        JSInterop.Mode = JSRuntimeMode.Loose;
        _tableJsModule = JSInterop.SetupModule("./_content/EventLogExpert.UI/LogTable/LogTablePane.razor.js");

        _columnDefaults.ColumnOrder.Returns(ImmutableList.Create(ColumnName.Source));
        _filterPaneState.Value.Returns(new FilterPaneState());
        _filterSelection.Current.Returns(ImmutableList<SavedFilter>.Empty);
        _highlightSelector.Select(Arg.Any<ImmutableList<SavedFilter>>()).Returns([]);
        _highlightSelector.ComputeHighlightKey(Arg.Any<ImmutableList<SavedFilter>>()).Returns(0);
        _settings.TimeZoneInfo.Returns(TimeZoneInfo.Utc);
        _selectedEvent.Current.Returns((SelectionEntry?)null);
        _selectedEvents.Current.Returns(ImmutableList<SelectionEntry>.Empty);
        _revealFocus.Current.Returns((EventLocator?)null);

        _eventLogCommands
            .When(commands => commands.ConsumeRevealFocus(Arg.Any<EventLocator>()))
            .Do(call =>
            {
                if (_revealFocus.Current == call.Arg<EventLocator>())
                {
                    _revealFocus.Current.Returns((EventLocator?)null);
                }
            });

        Services.AddLogTablePaneDependencies();
        Services.AddSingleton(_columnDefaults);
        Services.AddSingleton(_eventLogCommands);
        Services.AddSingleton(_filterPaneState);
        Services.AddSingleton(_filterSelection);
        Services.AddSingleton(_highlightSelector);
        Services.AddSingleton(_logTableState);
        Services.AddSingleton(_selectedEvent);
        Services.AddSingleton(_selectedEvents);
        Services.AddSingleton(_settings);
        Services.AddSingleton(_logTableCommands);
        Services.AddSingleton(_menuService);

        Services.AddSingleton(_revealFocus);

        Services.AddSingleton(_viewSource);

        Services.AddFluxor(options => options.ScanAssemblies(typeof(LogTablePane).Assembly));
    }

    [Fact]
    public async Task ACommittedStateChangeAlone_DoesNotRepaintTheDecoupledGrid()
    {
        var otherTabId = EventLogId.Create();

        SetCommittedState(_logId, [_logId, otherTabId], Event(1, "Alpha"));
        SetPresentation(_logId, Event(1, "Alpha"));

        var cut = Render<LogTablePane>();

        Assert.Contains("Alpha", cut.Markup);

        SetCommittedState(otherTabId, [_logId, otherTabId], Event(9, "Zeta"));
        await cut.InvokeAsync(() => _logTableState.StateChanged += Raise.Event<EventHandler>(_logTableState, EventArgs.Empty));

        Assert.Contains("Alpha", cut.Markup);
        Assert.DoesNotContain("Zeta", cut.Markup);
    }

    [Fact]
    public void AFirstSameViewPublication_DoesNotRescrollToTheSelection()
    {
        SetCommittedState(_logId, [_logId], Event(1, "Alpha"), Event(2, "Beta"));

        var presentation = DisplayViewTestFactory.Presentation(_logId, [Event(1, "Alpha"), Event(2, "Beta")]);
        _viewSource.Current.Returns(presentation);
        _selectedEvents.Current.Returns(ImmutableList.Create(EntryFor(Event(2, "Beta"))));

        var cut = Render<LogTablePane>();

        Assert.Single(cut.FindAll("thead th[data-column]"));
        int scrollsAfterRender = ScrollCount();

        var sameRowsNewColumn = presentation with
        {
            Revision = 2,
            ColumnOrder = ImmutableList.Create(ColumnName.Source, ColumnName.EventId)
        };

        _viewSource.Current.Returns(sameRowsNewColumn);
        cut.InvokeAsync(() => _viewSource.Updated += Raise.Event<Action<OrderedViewPresentation>>(sameRowsNewColumn));

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("thead th[data-column]").Count));
        Assert.Equal(scrollsAfterRender, ScrollCount());
    }

    [Fact]
    public void AFocusChange_RepaintsTheDecoupledPane_MovingTheKeyboardCursor()
    {
        SetCommittedState(_logId, [_logId], Event(1, "Alpha"), Event(2, "Beta"), Event(3, "Gamma"));
        _viewSource.Current.Returns(
            DisplayViewTestFactory.Presentation(_logId, [Event(1, "Alpha"), Event(2, "Beta"), Event(3, "Gamma")]));

        var cut = Render<LogTablePane>();

        var focusEntry = EntryFor(Event(2, "Beta"));
        _selectedEvent.Current.Returns(focusEntry);
        cut.InvokeAsync(() => _selectedEvent.Changed += Raise.Event<Action>());

        _eventLogCommands.ClearReceivedCalls();
        Press(cut, "ArrowDown");

        _eventLogCommands.Received(1).SetSelectedEvents(
            Arg.Any<IReadOnlyCollection<SelectionEntry>>(),
            Arg.Is<SelectionEntry?>(focus => focus!.Value.CurrentHandle!.Value.Index == 2));
    }

    [Fact]
    public void AHighlightColourChange_RepaintsRowHighlights_ThoughThePresentationIsUnchanged()
    {
        var red = SavedFilter.TryCreate("Id == 1", color: HighlightColor.LightRed, isEnabled: true)!;
        var blue = red with { Color = HighlightColor.LightBlue };

        _filterSelection.Current.Returns(ImmutableList.Create(red));
        _highlightSelector.Select(Arg.Any<ImmutableList<SavedFilter>>()).Returns([red]);
        _highlightSelector.ComputeHighlightKey(Arg.Any<ImmutableList<SavedFilter>>()).Returns(1);

        SetCommittedState(_logId, [_logId], Event(1, "Alpha"));
        SetPresentation(_logId, Event(1, "Alpha"));

        var cut = Render<LogTablePane>();

        Assert.Equal(HighlightColor.LightRed.ToCssName(), RowHighlight(cut, 0));

        _filterSelection.Current.Returns(ImmutableList.Create(blue));
        _highlightSelector.Select(Arg.Any<ImmutableList<SavedFilter>>()).Returns([blue]);
        _highlightSelector.ComputeHighlightKey(Arg.Any<ImmutableList<SavedFilter>>()).Returns(2);

        cut.InvokeAsync(() => _filterSelection.Changed += Raise.Event<Action>());

        cut.WaitForAssertion(() => Assert.Equal(HighlightColor.LightBlue.ToCssName(), RowHighlight(cut, 0)));
    }

    [Fact]
    public void APublicationAlone_RepaintsWithoutAnyCommittedStateChange()
    {
        SetCommittedState(_logId, [_logId], Event(1, "Alpha"));
        SetPresentation(_logId, Event(1, "Alpha"));

        var cut = Render<LogTablePane>();

        Assert.DoesNotContain("Delta", cut.Markup);

        var next = Presentation(_logId, revision: 2, Event(1, "Alpha"), Event(4, "Delta"));

        _viewSource.Current.Returns(next);
        cut.InvokeAsync(() => _viewSource.Updated += Raise.Event<Action<OrderedViewPresentation>>(next));

        cut.WaitForAssertion(() => Assert.Contains("Delta", cut.Markup));
    }

    [Fact]
    public void ASelectionChange_RepaintsTheDecoupledPane_ThoughNoPresentationIsPublished()
    {
        SetCommittedState(_logId, [_logId], Event(1, "Alpha"), Event(2, "Beta"));
        _viewSource.Current.Returns(DisplayViewTestFactory.Presentation(_logId, [Event(1, "Alpha"), Event(2, "Beta")]));

        var cut = Render<LogTablePane>();

        Assert.Equal("false", RowSelected(cut, 0));

        _selectedEvents.Current.Returns(ImmutableList.Create(EntryFor(Event(1, "Alpha"))));
        cut.InvokeAsync(() => _selectedEvents.Changed += Raise.Event<Action>());

        cut.WaitForAssertion(() => Assert.Equal("true", RowSelected(cut, 0)));
    }

    [Fact]
    public void ATimeZoneChange_RepaintsTheDateCells_ThoughThePresentationIsUnchanged()
    {
        var timedEvent = Event(1, "Alpha") with { TimeCreated = new DateTime(2020, 1, 1, 12, 0, 0, DateTimeKind.Utc) };

        SetCommittedState(_logId, [_logId], timedEvent);

        var presentation = new OrderedViewPresentation(
            DisplayViewTestFactory.Identity([timedEvent]),
            _logId,
            default,
            PresentationState.Current,
            Revision: 1)
        { Columns = s_dateColumn, ColumnOrder = ImmutableList.Create(ColumnName.DateAndTime) };

        _viewSource.Current.Returns(presentation);

        var cut = Render<LogTablePane>();

        string utcText = DateCell(cut);

        var plusFive = TimeZoneInfo.CreateCustomTimeZone("t+5", TimeSpan.FromHours(5), "t+5", "t+5");
        _settings.TimeZoneInfo.Returns(plusFive);

        cut.InvokeAsync(() => _settings.TimeZoneChanged?.Invoke(_settings, plusFive));

        cut.WaitForAssertion(() => Assert.NotEqual(utcText, DateCell(cut)));
    }

    [Fact]
    public void ATransientlyEmptyGroupedView_KeepsTheUsersPlaceToo()
    {
        SetCommittedState(_logId, [_logId], Event(1, "Alpha"), Event(2, "Alpha"), Event(3, "Alpha"));

        var served = new OrderedViewPresentation(
            DisplayViewTestFactory.Identity([Event(1, "Alpha"), Event(2, "Alpha"), Event(3, "Alpha")], ColumnName.Source),
            _logId,
            new DisplayOrdering(null, false, ColumnName.Source, false),
            PresentationState.Current,
            Revision: 1)
        { Columns = s_sourceColumn };

        _viewSource.Current.Returns(served);

        var cut = Render<LogTablePane>();

        Press(cut, "Home");
        Press(cut, "ArrowDown");
        Press(cut, "ArrowDown");

        Publish(cut, served with { View = DisplayViewTestFactory.Identity([], ColumnName.Source), State = PresentationState.Updating, Revision = 2 }, expectedRows: 0);

        Publish(cut, served with { Revision = 3 }, expectedRows: 4);

        _eventLogCommands.ClearReceivedCalls();

        Press(cut, "ArrowDown");

        _eventLogCommands.Received(1).SetSelectedEvents(
            Arg.Any<IReadOnlyCollection<SelectionEntry>>(),
            Arg.Is<SelectionEntry?>(focus => focus!.Value.CurrentHandle!.Value.Index == 2));
    }

    [Fact]
    public void ATransientlyEmptyView_KeepsTheUsersPlaceUntilTheAnswerSettles()
    {
        SetCommittedState(_logId, [_logId], Event(1, "Alpha"), Event(2, "Beta"), Event(3, "Gamma"));

        var served = Presentation(_logId, revision: 1, Event(1, "Alpha"), Event(2, "Beta"), Event(3, "Gamma"));
        _viewSource.Current.Returns(served);

        var cut = Render<LogTablePane>();

        Press(cut, "Home");
        Press(cut, "ArrowDown");

        Publish(cut, EmptyPresentation(revision: 2, PresentationState.Updating), expectedRows: 0);

        Publish(cut, served with { Revision = 3 }, expectedRows: 3);

        _eventLogCommands.ClearReceivedCalls();

        Press(cut, "ArrowDown");

        _eventLogCommands.Received(1).SetSelectedEvents(
            Arg.Any<IReadOnlyCollection<SelectionEntry>>(),
            Arg.Is<SelectionEntry?>(focus => focus!.Value.CurrentHandle!.Value.Index == 2));
    }

    [Fact]
    public void AnEmptyViewTheEngineHasSettledOn_DropsTheUsersPlace()
    {
        // reference, so a drop written inside that guard would never run at all.
        SetCommittedState(_logId, [_logId], Event(1, "Alpha"), Event(2, "Beta"), Event(3, "Gamma"));

        var served = Presentation(_logId, revision: 1, Event(1, "Alpha"), Event(2, "Beta"), Event(3, "Gamma"));
        _viewSource.Current.Returns(served);

        var cut = Render<LogTablePane>();

        Press(cut, "Home");
        Press(cut, "ArrowDown");

        Publish(cut, EmptyPresentation(revision: 2, PresentationState.Updating), expectedRows: 0);
        Publish(cut, EmptyPresentation(revision: 3, PresentationState.Current), expectedRows: 0);
        Publish(cut, served with { Revision = 4 }, expectedRows: 3);

        _eventLogCommands.ClearReceivedCalls();

        Press(cut, "ArrowDown");

        _eventLogCommands.Received(1).SetSelectedEvents(
            Arg.Any<IReadOnlyCollection<SelectionEntry>>(),
            Arg.Is<SelectionEntry?>(focus => focus!.Value.CurrentHandle!.Value.Index == 1));
    }

    [Fact]
    public void ColumnWidth_FollowsThePresentation_NotTheCommittedState()
    {
        SetCommittedState(_logId, [_logId], Event(1, "Alpha"));

        var presentation = DisplayViewTestFactory.Presentation(_logId, [Event(1, "Alpha")]) with
        {
            Columns = s_sourceColumn,
            ColumnWidths = ImmutableDictionary<ColumnName, int>.Empty.Add(ColumnName.Source, 321)
        };

        _viewSource.Current.Returns(presentation);

        var cut = Render<LogTablePane>();

        Assert.Contains("width: 321px", cut.Find("th[data-column='Source']").GetAttribute("style"));
    }

    [Fact]
    public void GroupCollapse_FollowsThePresentation_NotTheCommittedState()
    {
        SetCommittedState(_logId, [_logId], Event(1, "Alpha"), Event(2, "Beta"));

        var presentation = DisplayViewTestFactory.Presentation(
            _logId, [Event(1, "Alpha"), Event(2, "Beta")], groupBy: ColumnName.Source, groupsCollapsedByDefault: true);

        _viewSource.Current.Returns(presentation);

        var cut = Render<LogTablePane>();

        var headers = cut.FindAll("tr.group-header-row");

        Assert.NotEmpty(headers);
        Assert.All(headers, header => Assert.Equal("true", header.GetAttribute("data-collapsed")));
    }

    [Fact]
    public void Grouping_FollowsThePresentation_NotTheCommittedColumn()
    {
        SetCommittedState(_logId, [_logId], Event(1, "Alpha"), Event(2, "Beta"));
        SetCommittedGrouping(ColumnName.Level);
        SetPresentationGrouping(ColumnName.Source, Event(1, "Alpha"), Event(2, "Beta"));

        var cut = Render<LogTablePane>();

        Assert.Equal(2, cut.FindAll("tr.group-header-row").Count);
        Assert.Contains("Source", cut.Find("span.group-name").TextContent);
        Assert.Contains("Alpha", cut.FindAll("span.group-value")[0].TextContent);
    }

    [Fact]
    public void ReloadRestore_AnOrdinarySelectionChange_DoesNotScroll()
    {
        SetCommittedState(_logId, [_logId], Event(1, "Alpha"), Event(2, "Beta"));
        _viewSource.Current.Returns(DisplayViewTestFactory.Presentation(_logId, [Event(1, "Alpha"), Event(2, "Beta")]));

        var cut = Render<LogTablePane>();

        int scrollsAfterRender = ScrollCount();

        _selectedEvents.Current.Returns(ImmutableList.Create(EntryFor(Event(2, "Beta"))));
        cut.InvokeAsync(() => _selectedEvents.Changed += Raise.Event<Action>());

        cut.WaitForAssertion(() => Assert.Equal("true", RowSelected(cut, 1)));
        Assert.Equal(scrollsAfterRender, ScrollCount());
    }

    [Fact]
    public void ReloadRestore_WhenARemountedPaneMountsWithAPendingReveal_ScrollsToIt()
    {
        var target = new EventLocator(_logId, 0, 1);
        SetCommittedState(_logId, [_logId], Event(1, "Alpha"), Event(2, "Beta"));
        _viewSource.Current.Returns(DisplayViewTestFactory.Presentation(_logId, [Event(1, "Alpha"), Event(2, "Beta")]));
        _selectedEvent.Current.Returns(EntryFor(Event(2, "Beta")));
        _revealFocus.Current.Returns(target);

        var cut = Render<LogTablePane>();

        cut.WaitForAssertion(() => Assert.Equal(1, ScrollCount()));
        Assert.Equal(1, LastScrollRow());
        _eventLogCommands.Received().ConsumeRevealFocus(target);
    }

    [Fact]
    public void ReloadRestore_WhenTheFreshViewPublishesBeforeTheSelectionRestore_ScrollsToTheRestoredTarget()
    {
        SetCommittedState(_logId, [_logId], Event(1, "Alpha"), Event(2, "Beta"));
        _viewSource.Current.Returns(DisplayViewTestFactory.Presentation(_logId, [Event(1, "Alpha"), Event(2, "Beta")]));

        var cut = Render<LogTablePane>();

        var reloaded = DisplayViewTestFactory.Presentation(_logId, [Event(1, "Alpha"), Event(2, "Beta")], revision: 2);
        Publish(cut, reloaded, expectedRows: 2);

        int scrollsAfterReloadPublish = ScrollCount();

        var target = new EventLocator(_logId, 0, 1);
        _selectedEvent.Current.Returns(EntryFor(Event(2, "Beta")));
        _revealFocus.Current.Returns(target);
        cut.InvokeAsync(() => _revealFocus.Changed += Raise.Event<Action>());

        cut.WaitForAssertion(() => Assert.Equal(scrollsAfterReloadPublish + 1, ScrollCount()));
        Assert.Equal(1, LastScrollRow());
        _eventLogCommands.Received().ConsumeRevealFocus(target);
    }

    [Fact]
    public void ReloadRestore_WhenTheRevealArrivesAfterTheFocusIsAlreadySet_StillScrolls()
    {
        var target = new EventLocator(_logId, 0, 1);
        SetCommittedState(_logId, [_logId], Event(1, "Alpha"), Event(2, "Beta"));
        _viewSource.Current.Returns(DisplayViewTestFactory.Presentation(_logId, [Event(1, "Alpha"), Event(2, "Beta")]));
        _selectedEvent.Current.Returns(EntryFor(Event(2, "Beta")));

        var cut = Render<LogTablePane>();

        Assert.Equal(0, ScrollCount());

        _revealFocus.Current.Returns(target);
        cut.InvokeAsync(() => _revealFocus.Changed += Raise.Event<Action>());

        cut.WaitForAssertion(() => Assert.Equal(1, ScrollCount()));
        Assert.Equal(1, LastScrollRow());
        _eventLogCommands.Received().ConsumeRevealFocus(target);
    }

    [Fact]
    public void ReloadRestore_WhenTheRevealTargetIsNotYetInTheView_WaitsThenScrollsWhenItAppears()
    {
        var target = new EventLocator(_logId, 0, 2);
        SetCommittedState(_logId, [_logId], Event(1, "Alpha"), Event(2, "Beta"), Event(3, "Gamma"));
        _viewSource.Current.Returns(DisplayViewTestFactory.Presentation(_logId, [Event(1, "Alpha"), Event(2, "Beta")]));
        _selectedEvent.Current.Returns(EntryFor(Event(3, "Gamma")));
        _revealFocus.Current.Returns(target);

        var cut = Render<LogTablePane>();

        Assert.Equal(0, ScrollCount());
        _eventLogCommands.DidNotReceive().ConsumeRevealFocus(Arg.Any<EventLocator>());

        var withGamma = DisplayViewTestFactory.Presentation(
            _logId, [Event(1, "Alpha"), Event(2, "Beta"), Event(3, "Gamma")], revision: 2);
        Publish(cut, withGamma, expectedRows: 3);

        cut.WaitForAssertion(() => Assert.Equal(1, ScrollCount()));
        Assert.Equal(2, LastScrollRow());
        _eventLogCommands.Received().ConsumeRevealFocus(target);
    }

    [Fact]
    public void ReloadRestore_WhenTheUserHasMovedOffTheRevealTarget_DropsItWithoutScrolling()
    {
        var staleTarget = new EventLocator(_logId, 0, 0);
        SetCommittedState(_logId, [_logId], Event(1, "Alpha"), Event(2, "Beta"));
        _viewSource.Current.Returns(DisplayViewTestFactory.Presentation(_logId, [Event(1, "Alpha"), Event(2, "Beta")]));
        _selectedEvent.Current.Returns(EntryFor(Event(2, "Beta")));
        _revealFocus.Current.Returns(staleTarget);

        var cut = Render<LogTablePane>();

        cut.WaitForAssertion(() => _eventLogCommands.Received().ConsumeRevealFocus(staleTarget));
        Assert.Equal(0, ScrollCount());
    }

    [Fact]
    public void Rows_ComeFromThePresentation_NotTheCommittedState()
    {
        SetCommittedState(_logId, [_logId], Event(1, "Alpha"));
        SetPresentation(_logId, Event(1, "Alpha"), Event(2, "Beta"), Event(3, "Gamma"));

        var cut = Render<LogTablePane>();

        Assert.Contains("Beta", cut.Markup);
        Assert.Contains("Gamma", cut.Markup);
    }

    [Fact]
    public void SortIndicator_DescribesThePresentationsOrder_NotTheRequestedOne()
    {
        SetCommittedState(_logId, [_logId], Event(1, "Alpha"), Event(2, "Beta"));
        SetPresentationOrdering(ColumnName.Source, isDescending: true, Event(1, "Alpha"), Event(2, "Beta"));

        var cut = Render<LogTablePane>();

        var header = cut.Find("th[data-column='Source']");

        Assert.Equal("descending", header.GetAttribute("aria-sort"));
        Assert.NotEmpty(cut.FindAll("th[data-column='Source'] .menu-toggle"));
    }

    [Fact]
    public void ThePane_SubscribesToItsAppStateSources()
    {
        SetCommittedState(_logId, [_logId], Event(1, "Alpha"));
        SetPresentation(_logId, Event(1, "Alpha"));

        Render<LogTablePane>();

        _selectedEvent.Received().Changed += Arg.Any<Action>();
        _selectedEvents.Received().Changed += Arg.Any<Action>();
        _filterSelection.Received().Changed += Arg.Any<Action>();
    }

    private static string DateCell(IRenderedComponent<LogTablePane> cut) =>
        cut.FindAll("tbody tr[role=row] td")[0].TextContent.Trim();

    private static ResolvedEvent Event(int id, string source) =>
        new(LogName, LogPathType.Channel) { Id = id, RecordId = id, Source = source };

    private static void Press(IRenderedComponent<LogTablePane> cut, string code) =>
        cut.Find(".table-container").KeyDown(new KeyboardEventArgs { Code = code, Key = code });

    private static string? RowHighlight(IRenderedComponent<LogTablePane> cut, int index) =>
        cut.FindAll("tbody tr[role=row]")[index].GetAttribute("data-highlight");

    private static string? RowSelected(IRenderedComponent<LogTablePane> cut, int index) =>
        cut.FindAll("tbody tr[role=row]")[index].GetAttribute("aria-selected");

    private OrderedViewPresentation EmptyPresentation(long revision, PresentationState state) =>
        new(DisplayViewTestFactory.Identity([]), _logId, default, state, revision) { Columns = s_sourceColumn };

    private SelectionEntry EntryFor(ResolvedEvent evt)
    {
        var handle = new EventLocator(_logId, 0, (int)(evt.RecordId!.Value - 1));
        ValueKey.TryCreate(evt, out var reloadKey);

        return new SelectionEntry(handle, handle, reloadKey);
    }

    private int LastScrollRow() => (int)_tableJsModule.Invocations["scrollToRow"][^1].Arguments[0]!;

    private OrderedViewPresentation Presentation(EventLogId tabId, long revision, params ResolvedEvent[] events) =>
        new(DisplayViewTestFactory.Identity(events), tabId, default, PresentationState.Current, revision) { Columns = s_sourceColumn };

    private void Publish(IRenderedComponent<LogTablePane> cut, OrderedViewPresentation presentation, int expectedRows)
    {
        _viewSource.Current.Returns(presentation);
        _viewSource.Updated += Raise.Event<Action<OrderedViewPresentation>>(presentation);

        cut.WaitForAssertion(() =>
            Assert.Equal((expectedRows + 1).ToString(), cut.Find("#eventTable").GetAttribute("aria-rowcount")));
    }

    private int ScrollCount() => _tableJsModule.Invocations["scrollToRow"].Count;

    private void SetCommittedGrouping(ColumnName groupBy) =>
        _logTableState.Value.Returns(_logTableState.Value with { GroupBy = groupBy });

    private void SetCommittedState(EventLogId activeId, EventLogId[] tabIds, params ResolvedEvent[] events)
    {
        var state = new LogTableState
        {
            ActiveEventLogId = activeId,
            EventTables = [.. tabIds.Select(id => new LogView(id) { LogName = LogName })],
            Columns = ImmutableDictionary<ColumnName, bool>.Empty.Add(ColumnName.Source, true),
            ColumnOrder = ImmutableList.Create(ColumnName.Source)
        };

        _logTableState.Value.Returns(state);
    }

    private void SetPresentation(EventLogId tabId, params ResolvedEvent[] events)
    {
        var presentation = Presentation(tabId, revision: 1, events);

        _viewSource.Current.Returns(presentation);
    }

    private void SetPresentationGrouping(ColumnName groupBy, params ResolvedEvent[] events)
    {
        var presentation = new OrderedViewPresentation(
            DisplayViewTestFactory.Identity(events, groupBy),
            _logId,
            new DisplayOrdering(null, false, groupBy, false),
            PresentationState.Current,
            Revision: 1)
        { Columns = s_sourceColumn };

        _viewSource.Current.Returns(presentation);
    }

    private void SetPresentationOrdering(ColumnName orderBy, bool isDescending, params ResolvedEvent[] events)
    {
        var presentation = new OrderedViewPresentation(
            DisplayViewTestFactory.Identity(events),
            _logId,
            new DisplayOrdering(orderBy, isDescending, null, false),
            PresentationState.Current,
            Revision: 1)
        { Columns = s_sourceColumn };

        _viewSource.Current.Returns(presentation);
    }
}
