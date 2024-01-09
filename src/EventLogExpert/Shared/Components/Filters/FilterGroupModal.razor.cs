// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;
using EventLogExpert.UI.Services;
using EventLogExpert.UI.Store.FilterGroup;
using EventLogExpert.UI.Store.FilterPane;
using Fluxor;
using Microsoft.AspNetCore.Components;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.Shared.Components.Filters;

public sealed partial class FilterGroupModal
{
    [Inject] private IAlertDialogService AlertDialogService { get; init; } = null!;

    [Inject] private IDispatcher Dispatcher { get; init; } = null!;

    [Inject] private IState<FilterGroupState> FilterGroupState { get; init; } = null!;

    protected override void OnInitialized()
    {
        SubscribeToAction<FilterGroupAction.OpenMenu>(action => Open().AndForget());

        base.OnInitialized();
    }

    private void ApplyFilters(FilterGroupModel model)
    {
        Dispatcher.Dispatch(new FilterPaneAction.ApplyFilterGroup(model));
        Close().AndForget();
    }

    private void CreateGroup() =>
        Dispatcher.Dispatch(new FilterGroupAction.AddGroup(new FilterGroupModel { IsEditing = true }));

    private void Export() { }

    private void Import() { }

    private void RemoveGroup(Guid id) => Dispatcher.Dispatch(new FilterGroupAction.RemoveGroup(id));

    private async void RenameGroup(FilterGroupModel model)
    {
        var groupName = await AlertDialogService.DisplayPrompt("Group Name", "What would you like to name this group?");

        if (string.IsNullOrEmpty(groupName)) { return; }

        model.Name = groupName;

        Dispatcher.Dispatch(new FilterGroupAction.SetGroup(model));
    }
}
