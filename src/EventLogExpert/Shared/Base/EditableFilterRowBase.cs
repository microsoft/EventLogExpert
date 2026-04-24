// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;
using Microsoft.AspNetCore.Components;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.Shared.Base;

/// <summary>
///     Base for filter rows that bind either to a saved <see cref="FilterModel" /> (via
///     <see cref="FilterRowBase{TValue}.Value" />) or to a parent-owned <see cref="FilterEditorModel" /> draft (via
///     <see cref="PendingDraft" />). Exactly one must be set.
/// </summary>
public abstract class EditableFilterRowBase : FilterRowBase<FilterModel?>
{
    /// <summary>Bubbled to the parent so it can track which saved rows are mid-edit.</summary>
    [Parameter] public EventCallback<(FilterId Id, bool IsEditing)> OnEditingChanged { get; set; }

    /// <summary>Pending-row cancel: parent removes the draft from its pending list (no dispatch).</summary>
    [Parameter] public EventCallback OnPendingDiscard { get; set; }

    /// <summary>Pending-row save: parent must remove the draft and dispatch the upsert atomically.</summary>
    [Parameter] public EventCallback<FilterModel> OnPendingSave { get; set; }

    /// <summary>
    ///     Parent-owned draft for a never-saved filter; mutually exclusive with
    ///     <see cref="FilterRowBase{TValue}.Value" />.
    /// </summary>
    [Parameter] public FilterEditorModel? PendingDraft { get; set; }

    [Inject] protected IDispatcher Dispatcher { get; init; } = null!;

    protected FilterEditorModel? Filter { get; set; }

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

        if (Value is not { } savedFilter) { return; }

        await OnEditingChanged.InvokeAsync((savedFilter.Id, false));
    }

    protected Task CommitPendingAsync(FilterModel filter) => OnPendingSave.InvokeAsync(filter);

    /// <summary>Subclasses dispatch the appropriate remove action. Saved rows only.</summary>
    protected abstract void DispatchRemoveFilter();

    protected async Task EditFilter()
    {
        if (IsPending) { return; }

        if (Value is not { } savedFilter) { return; }

        OnEditSessionResetting();
        Filter = FilterEditorModel.FromFilterModel(savedFilter);

        await OnEditingChanged.InvokeAsync((savedFilter.Id, true));
    }

    protected Task NotifyEditingEndedAsync() =>
        Value is { } savedFilter ? OnEditingChanged.InvokeAsync((savedFilter.Id, false)) : Task.CompletedTask;

    /// <summary>Hook for subclasses to clear transient UI state (validation, debounce CTS) before the draft changes.</summary>
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
            // Adopt once; later re-renders must not wipe in-flight edits.
            Filter ??= PendingDraft;
        }

        base.OnParametersSet();
    }

    protected async Task RemoveFilter()
    {
        if (IsPending)
        {
            await OnPendingDiscard.InvokeAsync();
            return;
        }

        if (Value is not { } savedFilter) { return; }

        DispatchRemoveFilter();

        await OnEditingChanged.InvokeAsync((savedFilter.Id, false));
    }
}
