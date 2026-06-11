// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Drafts;
using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Runtime.FilterLibrary;
using EventLogExpert.Runtime.FilterPane;
using Fluxor;
using Microsoft.AspNetCore.Components;
using System.Collections.Immutable;

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
            var eligible = FilterLibraryState.Value.Entries
                .OfType<LibraryEntrySavedFilter>()
                .Where(e => e.IsFavorite || e.LastUsedUtc is not null);

            var favorites = new List<(string Value, string SortName, ImmutableList<string> Tags)>();
            var recents = new List<(string Value, DateTimeOffset LastUsed, ImmutableList<string> Tags)>();

            foreach (var group in eligible.GroupBy(e => e.Filter.ComparisonText, StringComparer.OrdinalIgnoreCase))
            {
                var members = group.ToList();

                var tags = members
                    .SelectMany(e => e.Tags)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
                    .ToImmutableList();

                var favoriteNames = members
                    .Where(e => e.IsFavorite)
                    .Select(e => e.Name)
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (favoriteNames.Count > 0)
                {
                    favorites.Add((group.Key, favoriteNames[0], tags));
                }
                else
                {
                    recents.Add((group.Key, members.Max(e => e.LastUsedUtc!.Value), tags));
                }
            }

            var result = new List<CachedFilterOption>(favorites.Count + recents.Count);

            result.AddRange(favorites
                .OrderBy(f => f.SortName, StringComparer.OrdinalIgnoreCase)
                .Select(f => new CachedFilterOption(f.Value, true, f.Tags)));

            result.AddRange(recents
                .OrderByDescending(r => r.LastUsed)
                .Select(r => new CachedFilterOption(r.Value, false, r.Tags)));

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
