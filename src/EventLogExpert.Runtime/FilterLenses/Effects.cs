// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Announcement;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.FilterPane;
using Fluxor;

namespace EventLogExpert.Runtime.FilterLenses;

/// <summary>
///     Re-narrows the view whenever the lens stack changes by recomposing the effective filter from the current base
///     (<see cref="FilterPaneState" />) and the lens stack, then dispatching <c>ApplyFilterAction</c>. Composition rides
///     the existing apply/concurrency path (last dispatch wins via the filter token); lenses are never written back into
///     the persistent <see cref="FilterPaneState" />.
/// </summary>
internal sealed class Effects(
    IState<FilterLensState> lensState,
    IState<FilterPaneState> filterPaneState,
    IAnnouncementService announcementService)
{
    private readonly IAnnouncementService _announcementService = announcementService;
    private readonly IState<FilterPaneState> _filterPaneState = filterPaneState;
    private readonly IState<FilterLensState> _lensState = lensState;

    [EffectMethod(typeof(ClearFilterLensesAction))]
    public Task HandleClear(IDispatcher dispatcher) => Reapply(dispatcher);

    // Close-all clears every lens. A single-log close only drops the lenses that originated from that log: the user close
    // (tab button / tab menu) routes through EventLogCommands.CloseLog, which emits LogClosedByUserAction, whereas a
    // filter-driven reload dispatches CloseLogAction directly and never emits it - so this never wipes a live lens
    // mid-reload. Lenses with a null origin (or from a still-open log) persist and clear only on close-all.
    [EffectMethod(typeof(CloseAllLogsAction))]
    public Task HandleCloseAllLogs(IDispatcher dispatcher)
    {
        if (!_lensState.Value.Lenses.IsEmpty)
        {
            dispatcher.Dispatch(new ClearFilterLensesAction());
        }

        return Task.CompletedTask;
    }

    [EffectMethod]
    public Task HandleLogClosedByUser(LogClosedByUserAction action, IDispatcher dispatcher)
    {
        if (_lensState.Value.Lenses.Any(lens => string.Equals(lens.OriginLog, action.LogName, StringComparison.Ordinal)))
        {
            dispatcher.Dispatch(new RemoveLensesForLogAction(action.LogName));
        }

        return Task.CompletedTask;
    }

    [EffectMethod]
    public Task HandlePromote(PromoteFilterLensAction action, IDispatcher dispatcher)
    {
        var lens = _lensState.Value.Lenses.FirstOrDefault(candidate => candidate.Id == action.Id);

        if (lens is null) { return Task.CompletedTask; }

        // Persist the lens's natural promote form (a positive include for keep-only lenses), falling back to the
        // transient exclude-of-complement for hide lenses, whose exclude form is already the natural one.
        var filters = lens.PromoteFilters.IsEmpty ? lens.ExcludeFilters : lens.PromoteFilters;

        // An absent (already removed) or degenerate (nothing to keep) lens neither announces nor commits. The factory
        // never produces a degenerate lens; this is a defensive gate.
        if (filters.IsEmpty && lens.Window is not { IsEnabled: true })
        {
            return Task.CompletedTask;
        }

        _announcementService.Announce($"Kept as filter: {lens.Label}");

        dispatcher.Dispatch(new CommitPromotedLensAction(lens.Id, filters, lens.Window));

        return Task.CompletedTask;
    }

    [EffectMethod]
    public Task HandlePush(PushFilterLensAction action, IDispatcher dispatcher) => Reapply(dispatcher);

    [EffectMethod]
    public Task HandleRemove(RemoveFilterLensAction action, IDispatcher dispatcher) => Reapply(dispatcher);

    [EffectMethod]
    public Task HandleRemoveForLog(RemoveLensesForLogAction action, IDispatcher dispatcher) => Reapply(dispatcher);

    private Task Reapply(IDispatcher dispatcher)
    {
        var baseFilter = FilterPaneFilterBuilder.Build(_filterPaneState.Value);
        var effective = EffectiveFilterBuilder.Build(baseFilter, _lensState.Value.Lenses);

        dispatcher.Dispatch(new ApplyFilterAction(effective));

        return Task.CompletedTask;
    }
}
