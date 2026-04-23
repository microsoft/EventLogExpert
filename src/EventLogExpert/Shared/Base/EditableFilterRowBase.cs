// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;
using EventLogExpert.UI.Store.FilterPane;
using Microsoft.AspNetCore.Components;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.Shared.Base;

/// <summary>
/// Adds the edit lifecycle for rows that bind to a saved <see cref="FilterModel"/> and need a
/// local <see cref="FilterEditorModel"/> draft while the user edits. Subclasses implement
/// <see cref="DispatchRemoveFilter"/> with the appropriate Fluxor action (FilterPaneAction or
/// FilterGroupAction, etc.) and may override <see cref="OnEditSessionResetting"/> to clear
/// transient UI state (validation messages, debounce timers) before the base mutates the draft
/// or invokes the editing-state callback.
/// </summary>
public abstract class EditableFilterRowBase : FilterRowBase<FilterModel>
{
    protected FilterEditorModel? Filter { get; set; }

    /// <summary>
    /// Bubbles up to the parent (FilterPane / FilterGroup) so it can track which rows are mid-edit
    /// without relying on <see cref="FilterModel.IsEditing"/> in Fluxor state.
    /// </summary>
    [Parameter] public EventCallback<(FilterId Id, bool IsEditing)> OnEditingChanged { get; set; }

    [Inject] protected IDispatcher Dispatcher { get; init; } = null!;

    protected override void OnParametersSet()
    {
        // Auto-create a draft when the row mounts in edit mode (e.g. AddBasicFilter dispatches
        // AddFilter with IsEditing=true). The `Filter is null` guard ensures we don't overwrite
        // an in-flight draft when the parent re-renders due to unrelated state changes.
        if (Value.IsEditing && Filter is null)
        {
            Filter = FilterEditorModel.FromFilterModel(Value);
        }

        base.OnParametersSet();
    }

    /// <summary>
    /// Hook for subclasses to reset transient UI state (validation messages, debounce CTS, etc.)
    /// before <see cref="EditFilter"/> or <see cref="CancelFilter"/> mutate the draft or bubble
    /// the editing-state change. Runs synchronously on the UI thread before any state change.
    /// </summary>
    protected virtual void OnEditSessionResetting() { }

    /// <summary>
    /// Subclasses dispatch the appropriate remove action (e.g. <see cref="FilterPaneAction.RemoveFilter"/>
    /// or <see cref="EventLogExpert.UI.Store.FilterGroup.FilterGroupAction.RemoveFilter"/>).
    /// </summary>
    protected abstract void DispatchRemoveFilter();

    protected async Task EditFilter()
    {
        OnEditSessionResetting();
        Filter = FilterEditorModel.FromFilterModel(Value);
        await OnEditingChanged.InvokeAsync((Value.Id, true));
    }

    protected async Task CancelFilter()
    {
        OnEditSessionResetting();
        Filter = null;

        // A new filter has no saved comparison string - Cancel removes it entirely. An existing
        // filter just exits edit mode locally; the saved Value is untouched because the draft
        // was a copy.
        if (string.IsNullOrEmpty(Value.Comparison.Value))
        {
            DispatchRemoveFilter();
        }

        await OnEditingChanged.InvokeAsync((Value.Id, false));
    }

    protected async Task RemoveFilter()
    {
        DispatchRemoveFilter();
        await OnEditingChanged.InvokeAsync((Value.Id, false));
    }

    /// <summary>
    /// Helper for subclass <c>SaveFilter</c> implementations to bubble the edit-end signal after
    /// dispatching their type-specific SetFilter action.
    /// </summary>
    protected Task BubbleSavedAsync() => OnEditingChanged.InvokeAsync((Value.Id, false));
}