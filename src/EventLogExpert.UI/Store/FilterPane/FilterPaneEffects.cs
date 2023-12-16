// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;
using EventLogExpert.UI.Store.EventLog;
using EventLogExpert.UI.Store.FilterCache;
using Fluxor;
using System.Collections.Immutable;

namespace EventLogExpert.UI.Store.FilterPane;

public sealed class FilterPaneEffects(IState<FilterPaneState> filterPaneState)
{
    [EffectMethod(typeof(FilterPaneAction.AddCachedFilter))]
    public async Task HandleAddCachedFilter(IDispatcher dispatcher) =>
        await UpdateEventTableFiltersAsync(filterPaneState.Value, dispatcher);

    [EffectMethod]
    public async Task HandleAddFilter(FilterPaneAction.AddFilter action, IDispatcher dispatcher)
    {
        if (action.FilterModel?.ComparisonString is not null)
        {
            dispatcher.Dispatch(new FilterCacheAction.AddRecentFilter(
                new AdvancedFilterModel { ComparisonString = action.FilterModel.ComparisonString }));
        }

        await UpdateEventTableFiltersAsync(filterPaneState.Value, dispatcher);
    }

    [EffectMethod(typeof(FilterPaneAction.ClearAllFilters))]
    public async Task HandleClearAllFilters(IDispatcher dispatcher) =>
        await UpdateEventTableFiltersAsync(filterPaneState.Value, dispatcher);

    [EffectMethod(typeof(FilterPaneAction.RemoveCachedFilter))]
    public async Task HandleRemoveCachedFilter(IDispatcher dispatcher) =>
        await UpdateEventTableFiltersAsync(filterPaneState.Value, dispatcher);

    [EffectMethod(typeof(FilterPaneAction.RemoveFilter))]
    public async Task HandleRemoveFilter(IDispatcher dispatcher) =>
        await UpdateEventTableFiltersAsync(filterPaneState.Value, dispatcher);

    [EffectMethod]
    public async Task HandleSetAdvancedFilter(FilterPaneAction.SetAdvancedFilter action, IDispatcher dispatcher)
    {
        if (action.AdvancedFilterModel is not null)
        {
            dispatcher.Dispatch(new FilterCacheAction.AddRecentFilter(
                new AdvancedFilterModel { ComparisonString = action.AdvancedFilterModel.ComparisonString }));
        }

        await UpdateEventTableFiltersAsync(filterPaneState.Value, dispatcher);
    }

    [EffectMethod]
    public async Task HandleSetFilter(FilterPaneAction.SetFilter action, IDispatcher dispatcher)
    {
        if (!string.IsNullOrEmpty(action.FilterModel.ComparisonString))
        {
            dispatcher.Dispatch(new FilterCacheAction.AddRecentFilter(
                new AdvancedFilterModel { ComparisonString = action.FilterModel.ComparisonString }));
        }

        await UpdateEventTableFiltersAsync(filterPaneState.Value, dispatcher);
    }

    [EffectMethod(typeof(FilterPaneAction.SetFilterDateRange))]
    public async Task HandleSetFilterDateRange(IDispatcher dispatcher) =>
        await UpdateEventTableFiltersAsync(filterPaneState.Value, dispatcher);

    [EffectMethod(typeof(FilterPaneAction.ToggleAdvancedFilter))]
    public async Task HandleToggleAdvancedFilter(IDispatcher dispatcher) =>
        await UpdateEventTableFiltersAsync(filterPaneState.Value, dispatcher);

    [EffectMethod(typeof(FilterPaneAction.ToggleCachedFilter))]
    public async Task HandleToggleCachedFilter(IDispatcher dispatcher) =>
        await UpdateEventTableFiltersAsync(filterPaneState.Value, dispatcher);

    [EffectMethod(typeof(FilterPaneAction.ToggleEditFilter))]
    public async Task HandleToggleEditFilter(IDispatcher dispatcher) =>
        await UpdateEventTableFiltersAsync(filterPaneState.Value, dispatcher);

    [EffectMethod(typeof(FilterPaneAction.ToggleEnableFilter))]
    public async Task HandleToggleEnableFilter(IDispatcher dispatcher) =>
        await UpdateEventTableFiltersAsync(filterPaneState.Value, dispatcher);

    [EffectMethod(typeof(FilterPaneAction.ToggleFilterDate))]
    public async Task HandleToggleFilterDate(IDispatcher dispatcher) =>
        await UpdateEventTableFiltersAsync(filterPaneState.Value, dispatcher);

    private static EventFilter GetEventFilter(FilterPaneState filterPaneState) => new(
        filterPaneState.AdvancedFilter,
        filterPaneState.FilteredDateRange,
        filterPaneState.CachedFilters.Where(f => f.IsEnabled).ToImmutableList(),
        filterPaneState.CurrentFilters.Where(f => f.IsEnabled).ToImmutableList()
    );

    private static async Task UpdateEventTableFiltersAsync(FilterPaneState filterPaneState, IDispatcher dispatcher)
    {
        dispatcher.Dispatch(new FilterPaneAction.ToggleIsLoading());

        await Task.Run(() => dispatcher.Dispatch(new EventLogAction.SetFilters(GetEventFilter(filterPaneState))));

        dispatcher.Dispatch(new FilterPaneAction.ToggleIsLoading());
    }
}
