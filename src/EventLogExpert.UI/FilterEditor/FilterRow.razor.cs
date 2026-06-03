// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Drafts;
using EventLogExpert.Filtering.Evaluation;
using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Runtime.Alerts;
using EventLogExpert.Runtime.Announcement;
using EventLogExpert.Runtime.FilterLibrary;
using EventLogExpert.Runtime.FilterPane;
using EventLogExpert.UI.FilterEditor.Rows;
using Fluxor;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.UI.FilterEditor;

public sealed partial class FilterRow : FilterRowBase<SavedFilter?>
{
    private FilterRowShell? _shellRef;

    /// <summary>Notifies the parent which saved rows are mid-edit.</summary>
    [Parameter] public EventCallback<(FilterId Id, bool IsEditing)> OnEditingChanged { get; set; }

    /// <summary>Pending-row cancel: parent removes the draft (no dispatch).</summary>
    [Parameter] public EventCallback OnPendingDiscard { get; set; }

    /// <summary>Pending-row save: parent must remove the draft and dispatch the upsert atomically.</summary>
    [Parameter] public EventCallback<SavedFilter> OnPendingSave { get; set; }

    /// <summary>Saved-row remove: notifies parent BEFORE dispatch so focus restoration can capture pre-removal state.</summary>
    [Parameter] public EventCallback<FilterId> OnRemoved { get; set; }

    /// <summary>Mutually exclusive with <see cref="FilterRowBase{TValue}.Value" />.</summary>
    [Parameter] public FilterDraft? PendingDraft { get; set; }

    /// <summary>
    ///     Favourites listed first (flagged <see cref="CachedOption.IsFavorite" />), then previously-used filters minus
    ///     duplicates by case-insensitive <see cref="SavedFilter.ComparisonText" /> comparison. The dedupe key is
    ///     <see cref="SavedFilter.ComparisonText" /> ONLY (legacy UX): the cached quick-pick is a string-based shortcut where
    ///     Mode/IsExcluded come from the user's current <see cref="FilterDraft" /> at pick time, not from the source entry.
    ///     The library store itself dedupes by the richer <c>(ComparisonText, Mode, IsExcluded)</c> tuple — these are
    ///     different layers with different contracts. Excludes <see cref="LibraryEntryFilterSet" /> entries (filter sets have
    ///     no <c>Filter</c> property).
    /// </summary>
    internal List<CachedOption> CachedOptions
    {
        get
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<CachedOption>();

            var savedFilters = FilterLibraryState.Value.Entries.OfType<LibraryEntrySavedFilter>().ToList();

            foreach (var entry in savedFilters
                .Where(e => e.IsFavorite)
                .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (seen.Add(entry.Filter.ComparisonText))
                {
                    result.Add(new CachedOption(entry.Filter.ComparisonText, true));
                }
            }

            foreach (var entry in savedFilters
                .Where(e => !e.IsFavorite && e.LastUsedUtc is not null)
                .OrderByDescending(e => e.LastUsedUtc!.Value))
            {
                if (seen.Add(entry.Filter.ComparisonText))
                {
                    result.Add(new CachedOption(entry.Filter.ComparisonText, false));
                }
            }

            return result;
        }
    }

    internal bool IsEditing => Filter is not null;

    [Inject] private IAlertDialogService AlertDialogService { get; init; } = null!;

    [Inject] private IAnnouncementService AnnouncementService { get; init; } = null!;

    private string ErrorMessage { get; set; } = string.Empty;

    private FilterDraft? Filter { get; set; }

    [Inject] private IState<FilterLibraryState> FilterLibraryState { get; init; } = null!;

    [Inject] private IFilterPaneCommands FilterPaneCommands { get; init; } = null!;

    private bool IsPending => Value is null && PendingDraft is not null;

    internal ValueTask FocusEditAsync() =>
        _shellRef is null ? ValueTask.CompletedTask : _shellRef.FocusEditAsync();

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

    private async Task CancelFilter()
    {
        ResetEditSession();

        Filter = null;

        if (IsPending)
        {
            AnnouncementService.Announce("Filter discarded");
            await OnPendingDiscard.InvokeAsync();

            return;
        }

        if (Value is not { } savedFilter) { return; }

        AnnouncementService.Announce("Edit cancelled");
        await OnEditingChanged.InvokeAsync((savedFilter.Id, false));
    }

    private void DispatchRemoveFilter(FilterId id) => FilterPaneCommands.RemoveFilter(id);

    private void DispatchSetFilter(SavedFilter filter) => FilterPaneCommands.SetFilter(filter);

    private void DispatchToggleEnabled(FilterId id) => FilterPaneCommands.ToggleFilterEnabled(id);

    private async Task EditFilter()
    {
        if (IsPending) { return; }

        if (Value is not { } savedFilter) { return; }

        ResetEditSession();
        Filter = FilterDraft.FromSavedFilter(savedFilter);

        AnnouncementService.Announce("Editing filter");
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

    private async Task OnExclusionChangedAsync(bool isExcluded)
    {
        string label = isExcluded ? "Exclude" : "Include";

        if (Filter is not null)
        {
            AnnouncementService.Announce($"Filter set to {label}");
            Filter.IsExcluded = isExcluded;
            await InvokeAsync(StateHasChanged);

            return;
        }

        if (Value is { } savedFilter)
        {
            AnnouncementService.Announce($"Filter set to {label}");
            FilterPaneCommands.SetFilterExcluded(savedFilter.Id, isExcluded);
        }
    }

    private async Task RemoveFilter()
    {
        if (IsPending)
        {
            AnnouncementService.Announce("Filter discarded");
            await OnPendingDiscard.InvokeAsync();

            return;
        }

        if (Value is not { } savedFilter) { return; }

        AnnouncementService.Announce("Filter removed");
        await OnRemoved.InvokeAsync(savedFilter.Id);

        DispatchRemoveFilter(savedFilter.Id);
        await OnEditingChanged.InvokeAsync((savedFilter.Id, false));
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

        AnnouncementService.Announce("Filter saved");

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

        string newState = savedFilter.IsEnabled ? "disabled" : "enabled";

        AnnouncementService.Announce($"Filter {newState}");
        DispatchToggleEnabled(savedFilter.Id);
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
                    "Switching to Advanced will drop incomplete predicates from the Basic editor. Continue?",
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

        AnnouncementService.Announce($"Switched to {target} filter mode");
        Filter.ApplyModeSwitch(target);
        ErrorMessage = string.Empty;
    }

    internal sealed record CachedOption(string Value, bool IsFavorite);
}
