// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Models;
using EventLogExpert.UI;
using EventLogExpert.UI.Models;
using EventLogExpert.UI.Store.FilterCache;
using EventLogExpert.UI.Store.FilterPane;
using Fluxor;
using Microsoft.AspNetCore.Components;
using System.Linq.Dynamic.Core;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.Shared.Components.Filters;

public partial class FilterCacheRow
{
    private CacheType _cacheType = CacheType.Favorites;
    private FilterCacheModel _filter = null!;
    private string _filterValue = null!;
    private bool _isEditing;
    private string? _errorMessage;

    [Parameter] public FilterCacheModel Value { get; set; } = null!;

    [Inject] private IDispatcher Dispatcher { get; set; } = null!;

    [Inject] private IState<FilterCacheState> FilterCacheState { get; set; } = null!;

    private List<string> Items => _cacheType switch
    {
        CacheType.Favorites => FilterCacheState.Value.FavoriteFilters.Select(x => x.ComparisonString).ToList(),
        CacheType.Recent => FilterCacheState.Value.RecentFilters.Select(x => x.ComparisonString).ToList(),
        _ => new List<string>(),
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

    private static bool TryParseExpression(string? expression, out string error)
    {
        error = string.Empty;

        if (string.IsNullOrEmpty(expression)) { return false; }

        try
        {
            var _ = new List<DisplayEventModel>().AsQueryable()
                .Where(EventLogExpertCustomTypeProvider.ParsingConfig, expression)
                .ToList();

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private void RemoveFilter()
    {
        // TODO: This is bugged and will not delete the cache entry unless the Value is in the filter list
        _isEditing = false;

        Dispatcher.Dispatch(new FilterPaneAction.RemoveCachedFilter(Value));
    }

    private void SaveFilter()
    {
        if (!TryParseExpression(_filterValue, out _errorMessage)) { return; }

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
