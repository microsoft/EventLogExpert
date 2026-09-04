// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Microsoft.AspNetCore.Components.Web;

namespace EventLogExpert.UI.Menu;

internal static class ContextMenuInvocation
{
    /// <summary>
    ///     Determines whether a contextmenu event was raised by the keyboard (Shift+F10 / Menu key) rather than a mouse
    ///     right-click, so the opened menu shows its keyboard focus ring on the first item only when the keyboard opened it. A
    ///     mouse right-click reports the secondary button (2); a keyboard invocation reports no button, so anything other than
    ///     the secondary button is keyboard.
    /// </summary>
    public static bool WasKeyboardTriggered(MouseEventArgs args) => args.Button != 2;
}
