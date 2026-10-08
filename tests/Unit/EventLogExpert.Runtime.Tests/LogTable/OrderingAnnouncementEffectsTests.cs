// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Runtime.Announcement;
using EventLogExpert.Runtime.LogTable;
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
    public async Task Effect_IsDiscoveredByFluxor_AnnouncesThroughARealStore()
    {
        // Wires a minimal real store (the LogTable feature + its reducers + this effect) to prove Fluxor discovers the
        // [EffectMethod]s and runs them AFTER the reducer, so a dispatched ordering action actually announces - and that
        // toggling direction while unsorted stays silent end to end.
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
        announcer.DidNotReceive().Announce(Arg.Any<AnnouncementPayload>());
    }

    [Fact]
    public async Task LoadColumnsCompleted_ClearsHiddenGroup_AnnouncesOnceThenStaysSyncedOnNextSort()
    {
        // Hiding the grouped column clears grouping without a group action; the effect must announce that clear AND sync
        // its snapshot, so a later unrelated sort does not replay a stale "grouping cleared".
        _state.Value.Returns(State(groupBy: ColumnName.Source, isGroupDescending: false));
        var sut = CreateSut();

        _state.Value.Returns(State());
        await sut.HandleLoadColumnsCompleted(_dispatcher);
        _announcer.Received(1).Announce(Arg.Any<AnnouncementPayload.TableGroupCleared>());

        _announcer.ClearReceivedCalls();
        _state.Value.Returns(State(orderBy: ColumnName.DateAndTime, isDescending: true));
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
    public async Task SetGroupBy_AnnouncesGrouped()
    {
        var sut = CreateSut();
        _state.Value.Returns(State(groupBy: ColumnName.Level, isGroupDescending: false));

        await sut.HandleSetGroupBy(_dispatcher);

        _announcer.Received(1).Announce(Arg.Is<AnnouncementPayload.TableGrouped>(
            payload => payload.Column == ColumnName.Level && !payload.IsGroupDescending));
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
    public async Task ToggleGroupSorting_FlipsDirection_AnnouncesNewGroupDirection()
    {
        _state.Value.Returns(State(groupBy: ColumnName.Level, isGroupDescending: false));
        var sut = CreateSut();
        _state.Value.Returns(State(groupBy: ColumnName.Level, isGroupDescending: true));

        await sut.HandleToggleGroupSorting(_dispatcher);

        _announcer.Received(1).Announce(Arg.Is<AnnouncementPayload.TableGrouped>(
            payload => payload.Column == ColumnName.Level && payload.IsGroupDescending));
    }

    [Fact]
    public async Task ToggleGroupSorting_WhenAlsoSorted_AnnouncesGroupNotSort()
    {
        _state.Value.Returns(State(orderBy: ColumnName.Source, isDescending: true, groupBy: ColumnName.Level, isGroupDescending: false));
        var sut = CreateSut();
        _state.Value.Returns(State(orderBy: ColumnName.Source, isDescending: true, groupBy: ColumnName.Level, isGroupDescending: true));

        await sut.HandleToggleGroupSorting(_dispatcher);

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
    public async Task ToggleSorting_WhenUnsorted_DoesNotAnnounce()
    {
        // The reducer flips the hidden default-order direction without a sort column; nothing is announced because no
        // column is sorted, so reversing the default order stays silent.
        _state.Value.Returns(State(isDescending: false));
        var sut = CreateSut();
        _state.Value.Returns(State(isDescending: true));

        await sut.HandleToggleSorting(_dispatcher);

        _announcer.DidNotReceive().Announce(Arg.Any<AnnouncementPayload>());
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
