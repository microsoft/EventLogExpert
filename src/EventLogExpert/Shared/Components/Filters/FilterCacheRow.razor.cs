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
    private AdvancedFilterModel _filter = null!;
    private string _filterValue = null!;
    private bool _isEditing;
    private string? _errorMessage;

    [Parameter] public AdvancedFilterModel Value { get; set; } = null!;

    [Inject] private IDispatcher Dispatcher { get; set; } = null!;

    [Inject] private IState<FilterCacheState> FilterCacheState { get; set; } = null!;

    private List<string> Items => _cacheType switch
    {
        CacheType.Favorites => FilterCacheState.Value.FavoriteFilters.Select(x => x.ComparisonString).ToList(),
        CacheType.Recent => FilterCacheState.Value.RecentFilters.Select(x => x.ComparisonString).ToList(),
        _ => [],
    };

    protected override void OnInitialized()
    {
        _filterValue = Value.ComparisonString;

        base.OnInitialized();
    }

    private void EditFilter()
    {
        _isEditing = true;
        _filter = Value with { };
    }

    private void RemoveFilter()
    {
        // TODO: This is bugged and will not delete the cache entry unless the Value is in the filter list
        _isEditing = false;

        Dispatcher.Dispatch(new FilterPaneAction.RemoveCachedFilter(Value));
    }

    private void SaveFilter()
    {
        if (!FilterMethods.TryParseExpression(_filterValue, out _errorMessage)) { return; }

        _isEditing = false;

        if (string.Equals(Value.ComparisonString, _filterValue, StringComparison.OrdinalIgnoreCase)) { return; }

        try
        {
            _filter.ComparisonString = _filterValue;

            Dispatcher.Dispatch(new FilterPaneAction.RemoveCachedFilter(Value));
            Dispatcher.Dispatch(new FilterCacheAction.AddRecentFilter(_filter));
            Dispatcher.Dispatch(new FilterPaneAction.AddCachedFilter(_filter));
        }
        catch (Exception ex)
        {
            _errorMessage = ex.Message;
        }
    }

    private void ToggleFilter() => Dispatcher.Dispatch(new FilterPaneAction.ToggleCachedFilter(Value));
}
