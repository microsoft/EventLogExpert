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
///     INVARIANT: the five request actions handled below (SetGroupBy, SetOrderBy, ToggleGroupSorting, ToggleSorting,
///     LoadColumnsCompleted) are the ONLY writers of the <c>LogTableState.Requested*</c> ordering fields (verified by
///     searching every assignment). A future reducer that writes those fields must be routed through here as well, or
///     <see cref="_lastAnnounced" /> drifts and the next real change mis-announces.
///     <see cref="OrderedViewUpdatedAction" /> is additionally observed - not as a writer, but as the adopt signal that
///     flushes an announcement deferred while faulted. The snapshot is deliberately requested-only: the committed ordering
///     fields adopt the engine result later via that same action, so keying off them would reintroduce that drift. While
///     faulted the effect stays silent (the requested reorder is not taking effect) and leaves
///     <see cref="_lastAnnounced" /> untouched, so the deferred change is not dropped: when the reproject recovers and
///     adopts the requested ordering, the still-pending baseline diff narrates it once, on the axis that actually changed.
///     The announcer holds a single slot, so the rare case of two different-axis changes accumulated across ONE persistent
///     fault is flushed on recovery as a single combined TableSortAndGroupChanged announcement that narrates both axes,
///     rather than two messages where the second (group) would overwrite the first (sort) before the live region reads it.
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

    // The committed ordering adopts the requested reorder via this action. On the normal path the request was already
    // announced (baseline advanced), so re-diffing here is a no-op; after a fault - where the request stayed silent and
    // left the baseline untouched - this flushes the deferred announcement. Gate on the committed ordering actually
    // matching the requested one: a rejected/stale update, an OrderedViewCleared invalidation, or a fault that cleared
    // because the view went away (the last tab closed) rather than because the reorder landed, leaves committed !=
    // requested and must not narrate an ordering no view ever adopted.
    [EffectMethod(typeof(OrderedViewUpdatedAction))]
    public Task HandleOrderedViewUpdated(IDispatcher dispatcher) =>
        _logTableState.Value.HasPendingSortChange ? Task.CompletedTask : AnnounceIfChanged();

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
        var state = _logTableState.Value;
        AnnouncedOrdering current = Snapshot(state);

        // While faulted the requested reorder is not taking effect (the served view shows committed), so stay silent
        // AND leave the baseline untouched: the change is deferred, not dropped. When the reproject recovers,
        // OrderedViewUpdatedAction re-enters AnnounceIfChanged with the adopted ordering and the still-pending baseline
        // diff narrates the change exactly once, on the axis that actually changed. Advancing the baseline here would
        // drop it; announcing here would narrate a change no visible surface reflects yet.
        if (state.PresentationState == PresentationState.Faulted)
        {
            return Task.CompletedTask;
        }

        // On the normal path each handled reducer changes the sort dimension XOR the group dimension, so at most one of
        // the single-axis branches fires (none when the request was a no-op). The combined branch engages only when a
        // single flush carries a change on BOTH axes - a persistent fault that accumulated a sort change and a group
        // change, then recovered: the single-slot announcer renders one message, so two separate announcements would
        // let the second overwrite the first before the live region reads it. One combined announcement narrates both.
        bool sortChanged = SortChanged(_lastAnnounced, current);
        bool groupChanged = GroupChanged(_lastAnnounced, current);

        switch (sortChanged)
        {
            case true when groupChanged:
                _announcementService.Announce(new AnnouncementPayload.TableSortAndGroupChanged(
                    current.SortColumn,
                    current.SortDescending,
                    current.GroupColumn,
                    current.GroupDescending));

                break;
            case true:
                _announcementService.Announce(current.SortColumn is { } sortColumn ?
                    new AnnouncementPayload.TableSorted(sortColumn, current.SortDescending) :
                    new AnnouncementPayload.TableSortCleared());

                break;
            default:
                if (groupChanged)
                {
                    _announcementService.Announce(current.GroupColumn is { } groupColumn ?
                        new AnnouncementPayload.TableGrouped(groupColumn, current.GroupDescending) :
                        new AnnouncementPayload.TableGroupCleared());
                }

                break;
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
