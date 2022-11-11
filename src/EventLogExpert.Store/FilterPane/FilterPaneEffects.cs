// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.Models;
using EventLogExpert.Store.EventLog;
using Fluxor;

namespace EventLogExpert.Store.FilterPane;

public class FilterPaneEffects
{
    private readonly IState<FilterPaneState> _state;

    public FilterPaneEffects(IState<FilterPaneState> state) => _state = state;

    [EffectMethod(typeof(FilterPaneAction.ApplyFilters))]
    public Task HandleApplyFiltersAction(IDispatcher dispatcher)
    {
        List<Func<DisplayEventModel, bool>> filters = new();

        foreach (var filter in _state.Value.CurrentFilters)
        {
            if (filter.Comparison is not null)
            {
                filters.Add(filter.Comparison);
            }
        }

        dispatcher.Dispatch(new EventLogAction.FilterEvents(filters));

        return Task.CompletedTask;
    }
}
