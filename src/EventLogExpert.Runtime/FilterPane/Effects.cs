// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Evaluation;
using EventLogExpert.Runtime.Common.Display;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.FilterLenses;
using EventLogExpert.Runtime.FilterLibrary;
using Fluxor;

namespace EventLogExpert.Runtime.FilterPane;

internal sealed class Effects
{
    private readonly IStateSelection<EventLogState, Filter> _appliedFilter;
    private readonly ClearAllFiltersNotifier _clearAllFiltersNotifier;
    private readonly IState<FilterPaneState> _filterPaneState;
    private readonly FilterPromotedNotifier _filterPromotedNotifier;
    private readonly IState<FilterLensState> _lensState;
    private readonly SetFilterDateRangeSucceededNotifier _setFilterDateRangeSucceededNotifier;

    /// <summary>
    ///     The range the last zone change produced and the readings it was re-read from. Only used while the applied
    ///     bounds are still exactly that range; writing a new range (apply, narrow, lens keep) forgets it.
    /// </summary>
    private (DateTime? AfterUtc, DateTime? BeforeUtc, DateTime? AfterWallClock, DateTime? BeforeWallClock)? _lastReinterpretation;

    public Effects(
        IStateSelection<EventLogState, Filter> appliedFilter,
        IState<FilterPaneState> filterPaneState,
        IState<FilterLensState> lensState,
        ClearAllFiltersNotifier clearAllFiltersNotifier,
        SetFilterDateRangeSucceededNotifier setFilterDateRangeSucceededNotifier,
        FilterPromotedNotifier filterPromotedNotifier)
    {
        _appliedFilter = appliedFilter;
        _filterPaneState = filterPaneState;
        _lensState = lensState;
        _clearAllFiltersNotifier = clearAllFiltersNotifier;
        _setFilterDateRangeSucceededNotifier = setFilterDateRangeSucceededNotifier;
        _filterPromotedNotifier = filterPromotedNotifier;

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
        _clearAllFiltersNotifier.Raise();
        return Task.CompletedTask;
    }

    [EffectMethod]
    public Task HandleCommitPromoted(CommitPromotedLensAction action, IDispatcher dispatcher)
    {
        if (action.Window is { IsEnabled: true }) { _lastReinterpretation = null; }

        dispatcher.Dispatch(new ApplyFilterAction(BuildCandidate(_filterPaneState.Value)));

        if (action.Window is { IsEnabled: true })
        {
            _setFilterDateRangeSucceededNotifier.Raise();
        }

        _filterPromotedNotifier.Raise();

        return Task.CompletedTask;
    }

    [EffectMethod]
    public Task HandleCommitPromotedLenses(CommitPromotedLensesAction action, IDispatcher dispatcher)
    {
        if (action.Commits.Any(commit => commit.Window is { IsEnabled: true })) { _lastReinterpretation = null; }

        dispatcher.Dispatch(new ApplyFilterAction(BuildCandidate(_filterPaneState.Value)));

        if (action.Commits.Any(commit => commit.Window is { IsEnabled: true }))
        {
            _setFilterDateRangeSucceededNotifier.Raise();
        }

        _filterPromotedNotifier.Raise();

        return Task.CompletedTask;
    }

    [EffectMethod(typeof(MergeFiltersAction))]
    public Task HandleMergeFilters(IDispatcher dispatcher)
    {
        UpdateEventTableFilters(_filterPaneState.Value, dispatcher);
        return Task.CompletedTask;
    }

    [EffectMethod]
    public Task HandleNarrowFilterDateRange(NarrowFilterDateRangeAction action, IDispatcher dispatcher)
    {
        _lastReinterpretation = null;

        // A disabled range is not narrowing anything, so its bounds are not carried into the new one.
        var current = _filterPaneState.Value.FilteredDateRange is { IsEnabled: true } enabled ? enabled : null;

        // Intersect with the enabled range so a narrowing shortcut never brings back events it already hides.
        var narrowed = new DateFilter
        {
            After = Latest(action.After, current?.After),
            Before = Earliest(action.Before, current?.Before)
        };

        dispatcher.Dispatch(new SetFilterDateRangeSuccessAction(narrowed));

        return Task.CompletedTask;
    }

    [EffectMethod]
    public Task HandleReinterpretFilterDateRange(ReinterpretFilterDateRangeAction action, IDispatcher dispatcher)
    {
        if (_filterPaneState.Value.FilteredDateRange is not { } current) { return Task.CompletedTask; }

        // Re-reading from the previous reinterpretation's readings (while the range is untouched since) keeps a reading
        // skipped by a daylight-saving gap from drifting to the transition instant's reading across zone changes.
        var rangeIsUntouchedSinceLastReinterpretation = _lastReinterpretation is { } last &&
            last.AfterUtc == current.After && last.BeforeUtc == current.Before;
        var afterWallClock = rangeIsUntouchedSinceLastReinterpretation ?
            _lastReinterpretation!.Value.AfterWallClock :
            current.After?.ConvertTimeZone(action.PreviousZone);
        var beforeWallClock = rangeIsUntouchedSinceLastReinterpretation ?
            _lastReinterpretation!.Value.BeforeWallClock :
            current.Before?.ConvertTimeZone(action.PreviousZone);

        var reinterpreted = current with
        {
            After = afterWallClock?.ConvertWallClockToUtc(action.NewZone, AmbiguousTimeResolution.Earliest),
            Before = beforeWallClock?.ConvertWallClockToUtc(action.NewZone, AmbiguousTimeResolution.Latest)
        };

        // Bounds inside a repeated fall-back hour can read in reverse order; re-reading them would hide every event, so
        // such a range keeps its instants.
        if (reinterpreted.After > reinterpreted.Before && !(current.After > current.Before))
        {
            _lastReinterpretation = null;

            return Task.CompletedTask;
        }

        _lastReinterpretation = (reinterpreted.After, reinterpreted.Before, afterWallClock, beforeWallClock);

        if (reinterpreted != current)
        {
            dispatcher.Dispatch(new SetFilterDateRangeSuccessAction(reinterpreted));
        }

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
        // Clearing the range keeps the memory: a scenario rollback restores the exact prior range, which must still
        // re-read from its remembered readings. Any newly written range forgets it.
        if (action.DateFilter is not null) { _lastReinterpretation = null; }

        dispatcher.Dispatch(new SetFilterDateRangeSuccessAction(action.DateFilter));

        return Task.CompletedTask;
    }

    [EffectMethod(typeof(SetFilterDateRangeSuccessAction))]
    public Task HandleSetFilterDateRangeSuccess(IDispatcher dispatcher)
    {
        UpdateEventTableFilters(_filterPaneState.Value, dispatcher);
        _setFilterDateRangeSucceededNotifier.Raise();

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

    private static DateTime? Earliest(DateTime? first, DateTime? second) =>
        first is null ? second : second is null ? first : first < second ? first : second;

    private static DateTime? Latest(DateTime? first, DateTime? second) =>
        first is null ? second : second is null ? first : first > second ? first : second;

    private Filter BuildCandidate(FilterPaneState filterPaneState) =>
        EffectiveFilterBuilder.Build(FilterPaneFilterBuilder.Build(filterPaneState), _lensState.Value.Lenses);

    private void UpdateEventTableFilters(FilterPaneState filterPaneState, IDispatcher dispatcher)
    {
        var candidate = BuildCandidate(filterPaneState);

        if (!candidate.HasFilteringChangedFrom(_appliedFilter.Value))
        {
            return;
        }

        dispatcher.Dispatch(new ApplyFilterAction(candidate));
    }
}
