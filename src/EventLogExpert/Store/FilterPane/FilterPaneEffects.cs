// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.Models;
using EventLogExpert.Store.EventLog;
using Fluxor;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.Store.FilterPane;

public class FilterPaneEffects
{
    private readonly IState<FilterPaneState> _state;

    public FilterPaneEffects(IState<FilterPaneState> state) => _state = state;

    [EffectMethod(typeof(FilterPaneAction.ApplyFilters))]
    public Task HandleApplyFiltersAction(IDispatcher dispatcher)
    {
        List<FilterModel> filters = new();

        foreach (var filterModel in _state.Value.CurrentFilters)
        {
            if (filterModel.Comparison.Any()) { filters.Add(filterModel); }
        }

        dispatcher.Dispatch(new EventLogAction.FilterEvents(filters));

        return Task.CompletedTask;
    }
}
