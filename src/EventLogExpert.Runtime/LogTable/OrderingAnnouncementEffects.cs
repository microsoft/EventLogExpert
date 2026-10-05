// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Announcement;
using Fluxor;
using AnnouncementPayload = EventLogExpert.Runtime.Announcement.Announcement;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.Runtime.LogTable;

/// <summary>
///     Narrates sort/group changes to assistive technology from ONE place, so every surface that can change the
///     ordering (the header caret/indicator, the status-bar chips, the cell/column/group context menus, and the View menu)
///     announces identically. It diffs a requested-ordering snapshot rather than reacting to the raw action, so idempotent
///     re-selects and no-op clears/toggles stay silent and only a real change speaks.
/// </summary>
/// <remarks>
///     INVARIANT: the five actions handled below are the ONLY writers of the <c>LogTableState.Requested*</c> ordering
///     fields (verified by searching every assignment). A future reducer that writes those fields must be routed through
///     here as well, or <see cref="_lastAnnounced" /> drifts and the next real change mis-announces. The snapshot is
///     deliberately requested-only: the committed ordering fields adopt the engine result later via a different action
///     this effect does not observe, so including them would reintroduce that drift.
/// </remarks>
internal sealed class OrderingAnnouncementEffects(IState<LogTableState> logTableState, IAnnouncementService announcementService)
{
    private readonly IAnnouncementService _announcementService = announcementService;
    private readonly IState<LogTableState> _logTableState = logTableState;

    private AnnouncedOrdering _lastAnnounced = Snapshot(logTableState.Value);

    // Hiding the grouped column (column toggle / reset defaults / startup load) clears grouping without a group action;
    // routing it here announces that automatic clear and keeps the snapshot synced so a later ordering action is correct.
    [EffectMethod(typeof(LoadColumnsCompletedAction))]
    public Task HandleLoadColumnsCompleted(IDispatcher dispatcher) => AnnounceIfChanged();

    [EffectMethod(typeof(SetGroupByAction))]
    public Task HandleSetGroupBy(IDispatcher dispatcher) => AnnounceIfChanged();

    [EffectMethod(typeof(SetOrderByAction))]
    public Task HandleSetOrderBy(IDispatcher dispatcher) => AnnounceIfChanged();

    [EffectMethod(typeof(ToggleGroupSortingAction))]
    public Task HandleToggleGroupSorting(IDispatcher dispatcher) => AnnounceIfChanged();

    [EffectMethod(typeof(ToggleSortingAction))]
    public Task HandleToggleSorting(IDispatcher dispatcher) => AnnounceIfChanged();

    private static bool GroupChanged(in AnnouncedOrdering previous, in AnnouncedOrdering current) =>
        previous.GroupColumn != current.GroupColumn ||
        (current.GroupColumn is not null && previous.GroupDescending != current.GroupDescending);

    private static AnnouncedOrdering Snapshot(LogTableState state) =>
        new(
            state.RequestedOrderBy,
            state.RequestedIsDescending,
            state.RequestedGroupBy,
            state.RequestedIsGroupDescending);

    // A column change (set or clear) always speaks; a direction flip speaks only while a column is still selected, so a
    // direction toggle with no sort/group column (reversing the default order) stays silent.
    private static bool SortChanged(in AnnouncedOrdering previous, in AnnouncedOrdering current) =>
        previous.SortColumn != current.SortColumn ||
        (current.SortColumn is not null && previous.SortDescending != current.SortDescending);

    private Task AnnounceIfChanged()
    {
        AnnouncedOrdering current = Snapshot(_logTableState.Value);

        // At most one of these fires per action: each handled reducer changes the sort dimension XOR the group dimension,
        // so the single-slot announcer never has to carry two messages from one gesture.
        if (SortChanged(_lastAnnounced, current))
        {
            _announcementService.Announce(current.SortColumn is { } sortColumn ?
                new AnnouncementPayload.TableSorted(sortColumn, current.SortDescending) :
                new AnnouncementPayload.TableSortCleared());
        }

        if (GroupChanged(_lastAnnounced, current))
        {
            _announcementService.Announce(current.GroupColumn is { } groupColumn ?
                new AnnouncementPayload.TableGrouped(groupColumn, current.GroupDescending) :
                new AnnouncementPayload.TableGroupCleared());
        }

        _lastAnnounced = current;

        return Task.CompletedTask;
    }

    private readonly record struct AnnouncedOrdering(
        ColumnName? SortColumn,
        bool SortDescending,
        ColumnName? GroupColumn,
        bool GroupDescending);
}
