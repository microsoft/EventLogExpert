// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Runtime.Announcement;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.FilterLibrary;
using EventLogExpert.Runtime.FilterPane;
using Fluxor;
using System.Collections.Immutable;

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

        // An absent (already removed) or degenerate (nothing to keep) lens neither announces nor commits. The factory
        // never produces a degenerate lens; this is a defensive gate.
        if (lens is null || !IsPromotable(lens)) { return Task.CompletedTask; }

        _announcementService.AnnounceLensKept(lens.Label);

        dispatcher.Dispatch(new CommitPromotedLensAction(lens.Id, PromoteForm(lens), lens.Window));

        return Task.CompletedTask;
    }

    [EffectMethod(typeof(PromoteAllFilterLensesAction))]
    public Task HandlePromoteAll(IDispatcher dispatcher)
    {
        var commits = ImmutableList.CreateBuilder<PromotedLensCommit>();

        foreach (var lens in _lensState.Value.Lenses)
        {
            if (lens.ExcludeFilters.IsEmpty && lens.Window is not { IsEnabled: true }) { continue; }

            commits.Add(new PromotedLensCommit(lens.Id, lens.ExcludeFilters, lens.Window));
        }

        if (commits.Count == 0) { return Task.CompletedTask; }

        dispatcher.Dispatch(new CommitPromotedLensesAction(commits.ToImmutable()));

        _announcementService.AnnounceLensesSavedAll();

        return Task.CompletedTask;
    }

    [EffectMethod]
    public Task HandlePush(PushFilterLensAction action, IDispatcher dispatcher) => Reapply(dispatcher);

    [EffectMethod]
    public Task HandleRemove(RemoveFilterLensAction action, IDispatcher dispatcher) => Reapply(dispatcher);

    [EffectMethod]
    public Task HandleRemoveForLog(RemoveLensesForLogAction action, IDispatcher dispatcher) => Reapply(dispatcher);

    [EffectMethod]
    public Task HandleRemoveLenses(RemoveFilterLensesAction action, IDispatcher dispatcher) => Reapply(dispatcher);

    [EffectMethod]
    public Task HandleSaveFilterSetSucceeded(SaveFilterSetSucceededAction action, IDispatcher dispatcher)
    {
        if (action.Origin == SaveFilterSetOrigin.Lens)
        {
            _announcementService.AnnounceLensGroupSaved(action.Name);
        }

        // Save-and-clear removes only the lenses that were actually saved (captured before the persist began), so any
        // lens the user added while the write was in flight survives. The removal rides the confirmed-persist signal,
        // so a failed save never removes anything.
        if (action.LensesToClearOnSuccess is { Count: > 0 } lensesToClear)
        {
            dispatcher.Dispatch(new RemoveFilterLensesAction(lensesToClear));
        }

        return Task.CompletedTask;
    }

    [EffectMethod]
    public Task HandleSaveLensesAsGroup(SaveLensesAsGroupAction action, IDispatcher dispatcher)
    {
        if (string.IsNullOrWhiteSpace(action.Name)) { return Task.CompletedTask; }

        var lenses = _lensState.Value.Lenses;
        var filters = lenses.SelectMany(lens => lens.ExcludeFilters).ToImmutableList();

        if (filters.IsEmpty) { return Task.CompletedTask; }

        // For save-and-clear, capture the ids of exactly the lenses being saved. The terminal
        // SaveFilterSetSucceededAction removes only those - and only after the write succeeds - so a lens added while
        // the persist is in flight survives, and a failed save (surfaced via the error banner) discards nothing.
        var lensesToClearOnSuccess = action.ClearAfterSave ? lenses.Select(lens => lens.Id).ToImmutableList() : null;

        dispatcher.Dispatch(new SaveFilterSetAction(action.Name, filters, SaveFilterSetOrigin.Lens, lensesToClearOnSuccess));

        return Task.CompletedTask;
    }

    private static bool IsPromotable(FilterLens lens) =>
        !PromoteForm(lens).IsEmpty || lens.Window is { IsEnabled: true };

    private static ImmutableList<SavedFilter> PromoteForm(FilterLens lens) =>
        lens.PromoteFilters.IsEmpty ? lens.ExcludeFilters : lens.PromoteFilters;

    private Task Reapply(IDispatcher dispatcher)
    {
        var baseFilter = FilterPaneFilterBuilder.Build(_filterPaneState.Value);
        var effective = EffectiveFilterBuilder.Build(baseFilter, _lensState.Value.Lenses);

        dispatcher.Dispatch(new ApplyFilterAction(effective));

        return Task.CompletedTask;
    }
}
