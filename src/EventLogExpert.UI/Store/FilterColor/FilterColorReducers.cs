// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;
using Fluxor;

namespace EventLogExpert.UI.Store.FilterColor;

public sealed class FilterColorReducers
{
    [ReducerMethod(typeof(FilterColorAction.ClearAllFilters))]
    public static FilterColorState ReduceClearAllFilters(FilterColorState state) => state with { Filters = [] };

    [ReducerMethod]
    public static FilterColorState ReduceRemoveFilter(FilterColorState state, FilterColorAction.RemoveFilter action) =>
        state with { Filters = state.Filters.RemoveAll(x => x.Id.Equals(action.Id)) };

    [ReducerMethod]
    public static FilterColorState ReduceSetFilter(FilterColorState state, FilterColorAction.SetFilter action)
    {
        var filter = state.Filters.FirstOrDefault(x => x.Id.Equals(action.Filter.Id));

        if (filter is null)
        {
            return state with
            {
                Filters = state.Filters.Add(new FilterColorModel
                {
                    Id = action.Filter.Id,
                    Color = action.Filter.Color,
                    Comparison = action.Filter.Comparison with { }
                })
            };
        }

        return state with
        {
            Filters = state.Filters
                .Remove(filter)
                .Add(filter with
                {
                    Color = action.Filter.Color,
                    Comparison = action.Filter.Comparison
                })
        };
    }

    [ReducerMethod]
    public static FilterColorState ReduceSetFilters(FilterColorState state, FilterColorAction.SetFilters action)
    {
        if (state.Filters.IsEmpty)
        {
            return state with
            {
                Filters = state.Filters.AddRange(
                    action.Filters.Select(filter =>
                        new FilterColorModel
                        {
                            Id = filter.Id,
                            Color = filter.Color,
                            Comparison = filter.Comparison with { }
                        }))
            };
        }

        var updatedFilters = state.Filters;

        foreach (var newFilter in action.Filters)
        {
            foreach (var filter in state.Filters)
            {
                if (filter.Id == newFilter.Id)
                {
                    updatedFilters = updatedFilters
                        .Remove(filter)
                        .Add(filter with
                        {
                            Color = newFilter.Color,
                            Comparison = newFilter.Comparison with { }
                        });
                }
                else
                {
                    updatedFilters = updatedFilters.Add(
                        new FilterColorModel
                        {
                            Id = newFilter.Id,
                            Color = newFilter.Color,
                            Comparison = newFilter.Comparison with { }
                        });
                }
            }
        }

        return state with { Filters = updatedFilters };
    }
}
