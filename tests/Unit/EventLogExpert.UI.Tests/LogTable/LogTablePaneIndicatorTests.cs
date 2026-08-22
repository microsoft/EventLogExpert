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

public sealed class LogTablePaneIndicatorTests : BunitContext
{
    private const string LogName = "Application";

    private readonly ILogTableColumnDefaultsProvider _columnDefaults = Substitute.For<ILogTableColumnDefaultsProvider>();
    private readonly ManualDelay _delay = new();
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

    public LogTablePaneIndicatorTests()
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

        Services.AddSingleton(_ => new DisplayIndicatorGate(_viewSource, _delay.Delay));

        Services.AddFluxor(options => options.ScanAssemblies(typeof(LogTablePane).Assembly));
    }

    [Fact]
    public void AFailedView_SaysSo_AndSaysWhy()
    {
        SetCommittedState();

        _viewSource.Current.Returns(Presentation(PresentationState.Faulted, rows: 0, revision: 1));

        var cut = Render<LogTablePane>();

        _delay.Elapse();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("could not be prepared", cut.Markup);
            Assert.Contains("bad predicate", cut.Markup);
        });
    }

    [Fact]
    public void AnEmptyGridThatKeepsTheUserWaiting_SaysWhatItIsDoing()
    {
        SetCommittedState();

        _viewSource.Current.Returns(Presentation(PresentationState.Updating, rows: 0, revision: 1));

        var cut = Render<LogTablePane>();

        _delay.Elapse();

        cut.WaitForAssertion(() => Assert.Contains("Loading events", cut.Markup));
    }

    [Fact]
    public void AnIndicatorThatHasNotWaitedLongEnough_ShowsNoWordsEvenWhenSomethingIsOwed()
    {
        SetCommittedState();

        _viewSource.Current.Returns(Presentation(PresentationState.Faulted, rows: 0, revision: 1));

        var cut = Render<LogTablePane>();

        Assert.DoesNotContain("could not be prepared", cut.Markup);
    }

    [Fact]
    public void WorkThatFinishesQuickly_LeavesTheGridUntouched()
    {
        SetCommittedState();

        _viewSource.Current.Returns(Presentation(PresentationState.Updating, rows: 0, revision: 1));

        var cut = Render<LogTablePane>();

        Assert.Empty(cut.FindAll(".table-indicator"));
    }

    private static ResolvedEvent Event(long recordId) =>
        new(LogName, LogPathType.Channel)
        {
            RecordId = recordId,
            TimeCreated = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(recordId),
            Id = 1000,
            Level = "Information",
            Source = "Alpha",
            LogName = LogName
        };

    private OrderedViewPresentation Presentation(PresentationState state, int rows, long revision) =>
        new(
            DisplayViewTestFactory.Identity([.. Enumerable.Range(1, rows).Select(index => Event(index))]),
            _logId,
            default,
            state,
            revision,
            FaultCause: state == PresentationState.Faulted ? "InvalidOperationException: bad predicate" : null);

    private void SetCommittedState() =>
        _logTableState.Value.Returns(new LogTableState
        {
            ActiveEventLogId = _logId,
            EventTables = [new LogView(_logId) { LogName = LogName }],
            Columns = ImmutableDictionary<ColumnName, bool>.Empty.Add(ColumnName.Source, true),
            ColumnOrder = ImmutableList.Create(ColumnName.Source)
        });

    private sealed class ManualDelay
    {
        private readonly List<TaskCompletionSource> _pending = [];
        private readonly Lock _sync = new();

        public Task Delay(TimeSpan duration, CancellationToken token)
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            lock (_sync) { _pending.Add(completion); }

            return completion.Task;
        }

        public void Elapse()
        {
            TaskCompletionSource[] outstanding;

            lock (_sync)
            {
                outstanding = [.. _pending];

                _pending.Clear();
            }

            foreach (var completion in outstanding) { completion.TrySetResult(); }
        }
    }
}
