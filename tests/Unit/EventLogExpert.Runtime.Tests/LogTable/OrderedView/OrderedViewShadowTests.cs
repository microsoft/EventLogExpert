// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.Histogram;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.LogTable.OrderedView;
using EventLogExpert.Runtime.Tests.LogTable.TestSupport;
using Fluxor;
using NSubstitute;
using System.Collections.Immutable;
using CloseLogAction = EventLogExpert.Runtime.LogTable.CloseLogAction;
using Reducers = EventLogExpert.Runtime.LogTable.Reducers;
using static EventLogExpert.Runtime.Tests.LogTable.OrderedView.CombinedViewParityAsserts;
using static EventLogExpert.Runtime.Tests.TestUtils.RetainedViewTestFactory;

namespace EventLogExpert.Runtime.Tests.LogTable.OrderedView;

public sealed class OrderedViewShadowTests
{
    [Fact]
    public async Task AFrameThatRe_TrustsTheEngine_AlsoCommitsTheOrderingItWasBuiltUnder()
    {
        RoutedSetup setup = await BuildRoutedAsync(seed: 105, count: 60, TestContext.Current.CancellationToken, ColumnName.Source);
        LogTableState pending = PendingSortState(setup) with { OrderedViewDisplayEnabled = false };

        LogTableState after = Reducers.ReduceOrderedViewUpdated(pending, Republish(setup));

        Assert.True(after.OrderedViewDisplayEnabled);
        Assert.Equal(pending.RequestedGroupBy, after.GroupBy);
        Assert.False(after.HasPendingSortChange);
    }

    [Fact]
    public async Task ARetainedView_IsNotServedAfterItsScopeShrank()
    {
        RoutedSetup setup = await BuildRoutedAsync(seed: 204, count: 60, TestContext.Current.CancellationToken);

        var departed = EventLogId.Create();
        ViewIdentity current = setup.Routed.ViewIdentity;

        LogTableState shrunk = Retaining(setup) with
        {
            RetainedOrderedViews = RetainedMap(setup.Update with
            {
                Identity = new ViewIdentity(current.ActiveLogId,
                    current.Scope.Add(departed),
                    current.RequestedOrderBy,
                    current.RequestedIsDescending,
                    current.RequestedGroupBy,
                    current.RequestedIsGroupDescending,
                    current.TimelineVisible,
                    current.IsMultiLogDisplay,
                    current.Filter)
            })
        };

        Assert.Same(EmptyColumnView.Instance, shrunk.EventsForLog(setup.LogId));
    }

    [Fact]
    public async Task ARetainedView_IsNotServedAfterTheFilterChanged()
    {
        RoutedSetup setup = await BuildRoutedAsync(seed: 202, count: 60, TestContext.Current.CancellationToken);

        LogTableState filtered = Retaining(setup) with
        {
            AppliedFilter = new Filter(new DateFilter { IsEnabled = true }, [])
        };

        Assert.Same(EmptyColumnView.Instance, filtered.EventsForLog(setup.LogId));
    }

    [Fact]
    public async Task ARetainedView_IsNotServedAfterTheOrderingItWasBuiltUnderMoved()
    {
        RoutedSetup setup = await BuildRoutedAsync(seed: 203, count: 60, TestContext.Current.CancellationToken);

        LogTableState reordered = Retaining(setup) with { GroupBy = ColumnName.Source };

        Assert.Same(EmptyColumnView.Instance, reordered.EventsForLog(setup.LogId));
    }

    [Fact]
    public async Task ARetainedView_IsNotServedToADifferentTab()
    {
        RoutedSetup setup = await BuildRoutedAsync(seed: 201, count: 60, TestContext.Current.CancellationToken);

        var otherTab = EventLogId.Create();

        Assert.Same(EmptyColumnView.Instance, Retaining(setup).EventsForLog(otherTab));
    }

    [Fact]
    public async Task ARetainedView_IsNotServedToADifferentTabCoveringTheSameLogs()
    {
        RoutedSetup setup = await BuildRoutedAsync(seed: 206, count: 60, TestContext.Current.CancellationToken);
        ViewIdentity current = setup.Routed.ViewIdentity;

        LogTableState otherTab = Retaining(setup) with
        {
            RetainedOrderedViews = RetainedMap(setup.Update with
            {
                Identity = new ViewIdentity(EventLogId.Create(),
                    current.Scope,
                    current.RequestedOrderBy,
                    current.RequestedIsDescending,
                    current.RequestedGroupBy,
                    current.RequestedIsGroupDescending,
                    current.TimelineVisible,
                    current.IsMultiLogDisplay,
                    current.Filter)
            })
        };

        Assert.False(otherTab.IsRetainedViewServable(setup.LogId));
    }

    [Fact]
    public async Task ARetainedView_IsNotTakenFromAnUpdateTheDisplayNeverServed()
    {
        RoutedSetup setup = await BuildRoutedAsync(seed: 207, count: 60, TestContext.Current.CancellationToken);

        OrderedViewReady onScreen = setup.Update with { SnapshotVersion = setup.Update.SnapshotVersion - 1 };

        LogTableState declined = setup.Routed with
        {
            RequestedOrderBy = ColumnName.Source,
            RetainedOrderedViews = RetainedMap(onScreen)
        };

        Assert.NotNull(declined.ActiveOrderedView);
        Assert.Null(declined.ServingOrderedView);

        LogTableState afterDrop = Reducers.ReduceSetOrderBy(declined, new SetOrderByAction(ColumnName.Level));

        Assert.Same(onScreen, afterDrop.RetainedFor(onScreen));
    }

    [Fact]
    public async Task ARetainedView_IsStillServedByAFaultedEngine_BecauseTheRowsWereAlreadyOnScreen()
    {
        RoutedSetup setup = await BuildRoutedAsync(seed: 205, count: 60, TestContext.Current.CancellationToken);

        LogTableState faulted = Retaining(setup) with { OrderedViewDisplayEnabled = false };

        Assert.True(faulted.IsRetainedViewServable(setup.Routed.ActiveEventLogId!.Value));
        Assert.Equal(PresentationState.Faulted, faulted.PresentationState);
    }

    [Fact]
    public async Task ARetainedView_ServesTheSameTabWhileTheNextOneIsBuilding()
    {
        RoutedSetup setup = await BuildRoutedAsync(seed: 200, count: 60, TestContext.Current.CancellationToken);

        Assert.Same(setup.Update.View, Retaining(setup).EventsForLog(setup.LogId));
    }

    [Fact]
    public async Task ASortChange_RetainsWhatWasOnScreenWithoutWaitingForTheDriver()
    {
        RoutedSetup setup = await BuildRoutedAsync(seed: 205, count: 60, TestContext.Current.CancellationToken);

        Assert.NotNull(setup.Routed.ServingOrderedView);

        LogTableState afterSort = Reducers.ReduceSetOrderBy(setup.Routed, new SetOrderByAction(ColumnName.Source));

        Assert.Null(afterSort.ServingOrderedView);
        Assert.Same(setup.Update, afterSort.RetainedFor(setup.Update));
    }

    [Fact]
    public async Task AddingASecondLog_ReturningToTheFirst_ServesItsRetainedViewNotBlank()
    {
        RoutedSetup setup = await BuildRoutedAsync(seed: 94, count: 60, TestContext.Current.CancellationToken);
        EventLogId logA = setup.LogId;

        Assert.True(setup.Update.View.Count > 0);
        Assert.NotSame(EmptyColumnView.Instance, setup.Update.View);

        LogTableState afterAdd = Reducers.ReduceAddTable(
            setup.Routed, new AddTableAction(new EventLogData("Log1", LogPathType.Channel)));

        Assert.NotEqual(logA, afterAdd.ActiveEventLogId);
        Assert.Same(EmptyColumnView.Instance, afterAdd.EventsForLog(logA));

        LogTableState backOnA = Reducers.ReduceSetActiveTable(afterAdd, new SetActiveTableAction(logA));

        Assert.Equal(logA, backOnA.ActiveEventLogId);
        Assert.False(backOnA.IsOrderedViewServing(logA));
        Assert.True(backOnA.IsRetainedViewServable(logA));
        Assert.Same(setup.Update, backOnA.RetainedFor(setup.Update));
        Assert.Same(setup.Update.View, backOnA.EventsForLog(logA));
        Assert.True(backOnA.OrderingIsStale);
        Assert.Equal(PresentationState.Updating, backOnA.PresentationState);

        LogTableState withoutRetention = backOnA with
        {
            RetainedOrderedViews = ImmutableDictionary<EventLogId, OrderedViewReady>.Empty
        };

        Assert.Same(EmptyColumnView.Instance, withoutRetention.EventsForLog(logA));
    }

    [Fact]
    public async Task AdoptingANewGrouping_ClearsTheCollapseKeysOfTheGroupingItReplaces()
    {
        RoutedSetup setup = await BuildRoutedAsync(seed: 103, count: 60, TestContext.Current.CancellationToken, ColumnName.Source);
        LogTableState pending = PendingSortState(setup) with
        {
            GroupsCollapsedByDefault = true,
            GroupCollapseOverrides = ImmutableHashSet.Create(StringComparer.Ordinal, "1000")
        };

        LogTableState committed = Reducers.ReduceOrderedViewUpdated(pending, Republish(setup));

        Assert.False(committed.GroupsCollapsedByDefault);
        Assert.Empty(committed.GroupCollapseOverrides);
    }

    [Fact]
    public async Task AdoptingTheEnginesAnswer_CommitsTheOrderingItWasBuiltFor()
    {
        RoutedSetup setup = await BuildRoutedAsync(seed: 101, count: 60, TestContext.Current.CancellationToken, ColumnName.Source);
        LogTableState pending = PendingSortState(setup);

        Assert.True(pending.HasPendingSortChange);

        LogTableState committed = Reducers.ReduceOrderedViewUpdated(pending, Republish(setup));

        Assert.Equal(ColumnName.Source, committed.GroupBy);
        Assert.False(committed.HasPendingSortChange);
    }

    [Fact]
    public async Task AfterAnEngineFault_TheRetainedViewServes()
    {
        RoutedSetup setup = await BuildRoutedAsync(seed: 98, count: 60, TestContext.Current.CancellationToken);
        EventLogId logA = setup.LogId;

        LogTableState faulted = Reducers.ReduceOrderedViewDisplayFaulted(setup.Routed, Faults.Any);

        Assert.False(faulted.OrderedViewDisplayEnabled);
        Assert.Same(setup.Update.View, faulted.EventsForLog(logA));

        LogTableState withoutRetention = faulted with
        {
            RetainedOrderedViews = ImmutableDictionary<EventLogId, OrderedViewReady>.Empty
        };

        Assert.Equal(0, withoutRetention.EventsForLog(logA).Count);
    }

    [Fact]
    public async Task ApplyFilter_ActiveDateRange_ShadowMatchesLiveFilteredSubset()
    {
        var sample = new OrderedViewSample(seed: 31, logCount: 1);
        sample.SeedInterleaved(120);
        EventLogId logId = sample.LogId(0);

        await using var harness = new OrderedViewShadowHarness();

        var (unfiltered, rawStore, eventLog, reader) = SingleLog(logId, sample.Events(0), descending: true);
        harness.SetState(unfiltered, rawStore, eventLog);
        await harness.Effects.HandleLoadEvents(new LoadEventsAction(LogData(logId), sample.Events(0)), harness.Dispatcher);

        OrderedViewSnapshot unfilteredSnapshot = await harness.Writer.DrainAsync();
        int unfilteredCount = unfilteredSnapshot.Count;
        AssertShadowMatchesReference(unfilteredSnapshot, reader, logId, sample.Events(0), unfiltered.SortContext);

        var filter = new Filter(
            new DateFilter { After = sample.Events(0)[20].TimeCreated, Before = sample.Events(0)[90].TimeCreated, IsEnabled = true },
            []);
        var (filtered, _, filteredEventLog, _) = SingleLogFiltered(logId, sample.Events(0), filter, descending: true);
        harness.SetState(filtered, rawStore, filteredEventLog);
        await harness.Effects.HandleApplyFilter(new ApplyFilterAction(filter), harness.Dispatcher);

        OrderedViewSnapshot filteredSnapshot = await harness.Writer.DrainAsync();

        Assert.InRange(filteredSnapshot.Count, 1, unfilteredCount - 1);
        AssertShadowSurvivorsAreInReferenceOrder(filteredSnapshot, reader, sample.Events(0), filtered.SortContext);
        Assert.Null(harness.Issuer.LastFault);
    }

    [Fact]
    public async Task ApplyFilter_ColumnFilter_ShadowMatchesLiveSurvivorPredicate()
    {
        var sample = new OrderedViewSample(seed: 41, logCount: 1);
        sample.SeedInterleaved(120);
        EventLogId logId = sample.LogId(0);

        await using var harness = new OrderedViewShadowHarness();
        var (unfiltered, rawStore, eventLog, reader) = SingleLog(logId, sample.Events(0), descending: true);
        harness.SetState(unfiltered, rawStore, eventLog);
        await harness.Effects.HandleLoadEvents(new LoadEventsAction(LogData(logId), sample.Events(0)), harness.Dispatcher);
        int unfilteredCount = (await harness.Writer.DrainAsync()).Count;

        SavedFilter levelError = SavedFilter.TryCreate("Level == \"Error\"") ??
            throw new InvalidOperationException("Level filter failed to compile.");
        var filter = new Filter(null, [levelError]);
        var (filtered, _, filteredEventLog, _) = SingleLogFiltered(logId, sample.Events(0), filter, descending: true);
        harness.SetState(filtered, rawStore, filteredEventLog);
        await harness.Effects.HandleApplyFilter(new ApplyFilterAction(filter), harness.Dispatcher);

        OrderedViewSnapshot filteredSnapshot = await harness.Writer.DrainAsync();

        Assert.InRange(filteredSnapshot.Count, 1, unfilteredCount - 1);
        AssertShadowSurvivorsAreInReferenceOrder(filteredSnapshot, reader, sample.Events(0), filtered.SortContext);
        Assert.Null(harness.Issuer.LastFault);
    }

    [Fact]
    public async Task ApplyFilter_DisabledDateFilterClearsAnActiveFilter_ShadowReturnsToEveryRow()
    {
        var sample = new OrderedViewSample(seed: 37, logCount: 1);
        sample.SeedInterleaved(90);
        EventLogId logId = sample.LogId(0);

        await using var harness = new OrderedViewShadowHarness();
        var (unfiltered, rawStore, eventLog, reader) = SingleLog(logId, sample.Events(0), descending: true);
        harness.SetState(unfiltered, rawStore, eventLog);
        await harness.Effects.HandleLoadEvents(new LoadEventsAction(LogData(logId), sample.Events(0)), harness.Dispatcher);
        int fullCount = (await harness.Writer.DrainAsync()).Count;

        SavedFilter levelError = SavedFilter.TryCreate("Level == \"Error\"") ??
            throw new InvalidOperationException("Level filter failed to compile.");
        var active = new Filter(null, [levelError]);
        var (activeState, _, activeEventLog, _) = SingleLogFiltered(logId, sample.Events(0), active, descending: true);
        harness.SetState(activeState, rawStore, activeEventLog);
        await harness.Effects.HandleApplyFilter(new ApplyFilterAction(active), harness.Dispatcher);

        OrderedViewSnapshot narrowed = await harness.Writer.DrainAsync();
        Assert.InRange(narrowed.Count, 1, fullCount - 1);
        AssertShadowSurvivorsAreInReferenceOrder(narrowed, reader, sample.Events(0), activeState.SortContext);

        var disabled = new Filter(
            new DateFilter { After = sample.Events(0)[40].TimeCreated, Before = sample.Events(0)[41].TimeCreated, IsEnabled = false },
            []);
        var (fullState, _, fullEventLog, _) = SingleLogFiltered(logId, sample.Events(0), disabled, descending: true);
        harness.SetState(fullState, rawStore, fullEventLog);
        await harness.Effects.HandleApplyFilter(new ApplyFilterAction(disabled), harness.Dispatcher);

        AssertShadowMatchesReference(
            await harness.Writer.DrainAsync(), reader, logId, sample.Events(0), fullState.SortContext);

        Assert.Null(harness.Issuer.LastFault);
    }

    [Fact]
    public async Task CloseAllLogs_EmptiesTheShadow()
    {
        var sample = new OrderedViewSample(seed: 13, logCount: 1);
        sample.SeedInterleaved(50);
        EventLogId logId = sample.LogId(0);

        await using var harness = new OrderedViewShadowHarness();
        var (logTable, rawStore, eventLog, _) = SingleLog(logId, sample.Events(0), descending: true);
        harness.SetState(logTable, rawStore, eventLog);
        await harness.Effects.HandleLoadEvents(new LoadEventsAction(LogData(logId), sample.Events(0)), harness.Dispatcher);
        Assert.True((await harness.Writer.DrainAsync()).Count > 0);

        harness.SetState(new LogTableState(), new RawEventStoreState(), new EventLogState());
        await harness.Effects.HandleCloseAllLogs(harness.Dispatcher);

        Assert.Equal(0, (await harness.Writer.DrainAsync()).Count);
        Assert.Null(harness.Issuer.LastFault);
    }

    [Fact]
    public async Task CloseThenReopenWithFreshId_ShadowRebuildsForTheNewLog()
    {
        var sample = new OrderedViewSample(seed: 53, logCount: 1);
        sample.SeedInterleaved(50);
        EventLogId original = sample.LogId(0);
        IReadOnlyList<ResolvedEvent> events = [.. sample.Events(0)];

        await using var harness = new OrderedViewShadowHarness();
        var (loaded, loadedRaw, eventLog, _) = SingleLog(original, events, descending: true);
        harness.SetState(loaded, loadedRaw, eventLog);
        await harness.Effects.HandleLoadEvents(new LoadEventsAction(LogData(original), events), harness.Dispatcher);
        Assert.True((await harness.Writer.DrainAsync()).Count > 0);

        harness.SetState(new LogTableState(), new RawEventStoreState(), new EventLogState());
        await harness.Effects.HandleCloseLog(new CloseLogAction(original), harness.Dispatcher);
        Assert.Equal(0, (await harness.Writer.DrainAsync()).Count);

        EventLogId reopened = EventLogId.Create();
        var (reloaded, reloadedRaw, _, reopenedReader) = SingleLog(reopened, events, descending: true);
        harness.SetState(reloaded, reloadedRaw, eventLog);
        await harness.Effects.HandleLoadEvents(new LoadEventsAction(LogData(reopened), events), harness.Dispatcher);
        AssertShadowMatchesReference(
            await harness.Writer.DrainAsync(), reopenedReader, reopened, events, reloaded.SortContext);

        Assert.Null(harness.Issuer.LastFault);
    }

    [Fact]
    public async Task Disabled_ShadowIgnoresEveryAction()
    {
        var sample = new OrderedViewSample(seed: 17, logCount: 1);
        sample.SeedInterleaved(30);
        EventLogId logId = sample.LogId(0);

        await using var harness = new OrderedViewShadowHarness();
        harness.Issuer.Enabled = false;
        var (logTable, rawStore, eventLog, _) = SingleLog(logId, sample.Events(0), descending: true);
        harness.SetState(logTable, rawStore, eventLog);

        await harness.Effects.HandleLoadEvents(new LoadEventsAction(LogData(logId), sample.Events(0)), harness.Dispatcher);

        Assert.Equal(0, (await harness.Writer.DrainAsync()).Count);
        Assert.Null(harness.Issuer.LastFault);
    }

    [Fact]
    public async Task ForcingTheGroupingOff_RecordsWhatWasOnScreen_BecauseNothingLaterCan()
    {
        RoutedSetup setup = await BuildRoutedAsync(seed: 411, count: 60, TestContext.Current.CancellationToken);

        LogTableState groupedRequest = setup.Routed with
        {
            GroupBy = ColumnName.Source, RequestedGroupBy = ColumnName.Source
        };

        LogTableState grouped = groupedRequest with
        {
            ActiveOrderedView = setup.Update with { Config = groupedRequest.SortContext }
        };

        Assert.NotNull(grouped.ServingOrderedView);

        LogTableState ungrouped = Reducers.ReduceLoadColumnsCompleted(
            grouped,
            new LoadColumnsCompletedAction(
                ImmutableDictionary<ColumnName, bool>.Empty.Add(ColumnName.Source, false),
                grouped.ColumnWidths,
                grouped.ColumnOrder));

        Assert.Equal(ColumnName.Source, ungrouped.GroupBy);
        Assert.Null(ungrouped.RequestedGroupBy);
        Assert.Same(grouped.ServingOrderedView, ungrouped.RetainedFor(grouped.ServingOrderedView!));
    }

    [Fact]
    public async Task GoNoGo_AfterAFault_TheDisplayKeepsTheRowsTheUserWasReading()
    {
        RoutedSetup setup = await BuildRoutedAsync(seed: 900, count: 60, TestContext.Current.CancellationToken);

        Assert.True(setup.Routed.GetActiveDisplayedEvents().Count > 0);

        LogTableState faulted = Reducers.ReduceOrderedViewDisplayFaulted(setup.Routed, Faults.Any);

        Assert.False(faulted.OrderedViewDisplayEnabled);
        Assert.Equal(PresentationState.Faulted, faulted.PresentationState);

        Assert.True(
            faulted.GetActiveDisplayedEvents().Count > 0,
            "A faulted display must keep the rows the user was already reading. The fault is reported by the "
            + "presentation state and its cause, not by taking the rows away.");

        Assert.True(
            faulted.IsRetainedViewServable(setup.Routed.ActiveEventLogId!.Value),
            "and the retained bridge must be what carries them once the old path is gone");
    }

    [Fact]
    public async Task GroupRestructure_MoveTabIntoGroup_ExpandsActiveScopeShadowReconcilesBothMembers()
    {
        var sample = new OrderedViewSample(seed: 59, logCount: 2);
        sample.SeedInterleaved(80);
        EventLogId member0 = sample.LogId(0), member1 = sample.LogId(1);
        var groupId = LogTabGroupId.Create();
        var header = new LogView(EventLogId.Create()) { GroupId = groupId };

        var rawStore = new RawEventStoreState
        {
            ByLog = ImmutableDictionary<EventLogId, EventColumnStore>.Empty
                .Add(member0, EventColumnStore.Build(sample.Events(0), 0, 0))
                .Add(member1, EventColumnStore.Build(sample.Events(1), 0, 0))
        };

        LogTableState GroupState(ImmutableHashSet<EventLogId> members) => CommittedFrom(new LogTableState
        {
            ActiveEventLogId = header.Id,
            EventTables = [header, new LogView(member0), new LogView(member1)],
            Groups = [new LogTabGroup(groupId, "Group", members)],
            IsDescending = true,
            RequestedIsDescending = true
        });

        await using var harness = new OrderedViewShadowHarness();

        LogTableState oneMember = GroupState([member0]);
        harness.SetState(oneMember, rawStore, new EventLogState());
        await harness.Effects.HandleNewGroupFromTab(harness.Dispatcher);
        IEventColumnReader member0Reader = EventColumnStore.Build(sample.Events(0), 0, 0).CreateReader(member0);
        OrderedViewSnapshot oneMemberSnapshot = await harness.Writer.DrainAsync();
        AssertShadowMatchesReference(
            oneMemberSnapshot, member0Reader, member0, sample.Events(0), oneMember.SortContext);
        int oneMemberCount = oneMemberSnapshot.Count;

        LogTableState twoMembers = GroupState([member0, member1]);
        harness.SetState(twoMembers, rawStore, new EventLogState());
        await harness.Effects.HandleMoveTabToGroup(harness.Dispatcher);

        OrderedViewSnapshot snapshot = await harness.Writer.DrainAsync();

        Assert.True(snapshot.Count > oneMemberCount);
        Assert.Equal(sample.Events(0).Count + sample.Events(1).Count, snapshot.Count);
        Assert.Null(harness.Issuer.LastFault);
    }

    [Fact]
    public async Task InitialLoad_ShadowMatchesLiveDefaultDescendingOrder()
    {
        var sample = new OrderedViewSample(seed: 7, logCount: 1);
        sample.SeedInterleaved(80);
        EventLogId logId = sample.LogId(0);
        var (logTable, rawStore, eventLog, reader) = SingleLog(logId, sample.Events(0), descending: true);

        await using var harness = new OrderedViewShadowHarness();
        harness.SetState(logTable, rawStore, eventLog);
        await harness.Effects.HandleLoadEvents(new LoadEventsAction(LogData(logId), sample.Events(0)), harness.Dispatcher);

        AssertShadowMatchesReference(
            await harness.Writer.DrainAsync(), reader, logId, sample.Events(0), logTable.SortContext);

        Assert.Null(harness.Issuer.LastFault);
    }

    [Fact]
    public async Task Invalidating_KeepsWhatWasOnScreenRatherThanLosingIt()
    {
        RoutedSetup setup = await BuildRoutedAsync(seed: 206, count: 60, TestContext.Current.CancellationToken);

        LogTableState invalidated = Reducers.ReduceViewRequestInvalidated(
            setup.Routed,
            new ViewRequestInvalidatedAction(setup.Routed.HighestInvalidationSequence + 1));

        Assert.Null(invalidated.ActiveOrderedView);
        Assert.Same(setup.Update, invalidated.RetainedFor(setup.Update));
    }

    [Fact]
    public async Task LiveTailAppend_IngestRawEvents_ShadowGrowsToMatchLive()
    {
        var sample = new OrderedViewSample(seed: 43, logCount: 1);
        sample.SeedInterleaved(60);
        EventLogId logId = sample.LogId(0);
        IReadOnlyList<ResolvedEvent> initial = [.. sample.Events(0)];

        await using var harness = new OrderedViewShadowHarness();
        var (before, beforeRaw, eventLog, initialReader) = SingleLog(logId, initial, descending: true);
        harness.SetState(before, beforeRaw, eventLog);
        await harness.Effects.HandleLoadEvents(new LoadEventsAction(LogData(logId), initial), harness.Dispatcher);
        AssertShadowMatchesReference(await harness.Writer.DrainAsync(), initialReader, logId, initial, before.SortContext);

        sample.Append(0, 30);
        IReadOnlyList<ResolvedEvent> grown = [.. sample.Events(0)];
        var (after, afterRaw, _, grownReader) = SingleLog(logId, grown, descending: true);
        harness.SetState(after, afterRaw, eventLog);
        var appended = grown.Skip(initial.Count).ToList();
        await harness.Effects.HandleIngestRawEvents(
            new IngestRawEventsAction(new Dictionary<EventLogId, IReadOnlyList<ResolvedEvent>> { [logId] = appended }, RawIngestMode.Prepend),
            harness.Dispatcher);

        AssertShadowMatchesReference(await harness.Writer.DrainAsync(), grownReader, logId, grown, after.SortContext);
        Assert.Null(harness.Issuer.LastFault);
    }

    [Fact]
    public async Task PartialThenFinalLoad_ShadowReconcilesMonotonicallyToMatchLive()
    {
        var sample = new OrderedViewSample(seed: 47, logCount: 1);
        sample.SeedInterleaved(100);
        EventLogId logId = sample.LogId(0);
        IReadOnlyList<ResolvedEvent> all = [.. sample.Events(0)];
        IReadOnlyList<ResolvedEvent> partial = [.. all.Take(40)];

        await using var harness = new OrderedViewShadowHarness();

        var (partialState, partialRaw, eventLog, partialReader) = SingleLog(logId, partial, descending: true);
        harness.SetState(partialState, partialRaw, eventLog);
        await harness.Effects.HandleLoadEventsPartial(new LoadEventsPartialAction(LogData(logId), partial), harness.Dispatcher);
        AssertShadowMatchesReference(
            await harness.Writer.DrainAsync(), partialReader, logId, partial, partialState.SortContext);

        var (finalState, finalRaw, _, finalReader) = SingleLog(logId, all, descending: true);
        harness.SetState(finalState, finalRaw, eventLog);
        await harness.Effects.HandleLoadEvents(new LoadEventsAction(LogData(logId), all), harness.Dispatcher);
        AssertShadowMatchesReference(await harness.Writer.DrainAsync(), finalReader, logId, all, finalState.SortContext);
        Assert.Null(harness.Issuer.LastFault);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(42)]
    [InlineData(101)]
    [InlineData(2026)]
    [InlineData(31337)]
    [InlineData(8675309)]
    [InlineData(int.MaxValue)]
    public async Task RandomizedOrderingWalk_ShadowMatchesTheReferenceAfterEveryStep(int seed)
    {
        const int MinSeedEvents = 30;
        const int MaxSeedEvents = 120;
        const int MinOperations = 90;
        const int MaxOperations = 160;

        var random = new Random(seed);
        var sample = new OrderedViewSample(seed, logCount: 1);
        sample.SeedInterleaved(random.Next(MinSeedEvents, MaxSeedEvents));
        EventLogId logId = sample.LogId(0);
        IReadOnlyList<ResolvedEvent> events = sample.Events(0);

        ColumnName?[] orderColumns =
        [
            null, ColumnName.RecordId, ColumnName.DateAndTime, ColumnName.Source, ColumnName.EventId,
            ColumnName.Level, ColumnName.TaskCategory, ColumnName.ComputerName
        ];
        ColumnName?[] groupColumns = [null, ColumnName.Source, ColumnName.EventId, ColumnName.Level];

        await using var harness = new OrderedViewShadowHarness();

        var (initial, rawStore, eventLog, reader) = SingleLog(logId, events, descending: random.Next(2) == 0);
        harness.SetState(initial, rawStore, eventLog);
        await harness.Effects.HandleLoadEvents(new LoadEventsAction(LogData(logId), events), harness.Dispatcher);
        AssertShadowMatchesReference(await harness.Writer.DrainAsync(), reader, logId, events, initial.SortContext);

        int operations = random.Next(MinOperations, MaxOperations);

        for (int step = 0; step < operations; step++)
        {
            ColumnName? orderBy = orderColumns[random.Next(orderColumns.Length)];
            ColumnName? groupBy = groupColumns[random.Next(groupColumns.Length)];
            bool descending = random.Next(2) == 0;
            bool groupDescending = random.Next(2) == 0;

            LogTableState state = CommittedFrom(initial with
            {
                OrderBy = orderBy,
                RequestedOrderBy = orderBy,
                IsDescending = descending,
                RequestedIsDescending = descending,
                GroupBy = groupBy,
                RequestedGroupBy = groupBy,
                IsGroupDescending = groupDescending,
                RequestedIsGroupDescending = groupDescending
            });

            harness.SetState(state, rawStore, eventLog);
            await harness.Effects.HandleSetOrderBy(new SetOrderByAction(orderBy), harness.Dispatcher);

            AssertShadowMatchesReference(await harness.Writer.DrainAsync(), reader, logId, events, state.SortContext);
        }
    }

    [Fact]
    public void ReduceApplyFilter_SemanticNoOp_RetainsTheExistingFilterInstance()
    {
        SavedFilter levelError = SavedFilter.TryCreate("Level == \"Error\"") ??
            throw new InvalidOperationException("Level filter failed to compile.");

        Filter original = new(null, [levelError]);
        var state = new LogTableState { AppliedFilter = original };

        LogTableState after = Reducers.ReduceApplyFilter(state, new ApplyFilterAction(new Filter(null, [levelError])));

        Assert.Same(original.Filters, after.AppliedFilter.Filters);
    }

    [Fact]
    public async Task ReduceOrderedViewDisplayFaulted_StopsTrustingTheEngineButKeepsTheRows()
    {
        RoutedSetup setup = await BuildRoutedAsync(seed: 82, count: 80, TestContext.Current.CancellationToken);

        IEventColumnView beforeFault = setup.Routed.EventsForLog(setup.LogId);

        LogTableState after = Reducers.ReduceOrderedViewDisplayFaulted(setup.Routed, Faults.Any);

        Assert.False(after.OrderedViewDisplayEnabled);
        Assert.Null(after.ActiveOrderedView);

        Assert.Equal(PresentationState.Faulted, after.PresentationState);
        Assert.Same(beforeFault, after.EventsForLog(setup.LogId));
    }

    [Fact]
    public async Task ReduceOrderedViewUpdated_ClearedUpdateClearsRoutedView()
    {
        RoutedSetup setup = await BuildRoutedAsync(seed: 80, count: 80, TestContext.Current.CancellationToken);

        var invalidation = new OrderedViewCleared(
            setup.Update.SnapshotVersion + 1, setup.Routed.ViewIdentity, setup.Update.Sequence);
        LogTableState after = Reducers.ReduceOrderedViewUpdated(setup.Routed, new OrderedViewUpdatedAction(invalidation));
        Assert.Null(after.ActiveOrderedView);
    }

    [Fact]
    public async Task ReduceOrderedViewUpdated_ClearedUpdateForAnotherIdentity_LeavesRoutedView()
    {
        RoutedSetup setup = await BuildRoutedAsync(seed: 82, count: 80, TestContext.Current.CancellationToken);

        var foreign = new OrderedViewCleared(
            setup.Update.SnapshotVersion + 1,
            ViewRequests.Identity(scope: [EventLogId.Create()]),
            setup.Update.Sequence);
        LogTableState after = Reducers.ReduceOrderedViewUpdated(setup.Routed, new OrderedViewUpdatedAction(foreign));
        Assert.Same(setup.Routed.ActiveOrderedView, after.ActiveOrderedView);
    }

    [Fact]
    public async Task ReduceOrderedViewUpdated_RejectsAMismatchedIdentityEvenWhenNewer()
    {
        RoutedSetup setup = await BuildRoutedAsync(seed: 79, count: 80, TestContext.Current.CancellationToken);

        OrderedViewReady wrongIdentity = setup.Update with
        {
            Identity = ViewRequests.Identity(scope: [EventLogId.Create()]),
            SnapshotVersion = setup.Update.SnapshotVersion + 1
        };
        LogTableState after = Reducers.ReduceOrderedViewUpdated(setup.Routed, new OrderedViewUpdatedAction(wrongIdentity));
        Assert.Same(setup.Routed.ActiveOrderedView, after.ActiveOrderedView);
    }

    [Fact]
    public async Task ReduceOrderedViewUpdated_RejectsAPublicationFromASupersededIssue()
    {
        RoutedSetup setup = await BuildRoutedAsync(seed: 83, count: 80, TestContext.Current.CancellationToken);

        OrderedViewReady later = setup.Update with { SnapshotVersion = setup.Update.SnapshotVersion + 5 };
        Assert.NotNull(Reducers.ReduceOrderedViewUpdated(setup.Routed, new OrderedViewUpdatedAction(later)).ActiveOrderedView);

        LogTableState invalidated = Reducers.ReduceViewRequestInvalidated(
            setup.Routed, new ViewRequestInvalidatedAction(setup.Update.Sequence + 1));

        Assert.True(later.Sequence < invalidated.HighestInvalidationSequence);
        Assert.Null(Reducers.ReduceOrderedViewUpdated(invalidated, new OrderedViewUpdatedAction(later)).ActiveOrderedView);
    }

    [Fact]
    public async Task ReduceOrderedViewUpdated_RejectsOlderSnapshotVersion()
    {
        RoutedSetup setup = await BuildRoutedAsync(seed: 78, count: 80, TestContext.Current.CancellationToken);

        OrderedViewReady older = setup.Update with { SnapshotVersion = setup.Update.SnapshotVersion - 1 };
        LogTableState after = Reducers.ReduceOrderedViewUpdated(setup.Routed, new OrderedViewUpdatedAction(older));
        Assert.Same(setup.Routed.ActiveOrderedView, after.ActiveOrderedView);
    }

    [Fact]
    public async Task ReduceOrderedViewUpdated_RejectsUpdateBuiltUnderASupersededIdentity()
    {
        RoutedSetup setup = await BuildRoutedAsync(seed: 77, count: 80, TestContext.Current.CancellationToken);

        OrderedViewReady straggler = setup.Update with
        {
            Identity = ViewRequests.Identity(scope: [setup.LogId], orderBy: ColumnName.Level),
            SnapshotVersion = setup.Update.SnapshotVersion + 1
        };
        LogTableState after = Reducers.ReduceOrderedViewUpdated(setup.Routed, new OrderedViewUpdatedAction(straggler));
        Assert.Same(setup.Routed.ActiveOrderedView, after.ActiveOrderedView);
    }

    [Fact]
    public async Task ReduceViewRequestInvalidated_InvalidatesRoutedViewAndAdvancesSequence()
    {
        RoutedSetup setup = await BuildRoutedAsync(seed: 81, count: 80, TestContext.Current.CancellationToken);

        long next = setup.Routed.HighestInvalidationSequence + 1;
        LogTableState after = Reducers.ReduceViewRequestInvalidated(setup.Routed, new ViewRequestInvalidatedAction(next));
        Assert.Null(after.ActiveOrderedView);
        Assert.Equal(next, after.HighestInvalidationSequence);
    }

    [Fact]
    public async Task RoutedEngineView_ReachesTheDisplayWithEngineOrigin()
    {
        RoutedSetup setup = await BuildRoutedAsync(seed: 96, count: 60, TestContext.Current.CancellationToken);

        Assert.True(setup.Routed.IsOrderedViewServing(setup.LogId));

        var state = Substitute.For<IState<LogTableState>>();

        state.Value.Returns(setup.Routed);

        using var source = new OrderedViewSource(state, Substitute.For<ITraceLogger>());

        Assert.Equal(PresentationState.Current, source.Current.State);
        Assert.Same(setup.Update.View, source.Current.View);
        Assert.Equal(setup.LogId, source.Current.ActiveTabId);
    }

    [Fact]
    public async Task RoutingSeam_AppliedFilterMismatch_StopsServingTheViewBuiltUnderTheOldFilter()
    {
        RoutedSetup setup = await BuildRoutedAsync(seed: 76, count: 80, TestContext.Current.CancellationToken);

        SavedFilter levelError = SavedFilter.TryCreate("Level == \"Error\"") ??
            throw new InvalidOperationException("Level filter failed to compile.");
        LogTableState filterChanged = setup.Routed with { AppliedFilter = new Filter(null, [levelError]) };
        Assert.Same(EmptyColumnView.Instance, filterChanged.EventsForLog(setup.LogId));
    }

    [Fact]
    public async Task RoutingSeam_EmptyActiveLog_ServesAFinalEmptyAnswerRatherThanWaiting()
    {
        EventLogId logId = EventLogId.Create();
        await using var harness = new OrderedViewShadowHarness();

        var (live, rawStore, eventLog, _) = SingleLog(logId, [], descending: true);
        harness.SetState(live, rawStore, eventLog);
        await harness.Effects.HandleLoadEvents(new LoadEventsAction(LogData(logId), []), harness.Dispatcher);

        OrderedViewUpdate update = await DrainToUpdateAsync(harness, TestContext.Current.CancellationToken);

        var ready = Assert.IsType<OrderedViewReady>(update);
        Assert.Equal(0, ready.View.Count);
        Assert.Equal(logId, ready.SingleLogId);

        LogTableState routed = Reducers.ReduceOrderedViewUpdated(live, new OrderedViewUpdatedAction(ready));

        Assert.Equal(0, routed.EventsForLog(logId).Count);
        Assert.Equal(PresentationState.Current, routed.PresentationState);
    }

    [Fact]
    public async Task RoutingSeam_GroupedActiveLog_ServesEngineViewWithGroupParity()
    {
        RoutedSetup setup = await BuildRoutedAsync(
            seed: 73, count: 80, TestContext.Current.CancellationToken, groupBy: ColumnName.Source);

        IEventColumnView routed = setup.Routed.EventsForLog(setup.LogId);

        Assert.True(setup.Routed.IsOrderedViewServing(setup.LogId));
        Assert.IsType<OrderedColumnView>(routed);
        AssertOrderMatchesReference(routed, [(setup.LogId, setup.Events)], setup.Routed.SortContext);
    }

    [Fact]
    public async Task RoutingSeam_NonActiveLogId_IsNeverGivenTheActiveTabsView()
    {
        RoutedSetup setup = await BuildRoutedAsync(seed: 75, count: 80, TestContext.Current.CancellationToken);

        Assert.Same(EmptyColumnView.Instance, setup.Routed.EventsForLog(EventLogId.Create()));
    }

    [Fact]
    public async Task RoutingSeam_OrderedViewDisplayDisabled_StopsServingTheEnginesView()
    {
        RoutedSetup setup = await BuildRoutedAsync(seed: 74, count: 80, TestContext.Current.CancellationToken);

        LogTableState disabled = setup.Routed with { OrderedViewDisplayEnabled = false };
        Assert.Same(EmptyColumnView.Instance, disabled.EventsForLog(setup.LogId));
    }

    [Fact]
    public async Task RoutingSeam_PendingGroupChange_StopsServingTheViewBuiltUnderTheOldGrouping()
    {
        RoutedSetup setup = await BuildRoutedAsync(seed: 73, count: 80, TestContext.Current.CancellationToken);

        IEventColumnView beforeRegroup = setup.Routed.EventsForLog(setup.LogId);

        LogTableState pending = Reducers.ReduceSetGroupBy(setup.Routed, new SetGroupByAction(ColumnName.Source));

        Assert.True(pending.HasPendingSortChange);
        Assert.False(pending.IsOrderedViewServing(setup.LogId));

        Assert.Equal(PresentationState.Updating, pending.PresentationState);
        Assert.Same(beforeRegroup, pending.EventsForLog(setup.LogId));
    }

    [Fact]
    public async Task RoutingSeam_PendingSortChange_DoesNotFlipTheRowsAheadOfTheHeader()
    {
        RoutedSetup setup = await BuildRoutedAsync(seed: 72, count: 80, TestContext.Current.CancellationToken);

        LogTableState pending = setup.Routed with { OrderBy = ColumnName.Source };
        Assert.True(pending.HasPendingSortChange);
        Assert.Same(EmptyColumnView.Instance, pending.EventsForLog(setup.LogId));
    }

    [Fact]
    public async Task RoutingSeam_SemanticallyEqualFilterRebuiltFromFreshCollections_StaysServed()
    {
        RoutedSetup setup = await BuildRoutedAsync(seed: 84, count: 80, TestContext.Current.CancellationToken);

        SavedFilter levelError = SavedFilter.TryCreate("Level == \"Error\"") ??
            throw new InvalidOperationException("Level filter failed to compile.");

        Filter adopted = new(null, [levelError]);
        Filter reapplied = new(null, [levelError]);

        Assert.NotEqual(adopted, reapplied);

        LogTableState state = setup.Routed with
        {
            AppliedFilter = reapplied, ActiveOrderedView = setup.Update with { Filter = adopted }
        };

        Assert.True(state.IsOrderedViewServing(setup.LogId));
    }

    [Fact]
    public async Task RoutingSeam_UngroupedSingleLog_FlipsReadToOrderedViewMatchingLive()
    {
        RoutedSetup setup = await BuildRoutedAsync(seed: 71, count: 120, TestContext.Current.CancellationToken);

        Assert.True(setup.BridgeDispatched);
        Assert.NotNull(setup.Routed.ActiveOrderedView);
        Assert.Same(setup.Update.View, setup.Routed.EventsForLog(setup.LogId));
        AssertOrderMatchesReference(
            setup.Routed.EventsForLog(setup.LogId), [(setup.LogId, setup.Events)], setup.Routed.SortContext);
    }

    [Fact]
    public async Task SetActiveTable_SwitchesActiveLog_ShadowRescopesToTheNewLog()
    {
        var sample = new OrderedViewSample(seed: 61, logCount: 2);
        sample.SeedInterleaved(80);
        EventLogId log0 = sample.LogId(0), log1 = sample.LogId(1);

        var rawStore = new RawEventStoreState
        {
            ByLog = ImmutableDictionary<EventLogId, EventColumnStore>.Empty
                .Add(log0, EventColumnStore.Build(sample.Events(0), 0, 0))
                .Add(log1, EventColumnStore.Build(sample.Events(1), 0, 0))
        };

        LogTableState StateActive(EventLogId active) => CommittedFrom(new LogTableState
        {
            ActiveEventLogId = active,
            EventTables = [new LogView(log0), new LogView(log1)],
            IsDescending = true,
            RequestedIsDescending = true
        });

        await using var harness = new OrderedViewShadowHarness();

        LogTableState active0 = StateActive(log0);
        harness.SetState(active0, rawStore, new EventLogState());
        await harness.Effects.HandleLoadEvents(new LoadEventsAction(LogData(log0), sample.Events(0)), harness.Dispatcher);
        IEventColumnReader reader0 = EventColumnStore.Build(sample.Events(0), 0, 0).CreateReader(log0);
        AssertShadowMatchesReference(
            await harness.Writer.DrainAsync(), reader0, log0, sample.Events(0), active0.SortContext);

        LogTableState active1 = StateActive(log1);
        harness.SetState(active1, rawStore, new EventLogState());
        await harness.Effects.HandleSetActiveTable(harness.Dispatcher);
        IEventColumnReader reader1 = EventColumnStore.Build(sample.Events(1), 0, 0).CreateReader(log1);
        AssertShadowMatchesReference(
            await harness.Writer.DrainAsync(), reader1, log1, sample.Events(1), active1.SortContext);
        Assert.Null(harness.Issuer.LastFault);
    }

    [Fact]
    public async Task SetHistogramVisible_SingleLogNoExplicitSort_ShadowReSortsToTimelineOrder()
    {
        EventLogId logId = EventLogId.Create();
        List<ResolvedEvent> events = Rows("Log0", (1, 50), (2, 10), (3, 60), (4, 20), (5, 40), (6, 30));

        await using var harness = new OrderedViewShadowHarness();

        var (hidden, rawStore, eventLog, reader) = SingleLog(logId, events, descending: true);
        harness.SetState(hidden, rawStore, eventLog);
        await harness.Effects.HandleLoadEvents(new LoadEventsAction(LogData(logId), events), harness.Dispatcher);
        AssertShadowMatchesReference(await harness.Writer.DrainAsync(), reader, logId, events, hidden.SortContext);

        LogTableState shown = CommittedFrom(new LogTableState
        {
            ActiveEventLogId = logId,
            EventTables = [new LogView(logId)],
            IsDescending = true,
            RequestedIsDescending = true,
            TimelineVisible = true
        });
        harness.SetState(shown, rawStore, eventLog);
        await harness.Effects.HandleSetHistogramVisible(new SetHistogramVisibleAction(true), harness.Dispatcher);

        AssertReferenceOrderChanges(events, hidden.SortContext, shown.SortContext);
        AssertShadowMatchesReference(await harness.Writer.DrainAsync(), reader, logId, events, shown.SortContext);
        Assert.Null(harness.Issuer.LastFault);
    }

    [Fact]
    public async Task TheSurvivorOrderCheckItself_FailsWhenTheOrderIsWrong()
    {
        var sample = new OrderedViewSample(seed: 61, logCount: 1);
        sample.SeedInterleaved(80);
        EventLogId logId = sample.LogId(0);

        await using var harness = new OrderedViewShadowHarness();
        var (state, rawStore, eventLog, reader) = SingleLog(logId, sample.Events(0), descending: true);
        harness.SetState(state, rawStore, eventLog);
        await harness.Effects.HandleLoadEvents(new LoadEventsAction(LogData(logId), sample.Events(0)), harness.Dispatcher);

        OrderedViewSnapshot snapshot = await harness.Writer.DrainAsync();

        SortContext opposite = new(
            state.SortContext.OrderBy,
            !state.SortContext.IsDescending,
            state.SortContext.GroupBy,
            state.SortContext.IsGroupDescending);

        Assert.ThrowsAny<Exception>(
            () => AssertShadowSurvivorsAreInReferenceOrder(snapshot, reader, sample.Events(0), opposite));
    }

    [Fact]
    public async Task ToggleSorting_ShadowFlipsToAscending()
    {
        var sample = new OrderedViewSample(seed: 11, logCount: 1);
        sample.SeedInterleaved(80);
        EventLogId logId = sample.LogId(0);

        await using var harness = new OrderedViewShadowHarness();

        var (descending, rawStore, eventLog, reader) = SingleLog(logId, sample.Events(0), descending: true);
        harness.SetState(descending, rawStore, eventLog);
        await harness.Effects.HandleLoadEvents(new LoadEventsAction(LogData(logId), sample.Events(0)), harness.Dispatcher);
        AssertShadowMatchesReference(
            await harness.Writer.DrainAsync(), reader, logId, sample.Events(0), descending.SortContext);

        var (ascending, _, _, _) = SingleLog(logId, sample.Events(0), descending: false);
        harness.SetState(ascending, rawStore, eventLog);
        await harness.Effects.HandleToggleSorting(harness.Dispatcher);

        AssertReferenceOrderChanges(sample.Events(0), descending.SortContext, ascending.SortContext);
        AssertShadowMatchesReference(
            await harness.Writer.DrainAsync(), reader, logId, sample.Events(0), ascending.SortContext);
    }

    [Fact]
    public async Task TopologyGrowsToTwoLogs_ShadowReKeysActiveSingleLogTabToDateAndTime()
    {
        EventLogId log0 = EventLogId.Create();
        EventLogId log1 = EventLogId.Create();

        List<ResolvedEvent> events0 = Rows("Log0", (1, 50), (2, 10), (3, 60), (4, 20), (5, 40), (6, 30));
        List<ResolvedEvent> events1 = Rows("Log1", (1, 15), (2, 25), (3, 35));

        await using var harness = new OrderedViewShadowHarness();

        var (oneLog, oneRaw, eventLog, reader) = SingleLog(log0, events0, descending: true);
        harness.SetState(oneLog, oneRaw, eventLog);
        await harness.Effects.HandleLoadEvents(new LoadEventsAction(LogData(log0), events0), harness.Dispatcher);
        AssertShadowMatchesReference(await harness.Writer.DrainAsync(), reader, log0, events0, oneLog.SortContext);

        var (twoLog, twoRaw) = TwoLogs(log0, events0, log1, events1, descending: true);
        harness.SetState(twoLog, twoRaw, eventLog);
        await harness.Effects.HandleAddTable(harness.Dispatcher);

        AssertReferenceOrderChanges(events0, oneLog.SortContext, twoLog.SortContext);

        AssertShadowMatchesReference(await harness.Writer.DrainAsync(), reader, log0, events0, twoLog.SortContext);
        Assert.Null(harness.Issuer.LastFault);
    }

    [Fact]
    public async Task XmlFilter_MidLoadApplyThenTerminalLoad_ForcesReissueRebuildingToTheGrownStore()
    {
        // Scenario X regression: an XML view issued over a mid-load PARTIAL store must, when match becomes ready,
        // force a fresh full re-issue and rebuild over the GROWN store. ViewIdentity excludes the store stamp, so a
        // same-identity Sync would be deduped by the issuer - without ForceReissue the terminal rows are dropped, and
        // without a content-aware rebuild the writer would restamp the stale partial reader.
        const string logName = "FileLog";
        EventLogId logId = EventLogId.Create();
        var filter = new Filter(null, [SavedFilter.TryCreate("Xml.Contains(\"x\")") ??
            throw new InvalidOperationException("XML filter failed to compile.")]);

        await using var harness = new OrderedViewShadowHarness();

        List<ResolvedEvent> partial = SourcedRows(logName, (1, 0, "A"), (2, 10, "B"), (3, 20, "C"));
        EventColumnStore partialStore = EventColumnStore.Build(partial, generation: 0, contentVersion: 0);
        StampMatch(harness, filter, logId, partialStore);
        harness.SetState(XmlFilteredLogTable(logId, filter), StoreOf(logId, partialStore), OpenFileLog(logId, logName, filter));

        await harness.Effects.HandleLoadEvents(
            new LoadEventsAction(new EventLogData(logName, LogPathType.File) { Id = logId }, partial), harness.Dispatcher);

        Assert.Equal(partial.Count, (await harness.Writer.DrainAsync()).Count);

        // Terminal load grows the store (same generation, higher content version); the view identity is unchanged.
        List<ResolvedEvent> all = SourcedRows(logName, (1, 0, "A"), (2, 10, "B"), (3, 20, "C"), (4, 30, "D"), (5, 40, "E"));
        EventColumnStore terminalStore = partialStore.Append([.. all.Skip(partial.Count)]);
        StampMatch(harness, filter, logId, terminalStore);
        harness.SetState(XmlFilteredLogTable(logId, filter), StoreOf(logId, terminalStore), OpenFileLog(logId, logName, filter));

        await harness.Effects.HandleXmlFilterMatchReady(harness.Dispatcher);

        Assert.Equal(all.Count, (await harness.Writer.DrainAsync()).Count);
        Assert.Null(harness.Issuer.LastFault);
    }

    private static void AssertReferenceOrderChanges(
        IReadOnlyList<ResolvedEvent> events,
        SortContext before,
        SortContext after)
    {
        int[] beforeOrder = AosReferenceOrdering.Order(
            events, before.OrderBy, before.IsDescending, before.GroupBy, before.IsGroupDescending);

        int[] afterOrder = AosReferenceOrdering.Order(
            events, after.OrderBy, after.IsDescending, after.GroupBy, after.IsGroupDescending);

        Assert.NotEqual(beforeOrder, afterOrder);
    }

    private static void AssertShadowMatchesReference(
        OrderedViewSnapshot snapshot,
        IEventColumnReader reader,
        EventLogId logId,
        IReadOnlyList<ResolvedEvent> events,
        SortContext context)
    {
        var shadow = new OrderedColumnView(snapshot, reader);

        int[] expected = AosReferenceOrdering.Order(
            events, context.OrderBy, context.IsDescending, context.GroupBy, context.IsGroupDescending);

        Assert.Equal(expected.Length, shadow.Count);

        for (int position = 0; position < expected.Length; position++)
        {
            EventLocator actual = shadow.LocatorAt(position);

            Assert.Equal(logId, actual.LogId);
            Assert.Equal(expected[position], actual.Index);
        }
    }

    private static void AssertShadowSurvivorsAreInReferenceOrder(
        OrderedViewSnapshot snapshot,
        IEventColumnReader reader,
        IReadOnlyList<ResolvedEvent> events,
        SortContext context)
    {
        var shadow = new OrderedColumnView(snapshot, reader);

        var survivors = new List<ResolvedEvent>(shadow.Count);

        for (int position = 0; position < shadow.Count; position++)
        {
            survivors.Add(events[shadow.LocatorAt(position).Index]);
        }

        int[] resorted = AosReferenceOrdering.Order(
            survivors, context.OrderBy, context.IsDescending, context.GroupBy, context.IsGroupDescending);

        for (int position = 0; position < resorted.Length; position++)
        {
            Assert.Equal(position, resorted[position]);
        }
    }

    private static async Task<RoutedSetup> BuildRoutedAsync(
        int seed,
        int count,
        CancellationToken cancellationToken,
        ColumnName? groupBy = null)
    {
        var sample = new OrderedViewSample(seed, logCount: 1);
        sample.SeedInterleaved(count);
        EventLogId logId = sample.LogId(0);

        await using var harness = new OrderedViewShadowHarness();
        bool bridgeDispatched = false;
        harness.Dispatcher.When(dispatcher => dispatcher.Dispatch(Arg.Any<object>())).Do(callInfo =>
        {
            if (callInfo.Arg<object>() is OrderedViewUpdatedAction) { Volatile.Write(ref bridgeDispatched, true); }
        });

        var (initial, rawStore, eventLog, _) = SingleLog(logId, sample.Events(0), descending: true);

        LogTableState live = initial with
        {
            AppliedFilter = eventLog.AppliedFilter,
            GroupBy = groupBy,
            RequestedGroupBy = groupBy
        };

        if (groupBy is not null) { live = CommittedFrom(live); }

        harness.SetState(live, rawStore, eventLog);
        await harness.Effects.HandleLoadEvents(new LoadEventsAction(LogData(logId), sample.Events(0)), harness.Dispatcher);

        OrderedViewReady update = await DrainToReadyAsync(harness, cancellationToken);

        LogTableState routed = Reducers.ReduceOrderedViewUpdated(live, new OrderedViewUpdatedAction(update));

        return new RoutedSetup(routed, update, logId, Volatile.Read(ref bridgeDispatched), [.. sample.Events(0)]);
    }

    private static LogTableState CommittedFrom(LogTableState state) =>
        state with
        {
            CommittedEffectiveOrderBy = ResolvedEventOrdering.ResolveDefaultOrderBy(
                state.OrderBy, state.GroupBy, state.DisplayedLogCount, state.TimelineVisible)
        };

    private static async Task<OrderedViewReady> DrainToReadyAsync(OrderedViewShadowHarness harness, CancellationToken cancellationToken)
    {
        OrderedViewUpdate update = await DrainToUpdateAsync(harness, cancellationToken);

        return update as OrderedViewReady ??
            throw new InvalidOperationException("The writer did not raise a single-log view update.");
    }

    private static async Task<OrderedViewUpdate> DrainToUpdateAsync(OrderedViewShadowHarness harness, CancellationToken cancellationToken) =>
        await harness.DrainToUpdateAsync(cancellationToken);

    private static bool IsFragmentedBy(IEventColumnView view, ColumnName groupBy)
    {
        List<(string Key, int Count)> runs = GroupRuns(view, groupBy);

        return runs.Count != runs.Select(run => run.Key).Distinct(StringComparer.Ordinal).Count();
    }

    private static EventLocator[] Locators(IEventColumnView view)
    {
        var locators = new EventLocator[view.Count];

        for (int displayIndex = 0; displayIndex < view.Count; displayIndex++)
        {
            locators[displayIndex] = view.LocatorAt(displayIndex);
        }

        return locators;
    }

    private static EventLogData LogData(EventLogId logId) => new("Log0", LogPathType.Channel) { Id = logId };

    private static EventLogState OpenFileLog(EventLogId logId, string logName, Filter filter) =>
        new()
        {
            OpenLogs = ImmutableDictionary<string, OpenLogInfo>.Empty.Add(logName, new OpenLogInfo(logId, LogPathType.File)),
            AppliedFilter = filter
        };

    private static LogTableState PendingSortState(RoutedSetup setup)
    {
        LogTableState pending = setup.Routed with { GroupBy = null, IsGroupDescending = false };

        return CommittedFrom(pending);
    }

    private static OrderedViewUpdatedAction Republish(RoutedSetup setup) =>
        new(setup.Update with { SnapshotVersion = setup.Update.SnapshotVersion + 1 });

    private static LogTableState Retaining(RoutedSetup setup) =>
        setup.Routed with
        {
            ActiveOrderedView = null,
            RetainedOrderedViews = RetainedMap(setup.Update)
        };

    private static List<ResolvedEvent> Rows(string owningLog, params (long RecordId, int TimeMs)[] rows)
    {
        var baseTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var events = new List<ResolvedEvent>(rows.Length);

        foreach ((long recordId, int timeMs) in rows)
        {
            events.Add(new ResolvedEvent(owningLog, LogPathType.Channel)
            {
                RecordId = recordId,
                TimeCreated = baseTime.AddMilliseconds(timeMs),
                Id = 1000,
                Level = "Information",
                Source = "Provider.A",
                LogName = owningLog
            });
        }

        return events;
    }

    private static (LogTableState LogTable, RawEventStoreState RawStore, EventLogState EventLog, IEventColumnReader Reader) SingleLog(
        EventLogId logId, IReadOnlyList<ResolvedEvent> events, bool descending)
    {
        EventColumnStore store = EventColumnStore.Build(events, 0, 0);

        var rawStore = new RawEventStoreState
        {
            ByLog = ImmutableDictionary<EventLogId, EventColumnStore>.Empty.Add(logId, store)
        };

        LogTableState logTable = CommittedFrom(new LogTableState
        {
            ActiveEventLogId = logId,
            EventTables = [new LogView(logId)],
            IsDescending = descending,
            RequestedIsDescending = descending
        });

        return (logTable, rawStore, new EventLogState(), store.CreateReader(logId));
    }

    private static (LogTableState LogTable, RawEventStoreState RawStore, EventLogState EventLog, IEventColumnReader Reader) SingleLogFiltered(
        EventLogId logId, IReadOnlyList<ResolvedEvent> events, Filter filter, bool descending)
    {
        EventColumnStore store = EventColumnStore.Build(events, 0, 0);
        IEventColumnReader reader = store.CreateReader(logId);

        var rawStore = new RawEventStoreState
        {
            ByLog = ImmutableDictionary<EventLogId, EventColumnStore>.Empty.Add(logId, store)
        };

        LogTableState logTable = new()
        {
            ActiveEventLogId = logId,
            EventTables = [new LogView(logId)],
            IsDescending = descending,
            RequestedIsDescending = descending,

            AppliedFilter = filter
        };

        return (logTable, rawStore, new EventLogState { AppliedFilter = filter }, reader);
    }

    private static List<ResolvedEvent> SourcedRows(
        string owningLog,
        params (long RecordId, int TimeMs, string Source)[] rows)
    {
        var baseTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var events = new List<ResolvedEvent>(rows.Length);

        foreach ((long recordId, int timeMs, string source) in rows)
        {
            events.Add(new ResolvedEvent(owningLog, LogPathType.Channel)
            {
                RecordId = recordId,
                TimeCreated = baseTime.AddMilliseconds(timeMs),
                Id = 1000,
                Level = "Information",
                Source = source,
                LogName = owningLog
            });
        }

        return events;
    }

    private static void StampMatch(OrderedViewShadowHarness harness, Filter filter, EventLogId logId, EventColumnStore store)
    {
        bool[] matches = new bool[store.Count];
        Array.Fill(matches, true);

        harness.MatchCache.Set(
            filter,
            new Dictionary<EventLogId, XmlFilterMatch>
            {
                [logId] = new XmlFilterMatch(logId, store.Generation, store.ContentVersion, store.Count, matches)
            },
            harness.MatchCache.NextSequence());
    }

    private static RawEventStoreState StoreOf(EventLogId logId, EventColumnStore store) =>
        new() { ByLog = ImmutableDictionary<EventLogId, EventColumnStore>.Empty.Add(logId, store) };

    private static (LogTableState LogTable, RawEventStoreState RawStore) TwoLogs(
        EventLogId log0, IReadOnlyList<ResolvedEvent> events0,
        EventLogId log1, IReadOnlyList<ResolvedEvent> events1, bool descending)
    {
        var rawStore = new RawEventStoreState
        {
            ByLog = ImmutableDictionary<EventLogId, EventColumnStore>.Empty
                .Add(log0, EventColumnStore.Build(events0, 0, 0))
                .Add(log1, EventColumnStore.Build(events1, 0, 0))
        };

        LogTableState logTable = CommittedFrom(new LogTableState
        {
            ActiveEventLogId = log0,
            EventTables = [new LogView(log0), new LogView(log1)],
            IsDescending = descending,
            RequestedIsDescending = descending
        });

        return (logTable, rawStore);
    }

    private static LogTableState XmlFilteredLogTable(EventLogId logId, Filter filter) =>
        new()
        {
            ActiveEventLogId = logId,
            EventTables = [new LogView(logId)],
            IsDescending = true,
            RequestedIsDescending = true,
            AppliedFilter = filter
        };

    private sealed record RoutedSetup(
        LogTableState Routed,
        OrderedViewReady Update,
        EventLogId LogId,
        bool BridgeDispatched,
        ResolvedEvent[] Events);
}
