// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Windows.Storage;
using Windows.Storage.Pickers;

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

        PickerHostWindow.Initialize(picker);

        StorageFolder? folder = await picker.PickSingleFolderAsync();

        return folder?.Path;
    }
}
