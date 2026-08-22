// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.FilterPane;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.Memory;
using EventLogExpert.Runtime.StatusBar;
using Fluxor;
using NSubstitute;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.Tests.StatusBar;

public sealed class StatusBarSourceTests
{
    private static readonly StatusActivityId Activity = new(Guid.NewGuid());
    private static readonly EventLogId LogA = EventLogId.Create();
    private static readonly EventLogId LogB = EventLogId.Create();

    [Fact]
    public void AThrowingSubscriber_IsIsolatedFromOtherSubscribers()
    {
        var harness = new Harness();
        var reachedSecond = 0;
        harness.Source.Changed += () => throw new InvalidOperationException("subscriber blew up");
        harness.Source.Changed += () => reachedSecond++;

        harness.RawCount = new RawEventCountState { ByLog = ImmutableDictionary<EventLogId, ProviderResolutionCounts>.Empty.Add(LogA, Counts(5)) };
        harness.RaiseRawCount();

        Assert.Equal(1, reachedSecond);
    }

    [Fact]
    public void Changed_DoesNotFire_WhenAnEventLogFacetTheBarDoesNotReadChanges()
    {
        var harness = new Harness();
        var raised = 0;
        harness.Source.Changed += () => raised++;

        harness.EventLog = harness.EventLog with { AppliedFilter = new Filter(new DateFilter { IsEnabled = true }, []) };
        harness.RaiseEventLog();

        Assert.Equal(0, raised);
    }

    [Fact]
    public void Changed_DoesNotFire_WhenEventsLoadingMapIsEquivalent()
    {
        var harness = new Harness();
        harness.StatusBar = harness.StatusBar with
        {
            EventsLoading = ImmutableDictionary<StatusActivityId, (int, int, long?)>.Empty.Add(Activity, (12, 3, null))
        };
        harness.RaiseStatusBar();
        var raised = 0;
        harness.Source.Changed += () => raised++;

        harness.StatusBar = harness.StatusBar with
        {
            EventsLoading = ImmutableDictionary.CreateRange([new KeyValuePair<StatusActivityId, (int, int, long?)>(Activity, (12, 3, null))])
        };
        harness.RaiseStatusBar();

        Assert.Equal(0, raised);
    }

    [Fact]
    public void Changed_DoesNotFire_WhenRawCountsByLogIsEquivalent()
    {
        var harness = new Harness();
        harness.RawCount = new RawEventCountState { ByLog = ImmutableDictionary<EventLogId, ProviderResolutionCounts>.Empty.Add(LogA, Counts(5)) };
        harness.RaiseRawCount();
        var raised = 0;
        harness.Source.Changed += () => raised++;

        harness.RawCount = new RawEventCountState { ByLog = ImmutableDictionary.CreateRange([new KeyValuePair<EventLogId, ProviderResolutionCounts>(LogA, Counts(5))]) };
        harness.RaiseRawCount();

        Assert.Equal(0, raised);
    }

    [Fact]
    public void Changed_DoesNotFire_WhenTheMemoryFacetIsEquivalent()
    {
        var harness = new Harness();
        harness.Memory = new MemoryIndicatorState { UsedMebibytes = 256, WorkingSetBytes = 10, Level = MemoryUsageLevel.Normal };
        harness.RaiseMemory();
        var raised = 0;
        harness.Source.Changed += () => raised++;

        harness.Memory = new MemoryIndicatorState { UsedMebibytes = 256, WorkingSetBytes = 10, Level = MemoryUsageLevel.Normal };
        harness.RaiseMemory();

        Assert.Equal(0, raised);
    }

    [Fact]
    public void Changed_Fires_WhenAnEventLogFacetChanges()
    {
        var harness = new Harness();
        var raised = 0;
        harness.Source.Changed += () => raised++;

        harness.EventLog = harness.EventLog with { ContinuouslyUpdate = true };
        harness.RaiseEventLog();

        Assert.Equal(1, raised);
        Assert.True(harness.Source.Current.ContinuouslyUpdate);
    }

    [Fact]
    public void Changed_Fires_WhenLoadingActivitiesChange_AndProjectsLoadedAndFailed()
    {
        var harness = new Harness();
        var raised = 0;
        harness.Source.Changed += () => raised++;

        harness.StatusBar = harness.StatusBar with
        {
            EventsLoading = ImmutableDictionary<StatusActivityId, (int, int, long?)>.Empty.Add(Activity, (12, 3, null))
        };
        harness.RaiseStatusBar();

        Assert.Equal(1, raised);
        var progress = harness.Source.Current.LoadingActivities[Activity];
        Assert.Equal(12, progress.Loaded);
        Assert.Equal(3, progress.Failed);
    }

    [Fact]
    public void Changed_Fires_WhenLogTableTopologyChanges()
    {
        var harness = new Harness();
        var raised = 0;
        harness.Source.Changed += () => raised++;

        harness.LogTable = harness.LogTable with { ActiveEventLogId = LogA };
        harness.RaiseLogTable();

        Assert.Equal(1, raised);
        Assert.Equal(LogA, harness.Source.Current.ActiveTabId);
    }

    [Fact]
    public void Changed_Fires_WhenOnlyTotalChanges_AndProjectsTotal()
    {
        var harness = new Harness();
        harness.StatusBar = harness.StatusBar with
        {
            EventsLoading = ImmutableDictionary<StatusActivityId, (int, int, long?)>.Empty.Add(Activity, (12, 3, null))
        };
        harness.RaiseStatusBar();
        var raised = 0;
        harness.Source.Changed += () => raised++;

        harness.StatusBar = harness.StatusBar with
        {
            EventsLoading = ImmutableDictionary<StatusActivityId, (int, int, long?)>.Empty.Add(Activity, (12, 3, 10_000))
        };
        harness.RaiseStatusBar();

        Assert.Equal(1, raised);
        Assert.Equal(10_000, harness.Source.Current.LoadingActivities[Activity].Total);
    }

    [Fact]
    public void Changed_Fires_WhenPersistentFilterChanges()
    {
        var harness = new Harness();
        var raised = 0;
        harness.Source.Changed += () => raised++;

        harness.FilterPane = harness.FilterPane with { FilteredDateRange = new DateFilter { IsEnabled = true } };
        harness.RaiseFilterPane();

        Assert.Equal(1, raised);
        Assert.True(harness.Source.Current.IsPersistentFilterActive);
    }

    [Fact]
    public void Changed_Fires_WhenRawCountsRedistributeAtTheSameTotal()
    {
        var harness = new Harness();
        harness.RawCount = new RawEventCountState { ByLog = ImmutableDictionary<EventLogId, ProviderResolutionCounts>.Empty.Add(LogA, Counts(5)) };
        harness.RaiseRawCount();
        var raised = 0;
        harness.Source.Changed += () => raised++;

        harness.RawCount = new RawEventCountState
        {
            ByLog = ImmutableDictionary<EventLogId, ProviderResolutionCounts>.Empty.Add(LogA, Counts(2)).Add(LogB, Counts(3))
        };
        harness.RaiseRawCount();

        Assert.Equal(1, raised);
        Assert.Equal(5, harness.Source.Current.RawEventTotal);
    }

    [Fact]
    public void Changed_Fires_WhenResolutionStatusChangesAtSameTotal()
    {
        var harness = new Harness();
        harness.RawCount = new RawEventCountState
        {
            ByLog = ImmutableDictionary<EventLogId, ProviderResolutionCounts>.Empty.Add(LogA, new ProviderResolutionCounts(5, 5, 0, 0, 0))
        };
        harness.RaiseRawCount();
        var raised = 0;
        harness.Source.Changed += () => raised++;

        // Same total (5), different resolution breakdown - full-struct facet equality must still detect the change.
        harness.RawCount = new RawEventCountState
        {
            ByLog = ImmutableDictionary<EventLogId, ProviderResolutionCounts>.Empty.Add(LogA, new ProviderResolutionCounts(5, 3, 2, 0, 0))
        };
        harness.RaiseRawCount();

        Assert.Equal(1, raised);
    }

    [Fact]
    public void Changed_Fires_WhenStatusActivityChanges()
    {
        var harness = new Harness();
        var raised = 0;
        harness.Source.Changed += () => raised++;

        harness.StatusBar = harness.StatusBar with { ResolverStatus = "Providers unavailable" };
        harness.RaiseStatusBar();

        Assert.Equal(1, raised);
        Assert.Equal("Providers unavailable", harness.Source.Current.ResolverStatus);
    }

    [Fact]
    public void Changed_Fires_WhenTheMemoryFacetChanges()
    {
        var harness = new Harness();
        var raised = 0;
        harness.Source.Changed += () => raised++;

        harness.Memory = harness.Memory with { UsedMebibytes = 256, Level = MemoryUsageLevel.Elevated };
        harness.RaiseMemory();

        Assert.Equal(1, raised);
    }

    [Fact]
    public void Construction_ReconcilesARawCountChangeBetweenSeedAndSubscribe_SoALaterUnchangedNotificationDoesNotRaise()
    {
        var populated = new RawEventCountState { ByLog = ImmutableDictionary<EventLogId, ProviderResolutionCounts>.Empty.Add(LogA, Counts(7)) };
        var rawCount = Substitute.For<IState<RawEventCountState>>();
        rawCount.Value.Returns(new RawEventCountState(), populated);
        using var source = NewSource(rawCount: rawCount);
        var raised = 0;
        source.Changed += () => raised++;

        rawCount.StateChanged += Raise.Event<EventHandler>(rawCount, EventArgs.Empty);

        Assert.Equal(0, raised);
        Assert.Equal(7, source.Current.RawEventTotal);
    }

    [Fact]
    public void Construction_ReconcilesAStatusChangeBetweenSeedAndSubscribe_SoALaterUnchangedNotificationDoesNotRaise()
    {
        var late = new StatusBarState { ResolverStatus = "Late" };
        var statusBar = Substitute.For<IState<StatusBarState>>();
        statusBar.Value.Returns(new StatusBarState(), late);
        using var source = NewSource(statusBar: statusBar);
        var raised = 0;
        source.Changed += () => raised++;

        statusBar.StateChanged += Raise.Event<EventHandler>(statusBar, EventArgs.Empty);

        Assert.Equal(0, raised);
        Assert.Equal("Late", source.Current.ResolverStatus);
    }

    [Fact]
    public void Current_ProjectsMemoryFacets()
    {
        var harness = new Harness
        {
            Memory = new MemoryIndicatorState
            {
                UsedMebibytes = 512,
                WorkingSetBytes = 900_000_000,
                Level = MemoryUsageLevel.Elevated
            }
        };

        var presentation = harness.Source.Current;

        Assert.Equal(512, presentation.MemoryUsedMebibytes);
        Assert.Equal(900_000_000, presentation.MemoryWorkingSetBytes);
        Assert.Equal(MemoryUsageLevel.Elevated, presentation.MemoryLevel);
    }

    [Fact]
    public void Current_ReadsEveryBackingStateLive()
    {
        var harness = new Harness();

        harness.EventLog = harness.EventLog with { ContinuouslyUpdate = true };
        harness.FilterPane = harness.FilterPane with { FilteredDateRange = new DateFilter { IsEnabled = true } };
        harness.RawCount = new RawEventCountState { ByLog = ImmutableDictionary<EventLogId, ProviderResolutionCounts>.Empty.Add(LogA, Counts(9)) };
        harness.LogTable = harness.LogTable with { ActiveEventLogId = LogA };

        var current = harness.Source.Current;

        Assert.True(current.ContinuouslyUpdate);
        Assert.True(current.IsPersistentFilterActive);
        Assert.Equal(9, current.RawEventTotal);
        Assert.Equal(LogA, current.ActiveTabId);
    }

    [Fact]
    public void Current_ShowsLiveStatusActivity_EvenWithoutAStatusNotification()
    {
        var harness = new Harness();

        harness.StatusBar = harness.StatusBar with { ResolverStatus = "Fresh" };

        Assert.Equal("Fresh", harness.Source.Current.ResolverStatus);
    }

    [Fact]
    public void Dispose_UnsubscribesFromAllSixStates()
    {
        var eventLog = Substitute.For<IState<EventLogState>>();
        eventLog.Value.Returns(new EventLogState());
        var filterPane = Substitute.For<IState<FilterPaneState>>();
        filterPane.Value.Returns(new FilterPaneState());
        var rawCount = Substitute.For<IState<RawEventCountState>>();
        rawCount.Value.Returns(new RawEventCountState());
        var statusBar = Substitute.For<IState<StatusBarState>>();
        statusBar.Value.Returns(new StatusBarState());
        var logTable = Substitute.For<IState<LogTableState>>();
        logTable.Value.Returns(new LogTableState());
        var memory = Substitute.For<IState<MemoryIndicatorState>>();
        memory.Value.Returns(new MemoryIndicatorState());
        var source = new StatusBarSource(eventLog, filterPane, rawCount, statusBar, logTable, memory, Substitute.For<ITraceLogger>());

        source.Dispose();

        eventLog.Received().StateChanged -= Arg.Any<EventHandler>();
        filterPane.Received().StateChanged -= Arg.Any<EventHandler>();
        rawCount.Received().StateChanged -= Arg.Any<EventHandler>();
        statusBar.Received().StateChanged -= Arg.Any<EventHandler>();
        logTable.Received().StateChanged -= Arg.Any<EventHandler>();
        memory.Received().StateChanged -= Arg.Any<EventHandler>();
    }

    [Fact]
    public void StatusChangeAlone_DoesNotRaiseThroughAnotherInputsNotification()
    {
        var harness = new Harness();
        var raised = 0;
        harness.Source.Changed += () => raised++;

        harness.StatusBar = harness.StatusBar with { ResolverStatus = "Would-be-leaked" };
        harness.RaiseRawCount();

        Assert.Equal(0, raised);
    }

    private static ProviderResolutionCounts Counts(int total) => new(total, total, 0, 0, 0);

    private static StatusBarSource NewSource(
        IState<EventLogState>? eventLog = null,
        IState<FilterPaneState>? filterPane = null,
        IState<RawEventCountState>? rawCount = null,
        IState<StatusBarState>? statusBar = null,
        IState<LogTableState>? logTable = null,
        IState<MemoryIndicatorState>? memory = null)
    {
        eventLog ??= StateOf(new EventLogState());
        filterPane ??= StateOf(new FilterPaneState());
        rawCount ??= StateOf(new RawEventCountState());
        statusBar ??= StateOf(new StatusBarState());
        logTable ??= StateOf(new LogTableState());
        memory ??= StateOf(new MemoryIndicatorState());

        return new StatusBarSource(eventLog, filterPane, rawCount, statusBar, logTable, memory, Substitute.For<ITraceLogger>());
    }

    private static IState<TState> StateOf<TState>(TState value)
    {
        var state = Substitute.For<IState<TState>>();
        state.Value.Returns(value);

        return state;
    }

    private sealed class Harness
    {
        private readonly IState<EventLogState> _eventLog = Substitute.For<IState<EventLogState>>();
        private readonly IState<FilterPaneState> _filterPane = Substitute.For<IState<FilterPaneState>>();
        private readonly IState<LogTableState> _logTable = Substitute.For<IState<LogTableState>>();
        private readonly IState<MemoryIndicatorState> _memory = Substitute.For<IState<MemoryIndicatorState>>();
        private readonly IState<RawEventCountState> _rawCount = Substitute.For<IState<RawEventCountState>>();
        private readonly IState<StatusBarState> _statusBar = Substitute.For<IState<StatusBarState>>();

        public Harness()
        {
            _eventLog.Value.Returns(_ => EventLog);
            _filterPane.Value.Returns(_ => FilterPane);
            _rawCount.Value.Returns(_ => RawCount);
            _statusBar.Value.Returns(_ => StatusBar);
            _logTable.Value.Returns(_ => LogTable);
            _memory.Value.Returns(_ => Memory);
            Source = new StatusBarSource(
                _eventLog, _filterPane, _rawCount, _statusBar, _logTable, _memory, Substitute.For<ITraceLogger>());
        }

        public EventLogState EventLog { get; set; } = new();

        public FilterPaneState FilterPane { get; set; } = new();

        public LogTableState LogTable { get; set; } = new();

        public MemoryIndicatorState Memory { get; set; } = new();

        public RawEventCountState RawCount { get; set; } = new();

        public StatusBarSource Source { get; }

        public StatusBarState StatusBar { get; set; } = new();

        public void RaiseEventLog() =>
            _eventLog.StateChanged += Raise.Event<EventHandler>(_eventLog, EventArgs.Empty);

        public void RaiseFilterPane() =>
            _filterPane.StateChanged += Raise.Event<EventHandler>(_filterPane, EventArgs.Empty);

        public void RaiseLogTable() =>
            _logTable.StateChanged += Raise.Event<EventHandler>(_logTable, EventArgs.Empty);

        public void RaiseMemory() =>
            _memory.StateChanged += Raise.Event<EventHandler>(_memory, EventArgs.Empty);

        public void RaiseRawCount() =>
            _rawCount.StateChanged += Raise.Event<EventHandler>(_rawCount, EventArgs.Empty);

        public void RaiseStatusBar() =>
            _statusBar.StateChanged += Raise.Event<EventHandler>(_statusBar, EventArgs.Empty);
    }
}
