// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

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
    public async Task SetGroupBy_AnnouncesGrouped()
    {
        var sut = CreateSut();
        _state.Value.Returns(State(groupBy: ColumnName.Level, isGroupDescending: false));

        await sut.HandleSetGroupBy(_dispatcher);

        _announcer.Received(1).Announce(Arg.Is<AnnouncementPayload.TableGrouped>(
            payload => payload.Column == ColumnName.Level && !payload.IsGroupDescending));
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
