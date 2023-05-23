// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.Models;
using EventLogExpert.Store.EventLog;
using Fluxor;
using System.Collections.Immutable;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.Store.FilterPane;

public class FilterPaneReducers
{
    private readonly IDispatcher _dispatcher;

    public FilterPaneReducers(IDispatcher dispatcher) => _dispatcher = dispatcher;

    [ReducerMethod]
    public static AvailableFilterState ReduceAddEvent(AvailableFilterState state, EventLogAction.AddEvent action)
    {
        var ev = action.NewEvent;

        var newState = state;

        // These lookups against EventIdsAll, etc, could be slow if
        // we have a lot of values. Consider whether we should change these to
        // ImmutableHashSets.
        if (!state.EventIdsAll.Contains(ev.Id))
        {
            var newId = new List<int> { ev.Id };
            var allIds = newId.Concat(state.EventIdsAll).ToImmutableList();
            newState = state with { EventIdsAll = allIds };
        }

        if (!state.EventProviderNamesAll.Contains(ev.Source))
        {
            var newProvider = new List<string> { ev.Source };
            var allProviders = newProvider.Concat(state.EventProviderNamesAll).ToImmutableList();
            newState = state with { EventProviderNamesAll = allProviders };
        }

        if (!state.TaskNamesAll.Contains(ev.TaskCategory))
        {
            var newTask = new List<string> { ev.TaskCategory };
            var allTasks = newTask.Concat(state.TaskNamesAll).ToImmutableList();
            newState = state with { TaskNamesAll = allTasks };
        }

        return newState;
    }

    [ReducerMethod(typeof(FilterPaneAction.AddFilter))]
    public static FilterPaneState ReduceAddFilterAction(FilterPaneState state)
    {
        var updatedList = state.CurrentFilters.ToList();

        if (updatedList.Count <= 0)
        {
            return state with { CurrentFilters = new List<FilterModel> { new(Guid.NewGuid()) } };
        }

        updatedList.Add(new FilterModel(Guid.NewGuid()));

        return state with { CurrentFilters = updatedList };
    }

    [ReducerMethod(typeof(FilterPaneAction.AddAvailableFilters))]
    public static AvailableFilterState ReduceAddRecentFilter(AvailableFilterState state) => state;

    [ReducerMethod]
    public static FilterPaneState ReduceAddSubFilterAction(FilterPaneState state, FilterPaneAction.AddSubFilter action)
    {
        var updatedList = state.CurrentFilters.ToList();
        var parentFilter = updatedList.FirstOrDefault(parent => parent.Id == action.ParentId);

        if (parentFilter is null) { return state; } // If not parent filter, something went wrong and bail

        parentFilter.SubFilters.Add(new SubFilterModel(parentFilter.FilterType));

        return state with { CurrentFilters = updatedList };
    }

    [ReducerMethod]
    public static AvailableFilterState ReduceLoadEventsAction(AvailableFilterState state,
        EventLogAction.LoadEvents action) => state with
    {
        EventIdsAll = action.AllEventIds,
        EventProviderNamesAll = action.AllProviderNames,
        TaskNamesAll = action.AllTaskNames,
        EventDateRange = new FilterDateModel
        {
            After = action.Events.Last().TimeCreated, 
            Before = action.Events.First().TimeCreated
        }
    };

    [ReducerMethod]
    public static FilterPaneState ReduceRemoveFilterAction(FilterPaneState state, FilterPaneAction.RemoveFilter action)
    {
        var updatedList = state.CurrentFilters.ToList();
        var filter = updatedList.FirstOrDefault(filter => filter.Id == action.FilterModel.Id);

        if (filter is null)
        {
            return state;
        }

        updatedList.Remove(filter);

        return state with { CurrentFilters = updatedList };
    }

    [ReducerMethod]
    public static FilterPaneState ReduceRemoveSubFilterAction(FilterPaneState state,
        FilterPaneAction.RemoveSubFilter action)
    {
        var updatedList = state.CurrentFilters.ToList();
        var parentFilter = updatedList.FirstOrDefault(parent => parent.Id == action.ParentId);

        if (parentFilter is null) { return state; }

        parentFilter.SubFilters.Remove(action.SubFilterModel);

        return state with { CurrentFilters = updatedList };
    }

    [ReducerMethod(typeof(FilterPaneAction.ApplyFilters))]
    public static FilterPaneState ReduceApplyFilters(FilterPaneState state)
    {
        List<FilterModel> filters = new();

        foreach (var filterModel in state.CurrentFilters)
        {
            if (filterModel.Comparison.Any())
            {
                filters.Add(filterModel);
            }
        }

        return state with { AppliedFilters = filters };
    }

    [ReducerMethod]
    public static FilterPaneState
        ReduceSetFilterDateRange(FilterPaneState state, FilterPaneAction.SetFilterDateRange action) =>
        state with { FilteredDateRange = action.FilterDateModel };
}
