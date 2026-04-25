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
        if (!string.IsNullOrEmpty(action.FilterModel.ComparisonText))
        {
            UpdateEventTableFilters(_filterPaneState.Value, dispatcher);
        }

        if (action.FilterModel.FilterType is not FilterType.Cached &&
            !string.IsNullOrEmpty(action.FilterModel.ComparisonText))
        {
            dispatcher.Dispatch(
                new FilterCacheAction.AddRecentFilter(action.FilterModel.ComparisonText));
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
        // New Id so re-applying the group inserts cleanly into the pane's de-dup; IsEnabled cleared
        // so the user opts in by toggling. All other identity is preserved verbatim via record copy.
        dispatcher.Dispatch(
            new FilterGroupAction.AddGroup(
                new FilterGroupModel
                {
                    Name = action.Name,
                    Filters =
                    [
                        .. _filterPaneState.Value.Filters.Select(filter =>
                            filter with { Id = FilterId.Create(), IsEnabled = false })
                    ]
                }));

        return Task.CompletedTask;
    }

    [EffectMethod]
    public Task HandleSetFilter(FilterPaneAction.SetFilter action, IDispatcher dispatcher)
    {
        UpdateEventTableFilters(_filterPaneState.Value, dispatcher);

        if (!string.IsNullOrEmpty(action.FilterModel.ComparisonText) &&
            action.FilterModel.FilterType is not FilterType.Cached)
        {
            dispatcher.Dispatch(new FilterCacheAction.AddRecentFilter(action.FilterModel.ComparisonText));
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

        if (updatedAfter is null || updatedBefore is null)
        {
            var (after, before) = DateRangeDefaults.ComputeFromActiveLogs(
                _eventLogState.Value.ActiveLogs.Values,
                DateTime.UtcNow);

            updatedAfter ??= after;
            updatedBefore ??= before;
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
