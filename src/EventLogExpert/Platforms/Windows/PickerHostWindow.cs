// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using WinRT.Interop;

namespace EventLogExpert.Platforms.Windows;

/// <summary>
///     Single source of truth for the WinUI picker-windowing dance. WinUI's <c>FileSavePicker</c>,
///     <c>FolderPicker</c>, and the Win32 <see cref="Win32FileDialog" /> all need the active MAUI host window's
///     <c>HWND</c> to present correctly — <c>FileSavePicker</c>/<c>FolderPicker</c> via
///     <see cref="InitializeWithWindow.Initialize(object, IntPtr)" /> (COM interop that fails with <c>0x80004005</c> when
///     the process is elevated and the picker isn't bound to a window), <see cref="Win32FileDialog" /> via the
///     <c>hwndOwner</c> argument of <c>IFileOpenDialog::Show</c>.
/// </summary>
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

    /// <summary>
    ///     Associates <paramref name="picker" /> with the active MAUI host window via the WinUI
    ///     <see cref="InitializeWithWindow.Initialize(object, IntPtr)" /> COM interop. Use for <c>FileSavePicker</c> /
    ///     <c>FolderPicker</c>; for file-open use <see cref="Win32FileDialog" /> which takes the <c>HWND</c> directly via
    ///     <see cref="GetHandle" />.
    /// </summary>
    public static void Initialize(object picker)
    {
        ArgumentNullException.ThrowIfNull(picker);
        InitializeWithWindow.Initialize(picker, GetHandle());
    }
}
