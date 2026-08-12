// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.LogTable.OrderedView;
using EventLogExpert.Runtime.Tests.LogTable.TestSupport;
using NSubstitute;
using System.Collections.Immutable;
using Reducers = EventLogExpert.Runtime.LogTable.Reducers;
using static EventLogExpert.Runtime.Tests.LogTable.OrderedView.CombinedViewParityAsserts;
using static EventLogExpert.Runtime.Tests.TestUtils.RetainedViewTestFactory;

namespace EventLogExpert.Runtime.Tests.LogTable.OrderedView;

public sealed class OrderedViewCombinedRoutingTests
{
    [Fact]
    public async Task ARetainedCombinedView_IsRefusedByASingleLogTabCoveringTheSameLogs()
    {
        CombinedRoutedSetup setup = await BuildAllLogsAsync(seed: 331, logCount: 1, events: 60, emptyLogs: 1);
        EventLogId logA = setup.LogIds[0];
        OrderedViewReady served = setup.Routed.ActiveOrderedView!;

        Assert.Null(served.SingleLogId);

        LogTableState switchedToMember = setup.Routed with
        {
            ActiveEventLogId = logA,
            ActiveOrderedView = null,
            RetainedOrderedViews = RetainedMap(served)
        };

        Assert.Same(EmptyColumnView.Instance, switchedToMember.EventsForLog(logA));
        Assert.False(switchedToMember.IsRetainedViewServable(logA));
    }

    [Fact]
    public async Task ARetainedCombinedView_StillServesItsOwnTabWhenAMemberIsEmpty()
    {
        CombinedRoutedSetup setup = await BuildAllLogsAsync(seed: 330, logCount: 2, events: 90, emptyLogs: 1);
        OrderedViewReady served = setup.Routed.ActiveOrderedView!;

        Assert.DoesNotContain(setup.EmptyLogIds[0], served.InScope.Select(member => member.LogId));
        Assert.Contains(setup.EmptyLogIds[0], served.Identity!.Scope);

        LogTableState retaining = setup.Routed with { ActiveOrderedView = null, RetainedOrderedViews = RetainedMap(served) };

        Assert.True(retaining.IsRetainedViewServable(retaining.ActiveEventLogId!.Value));
    }

    [Fact]
    public async Task AllLogsTab_DisplayFaulted_KeepsShowingTheRowsItWasShowing()
    {
        CombinedRoutedSetup setup = await BuildAllLogsAsync(seed: 301, logCount: 3, events: 90, emptyLogs: 0);

        IEventColumnView beforeFault = setup.Routed.GetActiveDisplayedEvents();

        LogTableState faulted = Reducers.ReduceOrderedViewDisplayFaulted(setup.Routed, Faults.Any);

        Assert.Null(faulted.ActiveOrderedView);
        Assert.Equal(PresentationState.Faulted, faulted.PresentationState);
        Assert.Same(beforeFault, faulted.GetActiveDisplayedEvents());
    }

    [Fact]
    public async Task AllLogsTab_GroupedDisplay_ServesEngineCombinedViewWithGroupParity()
    {
        CombinedRoutedSetup setup = await BuildAllLogsAsync(
            seed: 302, logCount: 3, events: 90, emptyLogs: 0, groupBy: ColumnName.Source);

        IEventColumnView routed = setup.Routed.GetActiveDisplayedEvents();

        Assert.IsType<CombinedOrderedColumnView>(routed);
        AssertOrderMatchesReference(routed, setup.PerLog, setup.Routed.SortContext);
    }

    [Fact]
    public async Task AllLogsTab_NewlyOpenedLogNotYetInScope_StopsServingUntilTheScopeCatchesUp()
    {
        CombinedRoutedSetup setup = await BuildAllLogsAsync(seed: 303, logCount: 3, events: 90, emptyLogs: 0);

        LogTableState opening = setup.Routed with
        {
            EventTables = setup.Routed.EventTables.Add(new LogView(EventLogId.Create()))
        };

        Assert.Same(EmptyColumnView.Instance, opening.GetActiveDisplayedEvents());
    }

    [Fact]
    public async Task AllLogsTab_PendingGroupChange_StopsServingTheViewBuiltUnderTheOldGrouping()
    {
        CombinedRoutedSetup setup = await BuildAllLogsAsync(seed: 302, logCount: 3, events: 90, emptyLogs: 0);

        IEventColumnView beforeRegroup = setup.Routed.GetActiveDisplayedEvents();

        LogTableState pending = Reducers.ReduceSetGroupBy(setup.Routed, new SetGroupByAction(ColumnName.Source));

        Assert.True(pending.HasPendingSortChange);

        Assert.Equal(PresentationState.Updating, pending.PresentationState);
        Assert.Same(beforeRegroup, pending.GetActiveDisplayedEvents());

        Assert.True(pending.OrderingIsStale);
    }

    [Fact]
    public async Task AllLogsTab_PendingSortChange_KeepsShowingTheOrderItAlreadyBuilt()
    {
        CombinedRoutedSetup setup = await BuildAllLogsAsync(seed: 304, logCount: 3, events: 90, emptyLogs: 0);

        IEventColumnView beforeResort = setup.Routed.GetActiveDisplayedEvents();

        LogTableState resorting = Reducers.ReduceToggleSorting(setup.Routed);

        Assert.True(resorting.HasPendingSortChange);
        Assert.Equal(PresentationState.Updating, resorting.PresentationState);
        Assert.Same(beforeResort, resorting.GetActiveDisplayedEvents());
        Assert.True(resorting.OrderingIsStale);
    }

    [Fact]
    public async Task AllLogsTab_RequestSuperseded_ClearsRoutedViewAndBridgesWithWhatWasOnScreen()
    {
        CombinedRoutedSetup setup = await BuildAllLogsAsync(seed: 305, logCount: 3, events: 90, emptyLogs: 0);

        IEventColumnView beforeInvalidation = setup.Routed.GetActiveDisplayedEvents();

        LogTableState resacoped = Reducers.ReduceViewRequestInvalidated(
            setup.Routed,
            new ViewRequestInvalidatedAction(setup.Routed.HighestInvalidationSequence + 1));

        Assert.Null(resacoped.ActiveOrderedView);
        Assert.Equal(PresentationState.Updating, resacoped.PresentationState);
        Assert.Same(beforeInvalidation, resacoped.GetActiveDisplayedEvents());
    }

    [Fact]
    public async Task AllLogsTab_ServesEngineCombinedViewWithFullParityToAnIndependentReference()
    {
        CombinedRoutedSetup setup = await BuildAllLogsAsync(seed: 306, logCount: 3, events: 120, emptyLogs: 0);

        IEventColumnView routedView = setup.Routed.GetActiveDisplayedEvents();

        Assert.IsType<CombinedOrderedColumnView>(routedView);
        Assert.True(routedView.Count > 0);
        AssertOrderMatchesReference(routedView, setup.PerLog, setup.Routed.SortContext);
    }

    [Fact]
    public async Task AllLogsTab_StaleAppliedFilter_StopsServingTheViewBuiltUnderTheOldFilter()
    {
        CombinedRoutedSetup setup = await BuildAllLogsAsync(seed: 307, logCount: 3, events: 90, emptyLogs: 0);

        SavedFilter levelError = SavedFilter.TryCreate("Level == \"Error\"") ??
            throw new InvalidOperationException("Level filter failed to compile.");
        LogTableState filterChanged = setup.Routed with { AppliedFilter = new Filter(null, [levelError]) };

        Assert.Same(EmptyColumnView.Instance, filterChanged.GetActiveDisplayedEvents());
    }

    [Fact]
    public async Task AllLogsTab_WithAnEmptyOpenLog_StillServesEngine()
    {
        CombinedRoutedSetup setup = await BuildAllLogsAsync(seed: 308, logCount: 2, events: 90, emptyLogs: 1);

        IEventColumnView routedView = setup.Routed.GetActiveDisplayedEvents();

        Assert.Equal(4, setup.Routed.EventTables.Count);
        Assert.Contains(setup.EmptyLogIds[0], setup.Routed.ViewIdentity.Scope);
        Assert.IsType<CombinedOrderedColumnView>(routedView);
        Assert.True(routedView.Count > 0);
        AssertOrderMatchesReference(routedView, setup.PerLog, setup.Routed.SortContext);
    }

    [Fact]
    public async Task CombinedScopeWithOneNonEmptyMember_ReportsNoSingleLog_SoSuppressionCannotFireForIt()
    {
        CombinedRoutedSetup setup = await BuildAllLogsAsync(seed: 319, logCount: 1, events: 60, emptyLogs: 1);
        EventLogId logA = setup.LogIds[0];

        Assert.NotNull(setup.Routed.ActiveOrderedView);
        Assert.Single(setup.Routed.ActiveOrderedView.InScope);
        Assert.Null(setup.Routed.ActiveOrderedView.SingleLogId);
        Assert.IsType<CombinedOrderedColumnView>(setup.Routed.GetActiveDisplayedEvents());

        LogTableState switchedToMember = setup.Routed with { ActiveEventLogId = logA };

        Assert.False(switchedToMember.IsOrderedViewServing(logA));
        Assert.Same(EmptyColumnView.Instance, switchedToMember.EventsForLog(logA));
    }

    [Fact]
    public async Task CombinedServing_NeverSatisfiesTheSingleLogSuppressionPredicate()
    {
        CombinedRoutedSetup allLogs = await BuildAllLogsAsync(seed: 310, logCount: 3, events: 90, emptyLogs: 0);

        Assert.IsType<CombinedOrderedColumnView>(allLogs.Routed.GetActiveDisplayedEvents());

        foreach (EventLogId memberId in allLogs.LogIds)
        {
            Assert.False(allLogs.Routed.IsOrderedViewServing(memberId));
        }

        GroupRoutedSetup oneMember = await BuildGroupAsync(seed: 311, logCount: 3, events: 90, memberCount: 1);

        Assert.NotNull(oneMember.Routed.ActiveOrderedView);
        Assert.Equal(oneMember.MemberIds[0], oneMember.Routed.ActiveOrderedView.SingleLogId);
        Assert.False(oneMember.Routed.IsOrderedViewServing(oneMember.MemberIds[0]));
    }

    [Fact]
    public async Task CombinedServing_OrdersRowsTheWayAnIndependentReferenceDoes()
    {
        CombinedRoutedSetup setup = await BuildAllLogsAsync(seed: 511, logCount: 3, events: 90, emptyLogs: 0);

        IEventColumnView combined = setup.Routed.GetActiveDisplayedEvents();

        Assert.IsType<CombinedOrderedColumnView>(combined);

        AssertOrderMatchesReference(combined, setup.PerLog, setup.Routed.SortContext);
    }

    [Fact]
    public async Task GroupTab_MembershipDivergesFromScope_StopsServingTheViewBuiltForTheOldMembership()
    {
        GroupRoutedSetup setup = await BuildGroupAsync(seed: 312, logCount: 3, events: 90, memberCount: 2);

        LogTabGroup widened = setup.Group with { MemberIds = setup.Group.MemberIds.Add(setup.OutsiderId) };
        LogTableState diverged = setup.Routed with { Groups = [widened] };

        Assert.Same(EmptyColumnView.Instance, diverged.GetActiveDisplayedEvents());
    }

    [Fact]
    public async Task GroupTab_NotTheActiveTab_IsNeverGivenTheActiveTabsView()
    {
        GroupRoutedSetup setup = await BuildGroupAsync(seed: 313, logCount: 3, events: 90, memberCount: 2);

        LogTableState background = setup.Routed with { ActiveEventLogId = setup.OutsiderId };

        Assert.Same(EmptyColumnView.Instance, background.DisplayedEventsForTab(setup.Header));
    }

    [Fact]
    public async Task GroupTab_OneMember_ServesTheEngineSingleLogViewWithParity()
    {
        GroupRoutedSetup setup = await BuildGroupAsync(seed: 314, logCount: 3, events: 90, memberCount: 1);

        IEventColumnView routedView = setup.Routed.GetActiveDisplayedEvents();

        Assert.IsType<OrderedColumnView>(routedView);
        Assert.True(routedView.Count > 0);
        AssertOrderMatchesReference(routedView, setup.PerLog, setup.Routed.SortContext);
    }

    [Fact]
    public async Task GroupTab_SubsetOfOpenLogs_ServesEngineCombinedViewWithFullParityToAnIndependentReference()
    {
        GroupRoutedSetup setup = await BuildGroupAsync(seed: 315, logCount: 3, events: 120, memberCount: 2);

        IEventColumnView routedView = setup.Routed.GetActiveDisplayedEvents();

        Assert.IsType<CombinedOrderedColumnView>(routedView);
        Assert.True(routedView.Count > 0);

        Assert.True(routedView.Count < setup.AllLogsRowCount);
        AssertOrderMatchesReference(routedView, setup.PerLog, setup.Routed.SortContext);
    }

    [Fact]
    public void ScopeAdvance_RejectsARebuildCapturedUnderThePreviousScope()
    {
        var sample = new OrderedViewSample(seed: 321, logCount: 2);
        sample.SeedInterleaved(60);
        EventLogId logA = sample.LogId(0), logB = sample.LogId(1);

        var state = new OrderedViewState();
        IEventColumnReader readerA = sample.Reader(0), readerB = sample.Reader(1);

        state.ReconcileLog(logA, readerA);
        state.ReconcileLog(logB, readerB);

        Assert.NotNull(ViewRequests.AdvanceScope(state, [logA, logB], 1));

        RebuildRequest staleForCombined = state.BeginRebuild((_, _) => true, new SortContext(ColumnName.DateAndTime, true, null, false));
        Assert.Equal(1, staleForCombined.ScopeVersion);
        Assert.Null(staleForCombined.SingleLog);

        Assert.True(state.TrySetActiveScope([logB], scopeVersion: 2));
        Assert.False(state.TryAdoptRebuild(staleForCombined, OrderedViewState.BuildIndex(staleForCombined, CancellationToken.None)));

        RebuildRequest reseed = state.CaptureScopeReseed();
        Assert.Equal(2, reseed.ScopeVersion);
        Assert.Equal(logB, reseed.SingleLog);
        Assert.True(state.TryAdoptRebuild(reseed, OrderedViewState.BuildIndex(reseed, CancellationToken.None)));
    }

    [Fact]
    public async Task StaleInvalidation_DoesNotWipeANewerRequestsPublication()
    {
        CombinedRoutedSetup setup = await BuildAllLogsAsync(seed: 322, logCount: 3, events: 90, emptyLogs: 0);

        Assert.NotNull(setup.Routed.ActiveOrderedView);

        LogTableState stale = Reducers.ReduceViewRequestInvalidated(
            setup.Routed,
            new ViewRequestInvalidatedAction(setup.Routed.HighestInvalidationSequence));

        Assert.NotNull(stale.ActiveOrderedView);
        Assert.IsType<CombinedOrderedColumnView>(stale.GetActiveDisplayedEvents());
    }

    [Fact]
    public async Task TabSwitch_AllLogsToSingleLogAndBack_RoutesTheRightEngineViewEachTime()
    {
        var sample = new OrderedViewSample(seed: 317, logCount: 3);
        sample.SeedInterleaved(120);
        EventLogId[] logIds = [sample.LogId(0), sample.LogId(1), sample.LogId(2)];
        RawEventStoreState rawStore = RawStore(sample, logIds, []);
        var combined = new LogView(EventLogId.Create()) { GroupId = LogTabGroupId.AllLogs };
        ImmutableList<LogView> tables = [combined, .. logIds.Select(id => new LogView(id))];

        LogTableState Live(EventLogId activeId) => CommittedFrom(
            new LogTableState { ActiveEventLogId = activeId, EventTables = tables, IsDescending = true, RequestedIsDescending = true });

        await using var harness = new OrderedViewShadowHarness();
        var capture = new InvalidationCapture(harness);
        var eventLog = new EventLogState();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        LogTableState allLogsFirst = await RouteAsync(harness, capture, Live(combined.Id), rawStore, eventLog, cancellationToken);
        Assert.IsType<CombinedOrderedColumnView>(allLogsFirst.GetActiveDisplayedEvents());
        int combinedCount = allLogsFirst.GetActiveDisplayedEvents().Count;

        LogTableState single = await RouteAsync(harness, capture, Live(logIds[1]), rawStore, eventLog, cancellationToken);
        IEventColumnView singleView = single.GetActiveDisplayedEvents();
        Assert.IsType<OrderedColumnView>(singleView);
        Assert.True(singleView.Count < combinedCount);
        AssertOrderMatchesReference(singleView, [(logIds[1], sample.Events(1))], single.SortContext);

        LogTableState allLogsAgain = await RouteAsync(harness, capture, Live(combined.Id), rawStore, eventLog, cancellationToken);
        IEventColumnView reseeded = allLogsAgain.GetActiveDisplayedEvents();
        Assert.IsType<CombinedOrderedColumnView>(reseeded);
        Assert.Equal(combinedCount, reseeded.Count);
        AssertOrderMatchesReference(
            reseeded,
            [.. logIds.Select((id, index) => (id, sample.Events(index)))],
            allLogsAgain.SortContext);
    }

    [Fact]
    public async Task TheReferenceCheckItself_FailsWhenTheOrderIsWrong()
    {
        CombinedRoutedSetup setup = await BuildAllLogsAsync(seed: 512, logCount: 3, events: 90, emptyLogs: 0);

        IEventColumnView combined = setup.Routed.GetActiveDisplayedEvents();

        IReadOnlyList<(EventLogId LogId, IReadOnlyList<ResolvedEvent> Events)> perturbed =
            [.. setup.PerLog.Select(entry => (entry.LogId, (IReadOnlyList<ResolvedEvent>)[.. entry.Events.Reverse()]))];

        Assert.ThrowsAny<Exception>(
            () => AssertOrderMatchesReference(combined, perturbed, setup.Routed.SortContext));
    }

    [Fact]
    public async Task ViewRequest_CarriesItsScopeReaders_SoEveryRoutablePublishIsMemberComplete()
    {
        var sample = new OrderedViewSample(seed: 320, logCount: 2);
        sample.SeedInterleaved(80);
        EventLogId logA = sample.LogId(0), logB = sample.LogId(1);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        await using var writer = new OrderedViewWriter(publishIntervalMs: 0);
        var updates = new List<OrderedViewUpdate>();
        Lock gate = new();

        writer.Updated += update =>
        {
            lock (gate) { updates.Add(update); }
        };

        ViewRequest request = ViewRequests.For(
            new SortContext(ColumnName.DateAndTime, true, null, false),
            ViewRequests.EmptyFilter,
            [logA, logB],
            readers: new Dictionary<EventLogId, IEventColumnReader>
            {
                [logA] = sample.Reader(0), [logB] = sample.Reader(1)
            });

        writer.EnqueueViewRequest(request);
        await writer.DrainAsync().WaitAsync(OrderedViewTestTimeouts.Default, cancellationToken);
        await writer.DrainAsync().WaitAsync(OrderedViewTestTimeouts.Default, cancellationToken);

        OrderedViewReady[] routable;

        lock (gate)
        {
            routable = [.. updates.OfType<OrderedViewReady>().Where(update => update.Identity == request.Identity)];
        }

        Assert.NotEmpty(routable);

        Assert.All(routable, ready =>
        {
            Assert.Null(ready.SingleLogId);
            Assert.Equal([logA, logB], ready.InScope.Select(member => member.LogId).ToHashSet());
        });
    }

    private static async Task<CombinedRoutedSetup> BuildAllLogsAsync(
        int seed,
        int logCount,
        int events,
        int emptyLogs,
        ColumnName? groupBy = null)
    {
        var sample = new OrderedViewSample(seed, logCount);
        sample.SeedInterleaved(events);
        EventLogId[] logIds = [.. Enumerable.Range(0, logCount).Select(sample.LogId)];
        EventLogId[] emptyLogIds = [.. Enumerable.Range(0, emptyLogs).Select(_ => EventLogId.Create())];

        var combined = new LogView(EventLogId.Create()) { GroupId = LogTabGroupId.AllLogs };
        ImmutableList<LogView> tables =
            [combined, .. logIds.Select(id => new LogView(id)), .. emptyLogIds.Select(id => new LogView(id))];

        LogTableState live = CommittedFrom(
            new LogTableState
            {
                ActiveEventLogId = combined.Id,
                EventTables = tables,
                IsDescending = true,
                RequestedIsDescending = true,

                GroupBy = groupBy,
                RequestedGroupBy = groupBy
            });

        (LogTableState scoped, LogTableState routed) = await RouteWithScopeAsync(
            live, RawStore(sample, logIds, emptyLogIds), new EventLogState(), TestContext.Current.CancellationToken);

        return new CombinedRoutedSetup(
            scoped,
            routed,
            logIds,
            emptyLogIds,
            [.. logIds.Select((id, index) => (id, sample.Events(index)))]);
    }

    private static async Task<GroupRoutedSetup> BuildGroupAsync(int seed, int logCount, int events, int memberCount)
    {
        var sample = new OrderedViewSample(seed, logCount);
        sample.SeedInterleaved(events);
        EventLogId[] logIds = [.. Enumerable.Range(0, logCount).Select(sample.LogId)];
        EventLogId[] memberIds = [.. logIds.Take(memberCount)];

        var groupId = LogTabGroupId.Create();
        var header = new LogView(EventLogId.Create()) { GroupId = groupId };
        var group = new LogTabGroup(groupId, "Group", [.. memberIds]);

        LogTableState live = CommittedFrom(
            new LogTableState
            {
                ActiveEventLogId = header.Id,
                EventTables = [header, .. logIds.Select(id => new LogView(id))],
                Groups = [group],
                IsDescending = true,
                RequestedIsDescending = true
            });

        (LogTableState scoped, LogTableState routed) = await RouteWithScopeAsync(
            live, RawStore(sample, logIds, []), new EventLogState(), TestContext.Current.CancellationToken);

        return new GroupRoutedSetup(
            scoped,
            routed,
            header,
            group,
            memberIds,
            logIds[^1],
            Enumerable.Range(0, logCount).Sum(index => sample.Events(index).Count),
            [.. memberIds.Select(id => (id, sample.Events(Array.IndexOf(logIds, id))))]);
    }

    private static LogTableState CommittedFrom(LogTableState state) =>
        state with
        {
            CommittedEffectiveOrderBy = ResolvedEventOrdering.ResolveDefaultOrderBy(
                state.OrderBy, state.GroupBy, state.DisplayedLogCount, state.TimelineVisible)
        };

    private static RawEventStoreState RawStore(OrderedViewSample sample, EventLogId[] logIds, EventLogId[] emptyLogIds)
    {
        ImmutableDictionary<EventLogId, EventColumnStore> byLog = ImmutableDictionary<EventLogId, EventColumnStore>.Empty;

        for (int i = 0; i < logIds.Length; i++)
        {
            byLog = byLog.Add(logIds[i], EventColumnStore.Build(sample.Events(i), 0, 0));
        }

        foreach (EventLogId emptyLogId in emptyLogIds)
        {
            byLog = byLog.Add(emptyLogId, EventColumnStore.Build([], 0, 0));
        }

        return new RawEventStoreState { ByLog = byLog };
    }

    private static async Task<LogTableState> RouteAsync(
        OrderedViewShadowHarness harness,
        InvalidationCapture capture,
        LogTableState live,
        RawEventStoreState rawStore,
        EventLogState eventLog,
        CancellationToken cancellationToken)
    {
        harness.SetState(live, rawStore, eventLog);
        await harness.Effects.HandleSetActiveTable(harness.Dispatcher);

        OrderedViewUpdate update = await harness.DrainToUpdateAsync(cancellationToken);
        LogTableState scoped = Reducers.ReduceViewRequestInvalidated(
            live with { AppliedFilter = eventLog.AppliedFilter }, capture.Latest);

        Assert.Null(harness.Issuer.LastFault);

        return Reducers.ReduceOrderedViewUpdated(scoped, new OrderedViewUpdatedAction(update));
    }

    private static async Task<(LogTableState Scoped, LogTableState Routed)> RouteWithScopeAsync(
        LogTableState live,
        RawEventStoreState rawStore,
        EventLogState eventLog,
        CancellationToken cancellationToken)
    {
        await using var harness = new OrderedViewShadowHarness();
        var capture = new InvalidationCapture(harness);

        LogTableState routed = await RouteAsync(harness, capture, live, rawStore, eventLog, cancellationToken);
        LogTableState scoped = Reducers.ReduceViewRequestInvalidated(
            live with { AppliedFilter = eventLog.AppliedFilter }, capture.Latest);

        return (scoped, routed);
    }

    private sealed class InvalidationCapture
    {
        private readonly Lock _gate = new();

        private ViewRequestInvalidatedAction? _latest;

        public InvalidationCapture(OrderedViewShadowHarness harness) =>
            harness.Dispatcher.When(dispatcher => dispatcher.Dispatch(Arg.Any<object>())).Do(callInfo =>
            {
                if (callInfo.Arg<object>() is ViewRequestInvalidatedAction invalidation)
                {
                    lock (_gate) { _latest = invalidation; }
                }
            });

        public ViewRequestInvalidatedAction Latest
        {
            get
            {
                lock (_gate)
                {
                    return _latest ?? throw new InvalidOperationException("The shadow issued no view request.");
                }
            }
        }
    }

    private sealed record CombinedRoutedSetup(
        LogTableState Scoped,
        LogTableState Routed,
        EventLogId[] LogIds,
        EventLogId[] EmptyLogIds,
        IReadOnlyList<(EventLogId LogId, IReadOnlyList<ResolvedEvent> Events)> PerLog);

    private sealed record GroupRoutedSetup(
        LogTableState Scoped,
        LogTableState Routed,
        LogView Header,
        LogTabGroup Group,
        EventLogId[] MemberIds,
        EventLogId OutsiderId,
        int AllLogsRowCount,
        IReadOnlyList<(EventLogId LogId, IReadOnlyList<ResolvedEvent> Events)> PerLog);
}

