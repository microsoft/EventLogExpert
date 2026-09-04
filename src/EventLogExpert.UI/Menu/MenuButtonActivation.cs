// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Microsoft.AspNetCore.Components.Web;

namespace EventLogExpert.UI.Menu;

internal static class MenuButtonActivation
{
    /// <summary>
    ///     Determines whether a click on a menu-opening button (menu bar item, split-button chevron, "More actions") was
    ///     synthesized by keyboard activation (Enter or Space on the focused button) rather than a pointer click, so the
    ///     opened menu shows its keyboard focus ring on the first item only when the keyboard opened it. A pointer click
    ///     reports a click count (detail) of at least 1; a keyboard-synthesized click reports 0.
    /// </summary>
    public static bool WasKeyboardTriggered(MouseEventArgs args) => args.Detail == 0;
}
