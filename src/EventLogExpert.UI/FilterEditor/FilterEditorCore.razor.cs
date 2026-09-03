// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Drafts;
using EventLogExpert.Filtering.Evaluation;
using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Localization;
using EventLogExpert.Runtime.Alerts;
using EventLogExpert.Runtime.Announcement;
using EventLogExpert.UI.Common;
using EventLogExpert.UI.FilterEditor.Rows;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;

namespace EventLogExpert.UI.FilterEditor;

public sealed partial class FilterEditorCore : ComponentBase
{
    private readonly List<string> _selectedTags = [];

    private FilterRowShell? _shellRef;

    [Parameter] public IReadOnlyList<CachedFilterOption>? CachedOptions { get; set; }

    [Parameter] public string Id { get; set; } = ComponentId.NewUnique().Value;

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

    private IReadOnlyList<string> AvailableCachedTags =>
        CachedOptions is null ? [] : AvailableTags(CachedOptions);

    private string CachedEmptyHint =>
        _selectedTags.Count > 0 ?
            Localizer["FilterEditor_Recent_EmptyNoMatches"] :
            Localizer["FilterEditor_Recent_EmptyNoneAvailable"];

    private string ErrorMessage { get; set; } = string.Empty;

    private FilterDraft? Filter { get; set; }

    private bool IsPending => Value is null && PendingDraft is not null;

    [Inject] private IStringLocalizer<SharedResource> Localizer { get; init; } = null!;

    private IReadOnlyList<CachedFilterOption> VisibleCachedOptions =>
        FilterCachedByTags(CachedOptions ?? [], _selectedTags, Filter?.ComparisonText);

    internal static IReadOnlyList<string> AvailableTags(IReadOnlyList<CachedFilterOption> options) =>
        [.. options.SelectMany(o => o.Tags).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(t => t, StringComparer.OrdinalIgnoreCase)];

    internal static IReadOnlyList<CachedFilterOption> FilterCachedByTags(
        IReadOnlyList<CachedFilterOption> options,
        IReadOnlyList<string> selectedTags,
        string? currentSelection)
    {
        if (selectedTags.Count == 0) { return options; }

        return
        [
            .. options.Where(o =>
                selectedTags.All(t => o.Tags.Contains(t, StringComparer.OrdinalIgnoreCase))
                || (!string.IsNullOrEmpty(currentSelection)
                    && string.Equals(o.Value, currentSelection, StringComparison.OrdinalIgnoreCase)))
        ];
    }

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

        if (_selectedTags.Count > 0 && CachedOptions is not null)
        {
            var available = AvailableTags(CachedOptions);
            _selectedTags.RemoveAll(t => !available.Contains(t, StringComparer.OrdinalIgnoreCase));
        }

        base.OnParametersSet();
    }

    private async Task CancelHandler()
    {
        ErrorMessage = string.Empty;
        Filter = null;

        if (IsPending)
        {
            AnnouncementService.Announce(FilterEditorAnnouncements.FilterDiscarded(Localizer));
            await OnPendingDiscard.InvokeAsync();

            return;
        }

        AnnouncementService.Announce(FilterEditorAnnouncements.EditCancelled(Localizer));
    }

    // Local edit transition: never add OnEdit/OnCancel host callbacks (WebView2 render bug).
    private Task EditHandler()
    {
        if (IsPending || Value is not { } savedFilter) { return Task.CompletedTask; }

        ErrorMessage = string.Empty;
        Filter = FilterDraft.FromSavedFilter(savedFilter);

        AnnouncementService.Announce(FilterEditorAnnouncements.EditingFilter(Localizer));

        return Task.CompletedTask;
    }

    private async Task ExclusionHandler(bool isExcluded)
    {
        if (Filter is not null)
        {
            AnnouncementService.Announce(FilterEditorAnnouncements.FilterSetTo(Localizer, isExcluded));
            Filter.IsExcluded = isExcluded;
            await InvokeAsync(StateHasChanged);

            return;
        }

        if (Value is not null)
        {
            AnnouncementService.Announce(FilterEditorAnnouncements.FilterSetTo(Localizer, isExcluded));
            await OnExclusionChanged.InvokeAsync(isExcluded);
        }
    }

    private IEnumerable<FilterMode> GetAvailableModes(FilterDraft filter)
    {
        foreach (var mode in Enum.GetValues<FilterMode>())
        {
            if (mode == FilterMode.Cached && (CachedOptions is null or { Count: 0 }) && filter.Mode != FilterMode.Cached)
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

    private void OnSelectedTagsChanged(List<string> tags)
    {
        if (ReferenceEquals(tags, _selectedTags)) { return; }

        _selectedTags.Clear();
        _selectedTags.AddRange(tags);
    }

    private async Task RemoveHandler()
    {
        if (IsPending)
        {
            AnnouncementService.Announce(FilterEditorAnnouncements.FilterDiscarded(Localizer));
            await OnPendingDiscard.InvokeAsync();

            return;
        }

        if (Value is null) { return; }

        AnnouncementService.Announce(FilterEditorAnnouncements.FilterRemoved(Localizer));
        await OnRemove.InvokeAsync();
    }

    private async Task SaveHandler()
    {
        if (Filter is null) { return; }

        if (!Filter.TryBuildSavedFilter(out var saved, out var failure))
        {
            ErrorMessage = failure switch
            {
                FilterDraftBuildFailure.EmptyFilter => Localizer["FilterEditor_SaveError_EmptyFilter"],
                FilterDraftBuildFailure.InvalidBasicStructure => Localizer["FilterEditor_SaveError_IncompletePredicates"],
                FilterDraftBuildFailure.CompilerDiagnostic diagnostic => diagnostic.Message,
                _ => throw new ArgumentOutOfRangeException(nameof(failure), failure, null)
            };

            return;
        }

        Filter = null;
        ErrorMessage = string.Empty;

        AnnouncementService.Announce(FilterEditorAnnouncements.FilterSaved(Localizer));

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

        AnnouncementService.Announce(FilterEditorAnnouncements.FilterEnabledState(Localizer, !savedFilter.IsEnabled));
        await OnToggleEnabled.InvokeAsync();
    }

    private async Task TryChangeModeAsync(FilterMode target)
    {
        if (Filter is null) { return; }

        if (Filter.Mode == target) { return; }

        if (Filter.WouldLoseDataSwitchingTo(target))
        {
            string message = FilterEditorModeSwitchLocalizer.ConfirmationMessage(Localizer, Filter.Mode, target);

            bool accepted = await AlertDialogService.ShowAlert(
                Localizer["FilterEditor_ModeSwitch_Title"],
                message,
                Localizer["FilterEditor_Action_Continue"],
                Localizer["Modal_Cancel"]);

            if (!accepted)
            {
                StateHasChanged();

                return;
            }
        }

        AnnouncementService.Announce(FilterEditorAnnouncements.SwitchedToMode(Localizer, target));
        Filter.ApplyModeSwitch(target);
        ErrorMessage = string.Empty;
    }
}
