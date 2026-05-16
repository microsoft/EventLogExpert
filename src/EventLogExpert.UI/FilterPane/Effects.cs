// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Filtering.Runtime;
using EventLogExpert.UI.EventLog;
using EventLogExpert.UI.FilterCache;
using EventLogExpert.UI.FilterGroup;
using EventLogExpert.UI.Filters;
using Fluxor;

namespace EventLogExpert.UI.FilterPane;

internal sealed class Effects
{
    private readonly IStateSelection<EventLogState, Filter> _appliedFilter;
    private readonly IStateSelection<EventLogState, (DateTime After, DateTime Before)?> _eventDateRange;
    private readonly IState<FilterPaneState> _filterPaneState;

    public Effects(
        IStateSelection<EventLogState, Filter> appliedFilter,
        IStateSelection<EventLogState, (DateTime After, DateTime Before)?> eventDateRange,
        IState<FilterPaneState> filterPaneState)
    {
        _appliedFilter = appliedFilter;
        _eventDateRange = eventDateRange;
        _filterPaneState = filterPaneState;

        _appliedFilter.Select(static s => s.AppliedFilter);
        _eventDateRange.Select(static s => s.ActiveLogs.Values.TryGetEventDateRange(out var range) ? range : null);
    }

    [EffectMethod]
    public Task HandleAddFilter(AddFilterAction action, IDispatcher dispatcher)
    {
        if (!string.IsNullOrEmpty(action.SavedFilter.ComparisonText))
        {
            UpdateEventTableFilters(_filterPaneState.Value, dispatcher);

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
        // Empty pane → no-op. Without this guard the user can create empty groups via the Save
        // affordance (icon click after the pane was just cleared, etc.).
        if (_filterPaneState.Value.Filters.IsEmpty) { return Task.CompletedTask; }

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

        if (!string.IsNullOrEmpty(action.SavedFilter.ComparisonText))
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
            var (after, before) = _eventDateRange.Value.RoundOrFallback(DateTime.UtcNow);

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

    private static Filter GetFilter(FilterPaneState filterPaneState) =>
        new(filterPaneState.FilteredDateRange, [.. filterPaneState.Filters.Where(f => f.IsEnabled)]);

    private void UpdateEventTableFilters(FilterPaneState filterPaneState, IDispatcher dispatcher)
    {
        var candidate = filterPaneState.IsEnabled
            ? GetFilter(filterPaneState)
            : new Filter(
                filterPaneState.FilteredDateRange,
                [.. filterPaneState.Filters.Where(filter => filter.IsExcluded)]);

        if (!candidate.HasFilteringChangedFrom(_appliedFilter.Value))
        {
            return;
        }

        dispatcher.Dispatch(new SetFiltersAction(candidate));
    }
}
