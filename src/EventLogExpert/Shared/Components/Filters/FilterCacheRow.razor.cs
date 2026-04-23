// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI;
using EventLogExpert.UI.Interfaces;
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
    private FilterEditorModel? _filter;

    [Parameter] public FilterModel Value { get; set; } = null!;

    [Inject] private IDispatcher Dispatcher { get; init; } = null!;

    [Inject] private IState<FilterCacheState> FilterCacheState { get; init; } = null!;

    [Inject] private IFilterService FilterService { get; init; } = null!;

    private List<string> Items => _cacheType switch
    {
        CacheType.Favorites => [.. FilterCacheState.Value.FavoriteFilters],
        CacheType.Recent => [.. FilterCacheState.Value.RecentFilters],
        _ => [],
    };

    protected override void OnParametersSet()
    {
        // Auto-create a draft when the row mounts in edit mode (e.g. AddCachedFilter dispatches
        // AddFilter with IsEditing=true). The `_filter is null` guard ensures we don't overwrite
        // an in-flight draft when the parent re-renders due to unrelated state changes.
        if (Value.IsEditing && _filter is null)
        {
            _filter = FilterEditorModel.FromFilterModel(Value);
        }

        base.OnParametersSet();
    }

    private void CancelFilter()
    {
        _filter = null;
        _errorMessage = string.Empty;

        // A new filter has no saved comparison string — Cancel removes it entirely. An existing
        // filter just exits edit mode; the saved Value is untouched because the draft was a copy.
        if (string.IsNullOrEmpty(Value.Comparison.Value))
        {
            Dispatcher.Dispatch(new FilterPaneAction.RemoveFilter(Value.Id));
        }
        else
        {
            Dispatcher.Dispatch(new FilterPaneAction.ToggleFilterEditing(Value.Id));
        }
    }

    private void EditFilter()
    {
        _filter = FilterEditorModel.FromFilterModel(Value);
        _errorMessage = string.Empty;

        Dispatcher.Dispatch(new FilterPaneAction.ToggleFilterEditing(Value.Id));
    }

    private void RemoveFilter() => Dispatcher.Dispatch(new FilterPaneAction.RemoveFilter(Value.Id));

    private void SaveFilter()
    {
        if (_filter is null) { return; }

        if (!FilterService.TryParseExpression(_filter.ComparisonText, out var message))
        {
            _errorMessage = message;
            return;
        }

        var newFilter = _filter.ToFilterModel() with
        {
            Comparison = new FilterComparison { Value = _filter.ComparisonText },
            IsEditing = false,
            IsEnabled = true
        };

        _filter = null;
        _errorMessage = string.Empty;

        Dispatcher.Dispatch(new FilterPaneAction.SetFilter(newFilter));
    }

    private void ToggleFilter() => Dispatcher.Dispatch(new FilterPaneAction.ToggleFilterEnabled(Value.Id));

    private void ToggleFilterExclusion() =>
        Dispatcher.Dispatch(new FilterPaneAction.ToggleFilterExcluded(Value.Id));
}
