// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Store.FilterGroup;

namespace EventLogExpert.Shared.Components.Filters;

public partial class FilterGroupModal
{
    protected override void OnInitialized()
    {
        SubscribeToAction<FilterGroupAction.OpenMenu>(action => Open().AndForget());

        base.OnInitialized();
    }

    private void Export() { }

    private void Import() { }
}
