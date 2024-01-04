// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Store.FilterGroup;
using Fluxor;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.Shared.Components.Filters;

public sealed partial class FilterGroupModal
{
    [Inject] private IState<FilterGroupState> FilterGroupState { get; init; } = null!;

    protected override void OnInitialized()
    {
        SubscribeToAction<FilterGroupAction.OpenMenu>(action => Open().AndForget());

        base.OnInitialized();
    }

    private void Export() { }

    private void Import() { }
}
