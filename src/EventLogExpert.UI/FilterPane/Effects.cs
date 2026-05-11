// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.EventLog;
using EventLogExpert.UI.Filter;
using EventLogExpert.UI.FilterCache;
using EventLogExpert.UI.FilterGroup;
using Fluxor;
using System.Collections.Immutable;

namespace EventLogExpert.UI.FilterPane;

public sealed class Effects(
    IState<EventLogState> eventLogState,
    IState<FilterPaneState> filterPaneState)
{
    private readonly IState<EventLogState> _eventLogState = eventLogState;
    private readonly IState<FilterPaneState> _filterPaneState = filterPaneState;

    [EffectMethod]
    public Task HandleAddFilter(AddFilterAction action, IDispatcher dispatcher)
    {
        if (!string.IsNullOrEmpty(action.SavedFilter.ComparisonText))
        {
            UpdateEventTableFilters(_filterPaneState.Value, dispatcher);
        }

        if (action.SavedFilter.FilterType is not FilterType.Cached &&
            !string.IsNullOrEmpty(action.SavedFilter.ComparisonText))
        {
            dispatcher.Dispatch(
                new AddRecentFilterAction(action.SavedFilter.ComparisonText));
        }

        return Task.CompletedTask;
    }

    [EffectMethod(typeof(ApplyFilterGroupAction))]
    public Task HandleApplyFilterGroup(IDispatcher dispatcher)
    {
        UpdateEventTableFilters(_filterPaneState.Value, dispatcher);
        return Task.CompletedTask;
    }

    [EffectMethod(typeof(ClearAllFiltersAction))]
    public Task HandleClearAllFilters(IDispatcher dispatcher)
    {
        UpdateEventTableFilters(_filterPaneState.Value, dispatcher);
        return Task.CompletedTask;
    }

    [EffectMethod(typeof(RemoveFilterAction))]
    public Task HandleRemoveAdvancedFilter(IDispatcher dispatcher)
    {
        UpdateEventTableFilters(_filterPaneState.Value, dispatcher);
        return Task.CompletedTask;
    }

    [EffectMethod]
    public Task HandleSaveFilterGroup(SaveFilterGroupAction action, IDispatcher dispatcher)
    {
        // New Id so re-applying the group inserts cleanly into the pane's de-dup; IsEnabled cleared
        // so the user opts in by toggling. All other identity is preserved verbatim via record copy.
        dispatcher.Dispatch(
            new AddGroupAction(
                new SavedFilterGroup
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
    public Task HandleSetFilter(SetFilterAction action, IDispatcher dispatcher)
    {
        UpdateEventTableFilters(_filterPaneState.Value, dispatcher);

        if (!string.IsNullOrEmpty(action.SavedFilter.ComparisonText) &&
            action.SavedFilter.FilterType is not FilterType.Cached)
        {
            dispatcher.Dispatch(new AddRecentFilterAction(action.SavedFilter.ComparisonText));
        }

        return Task.CompletedTask;
    }

    [EffectMethod]
    public Task HandleSetFilterDateRange(SetFilterDateRangeAction action, IDispatcher dispatcher)
    {
        if (action.DateFilter is null)
        {
            dispatcher.Dispatch(new SetFilterDateRangeSuccessAction(action.DateFilter));

            return Task.CompletedTask;
        }

        DateTime? updatedAfter = action.DateFilter?.After ?? _filterPaneState.Value.FilteredDateRange?.After;
        DateTime? updatedBefore = action.DateFilter?.Before ?? _filterPaneState.Value.FilteredDateRange?.Before;

        if (updatedAfter is null || updatedBefore is null)
        {
            var (after, before) = DateRangeDefaults.ComputeFromActiveLogs(
                _eventLogState.Value.ActiveLogs.Values,
                DateTime.UtcNow);

            updatedAfter ??= after;
            updatedBefore ??= before;
        }

        dispatcher.Dispatch(
            new SetFilterDateRangeSuccessAction(
                new DateFilter
                {
                    After = updatedAfter,
                    Before = updatedBefore
                }));

        return Task.CompletedTask;
    }

    [EffectMethod(typeof(SetFilterDateRangeSuccessAction))]
    public Task HandleSetFilterDateRangeSuccess(IDispatcher dispatcher)
    {
        UpdateEventTableFilters(_filterPaneState.Value, dispatcher);

        return Task.CompletedTask;
    }

    [EffectMethod(typeof(ToggleFilterDateAction))]
    public Task HandleToggleFilterDate(IDispatcher dispatcher)
    {
        UpdateEventTableFilters(_filterPaneState.Value, dispatcher);

        return Task.CompletedTask;
    }

    [EffectMethod(typeof(ToggleFilterEnabledAction))]
    public Task HandleToggleFilterEnabled(IDispatcher dispatcher)
    {
        UpdateEventTableFilters(_filterPaneState.Value, dispatcher);

        return Task.CompletedTask;
    }

    [EffectMethod(typeof(ToggleFilterExcludedAction))]
    public Task HandleToggleFilterExcluded(IDispatcher dispatcher)
    {
        UpdateEventTableFilters(_filterPaneState.Value, dispatcher);

        return Task.CompletedTask;
    }

    [EffectMethod(typeof(ToggleIsEnabledAction))]
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

        dispatcher.Dispatch(new SetFiltersAction(candidate));
    }
}
