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
///     flushes an announcement deferred while faulted AND the point at which a group-by announcement can carry the real
///     group count. The snapshot is deliberately requested-only: the committed ordering fields adopt the engine result
///     later via that same action, so keying off them would reintroduce that drift. While faulted the effect stays silent
///     (the requested reorder is not taking effect) and leaves <see cref="_lastAnnounced" /> untouched, so the deferred
///     change is not dropped: when the reproject recovers and adopts the requested ordering, the still-pending baseline
///     diff narrates it once, on the axis that actually changed. The announcer holds a single slot, so the rare case of
///     two different-axis changes accumulated across ONE persistent fault is flushed on recovery as a single combined
///     TableSortAndGroupChanged announcement that narrates both axes, rather than two messages where the second (group)
///     would overwrite the first (sort) before the live region reads it. A group-BY announcement (unlike a group clear)
///     carries the served group count, which only exists once the view reflects the grouping, so it is held back from the
///     request until the adopt flush even on the non-faulted path. <see cref="OrderedViewDisplayFaultedAction" /> is
///     observed separately to narrate the revert to the committed ordering the grid now shows when a reproject faults.
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

    // A reproject faulted and the grid reverted to the committed ordering (OrderedViewSource masks Requested* back to
    // committed). Narrate that revert using the committed fields the user now sees, then re-baseline to committed so a
    // later recovery that adopts the requested ordering re-announces it and this correction is not self-suppressed.
    [EffectMethod]
    public Task HandleOrderedViewDisplayFaulted(OrderedViewDisplayFaultedAction action, IDispatcher dispatcher)
    {
        var state = _logTableState.Value;

        // Mirror the reducer's identity guard: a stale fault from a superseded request (which the reducer rejects) must
        // not narrate the current view just because it happens to already be faulted.
        if (action.Identity is { } faultedIdentity && faultedIdentity != state.ViewIdentity) { return Task.CompletedTask; }

        // Narrate only a reorder that actually reverted: the fault must be in effect AND a reorder must have been
        // pending. The action also fires for non-ordering faults (ingest, close, tab switch) where requested ==
        // committed, and asserting a sort revert the user never requested would be wrong.
        if (state.PresentationState != PresentationState.Faulted || !state.HasPendingSortChange) { return Task.CompletedTask; }

        _announcementService.Announce(new AnnouncementPayload.TableReorderUnavailable(
            state.OrderBy,
            state.IsDescending,
            state.GroupBy,
            state.IsGroupDescending));

        _lastAnnounced = new AnnouncedOrdering(SortColumn: state.OrderBy,
            SortDescending: state.IsDescending,
            GroupColumn: state.GroupBy,
            GroupDescending: state.IsGroupDescending);

        return Task.CompletedTask;
    }

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

    private static int CountGroups(IEventColumnView view, ColumnName groupColumn)
    {
        int count = 0;
        string? currentKey = null;

        for (int index = 0; index < view.Count; index++)
        {
            string key = view.GroupKeyAt(view.LocatorAt(index), groupColumn);

            if (currentKey is null || !string.Equals(key, currentKey, StringComparison.Ordinal))
            {
                count++;
                currentKey = key;
            }
        }

        return count;
    }

    private static bool GroupChanged(in AnnouncedOrdering previous, in AnnouncedOrdering current) =>
        previous.GroupColumn != current.GroupColumn ||
        (current.GroupColumn is not null && previous.GroupDescending != current.GroupDescending);

    // Count the displayed view the grid actually renders (the same view GroupedRowView walks), not ActiveOrderedView:
    // an in-flight request nulls ActiveOrderedView while the retained view keeps rendering, so counting the displayed
    // view keeps the announced count equal to the headers on screen. It is ordered by the group column, so distinct
    // keys run contiguously and one walk counts the groups.
    private static int GroupCountForDisplayedView(LogTableState state, ColumnName groupColumn) =>
        CountGroups(state.GetActiveDisplayedEvents(), groupColumn);

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

        bool sortChanged = SortChanged(_lastAnnounced, current);
        bool groupChanged = GroupChanged(_lastAnnounced, current);

        // A group-BY announcement carries the served group count, which only exists once the view reflects the grouping
        // (HasPendingSortChange clears at the adopt flush). Hold it back from the request so the adopt flush narrates it
        // with the real count; a group CLEAR needs no count and speaks immediately like the sort axis.
        bool groupByDeferred = groupChanged && current.GroupColumn is not null && state.HasPendingSortChange;
        bool announceGroup = groupChanged && !groupByDeferred;

        // Direction flipped with no sort column. Detected even mid group change so a reversal toggled during a
        // deferred group adoption is not lost.
        bool defaultOrderReversed = !sortChanged &&
            current.SortColumn is null &&
            current.SortDescending != _lastAnnounced.SortDescending;

        // A concurrent group-BY adoption carries the reversal in one combined announcement, since the single
        // live-region slot would otherwise overwrite a standalone reversal and leave the within-group reorder silent.
        bool announceStandaloneReversal = defaultOrderReversed && !groupChanged;
        bool composeReversalWithGroup = defaultOrderReversed && announceGroup && current.GroupColumn is not null;

        switch (sortChanged)
        {
            case true when announceGroup:
                _announcementService.Announce(new AnnouncementPayload.TableSortAndGroupChanged(
                    current.SortColumn,
                    current.SortDescending,
                    current.GroupColumn,
                    current.GroupDescending,
                    current.GroupColumn is { } combinedGroupColumn ?
                        GroupCountForDisplayedView(state, combinedGroupColumn) : 0));

                break;
            case true:
                _announcementService.Announce(current.SortColumn is { } sortColumn ?
                    new AnnouncementPayload.TableSorted(sortColumn, current.SortDescending) :
                    new AnnouncementPayload.TableSortCleared());

                break;
            default:
                if (announceGroup && current.GroupColumn is { } groupColumn)
                {
                    int groupCount = GroupCountForDisplayedView(state, groupColumn);

                    _announcementService.Announce(composeReversalWithGroup ?
                        new AnnouncementPayload.TableGroupedWithDefaultOrderReversed(groupColumn,
                            current.GroupDescending,
                            groupCount,
                            current.SortDescending) :
                        new AnnouncementPayload.TableGrouped(groupColumn, current.GroupDescending, groupCount));
                }
                else if (announceGroup)
                {
                    _announcementService.Announce(new AnnouncementPayload.TableGroupCleared());
                }
                else if (announceStandaloneReversal)
                {
                    _announcementService.Announce(
                        new AnnouncementPayload.TableDefaultOrderReversed(current.SortDescending, current.GroupColumn is not null));
                }

                break;
        }

        // Hold an axis whose change is not yet announced so the adopt flush still sees it. HasPendingSortChange gates
        // the reversal hold to a pending flush; without it, a reversal coinciding with a group CLEAR (nothing to
        // adopt) would hold forever, desyncing the baseline and silencing the next toggle.
        bool reversalHeld = defaultOrderReversed &&
            !announceStandaloneReversal &&
            !composeReversalWithGroup &&
            state.HasPendingSortChange;

        _lastAnnounced = current with
        {
            SortDescending = reversalHeld ? _lastAnnounced.SortDescending : current.SortDescending,
            GroupColumn = announceGroup ? current.GroupColumn : _lastAnnounced.GroupColumn,
            GroupDescending = announceGroup ? current.GroupDescending : _lastAnnounced.GroupDescending,
        };

        return Task.CompletedTask;
    }

    private readonly record struct AnnouncedOrdering(
        ColumnName? SortColumn,
        bool SortDescending,
        ColumnName? GroupColumn,
        bool GroupDescending);
}
