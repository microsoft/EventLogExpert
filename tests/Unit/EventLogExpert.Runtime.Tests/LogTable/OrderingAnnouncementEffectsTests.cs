// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.Announcement;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.LogTable.OrderedView;
using Fluxor;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using AnnouncementPayload = EventLogExpert.Runtime.Announcement.Announcement;

namespace EventLogExpert.Runtime.Tests.LogTable;

public sealed class OrderingAnnouncementEffectsTests
{
    private readonly IAnnouncementService _announcer = Substitute.For<IAnnouncementService>();
    private readonly IDispatcher _dispatcher = Substitute.For<IDispatcher>();
    private readonly IState<LogTableState> _state = Substitute.For<IState<LogTableState>>();

    public OrderingAnnouncementEffectsTests() => _state.Value.Returns(State());

    [Fact]
    public async Task ClearGroup_AnnouncesGroupCleared()
    {
        _state.Value.Returns(State(groupBy: ColumnName.Level, isGroupDescending: false));
        var sut = CreateSut();
        _state.Value.Returns(State());

        await sut.HandleSetGroupBy(_dispatcher);

        _announcer.Received(1).Announce(Arg.Any<AnnouncementPayload.TableGroupCleared>());
    }

    [Fact]
    public async Task ClearSort_AnnouncesSortCleared()
    {
        _state.Value.Returns(State(orderBy: ColumnName.Source, isDescending: false));
        var sut = CreateSut();
        _state.Value.Returns(State());

        await sut.HandleSetOrderBy(_dispatcher);

        _announcer.Received(1).Announce(Arg.Any<AnnouncementPayload.TableSortCleared>());
    }

    [Fact]
    public async Task ClearSort_WhenAlreadyUnsorted_DoesNotAnnounce()
    {
        var sut = CreateSut();

        await sut.HandleSetOrderBy(_dispatcher);

        _announcer.DidNotReceive().Announce(Arg.Any<AnnouncementPayload>());
    }

    [Fact]
    public async Task ClearingAGroupWhileTheDefaultDirectionAlsoFlips_AdvancesTheBaselineSoTheNextToggleStillSpeaks()
    {
        // A group clear with a direction flip is not deferred, so no adopt flush arrives to release a held reversal.
        // The baseline must advance, or it desyncs and the next toggle is silently lost.
        _state.Value.Returns(AdoptedState(groupBy: ColumnName.Level, isDescending: true));
        var sut = CreateSut();

        _state.Value.Returns(AdoptedState(isDescending: false));
        await sut.HandleSetGroupBy(_dispatcher);
        _announcer.Received(1).Announce(Arg.Any<AnnouncementPayload.TableGroupCleared>());

        _announcer.ClearReceivedCalls();
        _state.Value.Returns(AdoptedState(isDescending: true));
        await sut.HandleToggleSorting(_dispatcher);

        _announcer.Received(1).Announce(Arg.Is<AnnouncementPayload.TableDefaultOrderReversed>(
            payload => payload.IsDescending && !payload.WithinGroups));
    }

    [Fact]
    public async Task Effect_IsDiscoveredByFluxor_AnnouncesThroughARealStore()
    {
        // Wires a minimal real store (the LogTable feature + its reducers + this effect) to prove Fluxor discovers the
        // [EffectMethod]s and runs them AFTER the reducer, so a dispatched ordering action actually announces - and that
        // toggling direction while unsorted announces the default-order reversal end to end.
        var announcer = Substitute.For<IAnnouncementService>();

        using ServiceProvider provider = new ServiceCollection()
            .AddSingleton(announcer)
            .AddFluxor(options => options.ScanTypes(typeof(LogTableState), typeof(Reducers), typeof(OrderingAnnouncementEffects)))
            .BuildServiceProvider();

        var store = provider.GetRequiredService<IStore>();
        await store.InitializeAsync();
        var dispatcher = provider.GetRequiredService<IDispatcher>();

        dispatcher.Dispatch(new SetOrderByAction(ColumnName.Source));
        announcer.Received(1).Announce(Arg.Is<AnnouncementPayload.TableSorted>(payload => payload.Column == ColumnName.Source));

        announcer.ClearReceivedCalls();
        dispatcher.Dispatch(new SetOrderByAction(null));
        announcer.Received(1).Announce(Arg.Any<AnnouncementPayload.TableSortCleared>());

        announcer.ClearReceivedCalls();
        dispatcher.Dispatch(new ToggleSortingAction());
        announcer.Received(1).Announce(Arg.Any<AnnouncementPayload.TableDefaultOrderReversed>());
    }

    [Fact]
    public async Task LoadColumnsCompleted_WhenGroupedColumnHidden_StaysSilentAndKeepsBaselineSynced()
    {
        // Hiding the grouped column no longer clears grouping, so LoadColumnsCompleted announces nothing; the baseline
        // stays synced so a later sort still announces exactly once.
        _state.Value.Returns(State(groupBy: ColumnName.Source, isGroupDescending: false));
        var sut = CreateSut();

        await sut.HandleLoadColumnsCompleted(_dispatcher);
        _announcer.DidNotReceive().Announce(Arg.Any<AnnouncementPayload>());

        _state.Value.Returns(State(groupBy: ColumnName.Source, isGroupDescending: false, orderBy: ColumnName.DateAndTime, isDescending: true));
        await sut.HandleSetOrderBy(_dispatcher);

        _announcer.Received(1).Announce(Arg.Is<AnnouncementPayload.TableSorted>(payload => payload.Column == ColumnName.DateAndTime));
        _announcer.DidNotReceive().Announce(Arg.Any<AnnouncementPayload.TableGroupCleared>());
    }

    [Fact]
    public async Task LoadColumnsCompleted_WithoutGroupChange_DoesNotAnnounce()
    {
        _state.Value.Returns(State(groupBy: ColumnName.Source, isGroupDescending: false));
        var sut = CreateSut();

        await sut.HandleLoadColumnsCompleted(_dispatcher);

        _announcer.DidNotReceive().Announce(Arg.Any<AnnouncementPayload>());
    }

    [Fact]
    public async Task OrderedViewDisplayFaulted_AnnouncesReorderUnavailableWithCommittedOrder()
    {
        // A reproject faults and the grid reverts to the committed ordering; the correction narrates the committed
        // fields the user now sees (here an explicit Source sort), not the requested ones.
        _state.Value.Returns(FaultedCommittedState(committedOrderBy: ColumnName.Source, committedIsDescending: true));
        var sut = CreateSut();

        await sut.HandleOrderedViewDisplayFaulted(new OrderedViewDisplayFaultedAction(new InvalidOperationException("boom")), _dispatcher);

        _announcer.Received(1).Announce(Arg.Is<AnnouncementPayload.TableReorderUnavailable>(
            payload => payload.CommittedOrderBy == ColumnName.Source && payload.CommittedIsDescending));
    }

    [Fact]
    public async Task OrderedViewDisplayFaulted_NonOrderingFault_StaysSilent()
    {
        // The action also fires for non-ordering faults (ingest, close, tab switch) where requested == committed, so
        // there is no reorder to revert; asserting "sort unavailable" the user never requested would be wrong.
        _state.Value.Returns(FaultedCommittedState(committedOrderBy: ColumnName.Source, requestedOrderBy: ColumnName.Source));
        var sut = CreateSut();

        await sut.HandleOrderedViewDisplayFaulted(new OrderedViewDisplayFaultedAction(new InvalidOperationException("boom")), _dispatcher);

        _announcer.DidNotReceive().Announce(Arg.Any<AnnouncementPayload>());
    }

    [Fact]
    public async Task OrderedViewDisplayFaulted_StaleFaultForASupersededIdentity_StaysSilent()
    {
        // A late fault from a superseded request (identity no longer current, which the reducer also rejects) must not
        // narrate a revert of the recovery now in flight, even though the view is still faulted from the earlier failure.
        var superseded = FaultedCommittedState(requestedOrderBy: ColumnName.Level);
        _state.Value.Returns(FaultedCommittedState(requestedOrderBy: ColumnName.Source));
        var sut = CreateSut();

        await sut.HandleOrderedViewDisplayFaulted(
            new OrderedViewDisplayFaultedAction(new InvalidOperationException("boom"), superseded.ViewIdentity), _dispatcher);

        _announcer.DidNotReceive().Announce(Arg.Any<AnnouncementPayload>());
    }

    [Fact]
    public async Task OrderedViewDisplayFaulted_ThenRecoveryAdoptsRequested_ReannouncesTheAdoptedSort()
    {
        // The fault re-baselines to the committed (shown) order, so when recovery adopts the requested sort the
        // baseline diff narrates it - the correction is not self-suppressed.
        _state.Value.Returns(State(orderBy: ColumnName.Source));
        var sut = CreateSut();

        _state.Value.Returns(FaultedCommittedState(requestedOrderBy: ColumnName.Source));
        await sut.HandleOrderedViewDisplayFaulted(new OrderedViewDisplayFaultedAction(new InvalidOperationException("boom")), _dispatcher);
        _announcer.Received(1).Announce(Arg.Any<AnnouncementPayload.TableReorderUnavailable>());

        _announcer.ClearReceivedCalls();
        _state.Value.Returns(AdoptedState(orderBy: ColumnName.Source));
        await sut.HandleOrderedViewUpdated(_dispatcher);

        _announcer.Received(1).Announce(Arg.Is<AnnouncementPayload.TableSorted>(payload => payload.Column == ColumnName.Source));
    }

    [Fact]
    public async Task OrderedViewDisplayFaulted_WhenNotActuallyFaulted_StaysSilent()
    {
        // A stale/mismatched fault that left the presentation non-faulted must not assert a revert no surface reflects.
        _state.Value.Returns(State(orderBy: ColumnName.Source));
        var sut = CreateSut();

        await sut.HandleOrderedViewDisplayFaulted(new OrderedViewDisplayFaultedAction(new InvalidOperationException("boom")), _dispatcher);

        _announcer.DidNotReceive().Announce(Arg.Any<AnnouncementPayload>());
    }

    [Fact]
    public async Task OrderedViewUpdated_WhenRequestedOrderingNotYetCommitted_DoesNotFlush()
    {
        // A fault-deferred request must not be announced by an OrderedViewUpdatedAction that did not actually adopt it
        // (a rejected/stale update, an OrderedViewCleared invalidation, or a fault that cleared because the view went
        // away). committed != requested proves the reorder has not landed, so the deferral stays pending.
        _state.Value.Returns(State(orderBy: ColumnName.Source));
        var sut = CreateSut();

        _state.Value.Returns(FaultedState(requestedGroupBy: ColumnName.Level, requestedOrderBy: ColumnName.Source));
        await sut.HandleSetGroupBy(_dispatcher);
        _announcer.DidNotReceive().Announce(Arg.Any<AnnouncementPayload>());

        // An OrderedViewUpdatedAction fires but the requested grouping is not committed (committed GroupBy still null).
        _state.Value.Returns(new LogTableState
        {
            OrderBy = ColumnName.Source,
            RequestedOrderBy = ColumnName.Source,
            GroupBy = null,
            RequestedGroupBy = ColumnName.Level
        });
        await sut.HandleOrderedViewUpdated(_dispatcher);

        _announcer.DidNotReceive().Announce(Arg.Any<AnnouncementPayload>());
    }

    [Fact]
    public async Task OrderedViewUpdated_WithNoDeferredChange_StaysSilent()
    {
        // The adopt signal fires on every view update (new events, tab switches); with committed == requested the gate
        // opens, but the baseline already matches the requested ordering, so it must not re-announce the current ordering.
        _state.Value.Returns(AdoptedState(orderBy: ColumnName.Source, isDescending: false));
        var sut = CreateSut();

        await sut.HandleOrderedViewUpdated(_dispatcher);

        _announcer.DidNotReceive().Announce(Arg.Any<AnnouncementPayload>());
    }

    [Fact]
    public async Task RecoveryAfterFaultAccumulatedBothAxes_AnnouncesOneCombinedSortAndGroup()
    {
        // A persistent fault accumulates a sort change AND a group change (each deferred and silent). The single adopt
        // flush sees both axes differ from the announced baseline; it must narrate them in ONE combined announcement so
        // the single-slot live region does not drop the sort (two messages would let group overwrite sort unread).
        _state.Value.Returns(State());
        var sut = CreateSut();

        // Fault; user requests sort Source then group Level - both silent, baseline stays at {no sort, no group}.
        _state.Value.Returns(FaultedState(requestedGroupBy: null, requestedOrderBy: ColumnName.Source));
        await sut.HandleSetOrderBy(_dispatcher);
        _state.Value.Returns(FaultedState(requestedGroupBy: ColumnName.Level, requestedOrderBy: ColumnName.Source));
        await sut.HandleSetGroupBy(_dispatcher);
        _announcer.DidNotReceive().Announce(Arg.Any<AnnouncementPayload>());

        // Recovery adopts both (committed == requested); one combined flush narrates both axes, not two messages.
        _state.Value.Returns(AdoptedState(orderBy: ColumnName.Source, groupBy: ColumnName.Level, isGroupDescending: false));
        await sut.HandleOrderedViewUpdated(_dispatcher);

        _announcer.Received(1).Announce(Arg.Is<AnnouncementPayload.TableSortAndGroupChanged>(
            payload => payload.SortColumn == ColumnName.Source && payload.SortDescending
                && payload.GroupColumn == ColumnName.Level && !payload.GroupDescending));
        _announcer.DidNotReceive().Announce(Arg.Any<AnnouncementPayload.TableSorted>());
        _announcer.DidNotReceive().Announce(Arg.Any<AnnouncementPayload.TableGrouped>());
    }

    [Fact]
    public async Task RequestWhileFaulted_AfterAnnouncedOrdering_StaysSilentOnBothAxes()
    {
        // A prior sort+group was announced optimistically; the reproject then faulted. A further request while faulted
        // must stay silent - the served view still shows committed, so narrating the requested change would assert a
        // reorder no visible surface reflects. The change is deferred to the adopt flush, not announced here.
        _state.Value.Returns(State(orderBy: ColumnName.Source, groupBy: ColumnName.Level));
        var sut = CreateSut();
        _state.Value.Returns(FaultedState(requestedGroupBy: ColumnName.DateAndTime));

        await sut.HandleSetGroupBy(_dispatcher);

        _announcer.DidNotReceive().Announce(Arg.Any<AnnouncementPayload>());
    }

    // A faulted presentation requires an active table plus OrderedViewDisplayEnabled=false; committed grouping stays
    // null so the mask the effect mirrors reports "no grouping" while the request carries the pending column.
    [Fact]
    public async Task RequestWhileFaulted_ThenRecoveryAdopts_AnnouncesTheDeferredChange()
    {
        // A reorder requested while faulted stays silent (the served view still shows committed), but the baseline is
        // left untouched so the change is deferred rather than dropped. When OrderedViewUpdatedAction adopts the
        // recovered ordering, the pending diff narrates exactly the axis that changed - here the deferred grouping, not
        // the sort that was already announced before the fault.
        _state.Value.Returns(State(orderBy: ColumnName.Source));
        var sut = CreateSut();

        // Fault; user requests group Level - silent, baseline stays at the announced {Source sort, no group}.
        _state.Value.Returns(FaultedState(requestedGroupBy: ColumnName.Level, requestedOrderBy: ColumnName.Source));
        await sut.HandleSetGroupBy(_dispatcher);
        _announcer.DidNotReceive().Announce(Arg.Any<AnnouncementPayload>());

        // Recovery adopts the requested Source sort + Level group (committed now equals requested); the adopt flush
        // announces only the grouping.
        _state.Value.Returns(AdoptedState(orderBy: ColumnName.Source, groupBy: ColumnName.Level));
        await sut.HandleOrderedViewUpdated(_dispatcher);

        _announcer.Received(1).Announce(Arg.Is<AnnouncementPayload.TableGrouped>(
            payload => payload.Column == ColumnName.Level && !payload.IsGroupDescending));
        _announcer.DidNotReceive().Announce(Arg.Any<AnnouncementPayload.TableSorted>());
        _announcer.DidNotReceive().Announce(Arg.Any<AnnouncementPayload.TableSortCleared>());
    }

    [Fact]
    public async Task SetGroupBy_DefersUntilServedView_ThenAnnouncesGroupedWithServedCount()
    {
        // A group-by announcement carries the served group count, which only exists once the view reflects the
        // grouping. The request stays silent (HasPendingSortChange); the adopt flush narrates it with the real count.
        var sut = CreateSut();

        _state.Value.Returns(State(groupBy: ColumnName.Level, isGroupDescending: false));
        await sut.HandleSetGroupBy(_dispatcher);
        _announcer.DidNotReceive().Announce(Arg.Any<AnnouncementPayload>());

        var served = GroupedViewState(ColumnName.Level, isGroupDescending: false, ["A", "A", "B", "C", "C"]);
        _state.Value.Returns(served);
        await sut.HandleOrderedViewUpdated(_dispatcher);

        _announcer.Received(1).Announce(Arg.Is<AnnouncementPayload.TableGrouped>(
            payload => payload.Column == ColumnName.Level && !payload.IsGroupDescending && payload.GroupCount == 3));
    }

    [Fact]
    public async Task SetGroupBy_WhileFaulted_StaysSilent_BecauseServedViewRevertsToCommitted()
    {
        // While faulted the served view masks Requested* back to committed (OrderedViewSource), so the effect must
        // mirror that mask: announcing "grouped by Level" when the chip, header, and menus still show the committed
        // (ungrouped) order would assert a reorder no visible surface reflects.
        var sut = CreateSut();
        _state.Value.Returns(FaultedState(requestedGroupBy: ColumnName.Level));

        await sut.HandleSetGroupBy(_dispatcher);

        _announcer.DidNotReceive().Announce(Arg.Any<AnnouncementPayload>());
    }

    [Fact]
    public async Task SetOrderBy_FromUnsorted_AnnouncesSorted()
    {
        var sut = CreateSut();
        _state.Value.Returns(State(orderBy: ColumnName.Source, isDescending: false));

        await sut.HandleSetOrderBy(_dispatcher);

        _announcer.Received(1).Announce(Arg.Is<AnnouncementPayload.TableSorted>(
            payload => payload.Column == ColumnName.Source && !payload.IsDescending));
    }

    [Fact]
    public async Task SetOrderBy_SameColumnIdempotent_DoesNotAnnounce()
    {
        _state.Value.Returns(State(orderBy: ColumnName.Source, isDescending: false));
        var sut = CreateSut();

        await sut.HandleSetOrderBy(_dispatcher);

        _announcer.DidNotReceive().Announce(Arg.Any<AnnouncementPayload>());
    }

    [Fact]
    public async Task ToggleGroupSorting_FlipsDirection_AnnouncesNewGroupDirectionOnAdoption()
    {
        // The toggle flips the requested direction while the committed view lags, so the announcement defers to the
        // adopt flush, which narrates the new direction with the served group count.
        var baseline = GroupedViewState(ColumnName.Level, isGroupDescending: false, ["A", "B"]);
        _state.Value.Returns(baseline);
        var sut = CreateSut();

        _state.Value.Returns(PendingGroupState(ColumnName.Level, committedIsGroupDescending: false, requestedIsGroupDescending: true));
        await sut.HandleToggleGroupSorting(_dispatcher);
        _announcer.DidNotReceive().Announce(Arg.Any<AnnouncementPayload>());

        var served = GroupedViewState(ColumnName.Level, isGroupDescending: true, ["A", "B"]);
        _state.Value.Returns(served);
        await sut.HandleOrderedViewUpdated(_dispatcher);

        _announcer.Received(1).Announce(Arg.Is<AnnouncementPayload.TableGrouped>(
            payload => payload.Column == ColumnName.Level && payload.IsGroupDescending && payload.GroupCount == 2));
    }

    [Fact]
    public async Task ToggleGroupSorting_WhenAlsoSorted_AnnouncesGroupNotSortOnAdoption()
    {
        var baseline = GroupedViewState(ColumnName.Level, isGroupDescending: false, ["A", "B"], ColumnName.Source, isDescending: true);
        _state.Value.Returns(baseline);
        var sut = CreateSut();

        var served = GroupedViewState(ColumnName.Level, isGroupDescending: true, ["A", "B"], ColumnName.Source, isDescending: true);
        _state.Value.Returns(served);
        await sut.HandleOrderedViewUpdated(_dispatcher);

        _announcer.Received(1).Announce(Arg.Is<AnnouncementPayload.TableGrouped>(
            payload => payload.Column == ColumnName.Level && payload.IsGroupDescending));
        _announcer.DidNotReceive().Announce(Arg.Any<AnnouncementPayload.TableSorted>());
        _announcer.DidNotReceive().Announce(Arg.Any<AnnouncementPayload.TableSortCleared>());
    }

    [Fact]
    public async Task ToggleGroupSorting_WhenUngrouped_DoesNotAnnounce()
    {
        var sut = CreateSut();

        await sut.HandleToggleGroupSorting(_dispatcher);

        _announcer.DidNotReceive().Announce(Arg.Any<AnnouncementPayload>());
    }

    [Fact]
    public async Task ToggleSorting_FlipsDirection_AnnouncesNewDirection()
    {
        _state.Value.Returns(State(orderBy: ColumnName.Source, isDescending: false));
        var sut = CreateSut();
        _state.Value.Returns(State(orderBy: ColumnName.Source, isDescending: true));

        await sut.HandleToggleSorting(_dispatcher);

        _announcer.Received(1).Announce(Arg.Is<AnnouncementPayload.TableSorted>(
            payload => payload.Column == ColumnName.Source && payload.IsDescending));
    }

    [Fact]
    public async Task ToggleSorting_WhenGroupedButUnsorted_AnnouncesWithinGroupReversal()
    {
        // Toggling the sort direction while grouped-but-unsorted reverses the default time/record order WITHIN each
        // group, so it announces a default-order reversal flagged WithinGroups for the dedicated within-group wording.
        // It must NOT emit a group announcement (the single live-region slot would clobber the reversal), the adopt
        // flush must then stay silent (no late grouping overwrites it), and a second toggle must re-announce the flip.
        _state.Value.Returns(State(groupBy: ColumnName.Level, isDescending: false));
        var sut = CreateSut();
        _state.Value.Returns(State(groupBy: ColumnName.Level, isDescending: true));

        await sut.HandleToggleSorting(_dispatcher);

        _announcer.Received(1).Announce(Arg.Is<AnnouncementPayload.TableDefaultOrderReversed>(
            payload => payload.IsDescending && payload.WithinGroups));
        _announcer.DidNotReceive().Announce(Arg.Any<AnnouncementPayload.TableSorted>());
        _announcer.DidNotReceive().Announce(Arg.Any<AnnouncementPayload.TableGrouped>());

        // The adopt flush re-enters with the grouping already in the baseline, so it must narrate nothing: a late
        // TableGrouped here would overwrite the within-group reversal in the single-slot live region.
        _announcer.ClearReceivedCalls();
        _state.Value.Returns(AdoptedState(groupBy: ColumnName.Level, isDescending: true));
        await sut.HandleOrderedViewUpdated(_dispatcher);

        _announcer.DidNotReceive().Announce(Arg.Any<AnnouncementPayload>());

        // The baseline advanced on the sort axis, so a second toggle re-announces the opposite within-group direction.
        _announcer.ClearReceivedCalls();
        _state.Value.Returns(State(groupBy: ColumnName.Level, isDescending: false));
        await sut.HandleToggleSorting(_dispatcher);

        _announcer.Received(1).Announce(Arg.Is<AnnouncementPayload.TableDefaultOrderReversed>(
            payload => !payload.IsDescending && payload.WithinGroups));
    }

    [Fact]
    public async Task ToggleSorting_WhenUnsorted_AnnouncesDefaultOrderReversed()
    {
        // The reducer flips the hidden default-order direction without a sort column; the effective time/record order
        // reverses, so it is announced as a default-order reversal (no column-sort announcement, because none is set).
        _state.Value.Returns(State(isDescending: false));
        var sut = CreateSut();
        _state.Value.Returns(State(isDescending: true));

        await sut.HandleToggleSorting(_dispatcher);

        _announcer.Received(1).Announce(Arg.Is<AnnouncementPayload.TableDefaultOrderReversed>(
            payload => payload.IsDescending && !payload.WithinGroups));
        _announcer.DidNotReceive().Announce(Arg.Any<AnnouncementPayload.TableSorted>());
    }

    [Fact]
    public async Task TogglingDefaultDirectionTwice_WhileGroupByAwaitsAdoption_AnnouncesTheGroupAlone()
    {
        // Two direction toggles during the deferred group net to zero, so the adopt flush announces the group alone.
        _state.Value.Returns(State(isDescending: false));
        var sut = CreateSut();

        _state.Value.Returns(State(groupBy: ColumnName.Level, isDescending: false));
        await sut.HandleSetGroupBy(_dispatcher);

        _state.Value.Returns(State(groupBy: ColumnName.Level, isDescending: true));
        await sut.HandleToggleSorting(_dispatcher);
        _state.Value.Returns(State(groupBy: ColumnName.Level, isDescending: false));
        await sut.HandleToggleSorting(_dispatcher);

        var served = GroupedViewState(ColumnName.Level, isGroupDescending: false, ["A", "B"], isDescending: false);
        _state.Value.Returns(served);
        await sut.HandleOrderedViewUpdated(_dispatcher);

        _announcer.Received(1).Announce(Arg.Is<AnnouncementPayload.TableGrouped>(
            payload => payload.Column == ColumnName.Level && payload.GroupCount == 2));
        _announcer.DidNotReceive().Announce(Arg.Any<AnnouncementPayload.TableGroupedWithDefaultOrderReversed>());
    }

    [Fact]
    public async Task TogglingDefaultDirection_WhileGroupByAwaitsAdoption_ComposesTheReversalIntoTheGroupAnnouncement()
    {
        // Group-by is deferred (HasPendingSortChange), so a direction toggle silently reverses the future within-group
        // order. The adopt flush must compose that reversal with the group, or the within-group reorder is never spoken.
        _state.Value.Returns(State(isDescending: false));
        var sut = CreateSut();

        _state.Value.Returns(State(groupBy: ColumnName.Level, isDescending: false));
        await sut.HandleSetGroupBy(_dispatcher);
        _announcer.DidNotReceive().Announce(Arg.Any<AnnouncementPayload>());

        _state.Value.Returns(State(groupBy: ColumnName.Level, isDescending: true));
        await sut.HandleToggleSorting(_dispatcher);
        _announcer.DidNotReceive().Announce(Arg.Any<AnnouncementPayload>());

        var served = GroupedViewState(ColumnName.Level, isGroupDescending: false, ["A", "B"], isDescending: true);
        _state.Value.Returns(served);
        await sut.HandleOrderedViewUpdated(_dispatcher);

        _announcer.Received(1).Announce(Arg.Is<AnnouncementPayload.TableGroupedWithDefaultOrderReversed>(
            payload => payload.Column == ColumnName.Level && !payload.IsGroupDescending && payload.GroupCount == 2 && payload.IsDescending));
        _announcer.DidNotReceive().Announce(Arg.Any<AnnouncementPayload.TableGrouped>());

        // Adoption released the hold and re-synced the baseline, so a further toggle still announces. An unconditional
        // hold would never re-sync and silently swallow it.
        _announcer.ClearReceivedCalls();
        _state.Value.Returns(State(groupBy: ColumnName.Level, isDescending: false));
        await sut.HandleToggleSorting(_dispatcher);

        _announcer.Received(1).Announce(Arg.Is<AnnouncementPayload.TableDefaultOrderReversed>(
            payload => !payload.IsDescending && payload.WithinGroups));
    }

    private static LogTableState AdoptedState(
        ColumnName? orderBy = null,
        bool isDescending = true,
        ColumnName? groupBy = null,
        bool isGroupDescending = false) =>
        new()
        {
            OrderBy = orderBy,
            IsDescending = isDescending,
            GroupBy = groupBy,
            IsGroupDescending = isGroupDescending,
            RequestedOrderBy = orderBy,
            RequestedIsDescending = isDescending,
            RequestedGroupBy = groupBy,
            RequestedIsGroupDescending = isGroupDescending
        };

    // Faulted presentation (active table, display disabled) with committed and requested ordering set independently, so
    // the revert correction and the later recovery diff can be exercised.
    private static LogTableState FaultedCommittedState(
        ColumnName? committedOrderBy = null,
        bool committedIsDescending = true,
        ColumnName? committedGroupBy = null,
        bool committedIsGroupDescending = false,
        ColumnName? requestedOrderBy = null,
        bool requestedIsDescending = true,
        ColumnName? requestedGroupBy = null,
        bool requestedIsGroupDescending = false)
    {
        var id = EventLogId.Create();

        return new LogTableState
        {
            ActiveEventLogId = id,
            EventTables = [new LogView(id)],
            OrderedViewDisplayEnabled = false,
            OrderBy = committedOrderBy,
            IsDescending = committedIsDescending,
            GroupBy = committedGroupBy,
            IsGroupDescending = committedIsGroupDescending,
            RequestedOrderBy = requestedOrderBy,
            RequestedIsDescending = requestedIsDescending,
            RequestedGroupBy = requestedGroupBy,
            RequestedIsGroupDescending = requestedIsGroupDescending
        };
    }

    private static LogTableState FaultedState(ColumnName? requestedGroupBy, ColumnName? requestedOrderBy = null)
    {
        var id = EventLogId.Create();

        return new LogTableState
        {
            ActiveEventLogId = id,
            EventTables = [new LogView(id)],
            OrderedViewDisplayEnabled = false,
            RequestedOrderBy = requestedOrderBy,
            RequestedGroupBy = requestedGroupBy
        };
    }

    // An adopted (committed == requested) ordered view whose served column view yields the given contiguous group keys,
    // so the effect can count groups off the served view exactly as the live walk does.
    private static LogTableState GroupedViewState(
        ColumnName groupBy,
        bool isGroupDescending,
        string[] groupKeys,
        ColumnName? orderBy = null,
        bool isDescending = true)
    {
        var view = Substitute.For<IEventColumnView>();
        view.Count.Returns(groupKeys.Length);
        view.GroupKeyAt(Arg.Any<EventLocator>(), groupBy).Returns(groupKeys[0], [.. groupKeys.Skip(1)]);

        var logId = EventLogId.Create();

        var served = new OrderedViewReady(
            SnapshotVersion: 0,
            Identity: null,
            Sequence: 0,
            SingleLogId: logId,
            InScope: [],
            View: view,
            Config: new SortContext(orderBy, isDescending, groupBy, isGroupDescending),
            Filter: new Filter(null, []));

        return new LogTableState
        {
            ActiveEventLogId = logId,
            EventTables = [new LogView(logId)],
            OrderBy = orderBy,
            IsDescending = isDescending,
            GroupBy = groupBy,
            IsGroupDescending = isGroupDescending,
            RequestedOrderBy = orderBy,
            RequestedIsDescending = isDescending,
            RequestedGroupBy = groupBy,
            RequestedIsGroupDescending = isGroupDescending,
            ActiveOrderedView = served
        };
    }

    // Grouping whose requested direction has diverged from the committed one (HasPendingSortChange), modelling the gap
    // between a group-direction toggle and the adopt flush.
    private static LogTableState PendingGroupState(
        ColumnName groupBy,
        bool committedIsGroupDescending,
        bool requestedIsGroupDescending) =>
        new()
        {
            GroupBy = groupBy,
            IsGroupDescending = committedIsGroupDescending,
            RequestedGroupBy = groupBy,
            RequestedIsGroupDescending = requestedIsGroupDescending
        };

    private static LogTableState State(
        ColumnName? orderBy = null,
        bool isDescending = true,
        ColumnName? groupBy = null,
        bool isGroupDescending = false) =>
        new()
        {
            RequestedOrderBy = orderBy,
            RequestedIsDescending = isDescending,
            RequestedGroupBy = groupBy,
            RequestedIsGroupDescending = isGroupDescending
        };

    private OrderingAnnouncementEffects CreateSut() => new(_state, _announcer);
}
