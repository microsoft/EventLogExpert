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
    private readonly IState<EventLogState> _eventLogState = eventLogState;
    private readonly IState<FilterPaneState> _filterPaneState = filterPaneState;

    [EffectMethod]
    public Task HandleAddFilter(FilterPaneAction.AddFilter action, IDispatcher dispatcher)
    {
        if (!string.IsNullOrEmpty(action.FilterModel.Comparison.Value))
        {
            UpdateEventTableFilters(_filterPaneState.Value, dispatcher);
        }

        if (action.FilterModel.FilterType is not FilterType.Cached &&
            !string.IsNullOrEmpty(action.FilterModel.Comparison.Value))
        {
            dispatcher.Dispatch(
                new FilterCacheAction.AddRecentFilter(action.FilterModel.Comparison.Value));
        }

        return Task.CompletedTask;
    }

    [EffectMethod(typeof(FilterPaneAction.ApplyFilterGroup))]
    public Task HandleApplyFilterGroup(IDispatcher dispatcher)
    {
        UpdateEventTableFilters(_filterPaneState.Value, dispatcher);
        return Task.CompletedTask;
    }

    [EffectMethod(typeof(FilterPaneAction.ClearAllFilters))]
    public Task HandleClearAllFilters(IDispatcher dispatcher)
    {
        UpdateEventTableFilters(_filterPaneState.Value, dispatcher);
        return Task.CompletedTask;
    }

    [EffectMethod(typeof(FilterPaneAction.RemoveFilter))]
    public Task HandleRemoveAdvancedFilter(IDispatcher dispatcher)
    {
        UpdateEventTableFilters(_filterPaneState.Value, dispatcher);
        return Task.CompletedTask;
    }

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
                        .. _filterPaneState.Value.Filters.Select(filter =>
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
    public Task HandleSetFilter(FilterPaneAction.SetFilter action, IDispatcher dispatcher)
    {
        UpdateEventTableFilters(_filterPaneState.Value, dispatcher);

        if (!string.IsNullOrEmpty(action.FilterModel.Comparison.Value) &&
            action.FilterModel.FilterType is not FilterType.Cached)
        {
            dispatcher.Dispatch(new FilterCacheAction.AddRecentFilter(action.FilterModel.Comparison.Value));
        }

        return Task.CompletedTask;
    }

    [EffectMethod]
    public Task HandleSetFilterDateRange(FilterPaneAction.SetFilterDateRange action, IDispatcher dispatcher)
    {
        if (action.FilterDateModel is null)
        {
            dispatcher.Dispatch(new FilterPaneAction.SetFilterDateRangeSuccess(action.FilterDateModel));

            return Task.CompletedTask;
        }

        DateTime? updatedAfter = action.FilterDateModel?.After ?? _filterPaneState.Value.FilteredDateRange?.After;
        DateTime? updatedBefore = action.FilterDateModel?.Before ?? _filterPaneState.Value.FilteredDateRange?.Before;

        long ticksPerHour = TimeSpan.FromHours(1).Ticks;

        // TODO: This computes intersection bounds (latest oldest / earliest newest) across active logs,
        // not the global envelope. Same logic is duplicated in FilterPane.razor.cs:AddDateFilter.
        // Lock down intent with tests across both call sites and resolve intersection-vs-envelope.
        if (updatedAfter is null)
        {
            long ticks =
                (_eventLogState.Value.ActiveLogs.Values.Select(log => log.Events.LastOrDefault()?.TimeCreated)
                        .Order()
                        .LastOrDefault() ??
                    DateTime.UtcNow)
                .Ticks;

            updatedAfter = new DateTime(ticks / ticksPerHour * ticksPerHour, DateTimeKind.Utc);
        }

        if (updatedBefore is null)
        {
            long ticks =
                (_eventLogState.Value.ActiveLogs.Values.Select(log => log.Events.FirstOrDefault()?.TimeCreated)
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
    public Task HandleSetFilterDateRangeSuccess(IDispatcher dispatcher)
    {
        UpdateEventTableFilters(_filterPaneState.Value, dispatcher);

        return Task.CompletedTask;
    }

    [EffectMethod(typeof(FilterPaneAction.ToggleFilterDate))]
    public Task HandleToggleFilterDate(IDispatcher dispatcher)
    {
        UpdateEventTableFilters(_filterPaneState.Value, dispatcher);

        return Task.CompletedTask;
    }

    [EffectMethod(typeof(FilterPaneAction.ToggleFilterEnabled))]
    public Task HandleToggleFilterEnabled(IDispatcher dispatcher)
    {
        UpdateEventTableFilters(_filterPaneState.Value, dispatcher);

        return Task.CompletedTask;
    }

    [EffectMethod(typeof(FilterPaneAction.ToggleFilterExcluded))]
    public Task HandleToggleFilterExcluded(IDispatcher dispatcher)
    {
        UpdateEventTableFilters(_filterPaneState.Value, dispatcher);

        return Task.CompletedTask;
    }

    [EffectMethod(typeof(FilterPaneAction.ToggleIsEnabled))]
    public Task HandleToggleIsEnabled(IDispatcher dispatcher)
    {
        UpdateEventTableFilters(_filterPaneState.Value, dispatcher);

        return Task.CompletedTask;
    }

    private static EventFilter GetEventFilter(FilterPaneState filterPaneState) =>
        new(filterPaneState.FilteredDateRange, filterPaneState.Filters.Where(f => f.IsEnabled).ToImmutableList());

    private void UpdateEventTableFilters(FilterPaneState filterPaneState, IDispatcher dispatcher)
    {
        var candidate = filterPaneState.IsEnabled
            ? GetEventFilter(filterPaneState)
            : new EventFilter(
                filterPaneState.FilteredDateRange,
                filterPaneState.Filters.Where(filter => filter.IsExcluded).ToImmutableList());

        if (!FilterMethods.HasFilteringChanged(candidate, _eventLogState.Value.AppliedFilter))
        {
            return;
        }

        dispatcher.Dispatch(new EventLogAction.SetFilters(candidate));
    }
}
