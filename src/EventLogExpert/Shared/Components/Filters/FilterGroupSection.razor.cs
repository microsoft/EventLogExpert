// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.Shared.Components.Filters;

public sealed partial class FilterGroupSection
{
    private bool _menuState = true;

    [Parameter] public FilterGroupData Data { get; set; } = null!;

    [Parameter] public string Name { get; set; } = null!;

    [Parameter] public FilterGroupModal Parent { get; set; } = null!;

    private string MenuState => _menuState.ToString().ToLower();

    private void ToggleMenu() => _menuState = !_menuState;
}
