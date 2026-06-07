// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Drafts;
using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Runtime.FilterLibrary;
using EventLogExpert.Runtime.FilterPane;
using Fluxor;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.UI.FilterEditor;

public sealed partial class FilterRow : FilterRowBase<SavedFilter?>, IDisposable
{
    private FilterEditorCore? _coreRef;

    [Parameter] public Action<FilterId>? OnDisposed { get; set; }

    [Parameter] public EventCallback<(FilterId Id, bool IsEditing)> OnEditingChanged { get; set; }

    [Parameter] public EventCallback OnPendingDiscard { get; set; }

    [Parameter] public EventCallback<SavedFilter> OnPendingSave { get; set; }

    [Parameter] public EventCallback<FilterId> OnRemoved { get; set; }

    [Parameter] public FilterDraft? PendingDraft { get; set; }

    internal IReadOnlyList<CachedFilterOption> CachedOptions
    {
        get
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<CachedFilterOption>();

            var savedFilters = FilterLibraryState.Value.Entries.OfType<LibraryEntrySavedFilter>().ToList();

            foreach (var entry in savedFilters
                .Where(e => e.IsFavorite)
                .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (seen.Add(entry.Filter.ComparisonText))
                {
                    result.Add(new CachedFilterOption(entry.Filter.ComparisonText, true));
                }
            }

            foreach (var entry in savedFilters
                .Where(e => e is { IsFavorite: false, LastUsedUtc: not null })
                .OrderByDescending(e => e.LastUsedUtc!.Value))
            {
                if (seen.Add(entry.Filter.ComparisonText))
                {
                    result.Add(new CachedFilterOption(entry.Filter.ComparisonText, false));
                }
            }

            return result;
        }
    }

    internal bool IsEditing => _coreRef?.IsEditing ?? false;

    [Inject] private IState<FilterLibraryState> FilterLibraryState { get; init; } = null!;

    [Inject] private IFilterPaneCommands FilterPaneCommands { get; init; } = null!;

    public void Dispose()
    {
        if (Value is { } filter) { OnDisposed?.Invoke(filter.Id); }
    }

    internal ValueTask FocusEditAsync() => _coreRef?.FocusEditAsync() ?? ValueTask.CompletedTask;

    private Task OnCancelFromCore()
    {
        if (Value is { } savedFilter)
        {
            return OnEditingChanged.InvokeAsync((savedFilter.Id, false));
        }

        return Task.CompletedTask;
    }

    private Task OnEditFromCore()
    {
        if (Value is { } savedFilter)
        {
            return OnEditingChanged.InvokeAsync((savedFilter.Id, true));
        }

        return Task.CompletedTask;
    }

    private Task OnExclusionChangedFromCore(bool isExcluded)
    {
        if (Value is not { } savedFilter) { return Task.CompletedTask; }

        FilterPaneCommands.SetFilterExcluded(savedFilter.Id, isExcluded);

        return Task.CompletedTask;
    }

    private Task OnPendingDiscardFromCore() => OnPendingDiscard.InvokeAsync();

    private Task OnPendingSaveFromCore(SavedFilter saved) => OnPendingSave.InvokeAsync(saved);

    private async Task OnRemoveFromCore()
    {
        if (Value is not { } savedFilter) { return; }

        await OnRemoved.InvokeAsync(savedFilter.Id);

        FilterPaneCommands.RemoveFilter(savedFilter.Id);

        await OnEditingChanged.InvokeAsync((savedFilter.Id, false));
    }

    private Task OnSaveFromCore(SavedFilter saved)
    {
        FilterPaneCommands.SetFilter(saved);

        if (Value is { } savedFilter)
        {
            return OnEditingChanged.InvokeAsync((savedFilter.Id, false));
        }

        return Task.CompletedTask;
    }

    private Task OnToggleEnabledFromCore()
    {
        if (Value is not { } savedFilter) { return Task.CompletedTask; }

        FilterPaneCommands.ToggleFilterEnabled(savedFilter.Id);

        return Task.CompletedTask;
    }
}
