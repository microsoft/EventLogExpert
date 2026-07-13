// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Filtering.Evaluation;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.FilterLenses;
using EventLogExpert.Runtime.FilterLibrary;
using EventLogExpert.Runtime.LogTable;
using Fluxor;

namespace EventLogExpert.Runtime.FilterPane;

internal sealed class Effects
{
    private readonly IStateSelection<EventLogState, Filter> _appliedFilter;
    private readonly IState<FilterPaneState> _filterPaneState;
    private readonly IState<FilterLensState> _lensState;
    private readonly IState<RawEventStoreState> _rawEventStore;

    public Effects(
        IStateSelection<EventLogState, Filter> appliedFilter,
        IState<RawEventStoreState> rawEventStore,
        IState<FilterPaneState> filterPaneState,
        IState<FilterLensState> lensState)
    {
        _appliedFilter = appliedFilter;
        _rawEventStore = rawEventStore;
        _filterPaneState = filterPaneState;
        _lensState = lensState;

        _appliedFilter.Select(static s => s.AppliedFilter);
    }

    [EffectMethod]
    public Task HandleAddFilter(AddFilterAction action, IDispatcher dispatcher)
    {
        if (string.IsNullOrEmpty(action.SavedFilter.ComparisonText))
        {
            return Task.CompletedTask;
        }

        UpdateEventTableFilters(_filterPaneState.Value, dispatcher);

        dispatcher.Dispatch(new RecordFilterAppliedAction(action.SavedFilter));

        return Task.CompletedTask;
    }

    [EffectMethod(typeof(ClearAllFiltersAction))]
    public Task HandleClearAllFilters(IDispatcher dispatcher)
    {
        UpdateEventTableFilters(_filterPaneState.Value, dispatcher);
        return Task.CompletedTask;
    }

    [EffectMethod(typeof(MergeFiltersAction))]
    public Task HandleMergeFilters(IDispatcher dispatcher)
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

    [EffectMethod(typeof(ReplaceFiltersAction))]
    public Task HandleReplaceFilters(IDispatcher dispatcher)
    {
        UpdateEventTableFilters(_filterPaneState.Value, dispatcher);
        return Task.CompletedTask;
    }

    [EffectMethod(typeof(RestoreFilterPaneStateAction))]
    public Task HandleRestoreFilterPaneState(IDispatcher dispatcher)
    {
        UpdateEventTableFilters(_filterPaneState.Value, dispatcher);
        return Task.CompletedTask;
    }

    [EffectMethod]
    public Task HandleSetFilter(SetFilterAction action, IDispatcher dispatcher)
    {
        UpdateEventTableFilters(_filterPaneState.Value, dispatcher);

        if (!string.IsNullOrEmpty(action.SavedFilter.ComparisonText))
        {
            dispatcher.Dispatch(new RecordFilterAppliedAction(action.SavedFilter));
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
            var (after, before) = _rawEventStore.Value.TryGetRawEventDateRange().RoundOrFallback(DateTime.UtcNow);

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

    [EffectMethod(typeof(SetFilterExcludedAction))]
    public Task HandleSetFilterExcluded(IDispatcher dispatcher)
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

    private void UpdateEventTableFilters(FilterPaneState filterPaneState, IDispatcher dispatcher)
    {
        // Build the effective filter by layering any active transient lenses onto the base through the single shared
        // EffectiveFilterBuilder, so the FilterPane apply path and the FilterLens push/pop path can never diverge.
        // FilterPaneFilterBuilder handles both the enabled and the excluded-only (pane-disabled) branch, so lenses narrow in both.
        var candidate = EffectiveFilterBuilder.Build(
            FilterPaneFilterBuilder.Build(filterPaneState),
            _lensState.Value.Lenses);

        if (!candidate.HasFilteringChangedFrom(_appliedFilter.Value))
        {
            return;
        }

        dispatcher.Dispatch(new ApplyFilterAction(candidate));
    }
}
