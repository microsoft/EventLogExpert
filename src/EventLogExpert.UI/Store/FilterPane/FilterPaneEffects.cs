// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;
using EventLogExpert.UI.Store.EventLog;
using EventLogExpert.UI.Store.FilterCache;
using EventLogExpert.UI.Store.FilterColor;
using EventLogExpert.UI.Store.FilterGroup;
using Fluxor;
using System.Collections.Immutable;

namespace EventLogExpert.UI.Store.FilterPane;

public sealed class FilterPaneEffects(IState<FilterPaneState> filterPaneState)
{
    [EffectMethod]
    public async Task HandleAddAdvancedFilter(FilterPaneAction.AddAdvancedFilter action, IDispatcher dispatcher)
    {
        if (!string.IsNullOrEmpty(action.FilterModel?.Comparison.Value))
        {
            await UpdateEventTableFiltersAsync(filterPaneState.Value, dispatcher);
        }

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

    [EffectMethod]
    public async Task HandleAddBasicFilter(FilterPaneAction.AddBasicFilter action, IDispatcher dispatcher)
    {
        if (!string.IsNullOrEmpty(action.FilterModel?.Comparison.Value))
        {
            await UpdateEventTableFiltersAsync(filterPaneState.Value, dispatcher);
        }

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

    [EffectMethod]
    public async Task HandleAddCachedFilter(FilterPaneAction.AddCachedFilter action, IDispatcher dispatcher)
    {
        await UpdateEventTableFiltersAsync(filterPaneState.Value, dispatcher);

        dispatcher.Dispatch(new FilterColorAction.SetFilter(action.FilterModel));
    }

    [EffectMethod(typeof(FilterPaneAction.ApplyFilterGroup))]
    public async Task HandleApplyFilterGroup(IDispatcher dispatcher)
    {
        await UpdateEventTableFiltersAsync(filterPaneState.Value, dispatcher);

        dispatcher.Dispatch(new FilterColorAction.SetFilters(
            filterPaneState.Value.AdvancedFilters.Where(filter => filter is { IsEditing: false, IsEnabled: true })));
    }

    [EffectMethod(typeof(FilterPaneAction.ClearAllFilters))]
    public async Task HandleClearAllFilters(IDispatcher dispatcher)
    {
        await UpdateEventTableFiltersAsync(filterPaneState.Value, dispatcher);

        dispatcher.Dispatch(new FilterColorAction.ClearAllFilters());
    }

    [EffectMethod]
    public async Task HandleRemoveAdvancedFilter(FilterPaneAction.RemoveAdvancedFilter action, IDispatcher dispatcher)
    {
        await UpdateEventTableFiltersAsync(filterPaneState.Value, dispatcher);

        dispatcher.Dispatch(new FilterColorAction.RemoveFilter(action.Id));
    }

    [EffectMethod]
    public async Task HandleRemoveBasicFilter(FilterPaneAction.RemoveBasicFilter action, IDispatcher dispatcher)
    {
        await UpdateEventTableFiltersAsync(filterPaneState.Value, dispatcher);

        dispatcher.Dispatch(new FilterColorAction.RemoveFilter(action.Id));
    }

    [EffectMethod]
    public async Task HandleRemoveCachedFilter(FilterPaneAction.RemoveCachedFilter action, IDispatcher dispatcher)
    {
        await UpdateEventTableFiltersAsync(filterPaneState.Value, dispatcher);

        dispatcher.Dispatch(new FilterColorAction.RemoveFilter(action.Id));
    }

    [EffectMethod]
    public Task HandleSaveFilterGroup(FilterPaneAction.SaveFilterGroup action, IDispatcher dispatcher)
    {
        var filters = filterPaneState.Value.BasicFilters
            .Concat(filterPaneState.Value.CachedFilters)
            .Concat(filterPaneState.Value.AdvancedFilters);

        dispatcher.Dispatch(
            new FilterGroupAction.AddGroup(
                new FilterGroupModel
                {
                    Name = action.Name,
                    Filters = [.. filters.Select(filter =>
                        new FilterModel {
                            Color = filter.Color,
                            Comparison = filter.Comparison with { }
                        })]
                }));

        return Task.CompletedTask;
    }

    [EffectMethod]
    public async Task HandleSetAdvancedFilter(FilterPaneAction.SetAdvancedFilter action, IDispatcher dispatcher)
    {
        await UpdateEventTableFiltersAsync(filterPaneState.Value, dispatcher);

        if (!string.IsNullOrEmpty(action.FilterModel.Comparison.Value))
        {
            dispatcher.Dispatch(new FilterCacheAction.AddRecentFilter(action.FilterModel.Comparison.Value));
        }

        UpdateFilterColors(action.FilterModel, dispatcher);
    }

    [EffectMethod]
    public async Task HandleSetBasicFilter(FilterPaneAction.SetBasicFilter action, IDispatcher dispatcher)
    {
        await UpdateEventTableFiltersAsync(filterPaneState.Value, dispatcher);

        if (!string.IsNullOrEmpty(action.FilterModel.Comparison.Value))
        {
            dispatcher.Dispatch(new FilterCacheAction.AddRecentFilter(action.FilterModel.Comparison.Value));
        }

        UpdateFilterColors(action.FilterModel, dispatcher);
    }

    [EffectMethod]
    public async Task HandleSetCachedFilter(FilterPaneAction.SetCachedFilter action, IDispatcher dispatcher)
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

    [EffectMethod(typeof(FilterPaneAction.ToggleAdvancedFilterEditing))]
    public async Task HandleToggleAdvancedFilterEditing(IDispatcher dispatcher) =>
        await UpdateEventTableFiltersAsync(filterPaneState.Value, dispatcher);

    [EffectMethod]
    public async Task HandleToggleAdvancedFilterEnabled(
        FilterPaneAction.ToggleAdvancedFilterEnabled action,
        IDispatcher dispatcher)
    {
        var filter = filterPaneState.Value.AdvancedFilters.FirstOrDefault(x => x.Id.Equals(action.Id));

        if (filter is null) { return; }

        await UpdateEventTableFiltersAsync(filterPaneState.Value, dispatcher);

        UpdateFilterColors(filter, dispatcher);
    }

    [EffectMethod(typeof(FilterPaneAction.ToggleBasicFilterEditing))]
    public async Task HandleToggleBasicFilterEditing(IDispatcher dispatcher) =>
        await UpdateEventTableFiltersAsync(filterPaneState.Value, dispatcher);

    [EffectMethod]
    public async Task HandleToggleBasicFilterEnabled(
        FilterPaneAction.ToggleBasicFilterEnabled action,
        IDispatcher dispatcher)
    {
        var filter = filterPaneState.Value.BasicFilters.FirstOrDefault(x => x.Id.Equals(action.Id));

        if (filter is null) { return; }

        await UpdateEventTableFiltersAsync(filterPaneState.Value, dispatcher);

        UpdateFilterColors(filter, dispatcher);
    }

    [EffectMethod(typeof(FilterPaneAction.ToggleCachedFilterEditing))]
    public async Task HandleToggleCachedFilterEditing(IDispatcher dispatcher) =>
        await UpdateEventTableFiltersAsync(filterPaneState.Value, dispatcher);

    [EffectMethod]
    public async Task HandleToggleCachedFilterEnabled(FilterPaneAction.ToggleCachedFilterEnabled action, IDispatcher dispatcher)
    {
        var filter = filterPaneState.Value.CachedFilters.FirstOrDefault(x => x.Id.Equals(action.Id));

        if (filter is null) { return; }

        await UpdateEventTableFiltersAsync(filterPaneState.Value, dispatcher);

        UpdateFilterColors(filter, dispatcher);
    }

    [EffectMethod(typeof(FilterPaneAction.ToggleFilterDate))]
    public async Task HandleToggleFilterDate(IDispatcher dispatcher) =>
        await UpdateEventTableFiltersAsync(filterPaneState.Value, dispatcher);

    [EffectMethod(typeof(FilterPaneAction.ToggleIsEnabled))]
    public async Task HandleToggleIsEnabled(IDispatcher dispatcher) =>
        await UpdateEventTableFiltersAsync(filterPaneState.Value, dispatcher);

    private static EventFilter GetEventFilter(FilterPaneState filterPaneState) => new(
        filterPaneState.FilteredDateRange,
        filterPaneState.AdvancedFilters.Where(f => f.IsEnabled).ToImmutableList(),
        filterPaneState.CachedFilters.Where(f => f.IsEnabled).ToImmutableList(),
        filterPaneState.BasicFilters.Where(f => f.IsEnabled).ToImmutableList()
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
                new EventLogAction.SetFilters(new EventFilter(filterPaneState.FilteredDateRange, [], [], []))));
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
