// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Platforms.Windows;

internal static class PickerHostWindow
{
    /// <summary>
    ///     Returns the <c>HWND</c> of the active MAUI host window. Throws when no MAUI host window is available so
    ///     callers can surface the broken-host condition instead of presenting a parentless dialog.
    /// </summary>
    public static IntPtr GetHandle()
    {
        var current = Application.Current;
        var hostWindow = current?.Windows.Count > 0 ? current.Windows[0] : null;

        if (hostWindow?.Handler?.PlatformView is not MauiWinUIWindow window)
        {
            throw new InvalidOperationException(
                "No MAUI host window is available to present the picker.");
        }

        return window.WindowHandle;
    }
}
