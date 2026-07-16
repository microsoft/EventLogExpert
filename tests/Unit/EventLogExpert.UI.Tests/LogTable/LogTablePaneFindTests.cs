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
using EventLogExpert.UI.LogTable.Find;
using EventLogExpert.UI.Tests.TestUtils;
using Fluxor;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using System.Collections.Immutable;
using System.Globalization;

namespace EventLogExpert.UI.Tests.LogTable;

public sealed class LogTablePaneFindTests : BunitContext
{
    private const string LogName = "Application";

    private readonly ILogTableColumnDefaultsProvider _columnDefaults = Substitute.For<ILogTableColumnDefaultsProvider>();
    private readonly IEventLogCommands _eventLogCommands = Substitute.For<IEventLogCommands>();
    private readonly IState<FilterPaneState> _filterPaneState = Substitute.For<IState<FilterPaneState>>();
    private readonly IHighlightSelector _highlightSelector = Substitute.For<IHighlightSelector>();
    private readonly EventLogId _logId = EventLogId.Create();
    private readonly IState<LogTableState> _logTableState = Substitute.For<IState<LogTableState>>();
    private readonly IStateSelection<EventLogState, SelectionEntry?> _selectedEvent = Substitute.For<IStateSelection<EventLogState, SelectionEntry?>>();
    private readonly IStateSelection<EventLogState, ImmutableList<SelectionEntry>> _selectedEvents = Substitute.For<IStateSelection<EventLogState, ImmutableList<SelectionEntry>>>();
    private readonly ISettingsService _settings = Substitute.For<ISettingsService>();

    private bool _selectionDispatched;

    public LogTablePaneFindTests()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.SetupModule("./_content/EventLogExpert.UI/LogTable/LogTablePane.razor.js");
        JSInterop.SetupModule("./_content/EventLogExpert.UI/LogTable/Find/FindBar.razor.js");

        _columnDefaults.ColumnOrder.Returns(ImmutableList.Create(ColumnName.Level));
        _filterPaneState.Value.Returns(new FilterPaneState());
        _highlightSelector.Select(Arg.Any<ImmutableList<SavedFilter>>()).Returns([]);
        _highlightSelector.ComputeHighlightKey(Arg.Any<ImmutableList<SavedFilter>>()).Returns(0);
        _settings.TimeZoneInfo.Returns(TimeZoneInfo.Utc);
        _selectedEvent.Value.Returns((SelectionEntry?)null);
        _selectedEvents.Value.Returns(ImmutableList<SelectionEntry>.Empty);

        _eventLogCommands
            .When(c => c.SetSelectedEvents(Arg.Any<IReadOnlyCollection<SelectionEntry>>(), Arg.Any<SelectionEntry?>()))
            .Do(_ => _selectionDispatched = true);

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
    public void ClosingFind_ClearsTheMarkerSource()
    {
        var markers = Services.GetRequiredService<IFindMarkerSource>();
        var cut = RenderWithEvents(NewEvent(1, "alpha match"), NewEvent(2, "beta"));

        OpenFindAndSearch(cut, "match");
        cut.WaitForAssertion(() => Assert.NotEmpty(markers.Ticks));

        cut.Find(".table-container").KeyDown(new KeyboardEventArgs { Code = "Escape", Key = "Escape" });

        cut.WaitForAssertion(() => Assert.Empty(markers.Ticks));
        Assert.Null(markers.Owner);
    }

    [Fact]
    public void CurrentMatch_RendersInlineMark()
    {
        var cut = RenderWithEvents(NewEvent(1, "alpha"), NewEvent(2, "beta match"), NewEvent(3, "gamma match"));

        OpenFindAndSearch(cut, "match");

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("mark.find-mark")));
        Assert.Equal("match", cut.Find("mark.find-mark").TextContent);
    }

    [Fact]
    public void CurrentMatchRow_HasAriaCurrent()
    {
        var cut = RenderWithEvents(NewEvent(1, "alpha match"), NewEvent(2, "beta match"), NewEvent(3, "gamma"));

        OpenFindAndSearch(cut, "match");

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("tr[data-find='current']")));
        Assert.Equal("true", cut.Find("tr[data-find='current']").GetAttribute("aria-current"));
        Assert.DoesNotContain(cut.FindAll("tr[data-find='match']"), row => row.HasAttribute("aria-current"));
    }

    [Fact]
    public void Escape_FromGrid_ClosesFindWithoutClearingSelection()
    {
        var cut = RenderWithEvents(NewEvent(1, "alpha match"), NewEvent(2, "beta"));

        OpenFindAndSearch(cut, "match");
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("tr[data-find]")));

        _selectionDispatched = false;
        cut.Find(".table-container").KeyDown(new KeyboardEventArgs { Code = "Escape", Key = "Escape" });

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".find-bar")));
        Assert.False(_selectionDispatched);
    }

    [Fact]
    public void OpenFind_ShowsFindBar()
    {
        var cut = RenderWithEvents(NewEvent(1, "alpha"));

        OpenFind(cut);

        Assert.NotEmpty(cut.FindAll(".find-bar"));
    }

    [Fact]
    public void Query_MarksMatchingRowsAndReportsCount()
    {
        var cut = RenderWithEvents(
            NewEvent(1, "alpha"), NewEvent(2, "beta match"), NewEvent(3, "gamma"), NewEvent(4, "delta match"));

        OpenFindAndSearch(cut, "match");

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("tr[data-find]").Count));
        Assert.Single(cut.FindAll("tr[data-find='current']"));
        Assert.Contains("/2", cut.Find(".find-count").TextContent);
    }

    [Fact]
    public void Query_PublishesMatchTimestampsToTheMarkerSource()
    {
        var markers = Services.GetRequiredService<IFindMarkerSource>();
        var second = NewEvent(2, "beta match");
        var fourth = NewEvent(4, "delta match");
        var cut = RenderWithEvents(NewEvent(1, "alpha"), second, NewEvent(3, "gamma"), fourth);

        OpenFindAndSearch(cut, "match");

        cut.WaitForAssertion(() => Assert.Equal(2, markers.Ticks.Count));
        Assert.Equal(_logId, markers.Owner);
        Assert.Equal(new[] { second.TimeCreated.Ticks, fourth.TimeCreated.Ticks }, markers.Ticks);
    }

    [Fact]
    public void Stepping_ToNextMatch_DoesNotChangeSelection()
    {
        var cut = RenderWithEvents(NewEvent(1, "match one"), NewEvent(2, "match two"), NewEvent(3, "other"));

        OpenFindAndSearch(cut, "match");
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("tr[data-find]").Count));

        _selectionDispatched = false;
        cut.FindAll(".find-nav")[1].Click();

        Assert.False(_selectionDispatched);
    }

    [Fact]
    public void SteppingPastLastMatch_AnnouncesWrap()
    {
        var cut = RenderWithEvents(NewEvent(1, "match one"), NewEvent(2, "match two"), NewEvent(3, "other"));

        OpenFindAndSearch(cut, "match");
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("tr[data-find]").Count));

        cut.FindAll(".find-nav")[1].Click();
        cut.FindAll(".find-nav")[1].Click();

        cut.WaitForAssertion(() => Assert.Contains("Wrapped to first", cut.Find(".find-wrap").TextContent));
    }

    [Fact]
    public void Typing_ImmediatelyEntersScanningState_BeforeDebounceFires()
    {
        var cut = RenderWithEvents(NewEvent(1, "alpha match"), NewEvent(2, "beta"));

        OpenFind(cut);
        cut.Find(".find-input").Input("match");

        Assert.Contains("Searching", cut.Find(".find-count").TextContent);
        Assert.True(cut.FindAll(".find-nav").All(button => button.HasAttribute("disabled")));
    }

    [Fact]
    public void UnmatchedRows_HaveNoFindMarker()
    {
        var cut = RenderWithEvents(NewEvent(1, "alpha match"), NewEvent(2, "beta"), NewEvent(3, "gamma"));

        OpenFindAndSearch(cut, "match");

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("tr[data-find]")));
    }

    [Fact]
    public void WholeWord_RestrictsMatchesToWordBoundedOccurrences()
    {
        var cut = RenderWithEvents(NewEvent(1, "match"), NewEvent(2, "matches found"), NewEvent(3, "rematch"));

        // Enable whole-word via the tray BEFORE typing so there is a single scan cycle (the tray opens against an empty query, avoiding a scan race).
        OpenFind(cut);
        cut.Find(".find-options-toggle").Click();
        cut.FindAll(".find-options .toggle-input")[1].Change(true);
        cut.Find(".find-input").Input("match");

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("tr[data-find]")), TimeSpan.FromSeconds(5));
    }

    private static ResolvedEvent NewEvent(int id, string description) =>
        new(LogName, LogPathType.Channel)
        {
            Id = id,
            RecordId = id,
            TimeCreated = new DateTime(2024, 1, 1, 0, 0, id, DateTimeKind.Utc),
            Description = description,
            Level = "Information"
        };

    private void OpenFind(IRenderedComponent<LogTablePane> cut)
    {
        var coordinator = Services.GetRequiredService<IFindCoordinator>();
        cut.InvokeAsync(() => coordinator.RequestOpen());
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".find-input")));
    }

    private void OpenFindAndSearch(IRenderedComponent<LogTablePane> cut, string query)
    {
        OpenFind(cut);
        cut.Find(".find-input").Input(query);
    }

    private IRenderedComponent<LogTablePane> RenderWithEvents(params ResolvedEvent[] events)
    {
        _logTableState.Value.Returns(new LogTableState
        {
            ActiveEventLogId = _logId,
            EventTables = ImmutableList.Create(new LogView(_logId) { LogName = LogName }),
            EventCountByLog = ImmutableDictionary<EventLogId, int>.Empty.Add(_logId, events.Length),
            Columns = ImmutableDictionary<ColumnName, bool>.Empty.Add(ColumnName.Level, true),
            ColumnOrder = ImmutableList.Create(ColumnName.Level)
        }.WithLogEvents(_logId, events));

        return Render<LogTablePane>();
    }
}
