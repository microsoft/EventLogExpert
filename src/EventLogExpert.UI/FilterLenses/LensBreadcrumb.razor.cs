// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Localization;
using EventLogExpert.Runtime.Alerts;
using EventLogExpert.Runtime.FilterLenses;
using EventLogExpert.Runtime.Settings;
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

    [Inject] private ISettingsService Settings { get; init; } = null!;

    protected override async ValueTask DisposeAsyncCore(bool disposing)
    {
        Settings.TimeZoneChanged -= OnTimeZoneChanged;

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
        Settings.TimeZoneChanged += OnTimeZoneChanged;

        base.OnInitialized();
    }

    // Arm the surviving neighbor chip (or the filters-pane fallback when none survives) to focus once a single-lens
    // removal propagates; run before the synchronous Commands dispatch. Every neighbor-chip restore is guarded: a
    // normal click/keyboard removal orphans focus to the body so the guard restores, while an assistive-tech or
    // synthetic activation that left focus elsewhere declines rather than yanking it to the neighbor.
    private void ArmChipRemovalFocus(FilterLensId removedId, bool keepButtonTarget)
    {
        var lenses = LensSource.Lenses;

        int removedIndex = -1;

        for (int index = 0; index < lenses.Count; index++)
        {
            if (lenses[index].Id == removedId) { removedIndex = index; break; }
        }

        if (removedIndex < 0) { return; }

        var refs = keepButtonTarget ? _keepRefs : _removeRefs;

        FilterLensId? targetId = NeighborFocus.TryGetNeighborAfterRemove(
            lenses, removedIndex, lens => refs.ContainsKey(lens.Id), out var neighbor) ?
                neighbor.Id : null;

        _pendingRestore = new PendingFocusRestore(
            RemovedId: removedId,
            TargetId: targetId,
            TargetIsKeepButton: keepButtonTarget);
    }

    // Save all / Clear all unmount the whole region; no removed id, so the arm consumes when the list empties and takes
    // the always-guarded pane fallback.
    private void ArmSynchronousBulkFocus() =>
        _pendingRestore = new PendingFocusRestore(
            RemovedId: null, TargetId: null, TargetIsKeepButton: false);

    private void ClearLensesWithFocus()
    {
        ArmSynchronousBulkFocus();
        Commands.ClearLenses();
    }

    // Escape while a chip's keep button holds focus removes the last lens and restores to the neighbor KEEP button, so
    // keeping lenses in sequence never lands focus on a delete control. stopPropagation (set in the markup) keeps the
    // container's HandleKeyDown from also firing and removing a second lens.
    private void HandleKeepKeyDown(KeyboardEventArgs args)
    {
        if (args.Key != "Escape") { return; }

        RemoveLastLensWithFocus(keepButtonTarget: true);
    }

    // Escape anywhere in the region removes the last lens. From the region, label, or an action button the focus
    // origin is a non-chip control, so the restore targets the neighbor remove button (the keep button has its own
    // handler for symmetry - see HandleKeepKeyDown).
    private void HandleKeyDown(KeyboardEventArgs args)
    {
        if (args.Key != "Escape") { return; }

        RemoveLastLensWithFocus(keepButtonTarget: false);
    }

    private ValueTask<IJSObjectReference> ImportFocusModuleAsync() =>
        JSRuntime.InvokeAsync<IJSObjectReference>("import", "./_content/EventLogExpert.UI/Common/focusGuard.js");

    // Time-window chip labels are stored in UTC and formatted in the display zone, so re-render on a zone change.
    private void OnTimeZoneChanged(object? sender, TimeZoneInfo timeZone) => RequestGuardedRender(StateHasChanged);

    private void PromoteLensWithFocus(FilterLensId lensId)
    {
        // Keep promotes and removes this chip; target the neighbor's KEEP button so the next Enter never lands on a
        // delete control.
        ArmChipRemovalFocus(lensId, keepButtonTarget: true);
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

    private void RemoveLastLensWithFocus(bool keepButtonTarget)
    {
        var lenses = LensSource.Lenses;

        if (lenses.IsEmpty) { return; }

        FilterLensId removedId = lenses[^1].Id;
        ArmChipRemovalFocus(removedId, keepButtonTarget);
        Commands.RemoveLens(removedId);
    }

    private void RemoveLensWithFocus(FilterLensId lensId)
    {
        ArmChipRemovalFocus(lensId, keepButtonTarget: false);
        Commands.RemoveLens(lensId);
    }

    // Guarded filters-pane restore (every pane fallback): focus may rest on a survivor or have moved during the module
    // import, so decline rather than steal.
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

    // Guarded neighbor-chip restore for every chip target (Escape, keep, remove): if the module is unavailable, do NOT
    // restore (focus may rest on a survivor).
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
            // Guarded (never a bare focus): a normal removal orphans focus to the body so the restore lands, while an
            // assistive-tech or synthetic activation that left focus elsewhere declines rather than stealing it.
            await RestoreFocusIfOrphanedAsync(targetRef);

            return;
        }

        // Sole-lens / bulk / missing ref: fall back to the filters pane, NEVER the opposite-kind button (a keep removal
        // must not land on a delete). Always guarded: the pane is reachable only through the module, so an unguarded
        // restore buys no fail-open resilience and only risks stealing focus that moved during the import await.
        await RestoreFilterPaneFocusIfOrphanedAsync();
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
        bool TargetIsKeepButton);
}
