// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;
using EventLogExpert.UI.Store.FilterCache;
using Fluxor;

namespace EventLogExpert.UI.Store.FilterPane;

public sealed class FilterPaneEffects
{
    [EffectMethod]
    public static Task HandleAddFilter(FilterPaneAction.AddFilter action, IDispatcher dispatcher)
    {
        if (action.FilterModel?.ComparisonString is not null)
        {
            dispatcher.Dispatch(new FilterCacheAction.AddRecentFilter(
                new AdvancedFilterModel { ComparisonString = action.FilterModel.ComparisonString }));
        }

        return Task.CompletedTask;
    }

    [EffectMethod]
    public static Task HandleSetAdvancedFilter(FilterPaneAction.SetAdvancedFilter action, IDispatcher dispatcher)
    {
        if (action.AdvancedFilterModel is not null)
        {
            dispatcher.Dispatch(new FilterCacheAction.AddRecentFilter(
                new AdvancedFilterModel { ComparisonString = action.AdvancedFilterModel.ComparisonString }));
        }

        return Task.CompletedTask;
    }

    [EffectMethod]
    public static Task HandleSetFilter(FilterPaneAction.SetFilter action, IDispatcher dispatcher)
    {
        if (!string.IsNullOrEmpty(action.FilterModel.ComparisonString))
        {
            dispatcher.Dispatch(new FilterCacheAction.AddRecentFilter(
                new AdvancedFilterModel { ComparisonString = action.FilterModel.ComparisonString }));
        }

        return Task.CompletedTask;
    }
}
