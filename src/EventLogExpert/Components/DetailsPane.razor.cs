// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Components;

public partial class DetailsPane
{
    private bool _expandMenu = false;

    private void ToggleMenu() => _expandMenu = !_expandMenu;
}
