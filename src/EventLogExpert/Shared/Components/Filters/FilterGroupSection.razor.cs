// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.Shared.Components.Filters;

public sealed partial class FilterGroupSection
{
    private bool _menuState = true;

    [Parameter] public required FilterGroupData Data { get; set; }

    [Parameter] public required string Name { get; set; }

    [Parameter] public required FilterGroupModal Parent { get; set; }

    private string MenuState => _menuState.ToString().ToLower();

    private void ToggleMenu() => _menuState = !_menuState;
}
