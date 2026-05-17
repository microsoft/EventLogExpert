// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.FilterGroup;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace EventLogExpert.Components.FilterGroup;

public sealed partial class FilterGroupSection
{
    private bool _menuState = true;

    [Parameter] public required string Name { get; set; }

    [Parameter] public required FilterGroupNode Node { get; set; }

    [Parameter] public required FilterGroupModal Parent { get; set; }

    private string MenuState => _menuState.ToString().ToLower();

    private void HandleKeyDown(KeyboardEventArgs e)
    {
        if (e.Key is "Enter" or " ")
        {
            ToggleMenu();
        }
    }

    private void ToggleMenu() => _menuState = !_menuState;
}
