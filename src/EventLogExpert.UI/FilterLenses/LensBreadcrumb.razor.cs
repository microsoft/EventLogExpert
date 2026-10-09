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

namespace EventLogExpert.UI.FilterLenses;

public sealed partial class LensBreadcrumb
{
    private readonly Dictionary<FilterLensId, ElementReference> _removeRefs = [];

    // "Save as group" stays keyboard-focusable while unavailable (aria-disabled instead of the native
    // disabled attribute, which drops focusability), so screen-reader users can reach it and hear the
    // reason via aria-describedby. The click/keyboard handler still guards the action (SaveAsGroupAsync).
    private readonly string _saveAsGroupHintId = $"lens-save-as-group-hint-{Guid.NewGuid():N}";

    private IJSObjectReference? _focusModule;
    private bool _pendingEscapeGuard;
    private FilterLensId? _pendingRemovedLensId;
    private FilterLensId? _pendingTargetLensId;
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
        PruneRemoveRefs();

        // Consume the arm only once the removal has propagated (the removed lens is gone from the live source), so a
        // pre-removal render never fires it. Clearing it unconditionally here keeps it single-shot.
        if (_pendingRemovedLensId is { } removedId && LensSource.Lenses.All(lens => lens.Id != removedId))
        {
            bool escapeGuard = _pendingEscapeGuard;
            FilterLensId? targetId = _pendingTargetLensId;

            _pendingRemovedLensId = null;
            _pendingTargetLensId = null;
            _pendingEscapeGuard = false;

            if (targetId is { } id && _removeRefs.TryGetValue(id, out var targetRef))
            {
                if (escapeGuard)
                {
                    await RestoreFocusIfOrphanedAsync(targetRef);
                }
                else
                {
                    await ElementFocus.SafelyAsync(targetRef, preventScroll: true);
                }
            }
        }

        await base.OnAfterRenderAsync(firstRender);
    }

    protected override void OnInitialized()
    {
        ObserveSource(LensSource);
        base.OnInitialized();
    }

    // Snapshots the lenses BEFORE removal and records the surviving neighbor chip to focus once the removal propagates.
    // Must run before Commands.RemoveLens, whose reducer updates the source synchronously.
    private void ArmNeighborFocus(FilterLensId removedId, bool escapeGuard)
    {
        var lenses = LensSource.Lenses;

        int removedIndex = -1;

        for (int index = 0; index < lenses.Count; index++)
        {
            if (lenses[index].Id == removedId) { removedIndex = index; break; }
        }

        if (removedIndex < 0) { return; }

        _pendingRemovedLensId = removedId;
        _pendingEscapeGuard = escapeGuard;
        _pendingTargetLensId = NeighborFocus.TryGetNeighborAfterRemove(
            lenses, removedIndex, lens => _removeRefs.ContainsKey(lens.Id), out var neighbor) ?
                neighbor.Id :
                null;
    }

    private void HandleKeyDown(KeyboardEventArgs args)
    {
        if (args.Key != "Escape") { return; }

        var lenses = LensSource.Lenses;

        if (lenses.IsEmpty) { return; }

        // Escape removes the LAST lens wherever focus sits; arm the previous chip but restore it only if the removal
        // orphaned focus (the guard), so Escape pressed from the region or an action button never steals focus.
        FilterLensId removedId = lenses[^1].Id;
        ArmNeighborFocus(removedId, escapeGuard: true);
        Commands.RemoveLens(removedId);
    }

    // Drops refs for chips no longer rendered: captures never re-run, so a stale entry would survive TryGetValue and
    // throw on focus instead of falling through. Pruned against the set captured at render time (the live source can
    // lag the render).
    private void PruneRemoveRefs()
    {
        if (_removeRefs.Count == 0) { return; }

        List<FilterLensId> stale = [.. _removeRefs.Keys.Where(id => !_renderedLensIds.Contains(id))];

        foreach (var id in stale) { _removeRefs.Remove(id); }
    }

    private void RemoveLensWithFocus(FilterLensId lensId)
    {
        // The clicked chip's button holds focus and is about to unmount, so focus is provably orphaned - restore the
        // neighbor directly (no guard needed on this path).
        ArmNeighborFocus(lensId, escapeGuard: false);
        Commands.RemoveLens(lensId);
    }

    // Escape removes the last lens wherever focus sits; restore the previous chip ONLY when focus fell to the document
    // root. If the guard module is unavailable, do NOT restore - focus may rest on the surviving region or an action
    // button, and an unconditional restore would steal it.
    private async ValueTask RestoreFocusIfOrphanedAsync(ElementReference target)
    {
        try
        {
            _focusModule ??= await JSRuntime.InvokeAsync<IJSObjectReference>(
                "import",
                "./_content/EventLogExpert.UI/Common/focusGuard.js");

            await _focusModule.InvokeAsync<bool>("focusIfNotElsewhere", target, true);
        }
        catch (JSDisconnectedException) { }
        catch (JSException) { }
        catch (ObjectDisposedException) { }
        catch (TaskCanceledException) { }
    }

    private void SaveAll() => Commands.PromoteAllLenses();

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

        // Save and clear (Secondary) defers the clear to the persist-success effect, so a failed save leaves the active
        // lenses intact instead of discarding them.
        Commands.SaveLensesAsGroup(name, clearAfterSave: outcome.Choice == PromptChoice.Secondary);
    }
}
