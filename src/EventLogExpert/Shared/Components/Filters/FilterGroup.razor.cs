// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;
using EventLogExpert.UI.Services;
using EventLogExpert.UI.Store.FilterGroup;
using EventLogExpert.UI.Store.FilterPane;
using Microsoft.AspNetCore.Components;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.Shared.Components.Filters;

public sealed partial class FilterGroup
{
    [Parameter] public FilterGroupModel Group { get; set; } = null!;

    [Parameter] public FilterGroupModal Parent { get; set; } = null!;

    [Inject] private IAlertDialogService AlertDialogService { get; init; } = null!;

    [Inject] private IDispatcher Dispatcher { get; init; } = null!;

    private void AddFilter() => Dispatcher.Dispatch(new FilterGroupAction.AddFilter(Group.Id));

    private void ApplyFilters()
    {
        Dispatcher.Dispatch(new FilterPaneAction.ApplyFilterGroup(Group));
        Parent.Close().AndForget();
    }

    private void CopyGroup() => Clipboard.SetTextAsync(Group.Filters.Count() > 1 ?
        string.Join(" || ", Group.Filters.Select(filter => $"({filter.Comparison.Value})")) :
        Group.Filters.First().Comparison.Value);

    private void RemoveGroup() => Dispatcher.Dispatch(new FilterGroupAction.RemoveGroup(Group.Id));

    private async void RenameGroup()
    {
        var newName = await AlertDialogService.DisplayPrompt("Group Name", "What would you like to name this group?", Group.Name);

        if (string.IsNullOrEmpty(newName) || string.Equals(newName, Group.Name)) { return; }

        Dispatcher.Dispatch(new FilterGroupAction.SetGroup(Group with { Name = newName }));
    }

    private void SaveGroup()
    {
        foreach (var filter in Group.Filters)
        {
            if (filter.IsEditing) { return; }
        }

        Dispatcher.Dispatch(new FilterGroupAction.SetGroup(Group));
    }

    private void ToggleGroup() => Dispatcher.Dispatch(new FilterGroupAction.ToggleGroup(Group.Id));
}
