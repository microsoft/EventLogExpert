// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Evaluation;
using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Runtime.Alerts;
using EventLogExpert.Runtime.FilterLibrary;
using Microsoft.AspNetCore.Components;
using System.Collections.Immutable;

namespace EventLogExpert.UI.FilterLibrary;

public sealed partial class LibraryEntryFilterEditor : ComponentBase
{
    private readonly List<FilterPendingDraft> _pendingDrafts = [];
    private readonly HashSet<FilterId> _removedIds = [];
    private readonly List<FilterRowDraft> _rowDrafts = [];

    private string _filterListId = string.Empty;
    private bool _isEditing;
    private bool _isExpanded;
    private LibraryEntryFilterSet? _lastSeenFilterSet;

    [Parameter][EditorRequired] public required LibraryEntryFilterSet FilterSet { get; set; }

    [Inject] private IAlertDialogService AlertDialogService { get; init; } = null!;

    [Inject] private IFilterLibraryCommands FilterLibraryCommands { get; init; } = null!;

    private bool HasUnsavedWork =>
        _removedIds.Count > 0
        || _pendingDrafts.Any(p => !string.IsNullOrWhiteSpace(p.ComparisonText))
        || _rowDrafts.Any(d => !_removedIds.Contains(d.Id) && d.IsDirty);

    protected override void OnInitialized()
    {
        _filterListId = $"library-entry-filter-editor-{FilterSet.Id.Value:N}";
        SyncDraftsFromFilterSet();
    }

    protected override void OnParametersSet()
    {
        if (!ReferenceEquals(_lastSeenFilterSet, FilterSet) && !_isEditing)
        {
            SyncDraftsFromFilterSet();
        }
    }

    private void AddPendingDraft() => _pendingDrafts.Add(new FilterPendingDraft());

    private Task CancelAsync()
    {
        CancelInternal();
        return Task.CompletedTask;
    }

    private void CancelInternal()
    {
        SyncDraftsFromFilterSet();
        _isEditing = false;
    }

    private void DiscardPendingDraft(FilterPendingDraft draft) => _pendingDrafts.Remove(draft);

    private void EnterEditMode() => _isEditing = true;

    private void MarkRowForRemoval(FilterId filterId) => _removedIds.Add(filterId);

    private async Task SaveAsync()
    {
        var newFilters = ImmutableList.CreateBuilder<SavedFilter>();

        foreach (var draft in _rowDrafts)
        {
            if (_removedIds.Contains(draft.Id)) { continue; }

            var trimmed = draft.ComparisonText?.Trim() ?? string.Empty;

            if (trimmed.Length == 0) { continue; }

            var created = SavedFilter.TryCreate(
                trimmed,
                basicFilter: null,
                color: draft.Color,
                isExcluded: draft.IsExcluded,
                isEnabled: draft.IsEnabled,
                mode: draft.Mode);

            if (created is null) { continue; }

            newFilters.Add(created with { Id = draft.Id });
        }

        foreach (var pending in _pendingDrafts)
        {
            var trimmed = pending.ComparisonText?.Trim() ?? string.Empty;

            if (trimmed.Length == 0) { continue; }

            var created = SavedFilter.TryCreate(trimmed);

            if (created is null) { continue; }

            newFilters.Add(created);
        }

        var updated = FilterSet with { Filters = newFilters.ToImmutable() };

        FilterLibraryCommands.UpdateEntry(updated);

        _isEditing = false;

        await Task.CompletedTask;
    }

    private void SyncDraftsFromFilterSet()
    {
        _rowDrafts.Clear();
        _pendingDrafts.Clear();
        _removedIds.Clear();

        foreach (var filter in FilterSet.Filters)
        {
            _rowDrafts.Add(new FilterRowDraft
            {
                Id = filter.Id,
                Color = filter.Color,
                Mode = filter.Mode,
                IsExcluded = filter.IsExcluded,
                IsEnabled = filter.IsEnabled,
                OriginalComparisonText = filter.ComparisonText,
                ComparisonText = filter.ComparisonText,
            });
        }

        _lastSeenFilterSet = FilterSet;
    }

    private async Task ToggleExpandAsync()
    {
        if (_isExpanded && HasUnsavedWork)
        {
            var confirmed = await AlertDialogService.ShowAlert(
                "Discard changes?",
                "Collapsing will discard unsaved edits to this filter set.",
                "Discard",
                "Cancel");

            if (!confirmed) { return; }

            CancelInternal();
        }

        _isExpanded = !_isExpanded;
    }

    private sealed class FilterPendingDraft
    {
        public string ComparisonText { get; set; } = string.Empty;
    }

    private sealed class FilterRowDraft
    {
        public required HighlightColor Color { get; init; }

        public string ComparisonText { get; set; } = string.Empty;

        public required FilterId Id { get; init; }

        public bool IsDirty => !string.Equals(OriginalComparisonText, ComparisonText, StringComparison.Ordinal);

        public required bool IsEnabled { get; init; }

        public required bool IsExcluded { get; init; }

        public required FilterMode Mode { get; init; }

        public required string OriginalComparisonText { get; init; }
    }
}
