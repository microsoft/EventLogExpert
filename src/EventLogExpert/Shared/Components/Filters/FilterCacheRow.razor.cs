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
    private FilterModel _filter = null!;
    private string _filterValue = null!;
    private bool _isEditing;
    private string? _errorMessage;

    [Parameter] public FilterModel Value { get; set; } = null!;

    [Inject] private IDispatcher Dispatcher { get; init; } = null!;

    [Inject] private IState<FilterCacheState> FilterCacheState { get; init; } = null!;

    private List<string> Items => _cacheType switch
    {
        CacheType.Favorites => [.. FilterCacheState.Value.FavoriteFilters],
        CacheType.Recent => [.. FilterCacheState.Value.RecentFilters],
        _ => [],
    };

    protected override void OnInitialized()
    {
        _filterValue = Value.Comparison.Value;

        base.OnInitialized();
    }

    private void EditFilter()
    {
        _isEditing = true;
        _filter = Value with { };
    }

    private void RemoveFilter()
    {
        _isEditing = false;

        Dispatcher.Dispatch(new FilterPaneAction.RemoveCachedFilter(Value.Id));
    }

    private void SaveFilter()
    {
        if (!FilterMethods.TryParseExpression(_filterValue, out _errorMessage)) { return; }

        _isEditing = false;

        if (string.Equals(Value.Comparison.Value, _filterValue, StringComparison.OrdinalIgnoreCase)) { return; }

        try
        {
            _filter.Comparison.Value = _filterValue;

            Dispatcher.Dispatch(new FilterPaneAction.RemoveCachedFilter(Value.Id));
            Dispatcher.Dispatch(new FilterCacheAction.AddRecentFilter(_filterValue));
            Dispatcher.Dispatch(new FilterPaneAction.AddCachedFilter(_filter));
        }
        catch (Exception ex)
        {
            _errorMessage = ex.Message;
        }
    }

    private void ToggleFilter() => Dispatcher.Dispatch(new FilterPaneAction.ToggleCachedFilter(Value.Id));
}
