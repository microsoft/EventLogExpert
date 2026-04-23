// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;
using EventLogExpert.UI.Store.FilterPane;
using Microsoft.AspNetCore.Components;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.Shared.Base;

/// <summary>
///     Adds the edit lifecycle for rows that bind to a saved <see cref="FilterModel" /> and need a local
///     <see cref="FilterEditorModel" /> draft while the user edits, OR rows rendered as a parent- owned pending draft (no
///     saved <see cref="FilterModel" /> yet). Subclasses implement <see cref="DispatchRemoveFilter" /> with the
///     appropriate Fluxor action and may override <see cref="OnEditSessionResetting" /> to clear transient UI state
///     (validation messages, debounce timers) before the base mutates the draft or invokes the editing-state callback.
/// </summary>
/// <remarks>
///     Exactly one of <see cref="FilterRowBase{TValue}.Value" /> or <see cref="PendingDraft" /> must be supplied by
///     the parent. Saved rows use <c>Value</c> (and bubble <see cref="OnEditingChanged" /> so the parent can track in-edit
///     rows). Pending rows use <c>PendingDraft</c> with <see cref="OnPendingSave" /> and <see cref="OnPendingDiscard" />;
///     the parent owns the pending list directly so no editing-state bubbling is required.
/// </remarks>
public abstract class EditableFilterRowBase : FilterRowBase<FilterModel?>
{
    /// <summary>
    ///     Bubbles up to the parent (FilterPane / FilterGroup) for saved rows so it can track which rows are mid-edit
    ///     without relying on <see cref="FilterModel.IsEditing" /> in Fluxor state. Pending rows do not bubble this — the
    ///     parent already owns the pending list.
    /// </summary>
    [Parameter] public EventCallback<(FilterId Id, bool IsEditing)> OnEditingChanged { get; set; }

    /// <summary>
    ///     Invoked by pending rows when the user cancels. The parent removes the draft from its pending list. No store
    ///     dispatch occurs because the draft was never persisted.
    /// </summary>
    [Parameter] public EventCallback OnPendingDiscard { get; set; }

    /// <summary>
    ///     Invoked by pending rows when the user saves a valid draft. The parent is expected to remove the draft from its
    ///     pending list and dispatch the appropriate add/upsert action in a single synchronous block to avoid a duplicate-id
    ///     render between local and store state.
    /// </summary>
    [Parameter] public EventCallback<FilterModel> OnPendingSave { get; set; }

    /// <summary>
    ///     Parent-owned draft for a never-saved filter. When set, the row renders in edit mode continuously and
    ///     Save/Cancel route through the pending callbacks instead of dispatching store actions directly. Must be null when
    ///     <see cref="FilterRowBase{TValue}.Value" /> is set.
    /// </summary>
    [Parameter] public FilterEditorModel? PendingDraft { get; set; }

    [Inject] protected IDispatcher Dispatcher { get; init; } = null!;

    protected FilterEditorModel? Filter { get; set; }

    /// <summary>True when this row renders a parent-owned pending draft (no saved filter yet).</summary>
    protected bool IsPending => PendingDraft is not null;

    protected async Task CancelFilter()
    {
        OnEditSessionResetting();
        Filter = null;

        if (IsPending)
        {
            await OnPendingDiscard.InvokeAsync();
            return;
        }

        // Saved-row branch: the exactly-one invariant in OnParametersSet guarantees Value is set.
        // Pattern-bind to a non-nullable local so the rest of the method reads without `!`.
        if (Value is not { } savedFilter) { return; }

        // Legacy: a saved-but-empty placeholder (FilterModel.IsEditing set, never persisted)
        // should be removed entirely. Dies with IsEditing in 3e.3.
        if (string.IsNullOrEmpty(savedFilter.Comparison.Value))
        {
            DispatchRemoveFilter();
        }

        await OnEditingChanged.InvokeAsync((savedFilter.Id, false));
    }

    /// <summary>
    ///     Subclass save helper: for pending rows, hands the materialized <see cref="FilterModel" /> to the parent's
    ///     commit callback (which removes the draft from the pending list AND dispatches the store action atomically).
    ///     Subclasses call <see cref="NotifyEditingEndedAsync" /> instead for saved-edit rows.
    /// </summary>
    protected Task CommitPendingAsync(FilterModel filter) => OnPendingSave.InvokeAsync(filter);

    /// <summary>
    ///     Subclasses dispatch the appropriate remove action (e.g. <see cref="FilterPaneAction.RemoveFilter" /> or
    ///     <see cref="EventLogExpert.UI.Store.FilterGroup.FilterGroupAction.RemoveFilter" />). Only invoked for saved rows;
    ///     pending rows discard via <see cref="OnPendingDiscard" />.
    /// </summary>
    protected abstract void DispatchRemoveFilter();

    protected async Task EditFilter()
    {
        // Pending rows are always in edit mode and should not expose an Edit button.
        if (IsPending) { return; }

        if (Value is not { } savedFilter) { return; }

        OnEditSessionResetting();
        Filter = FilterEditorModel.FromFilterModel(savedFilter);
        await OnEditingChanged.InvokeAsync((savedFilter.Id, true));
    }

    /// <summary>
    ///     Helper for subclass <c>SaveFilter</c> implementations to notify the parent that the row's edit session has
    ///     ended after dispatching their type-specific SetFilter action. No-op for pending rows (the parent already knows
    ///     because it owns the pending list).
    /// </summary>
    protected Task NotifyEditingEndedAsync() =>
        Value is { } savedFilter ? OnEditingChanged.InvokeAsync((savedFilter.Id, false)) : Task.CompletedTask;

    /// <summary>
    ///     Hook for subclasses to reset transient UI state (validation messages, debounce CTS, etc.) before
    ///     <see cref="EditFilter" /> or <see cref="CancelFilter" /> mutate the draft or bubble the editing-state change. Runs
    ///     synchronously on the UI thread before any state change.
    /// </summary>
    protected virtual void OnEditSessionResetting() { }

    protected override void OnParametersSet()
    {
        if (Value is null && PendingDraft is null)
        {
            throw new InvalidOperationException(
                $"{nameof(EditableFilterRowBase)} requires either {nameof(Value)} (saved) or " +
                $"{nameof(PendingDraft)} (pending) to be set.");
        }

        if (Value is not null && PendingDraft is not null)
        {
            throw new InvalidOperationException(
                $"{nameof(EditableFilterRowBase)} cannot have both {nameof(Value)} and " +
                $"{nameof(PendingDraft)} set; choose one.");
        }

        if (IsPending)
        {
            // Pending row: the parent's draft IS the editor. Adopt once; re-renders triggered by
            // unrelated parent state changes must not wipe in-flight edits, hence the null-guard.
            Filter ??= PendingDraft;
        }
        else if (Value is { IsEditing: true } savedFilter && Filter is null)
        {
            // Legacy auto-edit path for FilterModel.IsEditing placeholders. After 3e.2 no UI path
            // creates such placeholders, but the branch stays alive until 3e.3 retires IsEditing.
            Filter = FilterEditorModel.FromFilterModel(savedFilter);
        }

        base.OnParametersSet();
    }

    protected async Task RemoveFilter()
    {
        if (IsPending)
        {
            // Pending UI doesn't expose Remove, but route to discard if it ever gets invoked.
            await OnPendingDiscard.InvokeAsync();
            return;
        }

        if (Value is not { } savedFilter) { return; }

        DispatchRemoveFilter();

        await OnEditingChanged.InvokeAsync((savedFilter.Id, false));
    }
}
