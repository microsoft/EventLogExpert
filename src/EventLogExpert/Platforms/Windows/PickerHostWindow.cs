// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using WinRT.Interop;

namespace EventLogExpert.Platforms.Windows;

/// <summary>
///     Single source of truth for the picker-windowing dance. WinUI's <c>FileSavePicker</c> and <c>FolderPicker</c>
///     need the active MAUI host window's <c>HWND</c> to present correctly — they associate via
///     <see cref="InitializeWithWindow.Initialize(object, IntPtr)" /> (COM interop that fails with <c>0x80004005</c> when
///     the process is elevated and the picker isn't bound to a window). The Win32 <see cref="Win32FileDialog" /> also
///     takes an <c>HWND</c>, but through the <c>hwndOwner</c> field of the <c>OPENFILENAMEW</c> struct passed to the
///     procedural <c>comdlg32!GetOpenFileNameW</c> / <c>GetSaveFileNameW</c> APIs (no COM activation).
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
    ///     <see cref="InitializeWithWindow.Initialize(object, IntPtr)" /> COM interop. Use for WinUI <c>FileSavePicker</c> /
    ///     <c>FolderPicker</c>; for file-open / file-save use <see cref="Win32FileDialog" /> which takes the <c>HWND</c>
    ///     directly via <see cref="GetHandle" /> and passes it as the OPENFILENAMEW <c>hwndOwner</c> field.
    /// </summary>
    public static void Initialize(object picker)
    {
        ArgumentNullException.ThrowIfNull(picker);
        InitializeWithWindow.Initialize(picker, GetHandle());
    }
}
