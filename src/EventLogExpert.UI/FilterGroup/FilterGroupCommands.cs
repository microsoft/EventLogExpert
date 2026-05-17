// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Persistence;
using Fluxor;

namespace EventLogExpert.UI.FilterGroup;

internal sealed class FilterGroupCommands(IDispatcher dispatcher) : IFilterGroupCommands
{
    private readonly IDispatcher _dispatcher = dispatcher;

    public void AddGroup(SavedFilterGroup? group = null) => _dispatcher.Dispatch(new AddGroupAction(group));

    public void ImportGroups(IEnumerable<SavedFilterGroup> groups) => _dispatcher.Dispatch(new ImportGroupsAction(groups));

    public void LoadGroups() => _dispatcher.Dispatch(new LoadGroupsAction());

    public void RemoveFilter(FilterGroupId parentId, FilterId id) => _dispatcher.Dispatch(new RemoveGroupFilterAction(parentId, id));

    public void RemoveGroup(FilterGroupId id) => _dispatcher.Dispatch(new RemoveGroupAction(id));

    public void SetFilter(FilterGroupId parentId, SavedFilter filter) => _dispatcher.Dispatch(new SetGroupFilterAction(parentId, filter));

    public void SetGroup(SavedFilterGroup group) => _dispatcher.Dispatch(new SetGroupAction(group));

    public void ToggleFilterExcluded(FilterGroupId parentId, FilterId id) => _dispatcher.Dispatch(new ToggleGroupFilterExcludedAction(parentId, id));

    public void ToggleGroup(FilterGroupId id) => _dispatcher.Dispatch(new ToggleGroupAction(id));
}
