// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Components.Filters.Base;
using EventLogExpert.UI.Alerts;
using EventLogExpert.UI.Filter;
using EventLogExpert.UI.FilterCache;
using Fluxor;
using Microsoft.AspNetCore.Components;
using GroupActions = EventLogExpert.UI.FilterGroup;
using IDispatcher = Fluxor.IDispatcher;
using PaneActions = EventLogExpert.UI.FilterPane;

namespace EventLogExpert.Components.Filters;

/// <summary>
///     Single editor row that hosts every authoring mode (Basic, Advanced, Cached) behind a Mode dropdown. Mode
///     switches go through <see cref="FilterDraft.WouldLoseDataSwitchingTo" /> + a confirm dialog before
///     <see cref="FilterDraft.ApplyModeSwitch" /> mutates the draft, so a destructive switch can be cancelled without
///     leaking state. Save delegates to <see cref="FilterDraft.TryBuildSavedFilter" /> so the per-mode validation lives
///     next to the data and is unit-testable.
/// </summary>
public sealed partial class FilterRow : FilterRowBase<SavedFilter?>
{
    /// <summary>Notifies the parent which saved rows are mid-edit.</summary>
    [Parameter] public EventCallback<(FilterId Id, bool IsEditing)> OnEditingChanged { get; set; }

    /// <summary>Pending-row cancel: parent removes the draft (no dispatch).</summary>
    [Parameter] public EventCallback OnPendingDiscard { get; set; }

    /// <summary>Pending-row save: parent must remove the draft and dispatch the upsert atomically.</summary>
    [Parameter] public EventCallback<SavedFilter> OnPendingSave { get; set; }

    /// <summary>
    ///     When set, dispatches route through <see cref="GroupActions" /> with this parent group id and the row adopts
    ///     the group-row chrome variants (no enable/disable toggle, inline error row, group CSS).
    /// </summary>
    [Parameter] public FilterGroupId? ParentFilterGroupId { get; set; }

    /// <summary>Mutually exclusive with <see cref="FilterRowBase{TValue}.Value" />.</summary>
    [Parameter] public FilterDraft? PendingDraft { get; set; }

    [Inject] private IAlertDialogService AlertDialogService { get; init; } = null!;

    /// <summary>
    ///     Favourites listed first (flagged <see cref="CachedOption.IsFavorite" />), then recents minus duplicates by
    ///     case-insensitive comparison. Provides a stable selection list for Cached rows even when the same expression has
    ///     been promoted from recents to favourites (or duplicated across the two buckets via separate code paths).
    /// </summary>
    private List<CachedOption> CachedOptions
    {
        get
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<CachedOption>();

            foreach (string favourite in FilterCacheState.Value.FavoriteFilters)
            {
                if (seen.Add(favourite)) { result.Add(new CachedOption(favourite, true)); }
            }

            foreach (string recent in FilterCacheState.Value.RecentFilters)
            {
                if (seen.Add(recent)) { result.Add(new CachedOption(recent, false)); }
            }

            return result;
        }
    }

    [Inject] private IDispatcher Dispatcher { get; init; } = null!;

    private string ErrorMessage { get; set; } = string.Empty;

    private FilterDraft? Filter { get; set; }

    [Inject] private IState<FilterCacheState> FilterCacheState { get; init; } = null!;

    private bool IsPending => Value is null && PendingDraft is not null;

    private string? OuterCssClass => ParentFilterGroupId is not null ? "filter-group-row" : null;

    private string? RightCssClass => ParentFilterGroupId is not null ? "justify-self-right" : null;

    private bool ShowToggleEnabled => ParentFilterGroupId is null;

    private bool UseInlineErrorRow => ParentFilterGroupId is not null;

    protected override void OnParametersSet()
    {
        if (Value is null && PendingDraft is null)
        {
            throw new InvalidOperationException(
                $"{nameof(FilterRow)} requires either {nameof(Value)} (saved) or " +
                $"{nameof(PendingDraft)} (pending) to be set.");
        }

        if (Value is not null && PendingDraft is not null)
        {
            // Pending → saved transition: the parent reused the same key (FilterId is preserved on save),
            // so Blazor rebinds Value but does NOT clear the prior PendingDraft parameter. Drop the local
            // draft state; IsPending now evaluates false because Value is set, so PendingDraft is ignored.
            Filter = null;
        }

        if (IsPending)
        {
            // Adopt once; later re-renders must not wipe in-flight edits.
            Filter ??= PendingDraft;
        }

        base.OnParametersSet();
    }

    private void AddSubFilter()
    {
        if (Filter is null) { return; }

        Filter.SubFilters.Add(new SubFilterDraft());
    }

    private async Task CancelFilter()
    {
        ResetEditSession();

        Filter = null;

        if (IsPending)
        {
            await OnPendingDiscard.InvokeAsync();

            return;
        }

        if (Value is not { } savedFilter) { return; }

        await OnEditingChanged.InvokeAsync((savedFilter.Id, false));
    }

    private void DispatchRemoveFilter(FilterId id)
    {
        if (ParentFilterGroupId is { } parentId)
        {
            Dispatcher.Dispatch(new GroupActions.RemoveFilterAction(parentId, id));
        }
        else
        {
            Dispatcher.Dispatch(new PaneActions.RemoveFilterAction(id));
        }
    }

    private void DispatchSetFilter(SavedFilter filter)
    {
        if (ParentFilterGroupId is { } parentId)
        {
            Dispatcher.Dispatch(new GroupActions.SetFilterAction(parentId, filter));
        }
        else
        {
            Dispatcher.Dispatch(new PaneActions.SetFilterAction(filter));
        }
    }

    private void DispatchToggleEnabled(FilterId id)
    {
        if (ParentFilterGroupId is not null) { return; }

        Dispatcher.Dispatch(new PaneActions.ToggleFilterEnabledAction(id));
    }

    private void DispatchToggleExclusion(FilterId id)
    {
        if (ParentFilterGroupId is { } parentId)
        {
            Dispatcher.Dispatch(new GroupActions.ToggleFilterExcludedAction(parentId, id));
        }
        else
        {
            Dispatcher.Dispatch(new PaneActions.ToggleFilterExcludedAction(id));
        }
    }

    private async Task EditFilter()
    {
        if (IsPending) { return; }

        if (Value is not { } savedFilter) { return; }

        ResetEditSession();
        Filter = FilterDraft.FromSavedFilter(savedFilter);

        await OnEditingChanged.InvokeAsync((savedFilter.Id, true));
    }

    private void OnAdvancedTextInput(ChangeEventArgs eventArgs)
    {
        if (Filter is null) { return; }

        Filter.ComparisonText = eventArgs.Value as string ?? string.Empty;
        ErrorMessage = string.Empty;
    }

    private void OnCachedSelectionChanged(string value)
    {
        if (Filter is null) { return; }

        Filter.ComparisonText = value ?? string.Empty;
        ErrorMessage = string.Empty;
    }

    private async Task RemoveFilter()
    {
        if (IsPending)
        {
            await OnPendingDiscard.InvokeAsync();

            return;
        }

        if (Value is not { } savedFilter) { return; }

        DispatchRemoveFilter(savedFilter.Id);
        await OnEditingChanged.InvokeAsync((savedFilter.Id, false));
    }

    private void RemoveSubFilter(FilterId subFilterId)
    {
        if (Filter is null) { return; }

        Filter.SubFilters.RemoveAll(subFilter => subFilter.Id == subFilterId);
    }

    private void ResetEditSession() => ErrorMessage = string.Empty;

    private async Task SaveFilter()
    {
        if (Filter is null) { return; }

        if (!Filter.TryBuildSavedFilter(out var saved, out string error))
        {
            ErrorMessage = error;

            return;
        }

        var newFilter = saved;

        Filter = null;
        ErrorMessage = string.Empty;

        if (IsPending)
        {
            await OnPendingSave.InvokeAsync(newFilter);

            return;
        }

        DispatchSetFilter(newFilter);

        if (Value is { } savedFilter)
        {
            await OnEditingChanged.InvokeAsync((savedFilter.Id, false));
        }
    }

    private void ToggleFilter()
    {
        if (Value is not { } savedFilter) { return; }

        DispatchToggleEnabled(savedFilter.Id);
    }

    private void ToggleFilterExclusion()
    {
        if (Value is not { } savedFilter) { return; }

        DispatchToggleExclusion(savedFilter.Id);
    }

    /// <summary>
    ///     Mode-switch orchestrator: gate destructive transitions with a confirm dialog (per
    ///     <see cref="FilterDraft.WouldLoseDataSwitchingTo" />) before mutating the draft. The dropdown is one-way bound from
    ///     <see cref="FilterDraft.Mode" />, so a cancelled prompt re-renders with the prior mode selected — the user sees no
    ///     state change.
    /// </summary>
    private async Task TryChangeModeAsync(FilterMode target)
    {
        if (Filter is null) { return; }

        if (Filter.Mode == target)
        {
            // Defensive: ValueSelect can fire ValueChanged with the current value during initial sync.
            return;
        }

        if (Filter.WouldLoseDataSwitchingTo(target))
        {
            string message = (Filter.Mode, target) switch
            {
                (_, FilterMode.Cached) =>
                    "Switching to Cached will discard the current filter contents. Continue?",
                (FilterMode.Advanced, FilterMode.Basic) or (FilterMode.Cached, FilterMode.Basic) =>
                    "Switching to Basic will discard the current expression because it cannot be represented in the Basic editor. Continue?",
                (FilterMode.Basic, FilterMode.Advanced) =>
                    "Switching to Advanced will drop incomplete sub-filters from the Basic editor. Continue?",
                _ => "Switching modes will discard the current filter contents. Continue?"
            };

            bool accepted = await AlertDialogService.ShowAlert(
                "Switch Filter Mode",
                message,
                "Continue",
                "Cancel");

            if (!accepted)
            {
                // Force a re-render so the ValueSelect resyncs its highlighted item to the unchanged Filter.Mode.
                StateHasChanged();

                return;
            }
        }

        Filter.ApplyModeSwitch(target);
        ErrorMessage = string.Empty;
    }

    private sealed record CachedOption(string Value, bool IsFavorite);
}
