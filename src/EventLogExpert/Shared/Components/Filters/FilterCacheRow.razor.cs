// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Shared.Base;
using EventLogExpert.UI;
using EventLogExpert.UI.Interfaces;
using EventLogExpert.UI.Models;
using EventLogExpert.UI.Store.FilterCache;
using EventLogExpert.UI.Store.FilterPane;
using Fluxor;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.Shared.Components.Filters;

public sealed partial class FilterCacheRow : EditableFilterRowBase
{
    private CacheType _cacheType = CacheType.Favorites;
    private string? _errorMessage;

    [Inject] private IState<FilterCacheState> FilterCacheState { get; init; } = null!;

    [Inject] private IFilterService FilterService { get; init; } = null!;

    private List<string> Items =>
        _cacheType switch
        {
            CacheType.Favorites => [.. FilterCacheState.Value.FavoriteFilters],
            CacheType.Recent => [.. FilterCacheState.Value.RecentFilters],
            _ => [],
        };

    protected override void DispatchRemoveFilter()
    {
        if (Value is not { } savedFilter) { return; }

        Dispatcher.Dispatch(new FilterPaneAction.RemoveFilter(savedFilter.Id));
    }

    /// <summary>Clears the validation banner before the base mutates the draft.</summary>
    protected override void OnEditSessionResetting() => _errorMessage = string.Empty;

    private async Task SaveFilter()
    {
        if (Filter is null) { return; }

        if (!FilterService.TryParseExpression(Filter.ComparisonText, out var message))
        {
            _errorMessage = message;
            return;
        }

        var newFilter = Filter.ToFilterModel() with
        {
            Comparison = new FilterComparison { Value = Filter.ComparisonText },
            IsEnabled = true
        };

        Filter = null;
        _errorMessage = string.Empty;

        if (IsPending)
        {
            await CommitPendingAsync(newFilter);
            return;
        }

        Dispatcher.Dispatch(new FilterPaneAction.SetFilter(newFilter));

        await NotifyEditingEndedAsync();
    }

    private void ToggleFilter()
    {
        if (Value is not { } savedFilter) { return; }

        Dispatcher.Dispatch(new FilterPaneAction.ToggleFilterEnabled(savedFilter.Id));
    }

    private void ToggleFilterExclusion()
    {
        if (Value is not { } savedFilter) { return; }

        Dispatcher.Dispatch(new FilterPaneAction.ToggleFilterExcluded(savedFilter.Id));
    }
}
