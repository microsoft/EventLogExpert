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

[Collection(CultureSensitiveCollection.Name)]
public sealed class LogTablePaneViewSourceTests : CultureSensitiveBunitContext
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
        _revealFocus.Current.Returns((RevealFocusRequest?)null);

        _eventLogCommands
            .When(commands => commands.ConsumeRevealFocus(Arg.Any<RevealFocusRequest>()))
            .Do(call =>
            {
                if (_revealFocus.Current == call.Arg<RevealFocusRequest>())
                {
                    _revealFocus.Current.Returns((RevealFocusRequest?)null);
                }
            });

        Services.AddLogTablePaneDependencies();
        Services.AddImmediateCpuWorkScheduler();
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
    public void Grid_AriaBusy_DoesNotHold_WhenReorderFaultsThenUnrelatedRefresh()
    {
        SetCommittedState(_logId, [_logId], Event(1, "Alpha"), Event(2, "Beta"));

        var served = DisplayViewTestFactory.Identity([Event(1, "Alpha"), Event(2, "Beta")]);

        OrderedViewPresentation Ungrouped(IEventColumnView view, PresentationState state, bool stale, long revision) =>
            new(view, _logId,
                new DisplayOrdering(OrderBy: null, IsDescending: false, GroupBy: null, IsGroupDescending: false),
                state, revision, OrderingIsStale: stale)
            { Columns = s_sourceColumn };

        _viewSource.Current.Returns(Ungrouped(served, PresentationState.Current, stale: false, revision: 1));
        var cut = Render<LogTablePane>();

        var busySamples = new List<bool>();
        void SampleBusy(object? sender, EventArgs args) =>
            busySamples.Add(cut.Find("#eventTable").GetAttribute("aria-busy") == "true");
        cut.OnAfterRender += SampleBusy;

        // A reorder is requested (receipt latched) but the reproject faults, retaining the SAME served view, so the
        // adopting render sees no view-reference change and must consume - not leak - the receipt.
        _viewSource.Current.Returns(Ungrouped(served, PresentationState.Faulted, stale: false, revision: 2));
        _viewSource.Updated += Raise.Event<Action<OrderedViewPresentation>>(
            Ungrouped(served, PresentationState.Updating, stale: true, revision: 2));
        cut.WaitForAssertion(() => Assert.Null(cut.Find("#eventTable").GetAttribute("aria-busy")));

        // An unrelated ungrouped refresh (new events) changes the view reference; a leaked receipt would arm the
        // hold and report aria-busy here, but with the receipt already consumed it must stay settled.
        var refreshed = Ungrouped(
            DisplayViewTestFactory.Identity([Event(1, "Alpha"), Event(2, "Beta"), Event(3, "Gamma")]),
            PresentationState.Current, stale: false, revision: 3);
        _viewSource.Current.Returns(refreshed);
        _viewSource.Updated += Raise.Event<Action<OrderedViewPresentation>>(refreshed);

        cut.WaitForAssertion(() => Assert.Equal("4", cut.Find("#eventTable").GetAttribute("aria-rowcount")));
        cut.OnAfterRender -= SampleBusy;

        Assert.DoesNotContain(true, busySamples);
    }

    [Fact]
    public void Grid_AriaBusy_DoesNotLatch_AfterGroupedReorder()
    {
        SetCommittedState(_logId, [_logId], Event(1, "Alpha"), Event(2, "Alpha"));

        OrderedViewPresentation Grouped(PresentationState state, bool stale, long revision) =>
            new(DisplayViewTestFactory.Identity([Event(1, "Alpha"), Event(2, "Alpha")], ColumnName.Source),
                _logId,
                new DisplayOrdering(OrderBy: null, IsDescending: false, ColumnName.Source, IsGroupDescending: false),
                state, revision, OrderingIsStale: stale)
            { Columns = s_sourceColumn };

        _viewSource.Current.Returns(Grouped(PresentationState.Current, stale: false, revision: 1));
        var cut = Render<LogTablePane>();

        var reprojecting = Grouped(PresentationState.Updating, stale: true, revision: 2);
        _viewSource.Current.Returns(reprojecting);
        _viewSource.Updated += Raise.Event<Action<OrderedViewPresentation>>(reprojecting);
        cut.WaitForAssertion(() => Assert.Equal("true", cut.Find("#eventTable").GetAttribute("aria-busy")));

        var adopted = Grouped(PresentationState.Current, stale: false, revision: 3);
        _viewSource.Current.Returns(adopted);
        _viewSource.Updated += Raise.Event<Action<OrderedViewPresentation>>(adopted);

        cut.WaitForAssertion(() => Assert.Null(cut.Find("#eventTable").GetAttribute("aria-busy")));
    }

    [Fact]
    public void Grid_AriaBusy_Holds_ThroughUngroupedReorderRefresh()
    {
        SetCommittedState(_logId, [_logId], Event(1, "Alpha"), Event(2, "Beta"));

        OrderedViewPresentation Ungrouped(PresentationState state, bool stale, long revision) =>
            new(DisplayViewTestFactory.Identity([Event(1, "Alpha"), Event(2, "Beta")]),
                _logId,
                new DisplayOrdering(OrderBy: null, IsDescending: false, GroupBy: null, IsGroupDescending: false)
                {
                    RequestedOrderBy = ColumnName.Source
                },
                state, revision, OrderingIsStale: stale)
            { Columns = s_sourceColumn };

        _viewSource.Current.Returns(Ungrouped(PresentationState.Current, stale: false, revision: 1));
        var cut = Render<LogTablePane>();

        var reprojecting = Ungrouped(PresentationState.Updating, stale: true, revision: 2);
        _viewSource.Current.Returns(reprojecting);
        _viewSource.Updated += Raise.Event<Action<OrderedViewPresentation>>(reprojecting);
        cut.WaitForAssertion(() => Assert.Equal("true", cut.Find("#eventTable").GetAttribute("aria-busy")));

        // The wait must outlast the state flip: aria-busy stays true at the adoption paint (stale Virtualize
        // rows) and clears only after the viewport refreshes. Sampling every render isolates that window, so
        // deleting the hold makes this fail (aria-busy would already read null at the adoption paint).
        var busyAfterTheFlip = new List<bool>();
        void SampleBusy(object? sender, EventArgs args) =>
            busyAfterTheFlip.Add(cut.Find("#eventTable").GetAttribute("aria-busy") == "true");
        cut.OnAfterRender += SampleBusy;

        var adopted = Ungrouped(PresentationState.Current, stale: false, revision: 3);
        _viewSource.Current.Returns(adopted);
        _viewSource.Updated += Raise.Event<Action<OrderedViewPresentation>>(adopted);

        cut.WaitForAssertion(() => Assert.Null(cut.Find("#eventTable").GetAttribute("aria-busy")));
        cut.OnAfterRender -= SampleBusy;

        Assert.Contains(true, busyAfterTheFlip);
        Assert.False(busyAfterTheFlip[^1]);
    }

    [Fact]
    public void Grid_AriaBusy_Holds_WhenAnInterveningRenderPrecedesTheCoalescedReorderAdoption()
    {
        // The receipt is a revision stamp, not a bare flag, precisely so an intervening pre-adoption render cannot
        // steal it. A ReorderPending publication latches the stamp (revision 2) while Current still serves the
        // pre-reorder view (revision 1); an unrelated selection-driven render then runs RebuildRowMaps at revision 1.
        // A bare flag would be consumed by that render and the later adoption would fail to arm the hold; the stamp
        // (1 < 2) must survive it and arm only once revision 3 actually adopts the reorder.
        SetCommittedState(_logId, [_logId], Event(1, "Alpha"), Event(2, "Beta"));

        var preReorderView = DisplayViewTestFactory.IdentityFor(_logId, [Event(1, "Alpha"), Event(2, "Beta")]);
        var reorderedView = DisplayViewTestFactory.IdentityFor(_logId, [Event(2, "Beta"), Event(1, "Alpha")]);

        OrderedViewPresentation Ungrouped(IEventColumnView view, PresentationState state, bool stale, long revision) =>
            new(view, _logId,
                new DisplayOrdering(OrderBy: null, IsDescending: false, GroupBy: null, IsGroupDescending: false)
                {
                    RequestedOrderBy = ColumnName.Source
                },
                state, revision, OrderingIsStale: stale)
            { Columns = s_sourceColumn };

        _viewSource.Current.Returns(Ungrouped(preReorderView, PresentationState.Current, stale: false, revision: 1));
        var cut = Render<LogTablePane>();

        // The reorder is requested: the ReorderPending publication latches the receipt at revision 2, but Current still
        // serves revision 1 so the dispatched adopt is a no-op render that never paints the pending state.
        var reorderPending = Ungrouped(preReorderView, PresentationState.Updating, stale: true, revision: 2);
        cut.InvokeAsync(() => _viewSource.Updated += Raise.Event<Action<OrderedViewPresentation>>(reorderPending));

        // An unrelated selection change renders at revision 1 (< the stamp) and runs RebuildRowMaps. The stamp must
        // survive: no hold arms here (the served view is unchanged), and crucially the receipt is not consumed.
        _selectedEvents.Current.Returns(ImmutableList.Create(EntryFor(Event(1, "Alpha"))));
        cut.InvokeAsync(() => _selectedEvents.Changed += Raise.Event<Action>());
        cut.WaitForAssertion(() => Assert.Equal("true", RowSelected(cut, 0)));
        Assert.Null(cut.Find("#eventTable").GetAttribute("aria-busy"));

        var busyAfterTheFlip = new List<bool>();
        void SampleBusy(object? sender, EventArgs args) =>
            busyAfterTheFlip.Add(cut.Find("#eventTable").GetAttribute("aria-busy") == "true");
        cut.OnAfterRender += SampleBusy;

        // The reorder is adopted at revision 3 with the reordered view. Revision 3 >= the surviving stamp, so the hold
        // arms over the still-stale Virtualize rows; a stolen receipt would leave this adoption paint settled.
        var adopted = Ungrouped(reorderedView, PresentationState.Current, stale: false, revision: 3);
        _viewSource.Current.Returns(adopted);
        cut.InvokeAsync(() => _viewSource.Updated += Raise.Event<Action<OrderedViewPresentation>>(adopted));

        cut.WaitForAssertion(() => Assert.Null(cut.Find("#eventTable").GetAttribute("aria-busy")));
        cut.OnAfterRender -= SampleBusy;

        Assert.Contains(true, busyAfterTheFlip);
        Assert.False(busyAfterTheFlip[^1]);
    }

    [Fact]
    public void Grid_AriaBusy_Holds_WhenUngroupedReorderPaintCoalesces()
    {
        SetCommittedState(_logId, [_logId], Event(1, "Alpha"), Event(2, "Beta"));

        OrderedViewPresentation Ungrouped(PresentationState state, bool stale, long revision) =>
            new(DisplayViewTestFactory.Identity([Event(1, "Alpha"), Event(2, "Beta")]),
                _logId,
                new DisplayOrdering(OrderBy: null, IsDescending: false, GroupBy: null, IsGroupDescending: false)
                {
                    RequestedOrderBy = ColumnName.Source
                },
                state, revision, OrderingIsStale: stale)
            { Columns = s_sourceColumn };

        _viewSource.Current.Returns(Ungrouped(PresentationState.Current, stale: false, revision: 1));
        var cut = Render<LogTablePane>();

        var busyAfterTheFlip = new List<bool>();
        void SampleBusy(object? sender, EventArgs args) =>
            busyAfterTheFlip.Add(cut.Find("#eventTable").GetAttribute("aria-busy") == "true");
        cut.OnAfterRender += SampleBusy;

        // Simulate the coalesced path: the ReorderPending publication is received (latching the pending receipt)
        // but Current already holds the adopted view, so the dispatch adopts it without ever painting the pending
        // state. Without the receipt latch the hold would not arm (the last paint showed settled) and aria-busy
        // would read null over the still-stale Virtualize rows.
        var reprojecting = Ungrouped(PresentationState.Updating, stale: true, revision: 2);
        var adopted = Ungrouped(PresentationState.Current, stale: false, revision: 3);
        _viewSource.Current.Returns(adopted);
        _viewSource.Updated += Raise.Event<Action<OrderedViewPresentation>>(reprojecting);
        _viewSource.Updated += Raise.Event<Action<OrderedViewPresentation>>(adopted);

        cut.WaitForAssertion(() => Assert.Null(cut.Find("#eventTable").GetAttribute("aria-busy")));
        cut.OnAfterRender -= SampleBusy;

        Assert.Contains(true, busyAfterTheFlip);
        Assert.False(busyAfterTheFlip[^1]);
    }

    [Fact]
    public void Grid_IsAriaBusy_WhileReprojectingOptimisticOrdering()
    {
        SetCommittedState(_logId, [_logId], Event(1, "Alpha"), Event(2, "Beta"));

        var presentation = new OrderedViewPresentation(
            DisplayViewTestFactory.Identity([Event(1, "Alpha"), Event(2, "Beta")]),
            _logId,
            new DisplayOrdering(OrderBy: null, IsDescending: true, GroupBy: null, IsGroupDescending: false)
            {
                RequestedOrderBy = ColumnName.Source,
                RequestedIsDescending = false
            },
            PresentationState.Updating,
            Revision: 1,
            OrderingIsStale: true)
        { Columns = s_sourceColumn };

        _viewSource.Current.Returns(presentation);

        var cut = Render<LogTablePane>();

        Assert.Equal("true", cut.Find("#eventTable").GetAttribute("aria-busy"));
    }

    [Fact]
    public void Grid_IsNotAriaBusy_WhenOrderingHasSettled()
    {
        SetCommittedState(_logId, [_logId], Event(1, "Alpha"), Event(2, "Beta"));

        var presentation = new OrderedViewPresentation(
            DisplayViewTestFactory.Identity([Event(1, "Alpha"), Event(2, "Beta")]),
            _logId,
            new DisplayOrdering(ColumnName.Source, IsDescending: false, GroupBy: null, IsGroupDescending: false),
            PresentationState.Current,
            Revision: 1)
        { Columns = s_sourceColumn };

        _viewSource.Current.Returns(presentation);

        var cut = Render<LogTablePane>();

        Assert.Null(cut.Find("#eventTable").GetAttribute("aria-busy"));
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
    public void GroupIndicator_FollowsTheRequestedGroupColumnAndDirection_Optimistically()
    {
        SetCommittedState(_logId, [_logId], Event(1, "Alpha"), Event(2, "Beta"));

        var presentation = new OrderedViewPresentation(
            DisplayViewTestFactory.Identity([Event(1, "Alpha"), Event(2, "Beta")]),
            _logId,
            new DisplayOrdering(OrderBy: null, IsDescending: false, GroupBy: null, IsGroupDescending: false)
            {
                RequestedGroupBy = ColumnName.Source,
                RequestedIsGroupDescending = true
            },
            PresentationState.Current,
            Revision: 1)
        { Columns = s_sourceColumn };

        _viewSource.Current.Returns(presentation);

        var cut = Render<LogTablePane>();

        var header = cut.Find("th[data-column='Source']");

        Assert.Contains("has-indicator", header.GetAttribute("class"));

        var groupToggle = header.QuerySelector(".group-toggle");

        Assert.NotNull(groupToggle);
        Assert.Equal("true", groupToggle!.GetAttribute("data-rotate"));
        Assert.NotNull(groupToggle.QuerySelector("i.bi-chevron-double-up"));
        Assert.False(string.IsNullOrEmpty(groupToggle.GetAttribute("aria-label")));
        Assert.Null(header.QuerySelector(".sort-toggle"));
    }

    [Fact]
    public void GroupIndicator_TogglesGroupSortDirection_WhenClicked()
    {
        SetCommittedState(_logId, [_logId], Event(1, "Alpha"), Event(2, "Beta"));

        var presentation = new OrderedViewPresentation(
            DisplayViewTestFactory.Identity([Event(1, "Alpha"), Event(2, "Beta")]),
            _logId,
            new DisplayOrdering(OrderBy: null, IsDescending: false, GroupBy: null, IsGroupDescending: false)
            {
                RequestedGroupBy = ColumnName.Source
            },
            PresentationState.Current,
            Revision: 1)
        { Columns = s_sourceColumn };

        _viewSource.Current.Returns(presentation);

        var cut = Render<LogTablePane>();

        cut.Find("th[data-column='Source'] .group-toggle").Click();

        _logTableCommands.Received().ToggleGroupSortDirection();
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
    public void Header_ShowsBothSortAndGroupIndicators_WhenColumnIsSortedAndGrouped()
    {
        SetCommittedState(_logId, [_logId], Event(1, "Alpha"), Event(2, "Beta"));

        var presentation = new OrderedViewPresentation(
            DisplayViewTestFactory.Identity([Event(1, "Alpha"), Event(2, "Beta")]),
            _logId,
            new DisplayOrdering(OrderBy: null, IsDescending: false, GroupBy: null, IsGroupDescending: false)
            {
                RequestedOrderBy = ColumnName.Source,
                RequestedIsDescending = true,
                RequestedGroupBy = ColumnName.Source,
                RequestedIsGroupDescending = false
            },
            PresentationState.Current,
            Revision: 1)
        { Columns = s_sourceColumn };

        _viewSource.Current.Returns(presentation);

        var cut = Render<LogTablePane>();

        var header = cut.Find("th[data-column='Source']");

        Assert.Contains("has-indicators-dual", header.GetAttribute("class"));
        Assert.Equal("true", header.QuerySelector(".sort-toggle")!.GetAttribute("data-rotate"));
        Assert.Equal("false", header.QuerySelector(".group-toggle")!.GetAttribute("data-rotate"));
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
        _revealFocus.Current.Returns(new RevealFocusRequest(target, true));

        var cut = Render<LogTablePane>();

        cut.WaitForAssertion(() => Assert.Equal(1, ScrollCount()));
        Assert.Equal(1, LastScrollRow());
        _eventLogCommands.Received().ConsumeRevealFocus(Arg.Is<RevealFocusRequest>(request => request.Target == target));
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
        _revealFocus.Current.Returns(new RevealFocusRequest(target, true));
        cut.InvokeAsync(() => _revealFocus.Changed += Raise.Event<Action>());

        cut.WaitForAssertion(() => Assert.Equal(scrollsAfterReloadPublish + 1, ScrollCount()));
        Assert.Equal(1, LastScrollRow());
        _eventLogCommands.Received().ConsumeRevealFocus(Arg.Is<RevealFocusRequest>(request => request.Target == target));
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

        _revealFocus.Current.Returns(new RevealFocusRequest(target, true));
        cut.InvokeAsync(() => _revealFocus.Changed += Raise.Event<Action>());

        cut.WaitForAssertion(() => Assert.Equal(1, ScrollCount()));
        Assert.Equal(1, LastScrollRow());
        _eventLogCommands.Received().ConsumeRevealFocus(Arg.Is<RevealFocusRequest>(request => request.Target == target));
    }

    [Fact]
    public void ReloadRestore_WhenTheRevealTargetIsNotYetInTheView_WaitsThenScrollsWhenItAppears()
    {
        var target = new EventLocator(_logId, 0, 2);
        SetCommittedState(_logId, [_logId], Event(1, "Alpha"), Event(2, "Beta"), Event(3, "Gamma"));
        _viewSource.Current.Returns(DisplayViewTestFactory.Presentation(_logId, [Event(1, "Alpha"), Event(2, "Beta")]));
        _selectedEvent.Current.Returns(EntryFor(Event(3, "Gamma")));
        _revealFocus.Current.Returns(new RevealFocusRequest(target, true));

        var cut = Render<LogTablePane>();

        Assert.Equal(0, ScrollCount());
        _eventLogCommands.DidNotReceive().ConsumeRevealFocus(Arg.Any<RevealFocusRequest>());

        var withGamma = DisplayViewTestFactory.Presentation(
            _logId, [Event(1, "Alpha"), Event(2, "Beta"), Event(3, "Gamma")], revision: 2);
        Publish(cut, withGamma, expectedRows: 3);

        cut.WaitForAssertion(() => Assert.Equal(1, ScrollCount()));
        Assert.Equal(2, LastScrollRow());
        _eventLogCommands.Received().ConsumeRevealFocus(Arg.Is<RevealFocusRequest>(request => request.Target == target));
    }

    [Fact]
    public void ReloadRestore_WhenTheUserHasMovedOffTheRevealTarget_DropsItWithoutScrolling()
    {
        var staleTarget = new EventLocator(_logId, 0, 0);
        SetCommittedState(_logId, [_logId], Event(1, "Alpha"), Event(2, "Beta"));
        _viewSource.Current.Returns(DisplayViewTestFactory.Presentation(_logId, [Event(1, "Alpha"), Event(2, "Beta")]));
        _selectedEvent.Current.Returns(EntryFor(Event(2, "Beta")));
        _revealFocus.Current.Returns(new RevealFocusRequest(staleTarget, true));

        var cut = Render<LogTablePane>();

        cut.WaitForAssertion(() => _eventLogCommands.Received().ConsumeRevealFocus(Arg.Is<RevealFocusRequest>(request => request.Target == staleTarget)));
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
    public void SelectionReveal_WhenTheTargetIsInTheView_ScrollsAndConsumes()
    {
        var target = new EventLocator(_logId, 0, 1);
        SetCommittedState(_logId, [_logId], Event(1, "Alpha"), Event(2, "Beta"));
        _viewSource.Current.Returns(DisplayViewTestFactory.Presentation(_logId, [Event(1, "Alpha"), Event(2, "Beta")]));
        _selectedEvent.Current.Returns(EntryFor(Event(2, "Beta")));
        _revealFocus.Current.Returns(new RevealFocusRequest(target, false));

        var cut = Render<LogTablePane>();

        cut.WaitForAssertion(() => Assert.Equal(1, ScrollCount()));
        Assert.Equal(1, LastScrollRow());
        _eventLogCommands.Received().ConsumeRevealFocus(Arg.Is<RevealFocusRequest>(request => request.Target == target));
    }

    [Fact]
    public void SelectionReveal_WhenTheTargetIsNotInTheView_DiscardsWithoutScrolling()
    {
        // A one-shot (WaitForView=false) reveal whose target is filtered out of the settled view is discarded, not left pending (unlike the reload reveal above).
        var target = new EventLocator(_logId, 0, 2);
        SetCommittedState(_logId, [_logId], Event(1, "Alpha"), Event(2, "Beta"), Event(3, "Gamma"));
        _viewSource.Current.Returns(DisplayViewTestFactory.Presentation(_logId, [Event(1, "Alpha"), Event(2, "Beta")]));
        _selectedEvent.Current.Returns(EntryFor(Event(3, "Gamma")));
        _revealFocus.Current.Returns(new RevealFocusRequest(target, false));

        var cut = Render<LogTablePane>();

        cut.WaitForAssertion(() => _eventLogCommands.Received().ConsumeRevealFocus(Arg.Is<RevealFocusRequest>(request => request.Target == target)));
        Assert.Equal(0, ScrollCount());
    }

    [Fact]
    public void SortIndicator_FollowsTheRequestedColumnAndDirection_Optimistically()
    {
        SetCommittedState(_logId, [_logId], Event(1, "Alpha"), Event(2, "Beta"));

        var presentation = new OrderedViewPresentation(
            DisplayViewTestFactory.Identity([Event(1, "Alpha"), Event(2, "Beta")]),
            _logId,
            new DisplayOrdering(OrderBy: null, IsDescending: true, GroupBy: null, IsGroupDescending: false)
            {
                RequestedOrderBy = ColumnName.Source,
                RequestedIsDescending = false
            },
            PresentationState.Current,
            Revision: 1)
        { Columns = s_sourceColumn };

        _viewSource.Current.Returns(presentation);

        var cut = Render<LogTablePane>();

        var header = cut.Find("th[data-column='Source']");

        Assert.Equal("ascending", header.GetAttribute("aria-sort"));
        Assert.Equal("false", cut.Find("th[data-column='Source'] .menu-toggle").GetAttribute("data-rotate"));
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
}
