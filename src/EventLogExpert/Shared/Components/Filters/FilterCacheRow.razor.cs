// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI;
using EventLogExpert.UI.Models;
using EventLogExpert.UI.Store.FilterCache;
using EventLogExpert.UI.Store.FilterPane;
using Fluxor;
using Microsoft.AspNetCore.Components;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.Shared.Components.Filters;

public sealed partial class FilterCacheRow
{
    private CacheType _cacheType = CacheType.Favorites;
    private string? _errorMessage;
    private string _filterValue = string.Empty;

    [Parameter] public FilterModel Value { get; set; } = null!;

    [Inject] private IDispatcher Dispatcher { get; init; } = null!;

    [Inject] private IState<FilterCacheState> FilterCacheState { get; init; } = null!;

    private List<string> Items => _cacheType switch
    {
        CacheType.Favorites => [.. FilterCacheState.Value.FavoriteFilters],
        CacheType.Recent => [.. FilterCacheState.Value.RecentFilters],
        _ => [],
    };

    private void EditFilter()
    {
        _filterValue = Value.Comparison.Value;

        Dispatcher.Dispatch(new FilterPaneAction.ToggleFilterEditing(Value.Id));
    }

    private void RemoveFilter() => Dispatcher.Dispatch(new FilterPaneAction.RemoveFilter(Value.Id));

    private void SaveFilter()
    {
        if (!FilterMethods.TryParseExpression(_filterValue, out var message))
        {
            _errorMessage = message;
            return;
        }

        _errorMessage = string.Empty;

        FilterModel newModel = Value with
        {
            Comparison = new FilterComparison { Value = _filterValue },
            IsEditing = false,
            IsEnabled = true
        };

        Dispatcher.Dispatch(new FilterPaneAction.SetFilter(newModel));
    }

    private void ToggleFilter() => Dispatcher.Dispatch(new FilterPaneAction.ToggleFilterEnabled(Value.Id));

    private void ToggleFilterExclusion() =>
        Dispatcher.Dispatch(new FilterPaneAction.ToggleFilterExcluded(Value.Id));
}
