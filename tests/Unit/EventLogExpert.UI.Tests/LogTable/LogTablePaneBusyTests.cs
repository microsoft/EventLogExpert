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
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using System.Collections.Immutable;
using System.Globalization;

namespace EventLogExpert.UI.Tests.LogTable;

public sealed class LogTablePaneBusyTests : BunitContext
{
    private const string LogName = "Application";

    private readonly ILogTableColumnDefaultsProvider _columnDefaults = Substitute.For<ILogTableColumnDefaultsProvider>();
    private readonly IEventLogCommands _eventLogCommands = Substitute.For<IEventLogCommands>();
    private readonly IState<FilterPaneState> _filterPaneState = Substitute.For<IState<FilterPaneState>>();
    private readonly IHighlightSelector _highlightSelector = Substitute.For<IHighlightSelector>();
    private readonly EventLogId _logId = EventLogId.Create();
    private readonly ILogTableCommands _logTableCommands = Substitute.For<ILogTableCommands>();
    private readonly IState<LogTableState> _logTableState = Substitute.For<IState<LogTableState>>();
    private readonly IMenuService _menuService = Substitute.For<IMenuService>();
    private readonly IEventFocusSource _selectedEvent = Substitute.For<IEventFocusSource>();
    private readonly IEventSelectionSource _selectedEvents = Substitute.For<IEventSelectionSource>();
    private readonly ISettingsService _settings = Substitute.For<ISettingsService>();
    private readonly IOrderedViewSource _viewSource = Substitute.For<IOrderedViewSource>();

    public LogTablePaneBusyTests()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.SetupModule("./_content/EventLogExpert.UI/LogTable/LogTablePane.razor.js");

        _columnDefaults.ColumnOrder.Returns(ImmutableList.Create(ColumnName.Source));
        _filterPaneState.Value.Returns(new FilterPaneState());
        _highlightSelector.Select(Arg.Any<ImmutableList<SavedFilter>>()).Returns([]);
        _highlightSelector.ComputeHighlightKey(Arg.Any<ImmutableList<SavedFilter>>()).Returns(0);
        _settings.TimeZoneInfo.Returns(TimeZoneInfo.Utc);
        _selectedEvent.Current.Returns((SelectionEntry?)null);
        _selectedEvents.Current.Returns(ImmutableList<SelectionEntry>.Empty);

        Services.AddLogTablePaneDependencies();
        Services.AddImmediateCpuWorkScheduler();
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

        Services.AddSingleton(_viewSource);

        Services.AddFluxor(options => options.ScanAssemblies(typeof(LogTablePane).Assembly));
    }

    [Fact]
    public void AFailedGridWithNoRows_IsNotBusy_BecauseNothingIsComing()
    {
        Assert.False(RenderWith(PresentationState.Faulted, rows: 0));
    }

    [Fact]
    public void AFailedGridWithRows_IsNotBusy_Either()
    {
        Assert.False(RenderWith(PresentationState.Faulted, rows: 3));
    }

    [Fact]
    public void AGridWithRowsStillBeingBuilt_DoesNotSuppressTheRowsItAlreadyHas()
    {
        Assert.False(RenderWith(PresentationState.Updating, rows: 3));
    }

    [Fact]
    public void AGroupedGridEndsTheWaitAtItsOwnPaint_BecauseNothingWillEverRefreshItsViewport()
    {
        SetCommittedState(rows: 3);
        SetCommittedGrouping();

        _viewSource.Current.Returns(Grouped(PresentationState.Updating, rows: 0, revision: 1));

        var cut = Render<LogTablePane>();

        Assert.True(IsBusy(cut), "an empty pending grouped grid must assert it too");

        Publish(cut, Grouped(PresentationState.Current, rows: 3, revision: 2), expectedRows: 4);

        cut.WaitForAssertion(() => Assert.False(IsBusy(cut)));
    }

    [Fact]
    public void ALiveTailAppendOverRowsAlreadyOnScreen_NeverAnnouncesAWait()
    {
        SetCommittedState(rows: 3);

        _viewSource.Current.Returns(Presentation(PresentationState.Current, rows: 3, revision: 1));

        var cut = Render<LogTablePane>();

        Assert.False(IsBusy(cut), "settled rows must not announce a wait to begin with");

        var busySamples = new List<bool>();

        cut.OnAfterRender += (_, _) => busySamples.Add(IsBusy(cut));

        Publish(cut, Presentation(PresentationState.Current, rows: 4, revision: 2), expectedRows: 4);

        Assert.DoesNotContain(true, busySamples);
    }

    [Fact]
    public void ASettledEmptyGrid_IsNotBusy_BecauseTheEmptinessIsTheAnswer()
    {
        Assert.False(RenderWith(PresentationState.Current, rows: 0));
    }

    [Fact]
    public void ASettledGridWithRows_IsNotBusy()
    {
        Assert.False(RenderWith(PresentationState.Current, rows: 3));
    }

    [Fact]
    public void AnEmptyGridStillBeingBuilt_TellsAssistiveTechnologyToWait()
    {
        Assert.True(RenderWith(PresentationState.Updating, rows: 0));
    }

    [Fact]
    public void TheWaitOutlastsTheStateFlip_BecauseTheRowsLagOneRefreshBehindIt()
    {
        SetCommittedState(rows: 3);

        var pending = Presentation(PresentationState.Updating, rows: 0, revision: 1);

        _viewSource.Current.Returns(pending);

        var cut = Render<LogTablePane>();

        Assert.True(IsBusy(cut), "an empty pending grid must assert it before anything else happens");

        var busyAfterTheFlip = new List<bool>();

        cut.OnAfterRender += (_, _) => busyAfterTheFlip.Add(IsBusy(cut));

        Publish(cut, Presentation(PresentationState.Current, rows: 3, revision: 2), expectedRows: 3);

        cut.WaitForAssertion(() => Assert.False(IsBusy(cut)));

        Assert.Contains(true, busyAfterTheFlip);
        Assert.False(busyAfterTheFlip[^1], "and it must end once they are");
    }

    private static ResolvedEvent Event(long recordId, string source) =>
        new(LogName, LogPathType.Channel)
        {
            RecordId = recordId,
            TimeCreated = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(recordId),
            Id = 1000,
            Level = "Information",
            Source = source,
            LogName = LogName
        };

    private static ResolvedEvent[] Events(int count) =>
        [.. Enumerable.Range(1, count).Select(index => Event(index, $"Source{index}"))];

    private static DisplayIndicatorKind ExpectedKind(PresentationState state, int rows) =>
        state switch
        {
            PresentationState.Faulted => DisplayIndicatorKind.Fault,
            PresentationState.Updating when rows == 0 => DisplayIndicatorKind.EmptyPending,
            _ => DisplayIndicatorKind.None
        };

    private static bool IsBusy(IRenderedComponent<LogTablePane> cut) =>
        cut.Find("#eventTable").GetAttribute("aria-busy") == "true";

    private OrderedViewPresentation Grouped(PresentationState state, int rows, long revision) =>
        new(
            DisplayViewTestFactory.Identity(
                [.. Enumerable.Range(1, rows).Select(index => Event(index, "Alpha"))],
                ColumnName.Source),
            _logId,
            new DisplayOrdering(null, false, ColumnName.Source, false),
            state,
            revision,
            FaultCause: state == PresentationState.Faulted ? "InvalidOperationException: bad predicate" : null);

    private OrderedViewPresentation Presentation(PresentationState state, int rows, long revision) =>
        new(
            DisplayViewTestFactory.Identity(Events(rows)),
            _logId,
            default,
            state,
            revision,
            FaultCause: state == PresentationState.Faulted ? "InvalidOperationException: bad predicate" : null);

    private void Publish(IRenderedComponent<LogTablePane> cut, OrderedViewPresentation presentation, int expectedRows)
    {
        _viewSource.Current.Returns(presentation);
        _viewSource.Updated += Raise.Event<Action<OrderedViewPresentation>>(presentation);

        cut.WaitForAssertion(() =>
            Assert.Equal((expectedRows + 1).ToString(), cut.Find("#eventTable").GetAttribute("aria-rowcount")));
    }

    private bool RenderWith(PresentationState state, int rows)
    {
        SetCommittedState(rows);

        _viewSource.Current.Returns(Presentation(state, rows, revision: 1));

        var cut = Render<LogTablePane>();

        Assert.Equal(
            ExpectedKind(state, rows),
            _viewSource.Current.IndicatorKind);

        return IsBusy(cut);
    }

    private void SetCommittedGrouping() =>
        _logTableState.Value.Returns(_logTableState.Value with { GroupBy = ColumnName.Source });

    private void SetCommittedState(int rows)
    {
        var state = new LogTableState
        {
            ActiveEventLogId = _logId,
            EventTables = [new LogView(_logId) { LogName = LogName }],
            Columns = ImmutableDictionary<ColumnName, bool>.Empty.Add(ColumnName.Source, true),
            ColumnOrder = ImmutableList.Create(ColumnName.Source)
        };

        _logTableState.Value.Returns(state);
    }
}
