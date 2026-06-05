// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Drafts;
using EventLogExpert.Filtering.Evaluation;
using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Runtime.Alerts;
using EventLogExpert.Runtime.Announcement;
using EventLogExpert.UI.FilterEditor.Rows;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.UI.FilterEditor;

public sealed partial class FilterEditorCore : ComponentBase
{
    private FilterRowShell? _shellRef;

    [Parameter] public IReadOnlyList<CachedFilterOption>? CachedOptions { get; set; }

    [Parameter] public string Id { get; set; } = Guid.NewGuid().ToString();

    [Parameter] public EventCallback OnCancel { get; set; }

    [Parameter] public EventCallback OnEdit { get; set; }

    [Parameter] public EventCallback<bool> OnExclusionChanged { get; set; }

    [Parameter] public EventCallback OnPendingDiscard { get; set; }

    [Parameter] public EventCallback<SavedFilter> OnPendingSave { get; set; }

    [Parameter] public EventCallback OnRemove { get; set; }

    [Parameter] public EventCallback<SavedFilter> OnSave { get; set; }

    [Parameter] public EventCallback OnToggleEnabled { get; set; }

    [Parameter] public FilterDraft? PendingDraft { get; set; }

    [Parameter] public SavedFilter? Value { get; set; }

    internal bool IsEditing => Filter is not null;

    [Inject] private IAlertDialogService AlertDialogService { get; init; } = null!;

    [Inject] private IAnnouncementService AnnouncementService { get; init; } = null!;

    private string ErrorMessage { get; set; } = string.Empty;

    private FilterDraft? Filter { get; set; }

    private bool IsPending => Value is null && PendingDraft is not null;

    internal ValueTask FocusEditAsync() =>
        _shellRef?.FocusEditAsync() ?? ValueTask.CompletedTask;

    protected override void OnParametersSet()
    {
        if (Value is null && PendingDraft is null)
        {
            throw new InvalidOperationException(
                $"{nameof(FilterEditorCore)} requires either {nameof(Value)} (saved) or " +
                $"{nameof(PendingDraft)} (pending) to be set.");
        }

        if (Value is not null && PendingDraft is not null)
        {
            Filter = null;
        }

        if (IsPending)
        {
            Filter ??= PendingDraft;
        }

        base.OnParametersSet();
    }

    private static string GetFilterModeDisplayLabel(FilterMode mode) => mode switch
    {
        FilterMode.Cached => "Recent",
        _ => mode.ToString(),
    };

    private async Task CancelHandler()
    {
        ErrorMessage = string.Empty;
        Filter = null;

        if (IsPending)
        {
            AnnouncementService.Announce("Filter discarded");
            await OnPendingDiscard.InvokeAsync();

            return;
        }

        AnnouncementService.Announce("Edit cancelled");
        await OnCancel.InvokeAsync();
    }

    private async Task EditHandler()
    {
        if (IsPending) { return; }

        if (Value is not { } savedFilter) { return; }

        ErrorMessage = string.Empty;
        Filter = FilterDraft.FromSavedFilter(savedFilter);

        AnnouncementService.Announce("Editing filter");
        await OnEdit.InvokeAsync();
    }

    private async Task ExclusionHandler(bool isExcluded)
    {
        string label = isExcluded ? "Exclude" : "Include";

        if (Filter is not null)
        {
            AnnouncementService.Announce($"Filter set to {label}");
            Filter.IsExcluded = isExcluded;
            await InvokeAsync(StateHasChanged);

            return;
        }

        if (Value is not null)
        {
            AnnouncementService.Announce($"Filter set to {label}");
            await OnExclusionChanged.InvokeAsync(isExcluded);
        }
    }

    private IEnumerable<FilterMode> GetAvailableModes(FilterDraft filter)
    {
        foreach (var mode in Enum.GetValues<FilterMode>())
        {
            if (mode == FilterMode.Cached && CachedOptions is null && filter.Mode != FilterMode.Cached)
            {
                continue;
            }

            yield return mode;
        }
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

    private async Task RemoveHandler()
    {
        if (IsPending)
        {
            AnnouncementService.Announce("Filter discarded");
            await OnPendingDiscard.InvokeAsync();

            return;
        }

        if (Value is null) { return; }

        AnnouncementService.Announce("Filter removed");
        await OnRemove.InvokeAsync();
    }

    private async Task SaveHandler()
    {
        if (Filter is null) { return; }

        if (!Filter.TryBuildSavedFilter(out var saved, out string error))
        {
            ErrorMessage = error;

            return;
        }

        Filter = null;
        ErrorMessage = string.Empty;

        AnnouncementService.Announce("Filter saved");

        if (IsPending)
        {
            await OnPendingSave.InvokeAsync(saved);

            return;
        }

        await OnSave.InvokeAsync(saved);
    }

    private async Task ToggleEnabledHandler()
    {
        if (Value is not { } savedFilter) { return; }

        string newState = savedFilter.IsEnabled ? "disabled" : "enabled";
        AnnouncementService.Announce($"Filter {newState}");
        await OnToggleEnabled.InvokeAsync();
    }

    private async Task TryChangeModeAsync(FilterMode target)
    {
        if (Filter is null) { return; }

        if (Filter.Mode == target) { return; }

        if (Filter.WouldLoseDataSwitchingTo(target))
        {
            string message = (Filter.Mode, target) switch
            {
                (_, FilterMode.Cached) =>
                    "Switching to Recent will discard the current filter contents. Continue?",
                (FilterMode.Advanced, FilterMode.Basic) or (FilterMode.Cached, FilterMode.Basic) =>
                    "Switching to Basic will discard the current expression because it cannot be represented in the Basic editor. Continue?",
                (FilterMode.Basic, FilterMode.Advanced) =>
                    "Switching to Advanced will drop incomplete predicates from the Basic editor. Continue?",
                _ => "Switching modes will discard the current filter contents. Continue?",
            };

            bool accepted = await AlertDialogService.ShowAlert(
                "Switch Filter Mode",
                message,
                "Continue",
                "Cancel");

            if (!accepted)
            {
                StateHasChanged();

                return;
            }
        }

        AnnouncementService.Announce($"Switched to {GetFilterModeDisplayLabel(target)} filter mode");
        Filter.ApplyModeSwitch(target);
        ErrorMessage = string.Empty;
    }
}
