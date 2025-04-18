// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace EventLogExpert.Platforms.Windows;

public static class FolderPickerHelper
{
    public static async Task<string?> PickFolderAsync()
    {
        FolderPicker picker = new()
        {
            SuggestedStartLocation = PickerLocationId.Desktop,
            FileTypeFilter = { "*" } // Add a wildcard to allow folder selection
        };

        if (Application.Current?.Windows[0].Handler?.PlatformView is not MauiWinUIWindow window) { return null; }

        InitializeWithWindow.Initialize(picker, window.WindowHandle);

        StorageFolder? folder = await picker.PickSingleFolderAsync();

        return folder?.Path;
    }
}
