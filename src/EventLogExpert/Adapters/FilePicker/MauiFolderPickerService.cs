// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Platforms.Windows;
using EventLogExpert.Runtime.Common.Files;

namespace EventLogExpert.Adapters.FilePicker;

internal sealed class MauiFolderPickerService : IFolderPickerService
{
    public Task<string?> PickFolderAsync() =>
        MainThread.InvokeOnMainThreadAsync(() => Win32FileDialogService.PickFolderAsync("Select Folder"));
}
