// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Localization;
using EventLogExpert.Runtime.Alerts;
using EventLogExpert.Runtime.FilterLenses;
using EventLogExpert.UI.Common.Interop;
using EventLogExpert.UI.Focus;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Localization;
using Microsoft.JSInterop;
using System.Collections.Immutable;

namespace EventLogExpert.UI.FilterLenses;

public sealed partial class LensBreadcrumb
{
    // Filters-pane focus-restore landmark; the class + attribute disambiguate it from the breadcrumb's own
    // data-pane="filters" region regardless of DOM order. Pinned by FilterPaneTests.
    private const string FilterPaneFocusSelector = ".filter-pane[data-pane='filters']";

    private readonly Dictionary<FilterLensId, ElementReference> _keepRefs = [];
    private readonly Dictionary<FilterLensId, ElementReference> _removeRefs = [];

    // aria-disabled (not the native disabled attribute, which drops focusability) keeps this reachable for
    // screen-reader users; the handler still guards the action.
    private readonly string _saveAsGroupHintId = $"lens-save-as-group-hint-{Guid.NewGuid():N}";

    private IJSObjectReference? _focusModule;
    private ImmutableHashSet<FilterLensId> _pendingGroupClearIds = [];
    private PendingFocusRestore? _pendingRestore;
    private HashSet<FilterLensId> _renderedLensIds = [];

    [Inject] private IAlertDialogService AlertDialogService { get; init; } = null!;

    private bool CanSaveAsGroup => LensSource.Lenses.Any(lens => lens.Kind == LensKind.Property);

    [Inject] private IFilterLensCommands Commands { get; init; } = null!;

    [Inject] private IJSRuntime JSRuntime { get; init; } = null!;

    [Inject] private IFilterLensSource LensSource { get; init; } = null!;

    [Inject] private IStringLocalizer<SharedResource> Localizer { get; init; } = null!;

    private string SaveAsGroupAriaDisabled => CanSaveAsGroup ? "false" : "true";

    private string? SaveAsGroupHintRef => CanSaveAsGroup ? null : _saveAsGroupHintId;

    protected override async ValueTask DisposeAsyncCore(bool disposing)
    {
        await JsModuleInterop.DisposeModuleSafelyAsync(_focusModule);
        _focusModule = null;

        await base.DisposeAsyncCore(disposing);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        PruneChipRefs();

        var lenses = LensSource.Lenses;

        // Consume the immediate arm once removal propagates (single-lens: id gone; bulk: list empty). A bulk arm that
        // did not empty is a synchronous no-op, so disarm it; single-lens arms are retained until their id is gone.
        if (_pendingRestore is { } arm)
        {
            bool propagated = arm.RemovedId is { } removedId ?
                lenses.All(lens => lens.Id != removedId) :
                lenses.IsEmpty;

            if (propagated)
            {
                _pendingRestore = null;

                await RestoreImmediateFocusAsync(arm);
            }
            else if (arm.RemovedId is null)
            {
                _pendingRestore = null;
            }
        }

        // Deferred group-clear (the async persist clears only property lenses): drain per id against the RENDERED set
        // (the live source can lead the DOM), guarded, after the immediate arm so its focus has already landed.
        if (!_pendingGroupClearIds.IsEmpty)
        {
            var cleared = _pendingGroupClearIds.Where(id => !_renderedLensIds.Contains(id)).ToImmutableArray();

            if (cleared.Length > 0)
            {
                _pendingGroupClearIds = _pendingGroupClearIds.Except(cleared);

                await RestoreFilterPaneFocusIfOrphanedAsync();
            }
        }

        await base.OnAfterRenderAsync(firstRender);
    }

    protected override void OnInitialized()
    {
        ObserveSource(LensSource);

        base.OnInitialized();
    }

    // Arm the neighbor chip to focus once a single-lens removal propagates; run before the synchronous Commands
    // dispatch. Guard by region SURVIVAL, not neighbor-ref presence (a surviving region may hold focus; a full unmount
    // is provably orphaned, so fail open).
    private void ArmChipRemovalFocus(FilterLensId removedId, bool guardedOrigin, bool keepButtonTarget)
    {
        var lenses = LensSource.Lenses;

        int removedIndex = -1;

        for (int index = 0; index < lenses.Count; index++)
        {
            if (lenses[index].Id == removedId) { removedIndex = index; break; }
        }

        if (removedIndex < 0) { return; }

        var refs = keepButtonTarget ? _keepRefs : _removeRefs;
        bool regionSurvives = lenses.Count > 1;

        FilterLensId? targetId = NeighborFocus.TryGetNeighborAfterRemove(
            lenses, removedIndex, lens => refs.ContainsKey(lens.Id), out var neighbor) ?
                neighbor.Id : null;

        _pendingRestore = new PendingFocusRestore(
            RemovedId: removedId,
            TargetId: targetId,
            TargetIsKeepButton: keepButtonTarget,
            Guarded: guardedOrigin && regionSurvives);
    }

    // Save all / Clear all unmount the whole region (provably orphaned, fail open); no removed id, so the arm consumes
    // when the list empties.
    private void ArmSynchronousBulkFocus() =>
        _pendingRestore = new PendingFocusRestore(
            RemovedId: null, TargetId: null, TargetIsKeepButton: false, Guarded: false);

    private void ClearLensesWithFocus()
    {
        ArmSynchronousBulkFocus();
        Commands.ClearLenses();
    }

    private void HandleKeyDown(KeyboardEventArgs args)
    {
        if (args.Key != "Escape") { return; }

        var lenses = LensSource.Lenses;

        if (lenses.IsEmpty) { return; }

        // Escape removes the last lens; guard the restore since the focus origin is unknown.
        FilterLensId removedId = lenses[^1].Id;
        ArmChipRemovalFocus(removedId, guardedOrigin: true, keepButtonTarget: false);
        Commands.RemoveLens(removedId);
    }

    private ValueTask<IJSObjectReference> ImportFocusModuleAsync() =>
        JSRuntime.InvokeAsync<IJSObjectReference>("import", "./_content/EventLogExpert.UI/Common/focusGuard.js");

    private void PromoteLensWithFocus(FilterLensId lensId)
    {
        // Keep promotes and removes this chip; target the neighbor's KEEP button so the next Enter never lands on a
        // delete control.
        ArmChipRemovalFocus(lensId, guardedOrigin: false, keepButtonTarget: true);
        Commands.PromoteLens(lensId);
    }

    // Drop refs for unrendered chips (captures never re-run, so a stale entry throws on focus); prune against the
    // render-time set, which the live source can lag.
    private void PruneChipRefs()
    {
        PruneRefs(_removeRefs);
        PruneRefs(_keepRefs);
    }

    private void PruneRefs(Dictionary<FilterLensId, ElementReference> refs)
    {
        if (refs.Count == 0) { return; }

        List<FilterLensId> stale = [.. refs.Keys.Where(id => !_renderedLensIds.Contains(id))];

        foreach (var id in stale) { refs.Remove(id); }
    }

    private void RemoveLensWithFocus(FilterLensId lensId)
    {
        ArmChipRemovalFocus(lensId, guardedOrigin: false, keepButtonTarget: false);
        Commands.RemoveLens(lensId);
    }

    // Unguarded filters-pane restore for the synchronous region-unmount paths (provably orphaned).
    private async ValueTask RestoreFilterPaneFocusAsync()
    {
        try
        {
            _focusModule ??= await ImportFocusModuleAsync();
            await _focusModule.InvokeAsync<bool>("focusSelector", FilterPaneFocusSelector, true);
        }
        catch (JSDisconnectedException) { }
        catch (JSException) { }
        catch (ObjectDisposedException) { }
        catch (TaskCanceledException) { }
    }

    // Guarded filters-pane restore (Escape/keep fallback + deferred clear): focus may rest on a survivor or have moved.
    private async ValueTask RestoreFilterPaneFocusIfOrphanedAsync()
    {
        try
        {
            _focusModule ??= await ImportFocusModuleAsync();
            await _focusModule.InvokeAsync<bool>("focusSelectorIfNotElsewhere", FilterPaneFocusSelector, true);
        }
        catch (JSDisconnectedException) { }
        catch (JSException) { }
        catch (ObjectDisposedException) { }
        catch (TaskCanceledException) { }
    }

    // Guarded neighbor-chip restore (Escape path): if the module is unavailable, do NOT restore (focus may rest on a
    // survivor).
    private async ValueTask RestoreFocusIfOrphanedAsync(ElementReference target)
    {
        try
        {
            _focusModule ??= await ImportFocusModuleAsync();
            await _focusModule.InvokeAsync<bool>("focusIfNotElsewhere", target, true);
        }
        catch (JSDisconnectedException) { }
        catch (JSException) { }
        catch (ObjectDisposedException) { }
        catch (TaskCanceledException) { }
    }

    private async ValueTask RestoreImmediateFocusAsync(PendingFocusRestore arm)
    {
        var refs = arm.TargetIsKeepButton ? _keepRefs : _removeRefs;

        if (arm.TargetId is { } id && refs.TryGetValue(id, out var targetRef))
        {
            if (arm.Guarded) { await RestoreFocusIfOrphanedAsync(targetRef); }
            else { await ElementFocus.SafelyAsync(targetRef, preventScroll: true); }

            return;
        }

        // Sole-lens / bulk / missing ref: fall back to the filters pane, NEVER the opposite-kind button (a keep removal
        // must not land on a delete).
        if (arm.Guarded) { await RestoreFilterPaneFocusIfOrphanedAsync(); }
        else { await RestoreFilterPaneFocusAsync(); }
    }

    private void SaveAllWithFocus()
    {
        ArmSynchronousBulkFocus();
        Commands.PromoteAllLenses();
    }

    private async Task SaveAsGroupAsync()
    {
        if (!CanSaveAsGroup) { return; }

        PromptOutcome outcome = await AlertDialogService.DisplayPromptWithSecondary(
            Localizer["FilterLens_SaveAsGroup_PromptTitle"],
            Localizer["FilterLens_SaveAsGroup_PromptMessage"],
            Localizer["FilterLens_SaveAsGroup_DefaultName"],
            Localizer["FilterLens_SaveAsGroup_Save"],
            Localizer["FilterLens_SaveAsGroup_SaveAndClear"],
            Localizer["FilterLens_SaveAsGroup_Cancel"],
            candidate => string.IsNullOrWhiteSpace(candidate) ? Localizer["FilterLens_SaveAsGroup_NameRequired"].Value : null);

        if (outcome.Choice == PromptChoice.Cancel || string.IsNullOrWhiteSpace(outcome.Value)) { return; }

        string name = outcome.Value.Trim();
        bool clearAfterSave = outcome.Choice == PromptChoice.Secondary;

        if (clearAfterSave)
        {
            // The clear is deferred to the persist-success effect and removes only contributing (property) lenses
            // (Kind == Property mirrors its Where(!ExcludeFilters.IsEmpty)). Union their ids (overlap-safe) so the
            // deferred restore fires per id as each leaves the rendered set.
            var contributing =
                LensSource.Lenses.Where(lens => lens.Kind == LensKind.Property).Select(lens => lens.Id).ToImmutableArray();

            if (!contributing.IsEmpty)
            {
                _pendingGroupClearIds = _pendingGroupClearIds.Union(contributing);
            }
        }

        Commands.SaveLensesAsGroup(name, clearAfterSave: clearAfterSave);
    }

    private readonly record struct PendingFocusRestore(
        FilterLensId? RemovedId,
        FilterLensId? TargetId,
        bool TargetIsKeepButton,
        bool Guarded);
}
