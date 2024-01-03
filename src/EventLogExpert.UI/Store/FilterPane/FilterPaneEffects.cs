// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;
using EventLogExpert.UI.Store.EventLog;
using EventLogExpert.UI.Store.FilterCache;
using EventLogExpert.UI.Store.FilterColor;
using Fluxor;
using System.Collections.Immutable;

namespace EventLogExpert.UI.Store.FilterPane;

public sealed class FilterPaneEffects(IState<FilterPaneState> filterPaneState)
{
    [EffectMethod]
    public async Task HandleAddCachedFilter(FilterPaneAction.AddCachedFilter action, IDispatcher dispatcher)
    {
        await UpdateEventTableFiltersAsync(filterPaneState.Value, dispatcher);

        dispatcher.Dispatch(new FilterColorAction.SetFilter(action.FilterModel));
    }

    [EffectMethod]
    public async Task HandleAddFilter(FilterPaneAction.AddFilter action, IDispatcher dispatcher)
    {
        await UpdateEventTableFiltersAsync(filterPaneState.Value, dispatcher);

        if (action.FilterModel?.Comparison.Value is not null)
        {
            dispatcher.Dispatch(
                new FilterCacheAction.AddRecentFilter(action.FilterModel.Comparison.Value));
        }

        if (action.FilterModel?.IsEnabled is true)
        {
            dispatcher.Dispatch(new FilterColorAction.SetFilter(action.FilterModel));
        }
    }

    [EffectMethod(typeof(FilterPaneAction.ClearAllFilters))]
    public async Task HandleClearAllFilters(IDispatcher dispatcher)
    {
        await UpdateEventTableFiltersAsync(filterPaneState.Value, dispatcher);

        dispatcher.Dispatch(new FilterColorAction.ClearAllFilters());
    }

    [EffectMethod]
    public async Task HandleRemoveCachedFilter(FilterPaneAction.RemoveCachedFilter action, IDispatcher dispatcher)
    {
        await UpdateEventTableFiltersAsync(filterPaneState.Value, dispatcher);

        dispatcher.Dispatch(new FilterColorAction.RemoveFilter(action.FilterModel.Id));
    }

    [EffectMethod]
    public async Task HandleRemoveFilter(FilterPaneAction.RemoveFilter action, IDispatcher dispatcher)
    {
        await UpdateEventTableFiltersAsync(filterPaneState.Value, dispatcher);

        dispatcher.Dispatch(new FilterColorAction.RemoveFilter(action.Id));
    }

    [EffectMethod]
    public Task HandleSetAdvancedFilter(FilterPaneAction.SetAdvancedFilter action, IDispatcher dispatcher)
    {
        // Kind of a hack to ensure we are able to remove the color filter if AdvancedFilter is set to null
        var filter = filterPaneState.Value.AdvancedFilter;

        dispatcher.Dispatch(new FilterPaneAction.SetAdvancedFilterCompleted(action.FilterModel));

        if (filter is not null && action.FilterModel is null)
        {
            dispatcher.Dispatch(new FilterColorAction.RemoveFilter(filter.Id));
        }
        else
        {
            dispatcher.Dispatch(new FilterColorAction.SetFilter(action.FilterModel!));
        }

        return Task.CompletedTask;
    }

    [EffectMethod]
    public async Task HandleSetAdvancedFilterSuccess(FilterPaneAction.SetAdvancedFilterCompleted action, IDispatcher dispatcher)
    {
        await UpdateEventTableFiltersAsync(filterPaneState.Value, dispatcher);

        if (action.FilterModel is not null)
        {
            dispatcher.Dispatch(new FilterCacheAction.AddRecentFilter(action.FilterModel.Comparison.Value));
        }
    }

    [EffectMethod]
    public async Task HandleSetFilter(FilterPaneAction.SetFilter action, IDispatcher dispatcher)
    {
        await UpdateEventTableFiltersAsync(filterPaneState.Value, dispatcher);

        if (!string.IsNullOrEmpty(action.FilterModel.Comparison.Value))
        {
            dispatcher.Dispatch(new FilterCacheAction.AddRecentFilter(action.FilterModel.Comparison.Value));
        }

        UpdateFilterColors(action.FilterModel, dispatcher);
    }

    [EffectMethod(typeof(FilterPaneAction.SetFilterDateRange))]
    public async Task HandleSetFilterDateRange(IDispatcher dispatcher) =>
        await UpdateEventTableFiltersAsync(filterPaneState.Value, dispatcher);

    [EffectMethod(typeof(FilterPaneAction.ToggleAdvancedFilter))]
    public async Task HandleToggleAdvancedFilter(IDispatcher dispatcher)
    {
        await UpdateEventTableFiltersAsync(filterPaneState.Value, dispatcher);

        if (filterPaneState.Value.AdvancedFilter is null) { return; }

        UpdateFilterColors(filterPaneState.Value.AdvancedFilter, dispatcher);
    }

    [EffectMethod]
    public async Task HandleToggleCachedFilter(FilterPaneAction.ToggleCachedFilter action, IDispatcher dispatcher)
    {
        await UpdateEventTableFiltersAsync(filterPaneState.Value, dispatcher);

        UpdateFilterColors(action.FilterModel, dispatcher);
    }

    [EffectMethod(typeof(FilterPaneAction.ToggleEditFilter))]
    public async Task HandleToggleEditFilter(IDispatcher dispatcher) =>
        await UpdateEventTableFiltersAsync(filterPaneState.Value, dispatcher);

    [EffectMethod]
    public async Task HandleToggleEnableFilter(FilterPaneAction.ToggleEnableFilter action, IDispatcher dispatcher)
    {
        var filter = filterPaneState.Value.CurrentFilters.FirstOrDefault(x => x.Id.Equals(action.Id));

        if (filter?.IsEnabled is true)
        {
            dispatcher.Dispatch(new FilterColorAction.SetFilter(filter));
        }
        else
        {
            dispatcher.Dispatch(new FilterColorAction.RemoveFilter(action.Id));
        }

        await UpdateEventTableFiltersAsync(filterPaneState.Value, dispatcher);
    }

    [EffectMethod(typeof(FilterPaneAction.ToggleFilterDate))]
    public async Task HandleToggleFilterDate(IDispatcher dispatcher) =>
        await UpdateEventTableFiltersAsync(filterPaneState.Value, dispatcher);

    [EffectMethod(typeof(FilterPaneAction.ToggleIsEnabled))]
    public async Task HandleToggleIsEnabled(IDispatcher dispatcher) =>
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

        if (filterPaneState.IsEnabled)
        {
            await Task.Run(() => dispatcher.Dispatch(new EventLogAction.SetFilters(GetEventFilter(filterPaneState))));
        }
        else
        {
            // Keep date filtering but remove other filters
            await Task.Run(() => dispatcher.Dispatch(
                new EventLogAction.SetFilters(new EventFilter(null, filterPaneState.FilteredDateRange, [], []))));
        }

        dispatcher.Dispatch(new FilterPaneAction.ToggleIsLoading());
    }

    private static void UpdateFilterColors(FilterModel model, IDispatcher dispatcher)
    {
        if (model.IsEnabled)
        {
            dispatcher.Dispatch(new FilterColorAction.SetFilter(model));
        }
        else
        {
            dispatcher.Dispatch(new FilterColorAction.RemoveFilter(model.Id));
        }
    }
}
