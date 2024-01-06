// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;
using EventLogExpert.UI.Store.FilterGroup;
using EventLogExpert.UI.Store.FilterPane;
using Fluxor;
using Microsoft.AspNetCore.Components;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.Shared.Components.Filters;

public sealed partial class FilterGroupModal
{
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

    private void CreateGroup() => Dispatcher.Dispatch(new FilterGroupAction.AddGroup());

    private void Export() { }

    private void Import() { }
}
