// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;
using EventLogExpert.UI.Store.EventLog;
using EventLogExpert.UI.Store.FilterCache;
using EventLogExpert.UI.Store.FilterGroup;
using Fluxor;
using System.Collections.Immutable;

namespace EventLogExpert.UI.Store.FilterPane;

public sealed class FilterPaneEffects(
    IState<EventLogState> eventLogState,
    IState<FilterPaneState> filterPaneState)
{
    [EffectMethod]
    public async Task HandleAddFilter(FilterPaneAction.AddFilter action, IDispatcher dispatcher)
    {
        if (!string.IsNullOrEmpty(action.FilterModel?.Comparison.Value))
        {
            await UpdateEventTableFiltersAsync(filterPaneState.Value, dispatcher);
        }

        if (action.FilterModel?.FilterType is not FilterType.Cached && action.FilterModel?.Comparison.Value is not null)
        {
            dispatcher.Dispatch(
                new FilterCacheAction.AddRecentFilter(action.FilterModel.Comparison.Value));
        }
    }

    [EffectMethod(typeof(FilterPaneAction.ApplyFilterGroup))]
    public async Task HandleApplyFilterGroup(IDispatcher dispatcher) =>
        await UpdateEventTableFiltersAsync(filterPaneState.Value, dispatcher);

    [EffectMethod(typeof(FilterPaneAction.ClearAllFilters))]
    public async Task HandleClearAllFilters(IDispatcher dispatcher) =>
        await UpdateEventTableFiltersAsync(filterPaneState.Value, dispatcher);

    [EffectMethod(typeof(FilterPaneAction.RemoveFilter))]
    public async Task HandleRemoveAdvancedFilter(IDispatcher dispatcher) =>
        await UpdateEventTableFiltersAsync(filterPaneState.Value, dispatcher);

    [EffectMethod]
    public Task HandleSaveFilterGroup(FilterPaneAction.SaveFilterGroup action, IDispatcher dispatcher)
    {
        dispatcher.Dispatch(
            new FilterGroupAction.AddGroup(
                new FilterGroupModel
                {
                    Name = action.Name,
                    Filters =
                    [
                        .. filterPaneState.Value.Filters.Select(filter =>
                            new FilterModel
                            {
                                Color = filter.Color,
                                Comparison = filter.Comparison with { },
                                IsExcluded = filter.IsExcluded
                            })
                    ]
                }));

        return Task.CompletedTask;
    }

    [EffectMethod]
    public async Task HandleSetFilter(FilterPaneAction.SetFilter action, IDispatcher dispatcher)
    {
        await UpdateEventTableFiltersAsync(filterPaneState.Value, dispatcher);

        if (!string.IsNullOrEmpty(action.FilterModel.Comparison.Value) &&
            action.FilterModel.FilterType is not FilterType.Cached)
        {
            dispatcher.Dispatch(new FilterCacheAction.AddRecentFilter(action.FilterModel.Comparison.Value));
        }
    }

    [EffectMethod]
    public Task HandleSetFilterDateRange(FilterPaneAction.SetFilterDateRange action, IDispatcher dispatcher)
    {
        if (action.FilterDateModel is null)
        {
            dispatcher.Dispatch(new FilterPaneAction.SetFilterDateRangeSuccess(action.FilterDateModel));

            return Task.CompletedTask;
        }

        DateTime? updatedAfter = action.FilterDateModel?.After ?? filterPaneState.Value.FilteredDateRange?.After;
        DateTime? updatedBefore = action.FilterDateModel?.Before ?? filterPaneState.Value.FilteredDateRange?.Before;

        long ticksPerHour = TimeSpan.FromHours(1).Ticks;

        if (updatedAfter is null)
        {
            long ticks =
                (eventLogState.Value.ActiveLogs.Values.Select(log => log.Events.LastOrDefault()?.TimeCreated)
                        .Order()
                        .LastOrDefault() ??
                    DateTime.UtcNow)
                .Ticks;

            updatedAfter = new DateTime(ticks / ticksPerHour * ticksPerHour, DateTimeKind.Utc);
        }

        if (updatedBefore is null)
        {
            long ticks =
                (eventLogState.Value.ActiveLogs.Values.Select(log => log.Events.FirstOrDefault()?.TimeCreated)
                        .Order()
                        .FirstOrDefault() ??
                    DateTime.UtcNow)
                .Ticks;

            updatedBefore = new DateTime((ticks + ticksPerHour - 1) / ticksPerHour * ticksPerHour, DateTimeKind.Utc);
        }

        dispatcher.Dispatch(
            new FilterPaneAction.SetFilterDateRangeSuccess(
                new FilterDateModel
                {
                    After = updatedAfter,
                    Before = updatedBefore
                }));

        return Task.CompletedTask;
    }

    [EffectMethod(typeof(FilterPaneAction.SetFilterDateRangeSuccess))]
    public async Task HandleSetFilterDateRangeSuccess(IDispatcher dispatcher) =>
        await UpdateEventTableFiltersAsync(filterPaneState.Value, dispatcher);

    [EffectMethod(typeof(FilterPaneAction.ToggleFilterDate))]
    public async Task HandleToggleFilterDate(IDispatcher dispatcher) =>
        await UpdateEventTableFiltersAsync(filterPaneState.Value, dispatcher);

    [EffectMethod(typeof(FilterPaneAction.ToggleFilterEditing))]
    public async Task HandleToggleFilterEditing(IDispatcher dispatcher) =>
        await UpdateEventTableFiltersAsync(filterPaneState.Value, dispatcher);

    [EffectMethod(typeof(FilterPaneAction.ToggleFilterEnabled))]
    public async Task HandleToggleFilterEnabled(IDispatcher dispatcher) =>
        await UpdateEventTableFiltersAsync(filterPaneState.Value, dispatcher);

    [EffectMethod(typeof(FilterPaneAction.ToggleFilterExcluded))]
    public async Task HandleToggleFilterExcluded(IDispatcher dispatcher) =>
        await UpdateEventTableFiltersAsync(filterPaneState.Value, dispatcher);

    [EffectMethod(typeof(FilterPaneAction.ToggleIsEnabled))]
    public async Task HandleToggleIsEnabled(IDispatcher dispatcher) =>
        await UpdateEventTableFiltersAsync(filterPaneState.Value, dispatcher);

    private static EventFilter GetEventFilter(FilterPaneState filterPaneState) =>
        new(filterPaneState.FilteredDateRange, filterPaneState.Filters.Where(f => f.IsEnabled).ToImmutableList());

    private static async Task UpdateEventTableFiltersAsync(FilterPaneState filterPaneState, IDispatcher dispatcher)
    {
        dispatcher.Dispatch(new FilterPaneAction.ToggleIsLoading());

        if (filterPaneState.IsEnabled)
        {
            await Task.Run(() => dispatcher.Dispatch(new EventLogAction.SetFilters(GetEventFilter(filterPaneState))));
        }
        else
        {
            // Only keep date and excluded filters
            await Task.Run(() => dispatcher.Dispatch(
                new EventLogAction.SetFilters(
                    new EventFilter(filterPaneState.FilteredDateRange,
                        filterPaneState.Filters.Where(filter => filter.IsExcluded).ToImmutableList()))));
        }

        dispatcher.Dispatch(new FilterPaneAction.ToggleIsLoading());
    }
}
