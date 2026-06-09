// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Drafts;
using EventLogExpert.Filtering.Evaluation;
using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Runtime.FilterLibrary;
using Microsoft.AspNetCore.Components;
using System.Collections.Immutable;

namespace EventLogExpert.UI.FilterLibrary;

public sealed partial class LibraryEntryFilterEditor : ComponentBase
{
    private readonly List<FilterDraft> _pendingDrafts = [];

    private string _filterListId = string.Empty;

    [Parameter][EditorRequired] public required LibraryEntryFilterSet FilterSet { get; set; }

    [Parameter] public bool IsExpanded { get; set; }

    [Inject] private IFilterLibraryCommands FilterLibraryCommands { get; init; } = null!;

    protected override void OnInitialized()
    {
        _filterListId = $"library-entry-filter-editor-{FilterSet.Id.Value:N}";
    }

    private void AddPendingDraft() => _pendingDrafts.Add(new FilterDraft { Mode = FilterMode.Advanced });

    private void DiscardPendingDraft(FilterDraft draft) => _pendingDrafts.Remove(draft);

    private void OnExclusionChangedForSaved(FilterId existingId, bool isExcluded)
    {
        var newFilters = FilterSet.Filters
            .Select(f => f.Id.Equals(existingId) ? f with { IsExcluded = isExcluded } : f)
            .ToImmutableList();
        FilterLibraryCommands.SetFilterSetFilters(FilterSet.Id, newFilters);
    }

    private void OnRemoveSaved(FilterId existingId)
    {
        var newFilters = FilterSet.Filters.Where(f => !f.Id.Equals(existingId)).ToImmutableList();
        FilterLibraryCommands.SetFilterSetFilters(FilterSet.Id, newFilters);
    }

    private void OnSaveSavedFilter(FilterId existingId, SavedFilter updated)
    {
        var newFilters = FilterSet.Filters
            .Select(f => f.Id.Equals(existingId) ? updated with { Id = existingId } : f)
            .ToImmutableList();
        FilterLibraryCommands.SetFilterSetFilters(FilterSet.Id, newFilters);
    }

    private void OnToggleEnabledForSaved(FilterId existingId)
    {
        var newFilters = FilterSet.Filters
            .Select(f => f.Id.Equals(existingId) ? f with { IsEnabled = !f.IsEnabled } : f)
            .ToImmutableList();
        FilterLibraryCommands.SetFilterSetFilters(FilterSet.Id, newFilters);
    }

    private void SavePendingDraft(FilterDraft draft, SavedFilter built)
    {
        var newFilters = FilterSet.Filters.Add(built with { Id = FilterId.Create() });
        FilterLibraryCommands.SetFilterSetFilters(FilterSet.Id, newFilters);
        _pendingDrafts.Remove(draft);
    }
}
