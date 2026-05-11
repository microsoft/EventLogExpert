// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace EventLogExpert.Platforms.Windows;

internal static class FolderPickerHelper
{
    /// <summary>
    ///     Presents the WinUI folder picker. Returns the selected folder's path, or <c>null</c> only when the user
    ///     cancelled. Throws <see cref="InvalidOperationException" /> when no MAUI host window is available so callers can
    ///     surface the broken-host condition instead of silently treating it as a cancel.
    /// </summary>
    public static async Task<string?> PickFolderAsync()
    {
        FolderPicker picker = new()
        {
            SuggestedStartLocation = PickerLocationId.Desktop,
            FileTypeFilter = { "*" } // Add a wildcard to allow folder selection
        };

        var current = Application.Current;
        var hostWindow = current?.Windows.Count > 0 ? current.Windows[0] : null;

        if (hostWindow?.Handler?.PlatformView is not MauiWinUIWindow window)
        {
            throw new InvalidOperationException(
                "No MAUI host window is available to present the folder picker.");
        }

        InitializeWithWindow.Initialize(picker, window.WindowHandle);

        StorageFolder? folder = await picker.PickSingleFolderAsync();

        return folder?.Path;
    }
}
