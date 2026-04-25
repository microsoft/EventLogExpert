// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI;
using EventLogExpert.UI.Interfaces;
using EventLogExpert.UI.Models;
using EventLogExpert.UI.Services;
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

    protected string ErrorMessage { get; set; } = string.Empty;

    protected FilterEditorModel? Filter { get; set; }

    [Inject] protected IFilterService FilterService { get; init; } = null!;

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

    /// <summary>Subclasses dispatch the appropriate remove-filter action. Saved rows only.</summary>
    protected abstract void DispatchRemoveFilter();

    /// <summary>Subclasses dispatch the appropriate set-filter action (FilterPane, FilterGroup, etc.).</summary>
    protected abstract void DispatchSetFilter(FilterModel filter);

    /// <summary>Subclasses that expose an enable/disable toggle override this. Default is no-op.</summary>
    protected virtual void DispatchToggleEnabled(FilterId id) { }

    /// <summary>Subclasses dispatch toggle-exclusion. Saved rows only.</summary>
    protected abstract void DispatchToggleExclusion(FilterId id);

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

    /// <summary>Hook for subclasses to clear transient UI state before the draft changes. Base clears the error banner.</summary>
    protected virtual void OnEditSessionResetting() => ErrorMessage = string.Empty;

    /// <summary>Persist raw input into the draft and clear any stale error banner.</summary>
    protected void OnInputChanged(ChangeEventArgs eventArgs)
    {
        if (Filter is null) { return; }

        Filter.ComparisonText = eventArgs.Value as string ?? string.Empty;
        ErrorMessage = string.Empty;
    }

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
            // Pending → saved transition: the parent reused the same key (FilterId is preserved on save),
            // so Blazor rebinds Value but Blazor does NOT clear PendingDraft from the previous render.
            // Treat the saved value as authoritative and drop the stale draft reference.
            PendingDraft = null;
            Filter = null;
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

    protected async Task SaveFilter()
    {
        if (Filter is null) { return; }

        var newFilter = await TrySaveAsync(Filter);

        if (newFilter is null) { return; }

        Filter = null;
        ErrorMessage = string.Empty;

        if (IsPending)
        {
            await CommitPendingAsync(newFilter);
            return;
        }

        DispatchSetFilter(newFilter);
        await NotifyEditingEndedAsync();
    }

    protected void ToggleFilter()
    {
        if (Value is not { } savedFilter) { return; }

        DispatchToggleEnabled(savedFilter.Id);
    }

    protected void ToggleFilterExclusion()
    {
        if (Value is not { } savedFilter) { return; }

        DispatchToggleExclusion(savedFilter.Id);
    }

    /// <summary>
    ///     Validates the draft and produces the immutable <see cref="FilterModel" /> to dispatch. Returning
    ///     <see langword="null" /> aborts the save (subclass is responsible for surfacing the error). The default
    ///     implementation enforces non-empty text and compiles via <see cref="FilterCompiler.TryCompile" /> (the same compiler
    ///     used at filter-evaluation time).
    /// </summary>
    protected virtual ValueTask<FilterModel?> TrySaveAsync(FilterEditorModel draft)
    {
        if (string.IsNullOrWhiteSpace(draft.ComparisonText))
        {
            ErrorMessage = "Cannot save an empty filter";

            return ValueTask.FromResult<FilterModel?>(null);
        }

        if (!FilterCompiler.TryCompile(draft.ComparisonText, out var compiled, out var error))
        {
            ErrorMessage = error;

            return ValueTask.FromResult<FilterModel?>(null);
        }

        var result = new FilterModel
        {
            Id = draft.Id,
            Color = draft.Color,
            ComparisonText = draft.ComparisonText,
            Compiled = compiled,
            BasicSource = draft.FilterType == FilterType.Basic ? draft.ToBasicSource() : null,
            FilterType = draft.FilterType,
            IsEnabled = true,
            IsExcluded = draft.IsExcluded
        };

        return ValueTask.FromResult<FilterModel?>(result);
    }
}
