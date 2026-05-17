// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Filtering.Runtime;
using Fluxor;

namespace EventLogExpert.Runtime.FilterPane;

internal sealed class FilterPaneCommands(IDispatcher dispatcher) : IFilterPaneCommands
{
    private readonly IDispatcher _dispatcher = dispatcher;

    public void ApplyFilterGroup(SavedFilterGroup group) => _dispatcher.Dispatch(new ApplyFilterGroupAction(group));

    public void ClearAllFilters() => _dispatcher.Dispatch(new ClearAllFiltersAction());

    public void RemoveFilter(FilterId id) => _dispatcher.Dispatch(new RemoveFilterAction(id));

    public void SaveFilterGroup(string name) => _dispatcher.Dispatch(new SaveFilterGroupAction(name));

    public void SetFilter(SavedFilter filter) => _dispatcher.Dispatch(new SetFilterAction(filter));

    public void SetFilterDateRange(DateFilter? dateFilter) => _dispatcher.Dispatch(new SetFilterDateRangeAction(dateFilter));

    public void ToggleFilterDate() => _dispatcher.Dispatch(new ToggleFilterDateAction());

    public void ToggleFilterEnabled(FilterId id) => _dispatcher.Dispatch(new ToggleFilterEnabledAction(id));

    public void ToggleFilterExcluded(FilterId id) => _dispatcher.Dispatch(new ToggleFilterExcludedAction(id));

    public void ToggleFilteringEnabled() => _dispatcher.Dispatch(new ToggleIsEnabledAction());
}
