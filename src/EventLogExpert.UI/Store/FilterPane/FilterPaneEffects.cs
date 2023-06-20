// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Store.FilterCache;
using Fluxor;

namespace EventLogExpert.UI.Store.FilterPane;

public class FilterPaneEffects
{
    private readonly IState<FilterPaneState> _state;

    public FilterPaneEffects(IState<FilterPaneState> state) => _state = state;

    [EffectMethod]
    public async Task HandleAddFilter(FilterPaneAction.AddFilter action, IDispatcher dispatcher)
    {
        if (action.FilterModel?.ComparisonString is not null)
        {
            dispatcher.Dispatch(new FilterCacheAction.AddRecentFilter(action.FilterModel.ComparisonString));
        }
    }

    [EffectMethod]
    public async Task HandleSetAdvancedFilter(FilterPaneAction.SetAdvancedFilter action, IDispatcher dispatcher)
    {
        if (!string.IsNullOrEmpty(action.Expression))
        {
            dispatcher.Dispatch(new FilterCacheAction.AddRecentFilter(action.Expression));
        }
    }

    [EffectMethod]
    public async Task HandleSetFilter(FilterPaneAction.SetFilter action, IDispatcher dispatcher)
    {
        if (action.FilterModel.ComparisonString is not null)
        {
            dispatcher.Dispatch(new FilterCacheAction.AddRecentFilter(action.FilterModel.ComparisonString));
        }
    }
}
