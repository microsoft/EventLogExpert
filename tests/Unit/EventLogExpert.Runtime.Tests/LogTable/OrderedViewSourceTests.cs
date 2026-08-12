// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Histogram;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.LogTable.OrderedView;
using EventLogExpert.Runtime.Tests.LogTable.TestSupport;
using EventLogExpert.Runtime.Tests.TestUtils;
using Fluxor;
using NSubstitute;
using System.Collections.Immutable;
using Reducers = EventLogExpert.Runtime.LogTable.Reducers;

namespace EventLogExpert.Runtime.Tests.LogTable;

public sealed class OrderedViewSourceTests
{
    private const string LogName = "TestLog";

    [Fact]
    public void AChangeOfStateAlone_IsPublished_EvenWhenTheRowsAreTheSameObject()
    {
        var harness = new Harness();
        harness.OpenLogWithEvents(Event(1, "Alpha"));

        int publishedBefore = harness.Published.Count;
        IEventColumnView rowsBefore = harness.Source.Current.View;

        harness.SetDisplayEnabled(false);

        Assert.Equal(publishedBefore + 1, harness.Published.Count);
        Assert.Same(rowsBefore, harness.Published[^1].View);
        Assert.Equal(PresentationState.Faulted, harness.Published[^1].State);
    }

    [Fact]
    public void ACollapseOnlyChange_IsPublished_ThoughTheRowsAndTabAreUnchanged()
    {
        var harness = new Harness();
        harness.OpenLogWithEvents(Event(1, "Alpha"));

        int before = harness.Published.Count;
        IEventColumnView rowsBefore = harness.Source.Current.View;

        harness.Apply(state => state with { GroupCollapseOverrides = state.GroupCollapseOverrides.Add("Alpha") });

        Assert.Equal(before + 1, harness.Published.Count);
        Assert.Same(rowsBefore, harness.Published[^1].View);
        Assert.Contains("Alpha", harness.Published[^1].GroupCollapseOverrides);
    }

    [Fact]
    public void AColumnLayoutChange_IsPublished_ThoughTheRowsAreUnchanged()
    {
        var harness = new Harness();
        harness.OpenLogWithEvents(Event(1, "Alpha"));

        int before = harness.Published.Count;
        IEventColumnView rowsBefore = harness.Source.Current.View;

        harness.Apply(state => state with { ColumnWidths = state.ColumnWidths.SetItem(ColumnName.Source, 999) });

        Assert.Equal(before + 1, harness.Published.Count);
        Assert.Same(rowsBefore, harness.Published[^1].View);
        Assert.Equal(999, harness.Published[^1].ColumnWidths[ColumnName.Source]);
    }

    [Fact]
    public void AColumnOrderChange_IsPublished_ThoughTheRowsAreUnchanged()
    {
        var harness = new Harness();
        harness.OpenLogWithEvents(Event(1, "Alpha"));

        int before = harness.Published.Count;
        IEventColumnView rowsBefore = harness.Source.Current.View;

        harness.Apply(state => state with { ColumnOrder = state.ColumnOrder.Add(ColumnName.Source) });

        Assert.Equal(before + 1, harness.Published.Count);
        Assert.Same(rowsBefore, harness.Published[^1].View);
        Assert.Equal(ColumnName.Source, harness.Published[^1].ColumnOrder[0]);
    }

    [Fact]
    public void AColumnVisibilityChange_IsPublished_ThoughTheRowsAreUnchanged()
    {
        var harness = new Harness();
        harness.OpenLogWithEvents(Event(1, "Alpha"));

        int before = harness.Published.Count;
        IEventColumnView rowsBefore = harness.Source.Current.View;

        harness.Apply(state => state with { Columns = state.Columns.SetItem(ColumnName.Source, true) });

        Assert.Equal(before + 1, harness.Published.Count);
        Assert.Same(rowsBefore, harness.Published[^1].View);
        Assert.True(harness.Published[^1].Columns[ColumnName.Source]);
    }

    [Fact]
    public void ADefaultCollapseChange_IsPublished_ThoughTheRowsAndTabAreUnchanged()
    {
        var harness = new Harness();
        harness.OpenLogWithEvents(Event(1, "Alpha"));

        int before = harness.Published.Count;
        IEventColumnView rowsBefore = harness.Source.Current.View;

        harness.Apply(state => state with { GroupsCollapsedByDefault = true });

        Assert.Equal(before + 1, harness.Published.Count);
        Assert.Same(rowsBefore, harness.Published[^1].View);
        Assert.True(harness.Published[^1].GroupsCollapsedByDefault);
    }

    [Fact]
    public void AFaultCause_IsNeverPublishedBesideAStateThatSaysNothingFailed()
    {
        var harness = new Harness();
        harness.OpenLogWithEvents(Event(1, "Alpha"));

        harness.SetFaultCause("InvalidOperationException: bad predicate");

        Assert.Equal(PresentationState.Faulted, harness.Source.Current.State);
        Assert.NotNull(harness.Source.Current.FaultCause);

        harness.Apply(state => state with { ActiveEventLogId = null });

        Assert.Equal(PresentationState.Current, harness.Source.Current.State);
        Assert.Null(harness.Source.Current.FaultCause);
    }

    [Fact]
    public void ASecondFailureWithADifferentReason_IsPublished_ThoughNothingElseMoved()
    {
        var harness = new Harness();
        harness.OpenLogWithEvents(Event(1, "Alpha"));

        harness.SetFaultCause("InvalidOperationException: the first reason");

        int publishedBefore = harness.Published.Count;

        Assert.Equal("InvalidOperationException: the first reason", harness.Published[^1].FaultCause);

        harness.SetFaultCause("InvalidOperationException: a different reason");

        Assert.Equal(publishedBefore + 1, harness.Published.Count);
        Assert.Equal("InvalidOperationException: a different reason", harness.Published[^1].FaultCause);
    }

    [Fact]
    public void AThrowingSubscriber_IsIsolatedFromTheStatePipelineAndOtherSubscribers()
    {
        var harness = new Harness();
        var reachedSecond = new List<OrderedViewPresentation>();

        harness.Source.Updated += _ => throw new InvalidOperationException("subscriber blew up");
        harness.Source.Updated += presentation => reachedSecond.Add(presentation);

        harness.OpenLogWithEvents(Event(1, "Alpha"));

        Assert.Single(reachedSecond);
    }

    [Fact]
    public void AnEquivalentCollapseSet_IsNotRepublished()
    {
        var harness = new Harness();
        harness.OpenLogWithEvents(Event(1, "Alpha"));
        harness.Apply(state => state with { GroupCollapseOverrides = state.GroupCollapseOverrides.Add("Alpha") });

        int afterCollapse = harness.Published.Count;

        harness.Apply(state => state with
        {
            GroupCollapseOverrides = ImmutableHashSet.Create(StringComparer.Ordinal, "Alpha")
        });

        Assert.Equal(afterCollapse, harness.Published.Count);
    }

    [Fact]
    public void AnOrderingThatTheRowsHaveNotCaughtUpWith_IsPublished_ThoughNothingElseMoved()
    {
        var harness = new Harness();
        harness.OpenLogWithEvents(Event(1, "Alpha"));

        Assert.False(harness.Source.Current.OrderingIsStale);

        int publishedBefore = harness.Published.Count;
        DisplayOrdering orderingBefore = harness.Source.Current.Ordering;

        harness.Apply(state =>
            Reducers.ReduceSetHistogramVisible(state, new SetHistogramVisibleAction(true)));

        Assert.Equal(publishedBefore + 1, harness.Published.Count);
        Assert.True(harness.Published[^1].OrderingIsStale);
        Assert.Equal(orderingBefore, harness.Published[^1].Ordering);
    }

    [Fact]
    public void Construction_AdoptsAChangeThatLandsBetweenSeedAndSubscribe()
    {
        // one-tab state (the reconcile), with no StateChanged raised in between.
        var logId = EventLogId.Create();
        var opened = new LogTableState
        {
            ActiveEventLogId = logId,
            EventTables = ImmutableList.Create(new LogView(logId) { LogName = LogName })
        };
        var logTableState = Substitute.For<IState<LogTableState>>();
        logTableState.Value.Returns(new LogTableState(), opened);

        using var source = new OrderedViewSource(logTableState, Substitute.For<ITraceLogger>());

        Assert.Equal(logId, source.Current.ActiveTabId);
    }

    [Fact]
    public void Current_CarriesTheCommittedOrderingTheViewWasBuiltUnder()
    {
        var harness = new Harness();
        harness.OpenLogWithEvents(Event(1, "Alpha"), Event(2, "Beta"));
        harness.SetCommittedOrdering(ColumnName.EventId, isDescending: false, ColumnName.Source, isGroupDescending: true);

        DisplayOrdering ordering = harness.Source.Current.Ordering;

        Assert.Equal(ColumnName.EventId, ordering.OrderBy);
        Assert.False(ordering.IsDescending);
        Assert.Equal(ColumnName.Source, ordering.GroupBy);
        Assert.True(ordering.IsGroupDescending);
    }

    [Fact]
    public void Current_MatchesTheSeamForTheActiveTab()
    {
        var harness = new Harness();
        harness.OpenLogWithEvents(Event(1, "Alpha"), Event(2, "Beta"));

        LogTableState state = harness.State;

        Assert.Equal(2, harness.Source.Current.View.Count);
        Assert.Equal(state.ActiveEventLogId, harness.Source.Current.ActiveTabId);
    }

    [Fact]
    public void Dispose_StopsPublishing()
    {
        var harness = new Harness();
        var seen = new List<OrderedViewPresentation>();
        harness.Source.Updated += presentation => seen.Add(presentation);

        harness.Source.Dispose();
        harness.OpenLogWithEvents(Event(1, "Alpha"));

        Assert.Empty(seen);
    }

    [Fact]
    public void PublicationLog_AcrossALifecycle_DescribesTheDisplayAtEveryStep()
    {
        var harness = new Harness();

        var alpha = harness.OpenLog(Event(1, "Alpha"));
        harness.AppendTo(alpha, Event(1, "Alpha"), Event(2, "Alpha"));
        var beta = harness.OpenLog(Event(3, "Beta"));
        harness.SwitchTo(alpha);
        harness.CloseEverything();

        Assert.Equal(5, harness.Published.Count);

        Assert.Equal(alpha, harness.Published[0].ActiveTabId);
        Assert.Equal(1, harness.Published[0].View.Count);

        Assert.Equal(alpha, harness.Published[1].ActiveTabId);
        Assert.Equal(2, harness.Published[1].View.Count);

        Assert.Equal(beta, harness.Published[2].ActiveTabId);

        Assert.Equal(alpha, harness.Published[3].ActiveTabId);
        Assert.Equal(2, harness.Published[3].View.Count);

        Assert.Null(harness.Published[4].ActiveTabId);
        Assert.Equal(0, harness.Published[4].View.Count);
        Assert.Equal(PresentationState.Current, harness.Published[4].State);
    }

    [Fact]
    public void PublicationLog_DescribesTheRowsItPublishes_NotAnOrderingStillBeingRequested()
    {
        var harness = new Harness();
        harness.OpenLog(Event(1, "Alpha"), Event(2, "Beta"), Event(3, "Alpha"));
        harness.SetCommittedOrdering(ColumnName.Source, false, ColumnName.Source, false);

        harness.SetRequestedOrdering(ColumnName.Level, true, ColumnName.Level, true);

        OrderedViewPresentation published = harness.Published[^1];

        Assert.Equal(ColumnName.Source, published.Ordering.GroupBy);
        Assert.Equal(ColumnName.Source, published.Ordering.OrderBy);
        Assert.False(IsFragmentedBy(published.View, ColumnName.Source));
    }

    [Fact]
    public void PublicationLog_EveryEntryCarriesTheOrderingItsRowsWereBuiltUnder()
    {
        var harness = new Harness();
        var alpha = harness.OpenLog(Event(1, "Alpha"), Event(2, "Beta"), Event(3, "Alpha"));

        Assert.Null(harness.Published[^1].Ordering.GroupBy);
        Assert.True(IsFragmentedBy(harness.Published[^1].View, ColumnName.Source));

        harness.SetCommittedOrdering(ColumnName.Source, false, ColumnName.Source, false);

        Assert.Equal(ColumnName.Source, harness.Published[^1].Ordering.GroupBy);
        Assert.Equal(ColumnName.Source, harness.Published[^1].Ordering.OrderBy);
        Assert.Equal(alpha, harness.Published[^1].ActiveTabId);
        Assert.False(IsFragmentedBy(harness.Published[^1].View, ColumnName.Source));
    }

    [Fact]
    public void PublicationLog_RevisionsStrictlyIncrease()
    {
        var harness = new Harness();

        var alpha = harness.OpenLog(Event(1, "Alpha"));
        harness.AppendTo(alpha, Event(1, "Alpha"), Event(2, "Alpha"));
        harness.OpenLog(Event(3, "Beta"));
        harness.SwitchTo(alpha);
        harness.CloseEverything();

        Assert.NotEmpty(harness.Published);

        for (int index = 1; index < harness.Published.Count; index++)
        {
            Assert.True(
                harness.Published[index].Revision > harness.Published[index - 1].Revision,
                $"Revision did not advance at publication {index}.");
        }

        Assert.Equal(harness.Published[^1], harness.Source.Current);
    }

    [Fact]
    public void StateChange_RaisesUpdatedWithAnAdvancedRevision()
    {
        var harness = new Harness();
        var seen = new List<OrderedViewPresentation>();
        harness.Source.Updated += presentation => seen.Add(presentation);

        long before = harness.Source.Current.Revision;
        harness.OpenLogWithEvents(Event(1, "Alpha"));

        Assert.Single(seen);
        Assert.True(seen[0].Revision > before);
        Assert.Same(harness.Source.Current, seen[0]);
    }

    [Fact]
    public void StateChange_ThatLeavesTheAnswerUnchanged_DoesNotRaise()
    {
        var harness = new Harness();
        harness.OpenLogWithEvents(Event(1, "Alpha"));

        var seen = new List<OrderedViewPresentation>();
        harness.Source.Updated += presentation => seen.Add(presentation);

        harness.RaiseUnchanged();

        Assert.Empty(seen);
    }

    [Fact]
    public void UnservedTab_ReportsUpdatingRatherThanPassingOffItsRowsAsCurrent()
    {
        var harness = new Harness();
        harness.OpenLogNotYetServed(Event(1, "Alpha"));

        Assert.Equal(PresentationState.Updating, harness.Source.Current.State);
    }

    [Fact]
    public void WithNoActiveTab_PublishesAnEmptyViewRatherThanNull()
    {
        var harness = new Harness();

        Assert.Equal(0, harness.Source.Current.View.Count);
        Assert.Null(harness.Source.Current.ActiveTabId);
        Assert.Equal(PresentationState.Current, harness.Source.Current.State);
    }

    private static ResolvedEvent Event(int id, string source) =>
        new(LogName, LogPathType.Channel) { Id = id, RecordId = id, Source = source };

    private static bool IsFragmentedBy(IEventColumnView view, ColumnName groupBy)
    {
        var runs = new List<string>();

        for (int index = 0; index < view.Count; index++)
        {
            string key = view.GroupKeyAt(view.LocatorAt(index), groupBy);

            if (runs.Count == 0 || !string.Equals(runs[^1], key, StringComparison.Ordinal)) { runs.Add(key); }
        }

        return runs.Count != runs.Distinct(StringComparer.Ordinal).Count();
    }

    private sealed class Harness
    {
        private readonly IState<LogTableState> _logTableState = Substitute.For<IState<LogTableState>>();
        private readonly Dictionary<EventLogId, AosReferenceView> _views = [];

        public Harness()
        {
            State = new LogTableState();
            _logTableState.Value.Returns(_ => State);
            Source = new OrderedViewSource(_logTableState, Substitute.For<ITraceLogger>());

            Source.Updated += presentation => Published.Add(presentation);
        }

        public List<OrderedViewPresentation> Published { get; } = [];

        public OrderedViewSource Source { get; }

        public LogTableState State { get; private set; }

        public void AppendTo(EventLogId logId, params ResolvedEvent[] events)
        {
            _views[logId] = DisplayViewTestFactory.Build(logId, events);
            State = Serving(WithTab(State, logId), logId);

            PublishStateChange();
        }

        public void Apply(Func<LogTableState, LogTableState> change)
        {
            State = change(State);

            PublishStateChange();
        }

        public void CloseEverything()
        {
            _views.Clear();
            State = new LogTableState();

            PublishStateChange();
        }

        public EventLogId OpenLog(params ResolvedEvent[] events)
        {
            var logId = EventLogId.Create();

            _views[logId] = DisplayViewTestFactory.Build(logId, events);
            State = Serving(WithTab(State with { ActiveEventLogId = logId }, logId), logId);

            PublishStateChange();

            return logId;
        }

        public void OpenLogNotYetServed(params ResolvedEvent[] events)
        {
            var logId = EventLogId.Create();

            _views[logId] = DisplayViewTestFactory.Build(logId, events);
            State = new LogTableState
            {
                ActiveEventLogId = logId,
                EventTables = ImmutableList.Create(new LogView(logId) { LogName = LogName })
            };

            PublishStateChange();
        }

        public void OpenLogWithEvents(params ResolvedEvent[] events)
        {
            var logId = EventLogId.Create();

            _views[logId] = DisplayViewTestFactory.Build(logId, events);
            State = new LogTableState
            {
                ActiveEventLogId = logId,
                EventTables = ImmutableList.Create(new LogView(logId) { LogName = LogName })
            };

            State = Serving(State, logId);

            PublishStateChange();
        }

        public void RaiseUnchanged() => PublishStateChange();

        public void SetCommittedOrdering(
            ColumnName? orderBy,
            bool isDescending,
            ColumnName? groupBy,
            bool isGroupDescending)
        {
            LogTableState moved = State with
            {
                OrderBy = orderBy,
                IsDescending = isDescending,
                GroupBy = groupBy,
                IsGroupDescending = isGroupDescending,
                CommittedEffectiveOrderBy = ResolvedEventOrdering.ResolveDefaultOrderBy(
                    orderBy, groupBy, State.DisplayedLogCount, State.TimelineVisible)
            };

            foreach (var (logId, view) in _views.ToList())
            {
                if (!view.HasContext(moved.CommittedSortContext))
                {
                    _views[logId] = view.WithContext(moved.CommittedSortContext);
                }
            }

            State = moved;

            PublishStateChange();
        }

        public void SetDisplayEnabled(bool enabled)
        {
            State = enabled ?
                State with { OrderedViewDisplayEnabled = true } :
                Reducers.ReduceOrderedViewDisplayFaulted(
                    State, new OrderedViewDisplayFaultedAction(new InvalidOperationException("display disabled")));

            PublishStateChange();
        }

        public void SetFaultCause(string? cause)
        {
            State = Reducers.ReduceOrderedViewDisplayFaulted(
                State, new OrderedViewDisplayFaultedAction(new InvalidOperationException("faulted")));

            State = State with { FaultCause = cause };

            PublishStateChange();
        }

        public void SetRequestedOrdering(
            ColumnName? orderBy,
            bool isDescending,
            ColumnName? groupBy,
            bool isGroupDescending)
        {
            State = State with
            {
                RequestedOrderBy = orderBy,
                RequestedIsDescending = isDescending,
                RequestedGroupBy = groupBy,
                RequestedIsGroupDescending = isGroupDescending
            };

            PublishStateChange();
        }

        public void SwitchTo(EventLogId logId)
        {
            State = Serving(State with { ActiveEventLogId = logId }, logId);

            PublishStateChange();
        }

        private static LogTableState WithTab(LogTableState state, EventLogId logId) =>
            state.EventTables.Any(table => table.Id == logId) ?
                state :
                state with { EventTables = state.EventTables.Add(new LogView(logId) { LogName = LogName }) };

        private void PublishStateChange() => _logTableState.StateChanged += Raise.Event<EventHandler>(_logTableState, EventArgs.Empty);

        private LogTableState Serving(LogTableState state, EventLogId logId) =>
            state with
            {
                ActiveOrderedView = new OrderedViewReady(
                    SnapshotVersion: 1,
                    Identity: state.ViewIdentity,
                    Sequence: state.HighestInvalidationSequence,
                    SingleLogId: logId,
                    InScope: [new LogGeneration(logId, 0)],
                    View: _views[logId],
                    Config: state.SortContext,
                    Filter: state.AppliedFilter)
            };
    }
}
