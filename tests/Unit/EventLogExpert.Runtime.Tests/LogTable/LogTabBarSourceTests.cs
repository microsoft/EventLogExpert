// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.LogTable;
using Fluxor;
using NSubstitute;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.Tests.LogTable;

public sealed class LogTabBarSourceTests
{
    private static readonly EventLogId Alpha = EventLogId.Create();
    private static readonly EventLogId Beta = EventLogId.Create();

    [Fact]
    public void AThrowingSubscriber_IsIsolatedFromOtherSubscribers()
    {
        var harness = new Harness(TwoTabs());
        var reachedSecond = 0;
        harness.Source.Changed += () => throw new InvalidOperationException("subscriber blew up");
        harness.Source.Changed += () => reachedSecond++;

        harness.SetPresence(Presence((Alpha, FilteredLogPresence.NoSurvivor)));

        Assert.Equal(1, reachedSecond);
    }

    [Fact]
    public void Changed_DoesNotFire_WhenAnIrrelevantLogTableFacetChanges()
    {
        var harness = new Harness(TwoTabs());
        var raised = 0;
        harness.Source.Changed += () => raised++;

        harness.SetLogTable(harness.LogTable with { OrderBy = ColumnName.Source });

        Assert.Equal(0, raised);
    }

    [Fact]
    public void Changed_DoesNotFire_WhenPresenceChangesButKnownEmptyDoesNot()
    {
        var harness = new Harness(TwoTabs());
        var raised = 0;
        harness.Source.Changed += () => raised++;

        harness.SetPresence(Presence((Alpha, FilteredLogPresence.HasSurvivor)));

        Assert.Equal(0, raised);
    }

    [Fact]
    public void Changed_Fires_WhenActiveTabChanges()
    {
        var harness = new Harness(TwoTabs());
        var raised = 0;
        harness.Source.Changed += () => raised++;

        harness.SetLogTable(harness.LogTable with { ActiveEventLogId = Beta });

        Assert.Equal(1, raised);
        Assert.Equal(Beta, harness.Source.Current.ActiveTabId);
    }

    [Fact]
    public void Changed_Fires_WhenKnownEmptySwapsAtTheSameCardinality()
    {
        var harness = new Harness(TwoTabs(), Presence((Alpha, FilteredLogPresence.NoSurvivor)));
        var raised = 0;
        harness.Source.Changed += () => raised++;

        harness.SetPresence(Presence((Beta, FilteredLogPresence.NoSurvivor)));

        Assert.Equal(1, raised);
        Assert.True(harness.Source.Current.IsKnownEmpty(Beta));
        Assert.False(harness.Source.Current.IsKnownEmpty(Alpha));
    }

    [Fact]
    public void Changed_Fires_WhenPresenceKnownEmptyChanges_EvenWhenLogTableIsUnchanged()
    {
        var harness = new Harness(TwoTabs());
        var raised = 0;
        harness.Source.Changed += () => raised++;

        harness.SetPresence(Presence((Alpha, FilteredLogPresence.NoSurvivor)));

        Assert.Equal(1, raised);
        Assert.True(harness.Source.Current.IsKnownEmpty(Alpha));
    }

    [Fact]
    public void Construction_ReconcilesLogTableChangeThatLandsBetweenSeedAndSubscribe()
    {
        var logTableState = Substitute.For<IState<LogTableState>>();
        logTableState.Value.Returns(new LogTableState(), TwoTabs());
        var presenceState = Substitute.For<IState<FilteredLogPresenceState>>();
        presenceState.Value.Returns(new FilteredLogPresenceState());

        using var source = new LogTabBarSource(logTableState, presenceState, Substitute.For<ITraceLogger>());

        Assert.Equal(2, source.Current.Tabs.Count);
    }

    [Fact]
    public void Construction_ReconcilesPresenceThatLandsBetweenSeedAndSubscribe()
    {
        var logTableState = Substitute.For<IState<LogTableState>>();
        logTableState.Value.Returns(TwoTabs());
        var presenceState = Substitute.For<IState<FilteredLogPresenceState>>();
        presenceState.Value.Returns(new FilteredLogPresenceState(), Presence((Alpha, FilteredLogPresence.NoSurvivor)));

        using var source = new LogTabBarSource(logTableState, presenceState, Substitute.For<ITraceLogger>());

        Assert.True(source.Current.IsKnownEmpty(Alpha));
    }

    [Fact]
    public void Dispose_IsIdempotentAndStopsRaisingForBothInputs()
    {
        var harness = new Harness(TwoTabs());
        var raised = 0;
        harness.Source.Changed += () => raised++;

        harness.Source.Dispose();
        harness.Source.Dispose();

        harness.SetLogTable(harness.LogTable with { ActiveEventLogId = Beta });
        harness.SetPresence(Presence((Alpha, FilteredLogPresence.NoSurvivor)));

        Assert.Equal(0, raised);
    }

    [Fact]
    public void Dispose_UnsubscribesFromBothStates()
    {
        var logTableState = Substitute.For<IState<LogTableState>>();
        logTableState.Value.Returns(TwoTabs());
        var presenceState = Substitute.For<IState<FilteredLogPresenceState>>();
        presenceState.Value.Returns(new FilteredLogPresenceState());
        var source = new LogTabBarSource(logTableState, presenceState, Substitute.For<ITraceLogger>());

        source.Dispose();

        logTableState.Received().StateChanged -= Arg.Any<EventHandler>();
        presenceState.Received().StateChanged -= Arg.Any<EventHandler>();
    }

    [Fact]
    public void EitherInputNotification_ReprojectsBothLiveValues_SoTheInputsConverge()
    {
        var harness = new Harness(TwoTabs());

        harness.MutateLogTableWithoutRaising(harness.LogTable with { ActiveEventLogId = Beta });
        harness.SetPresence(Presence((Alpha, FilteredLogPresence.NoSurvivor)));

        Assert.Equal(Beta, harness.Source.Current.ActiveTabId);
        Assert.True(harness.Source.Current.IsKnownEmpty(Alpha));
    }

    [Fact]
    public void HasMultipleTabs_CountsAllTabsIncludingCombined()
    {
        var state = new LogTableState
        {
            EventTables =
            [
                new LogView(Alpha) { GroupId = LogTabGroupId.AllLogs },
                new LogView(Beta) { LogName = "Beta" }
            ]
        };
        var harness = new Harness(state);

        Assert.True(harness.Source.Current.HasMultipleTabs);
    }

    [Fact]
    public void KnownEmptyTabIds_ExcludesPendingLoadingAndCombinedTabs()
    {
        var combined = EventLogId.Create();
        var loading = EventLogId.Create();
        var absentVerdict = EventLogId.Create();
        var explicitPending = EventLogId.Create();
        var state = new LogTableState
        {
            EventTables =
            [
                new LogView(combined) { GroupId = LogTabGroupId.AllLogs },
                new LogView(loading) { LogName = "Loading", IsLoading = true },
                new LogView(absentVerdict) { LogName = "AbsentVerdict" },
                new LogView(explicitPending) { LogName = "ExplicitPending" },
                new LogView(Alpha) { LogName = "Alpha" }
            ]
        };
        var presence = Presence(
            (combined, FilteredLogPresence.NoSurvivor),
            (loading, FilteredLogPresence.NoSurvivor),
            (explicitPending, FilteredLogPresence.Pending),
            (Alpha, FilteredLogPresence.NoSurvivor));

        var harness = new Harness(state, presence);

        Assert.Equal([Alpha], harness.Source.Current.KnownEmptyTabIds);
    }

    private static FilteredLogPresenceState Presence(params (EventLogId Id, FilteredLogPresence Verdict)[] verdicts)
    {
        var byLog = ImmutableDictionary<EventLogId, FilteredLogPresence>.Empty;

        foreach (var (id, verdict) in verdicts) { byLog = byLog.SetItem(id, verdict); }

        return new FilteredLogPresenceState { ByLog = byLog };
    }

    private static LogTableState TwoTabs() =>
        new()
        {
            ActiveEventLogId = Alpha,
            EventTables = [new LogView(Alpha) { LogName = "Alpha" }, new LogView(Beta) { LogName = "Beta" }]
        };

    private sealed class Harness
    {
        private readonly IState<LogTableState> _logTableState = Substitute.For<IState<LogTableState>>();
        private readonly IState<FilteredLogPresenceState> _presenceState =
            Substitute.For<IState<FilteredLogPresenceState>>();

        public Harness(LogTableState logTable, FilteredLogPresenceState? presence = null)
        {
            LogTable = logTable;
            Presence = presence ?? new FilteredLogPresenceState();
            _logTableState.Value.Returns(_ => LogTable);
            _presenceState.Value.Returns(_ => Presence);
            Source = new LogTabBarSource(_logTableState, _presenceState, Substitute.For<ITraceLogger>());
        }

        public LogTableState LogTable { get; private set; }

        public FilteredLogPresenceState Presence { get; private set; }

        public LogTabBarSource Source { get; }

        public void MutateLogTableWithoutRaising(LogTableState next) => LogTable = next;

        public void SetLogTable(LogTableState next)
        {
            LogTable = next;
            _logTableState.StateChanged += Raise.Event<EventHandler>(_logTableState, EventArgs.Empty);
        }

        public void SetPresence(FilteredLogPresenceState next)
        {
            Presence = next;
            _presenceState.StateChanged += Raise.Event<EventHandler>(_presenceState, EventArgs.Empty);
        }
    }
}
